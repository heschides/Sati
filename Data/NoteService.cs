using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

public class NoteService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService,
    TimeProvider? timeProvider = null) : INoteService
{
    public async Task<Note> AddNoteAsync(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        var today = BillingRules.MaineBusinessDate((timeProvider ?? TimeProvider.System).GetUtcNow());
        NormalizeScheduling(note, today);
        ValidateCaseManagerInput(note);
        var actor = CurrentActor();
        await using var context = contextFactory.CreateDbContext();
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
        if (!await LocalTenantAccess.OwnsPersonAsync(context, actor, note.PersonId))
            throw new UnauthorizedAccessException("You may create notes only for your own caseload.");

        note.AgencyId = actor.AgencyId;
        await using var scheduleWrite = await ServiceTimeWriteScope.BeginAsync(
            context, actor.AgencyId, actor.Id);
        await EnsureExactFormLinkAsync(context, note);
        await EnsureServiceTimeAvailableAsync(context, actor.Id, note, null);
        context.Notes.Add(note);
        LocalAuditTrail.Record(context, actor, LocalAuditActions.NoteCreated, "Note");
        await context.SaveChangesAsync();
        if (await AttestLinkedFormAsync(context, actor, note, today))
            await context.SaveChangesAsync();
        // The note's own form completion is now visible to the ordinary window
        // gate. Any unrelated blocker still rolls this entire transaction back.
        await EnsureSubmissionAllowedAsync(context, actor, note, today);
        await scheduleWrite.CommitAsync();
        return note;
    }

    public async Task DeleteNoteAsync(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        var actor = CurrentActor();
        await using var context = contextFactory.CreateDbContext();
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
        await EnsureUserInScopeAsync(context, actor, actor.Id);
        await using var scheduleWrite = await ServiceTimeWriteScope.BeginAsync(
            context, actor.AgencyId, actor.Id);
        var stored = await context.Notes.Include(candidate => candidate.Person)
            .SingleOrDefaultAsync(candidate => candidate.Id == note.Id);
        if (stored is null || stored.Revision != note.Revision)
            throw new NoteConcurrencyException();
        if (stored.AgencyId != actor.AgencyId ||
            !await LocalTenantAccess.OwnsPersonAsync(context, actor, stored.PersonId))
            throw new UnauthorizedAccessException("You may delete notes only from your own caseload.");
        if (!NoteWorkflow.CanCaseManagerDelete((int?)stored.Status))
            throw new InvalidOperationException("Submitted and workflow-controlled notes are retained as part of the clinical record.");

        context.Notes.Remove(stored);
        try
        {
            await context.SaveChangesAsync();
            await scheduleWrite.CommitAsync();
        }
        catch (DbUpdateConcurrencyException ex) { throw new NoteConcurrencyException(ex); }
    }

    public async Task UpdateNoteAsync(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        var today = BillingRules.MaineBusinessDate((timeProvider ?? TimeProvider.System).GetUtcNow());
        NormalizeScheduling(note, today);
        ValidateCaseManagerInput(note);
        var actor = CurrentActor();
        await using var context = contextFactory.CreateDbContext();
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
        await EnsureUserInScopeAsync(context, actor, actor.Id);
        await using var scheduleWrite = await ServiceTimeWriteScope.BeginAsync(
            context, actor.AgencyId, actor.Id);
        var stored = await context.Notes.Include(candidate => candidate.Person)
            .SingleOrDefaultAsync(candidate => candidate.Id == note.Id);
        if (stored is null || stored.Revision != note.Revision)
            throw new NoteConcurrencyException();
        if (stored.AgencyId != actor.AgencyId ||
            !await LocalTenantAccess.OwnsPersonAsync(context, actor, stored.PersonId))
            throw new UnauthorizedAccessException("You may update notes only in your own caseload.");
        if (!NoteWorkflow.CanCaseManagerEdit((int?)stored.Status))
            throw new InvalidOperationException("Logged and approved notes cannot be edited. A supervisor must return a logged note before it can be corrected.");
        if (!NoteWorkflow.CanCaseManagerTransition((int?)stored.Status, (int?)note.Status))
            throw new InvalidOperationException(
                NoteWorkflow.DescribeRejectedTransition((int?)stored.Status, (int?)note.Status));

        var previousPersonId = stored.PersonId;
        var targetPerson = stored.Person;
        if (note.PersonId != previousPersonId)
        {
            if (!await LocalTenantAccess.OwnsPersonAsync(context, actor, note.PersonId))
                throw new UnauthorizedAccessException("You may reassign a note only within your current caseload.");
            targetPerson = await context.People.SingleOrDefaultAsync(person =>
                person.Id == note.PersonId && person.UserId == actor.Id &&
                person.AgencyId == actor.AgencyId)
                ?? throw new UnauthorizedAccessException(
                    "You may reassign a note only to another client on your own caseload.");
        }

        await EnsureExactFormLinkAsync(context, note);
        await EnsureServiceTimeAvailableAsync(context, actor.Id, note, stored.Id);
        CopyCaseManagerValues(note, stored);
        stored.PersonId = note.PersonId;
        stored.Person = targetPerson;
        stored.Revision++;
        await AttestLinkedFormAsync(context, actor, stored, today);
        LocalAuditTrail.Record(context, actor, LocalAuditActions.NoteUpdated, "Note", stored.Id);
        if (previousPersonId != stored.PersonId)
        {
            LocalAuditTrail.Record(
                context,
                actor,
                LocalAuditActions.NoteReassigned,
                "Note",
                stored.Id,
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    previousPersonId,
                    newPersonId = stored.PersonId
                }));
        }
        try
        {
            await context.SaveChangesAsync();
            // Evaluate after this note's form attestation is persisted, but before
            // commit so a remaining blocker rejects note and attestation together.
            await EnsureSubmissionAllowedAsync(context, actor, note, today);
            await scheduleWrite.CommitAsync();
            note.Revision = stored.Revision;
            note.Person = targetPerson;
        }
        catch (DbUpdateConcurrencyException ex) { throw new NoteConcurrencyException(ex); }
    }

    public async Task<List<Note>> GetAllByPersonAsync(int personId)
    {
        var actor = CurrentActor();
        await using var context = contextFactory.CreateDbContext();
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
        await EnsurePersonInScopeAsync(context, actor, personId);
        return await context.Notes.Where(n => n.PersonId == personId && n.AgencyId == actor.AgencyId).ToListAsync();
    }

    /// <summary>
    /// Ages out the signed-in case manager's own unfinished drafts. The sweep is
    /// scoped to the caller's caseload: a dashboard refresh must never rewrite
    /// another case manager's record, let alone another agency's.
    /// </summary>
    public async Task UpdateAbandonedNotesAsync(int abandonedAfterDays)
    {
        if (abandonedAfterDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(abandonedAfterDays),
                "The abandonment threshold must be a positive number of days.");

        var actor = CurrentActor();
        await using var context = contextFactory.CreateDbContext();
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
        await EnsureUserInScopeAsync(context, actor, actor.Id);
        var threshold = DateTime.Today.AddDays(-abandonedAfterDays);
        var notes = await context.Notes.Where(n => n.Person.UserId == actor.Id &&
            n.Person.AgencyId == actor.AgencyId &&
            n.AgencyId == actor.AgencyId &&
            n.Status == NoteStatus.Pending &&
            n.EventDate.HasValue && n.EventDate.Value < threshold).ToListAsync();
        foreach (var note in notes)
        {
            if (!NoteWorkflow.CanSystemAbandon((int?)note.Status))
                continue;
            note.Status = NoteStatus.Abandoned;
            note.Revision++;
        }
        await context.SaveChangesAsync();
    }

    public async Task<List<Note>> GetMonthlyNotesAsync(int userId)
    {
        var actor = CurrentActor();
        await using var context = contextFactory.CreateDbContext();
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
        await EnsureUserInScopeAsync(context, actor, userId);
        var firstDay = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var nextMonth = firstDay.AddMonths(1);
        return await context.Notes.Where(n => n.EventDate >= firstDay && n.EventDate < nextMonth &&
            n.Person.UserId == userId && n.Person.AgencyId == actor.AgencyId && n.AgencyId == actor.AgencyId).ToListAsync();
    }

    public async Task<List<Note>> GetDayScheduleAsync(int userId, DateTime date)
    {
        var actor = CurrentActor();
        await using var context = contextFactory.CreateDbContext();
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
        await EnsureUserInScopeAsync(context, actor, userId);
        var dayStart = date.Date;
        return await context.Notes.Include(n => n.Person).Where(n => n.Person.UserId == userId &&
            n.Person.AgencyId == actor.AgencyId && n.AgencyId == actor.AgencyId &&
            n.EventDate.HasValue && n.EventDate.Value >= dayStart && n.EventDate.Value < dayStart.AddDays(1)).ToListAsync();
    }

    public async Task<List<Note>> GetByYearAsync(int userId, int year)
    {
        var actor = CurrentActor();
        await using var context = contextFactory.CreateDbContext();
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
        await EnsureUserInScopeAsync(context, actor, userId);
        var firstDay = new DateTime(year, 1, 1);
        var end = firstDay.AddYears(1);
        return await context.Notes.Include(n => n.Person).Where(n => n.Person.UserId == userId &&
            n.Person.AgencyId == actor.AgencyId && n.AgencyId == actor.AgencyId &&
            n.EventDate.HasValue && n.EventDate.Value >= firstDay && n.EventDate.Value < end).ToListAsync();
    }

    private static void CopyCaseManagerValues(Note source, Note target)
    {
        target.Narrative = source.Narrative;
        target.EventDate = source.EventDate;
        target.Status = source.Status;
        target.Minutes = source.Minutes;
        target.StartTime = source.StartTime;
        target.FormType = source.FormType;
        target.FormId = source.FormId;
        target.FormDateCorrectionReason = source.FormDateCorrectionReason;
        target.NoteType = source.NoteType;
        target.GoalProgress = source.GoalProgress;
        target.CaseManagerJustification = source.CaseManagerJustification;
        target.VisitDocumentationJson = source.VisitDocumentationJson;
    }

    private User CurrentActor() => sessionService.CurrentUser
        ?? throw new UnauthorizedAccessException("A signed-in case manager is required.");

    private static async Task EnsureUserInScopeAsync(SatiContext context, User actor, int userId)
    {
        if (!await LocalTenantAccess.CanAccessUserAsync(context, actor, userId))
            throw new UnauthorizedAccessException("You may read notes only for yourself or a case manager you supervise.");
    }

    private static async Task EnsurePersonInScopeAsync(SatiContext context, User actor, int personId)
    {
        if (!await LocalTenantAccess.CanAccessPersonAsync(context, actor, personId))
            throw new UnauthorizedAccessException("You may read notes only for clients in your scope.");
    }

    private static void ValidateCaseManagerInput(Note note)
    {
        var formLinkError = FormNoteLinkRules.Validate(
            note.NoteType?.ToString(), note.FormType?.ToString(),
            note.Status?.ToString(), note.FormId, note.FormDateCorrectionReason);
        if (formLinkError is not null)
            throw new ArgumentException(formLinkError, nameof(note));
        if (note.Narrative is null || note.Narrative.Length > 1_000_000)
            throw new ArgumentException("Narrative is required and must not exceed 1,000,000 characters.", nameof(note));
        if (note.PersonId <= 0) throw new ArgumentException("A valid person is required.", nameof(note));
        if (note.NoteType == NoteType.Reminder && note.EventDate is null)
            throw new ArgumentException("A calendar reminder requires a date.", nameof(note));
        if (note.Minutes is < 0 or > 1_440)
            throw new ArgumentException("Minutes must be between 0 and 1,440.", nameof(note));
        if (note.StartTime is int start && (start < 0 || start > ServiceTimeline.WindowLengthMinutes))
            throw new ArgumentException("Service start time must fall inside the logging window.", nameof(note));
        if (!NoteWorkflow.IsCaseManagerWritableStatus((int?)note.Status))
            throw new InvalidOperationException("That note status is controlled by a supervisor workflow.");
        if (note.Status == NoteStatus.Logged && note.GoalProgress is null)
            throw new ArgumentException("Goal progress is required before a note can be submitted for review.", nameof(note));
    }

    private static void NormalizeScheduling(Note note, DateTime today)
    {
        var values = NoteSchedulingPolicy.Normalize(
            note.EventDate,
            today,
            note.Status?.ToString(),
            note.Minutes,
            note.StartTime,
            note.FormType?.ToString(),
            note.NoteType?.ToString(),
            note.CaseManagerJustification,
            note.VisitDocumentationJson,
            note.GoalProgress?.ToString());

        note.EventDate = values.EventDate;
        note.Status = ParseNullable<NoteStatus>(values.Status);
        note.Minutes = values.Minutes;
        note.StartTime = values.StartTime;
        note.FormType = ParseNullable<FormType>(values.FormType);
        note.NoteType = ParseNullable<NoteType>(values.NoteType);
        note.CaseManagerJustification = values.CaseManagerJustification;
        note.VisitDocumentationJson = values.VisitDocumentationJson;
        note.GoalProgress = ParseNullable<GoalProgressLevel>(values.GoalProgress);
    }

    private static T? ParseNullable<T>(string? value) where T : struct, Enum =>
        value is null ? null : Enum.Parse<T>(value, ignoreCase: false);

    private static async Task<bool> AttestLinkedFormAsync(
        SatiContext context, User actor, Note note, DateTime today)
    {
        if (note.NoteType != NoteType.Form || note.Status != NoteStatus.Logged ||
            FormNoteLinkRules.IsRelease(note.FormType?.ToString()))
            return false;

        if (note.FormId is not int formId || note.EventDate is not DateTime completedOn ||
            note.Id <= 0)
            throw new InvalidOperationException(
                "A submitted Form note must identify the exact obligation, activity date, and saved note.");

        var form = await context.Forms
            .Include(candidate => candidate.Person)
            .SingleAsync(candidate => candidate.Id == formId &&
                candidate.PersonId == note.PersonId && candidate.Type == note.FormType);

        var previousCompletedOn = form.CompletedDate;
        if (previousCompletedOn?.Date == completedOn.Date)
        {
            var latest = await context.FormAttestations.AsNoTracking()
                .Where(candidate => candidate.FormId == form.Id)
                .OrderByDescending(candidate => candidate.Id)
                .FirstOrDefaultAsync();
            if (latest is { Kind: FormAttestationKind.Attested } &&
                latest.EvidenceNoteId == note.Id)
                return false;

            form.Attest(FormAttestation.Attested(
                completedOn, AttestationActorKind.CaseManager, actor.Id,
                DateTime.UtcNow, evidenceNoteId: note.Id,
                prerequisiteStateJson: FormAttestationRules.NoPrerequisitesStateJson,
                reason: "Submitted form note cites this completion date."));
            LocalAuditTrail.Record(context, actor, LocalAuditActions.FormAttested,
                "Form", form.Id, System.Text.Json.JsonSerializer.Serialize(new
                {
                    formType = form.Type.ToString(),
                    completedOn = completedOn.Date.ToString("yyyy-MM-dd"),
                    evidenceNoteId = note.Id,
                    sameDateEvidenceAdded = true
                }));
            return true;
        }
        var correctionReason = note.FormDateCorrectionReason?.Trim();
        if (previousCompletedOn is DateTime prior &&
            string.IsNullOrWhiteSpace(correctionReason))
            throw new InvalidOperationException(
                $"This form was attested complete on {prior:M/d/yyyy}, but the note says " +
                $"the work occurred on {completedOn:M/d/yyyy}. Enter a reason for the " +
                "date correction before submitting this note.");

        var effectiveDate = form.Person.EffectiveDate
            ?? throw new InvalidOperationException("The client has no effective date.");
        var cycle = FormAttestationRules.ResolveCycleForForm(
            effectiveDate, form.Type.ToString(), form.DueDate,
            form.TargetEffectiveDate == default ? null : form.TargetEffectiveDate)
            ?? throw new InvalidOperationException(
                "The selected form is not attached to a valid compliance cycle.");
        var settings = await context.Settings.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.AgencyId == actor.AgencyId)
            ?? new Settings();
        var availableOn = FormDueDateCalculator.ComputeAvailableDateForDueDate(
            form.Type, form.DueDate, settings);
        var formFacts = await context.Forms.AsNoTracking()
            .Where(candidate => candidate.PersonId == note.PersonId)
            .Select(candidate => new FormFact(
                candidate.Id, candidate.PersonId, candidate.Type.ToString(),
                candidate.DueDate, candidate.CompletedDate, candidate.TargetEffectiveDate))
            .ToListAsync();
        var decision = FormAttestationRules.Evaluate(
            form.Type.ToString(), completedOn, cycle.CycleStart, today,
            AttestationActorKind.CaseManager, [], formFacts,
            targetEffectiveDate: form.TargetEffectiveDate == default
                ? null : form.TargetEffectiveDate,
            availableOn: availableOn);
        if (!decision.Accepted)
            throw new InvalidOperationException(
                decision.DateError ?? string.Join(" ",
                    decision.UnmetPrerequisites.Select(item => item.Message)));

        var prerequisiteState = FormAttestationRules.NoPrerequisitesStateJson;
        if (form.Type == FormType.Reclassification)
        {
            var assessment = formFacts.FirstOrDefault(candidate =>
                candidate.FormType == FormType.ComprehensiveAssessment.ToString() &&
                candidate.TargetEffectiveDate?.Date == form.TargetEffectiveDate.Date &&
                candidate.CompletedDate is DateTime assessmentDate &&
                assessmentDate.Date <= completedOn.Date);
            if (assessment is not null)
                prerequisiteState = FormAttestationRules.AssessmentPrerequisiteStateJson(
                    assessment.FormId);
        }

        var recordedAtUtc = DateTime.UtcNow;
        if (previousCompletedOn is not null)
        {
            form.RevokeAttestation(FormAttestation.Revoked(
                AttestationActorKind.CaseManager, actor.Id, recordedAtUtc,
                correctionReason!));
            LocalAuditTrail.Record(
                context, actor, LocalAuditActions.FormAttestationRevoked, "Form", form.Id,
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    formType = form.Type.ToString(),
                    actorKind = AttestationActorKind.CaseManager.ToString(),
                    evidenceNoteId = note.Id
                }));
        }

        form.Attest(FormAttestation.Attested(
            completedOn, AttestationActorKind.CaseManager, actor.Id, recordedAtUtc,
            evidenceNoteId: note.Id, prerequisiteStateJson: prerequisiteState,
            reason: correctionReason));
        if (previousCompletedOn is not null)
            await FormService.RecordLinkedNoteImpactFlagsAsync(
                context, form, previousCompletedOn, completedOn, correctionReason!,
                recordedAtUtc, changedNote: note);
        LocalAuditTrail.Record(
            context, actor, LocalAuditActions.FormAttested, "Form", form.Id,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                formType = form.Type.ToString(),
                targetEffectiveDate = form.TargetEffectiveDate == default
                    ? null : form.TargetEffectiveDate.ToString("yyyy-MM-dd"),
                cycleStart = cycle.CycleStart.ToString("yyyy-MM-dd"),
                completedOn = completedOn.Date.ToString("yyyy-MM-dd"),
                actorKind = AttestationActorKind.CaseManager.ToString(),
                evidenceNoteId = note.Id
            }));
        return true;
    }

    private static async Task EnsureExactFormLinkAsync(SatiContext context, Note note)
    {
        if (note.FormId is not int formId)
            return;

        var matches = await context.Forms.AsNoTracking().AnyAsync(form =>
            form.Id == formId && form.PersonId == note.PersonId &&
            form.Type == note.FormType);
        if (!matches)
            throw new ArgumentException(
                "The selected form obligation does not match this client and form type.",
                nameof(note));
    }

    private static async Task EnsureSubmissionAllowedAsync(
        SatiContext context, User actor, Note note, DateTime today)
    {
        _ = today;
        if (note.Status != NoteStatus.Logged) return;
        if (note.EventDate is not DateTime serviceDate) return;

        // The transitional local service repeats the API's rule rather than
        // relying on being the only caller: the note's own service date, under the
        // policy version in force on that date, with exact recipient obligations.
        var person = await context.People.AsNoTracking()
            .Include(candidate => candidate.Forms).ThenInclude(form => form.Attestations)
            .SingleAsync(x =>
                x.Id == note.PersonId && x.UserId == actor.Id && x.AgencyId == actor.AgencyId);
        await BillingComplianceProjectionLoader.PopulateAsync(
            context, [person], actor.AgencyId);
        var policy = await BillingCompliancePolicyContextLoader.LoadAsync(
            context, actor.AgencyId);
        var compliance = person.EvaluateBillingWindowDetailed(
            serviceDate, policy.Resolve(serviceDate), policy.Schedule, contactCandidate: note);
        if (compliance.Passed) return;

        var configurationInvalid = !BillingComplianceGate.IsSupported(policy.Resolve(serviceDate));
        var result = new NoteSubmissionResult(
            false, compliance.Reasons, !configurationInvalid, configurationInvalid);
        if (!NoteSubmissionGate.IsSubmissionAllowed(result, note.CaseManagerJustification))
            throw new NoteSubmissionException(result.Message);
    }

    internal static async Task EnsureServiceTimeAvailableAsync(
        SatiContext context, int userId, Note note, int? editingNoteId)
    {
        var candidate = ServiceTimeline.TryCreateBlock(editingNoteId ?? 0, note.StartTime,
            note.Minutes, note.Status?.ToString());
        if (candidate is null) return;
        var windowProblem = ServiceTimeline.DescribeWindowViolation(candidate.StartMinutes, candidate.Minutes);
        if (windowProblem is not null) throw new InvalidOperationException(windowProblem);
        if (note.EventDate is not DateTime eventDate) return;

        var agencyId = await context.Users.AsNoTracking().Where(user => user.Id == userId)
            .Select(user => (int?)user.AgencyId).SingleOrDefaultAsync()
            ?? throw new UnauthorizedAccessException("The note's case manager is no longer available.");
        var dayStart = eventDate.Date;
        var blocks = (await context.Notes.Include(existing => existing.Person)
                .Where(existing => existing.Person.UserId == userId &&
                    existing.Person.AgencyId == agencyId && existing.AgencyId == agencyId && existing.EventDate >= dayStart &&
                    existing.EventDate < dayStart.AddDays(1)).ToListAsync())
            .Select(existing => ServiceTimeline.TryCreateBlock(existing.Id, existing.StartTime,
                existing.Minutes, existing.Status?.ToString(), $"a note for {existing.Person.FullName}"))
            .OfType<ServiceBlock>();
        var conflicts = ServiceTimeline.FindConflicts(candidate, blocks);
        if (conflicts.Count > 0)
            throw new InvalidOperationException("This service time overlaps time already recorded on this date. " +
                string.Join(" ", conflicts.Select(conflict => conflict.Reason)));
    }
}
