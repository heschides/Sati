using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

public sealed class AdminFormNoteCorrectionService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService) : IAdminFormNoteCorrectionService
{
    public async Task<AdminFormNoteCorrectionTargetDto?> GetTargetAsync(
        int noteId,
        CancellationToken cancellationToken = default)
    {
        var actor = sessionService.CurrentUser
            ?? throw new UnauthorizedAccessException("Sign in as an administrator to review this correction.");
        if (!actor.HasAdminPermissions)
            throw new UnauthorizedAccessException("Only an administrator can review this correction.");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService, cancellationToken);
        var note = await context.Notes.AsNoTracking()
            .Include(candidate => candidate.Person)
            .SingleOrDefaultAsync(candidate => candidate.Id == noteId &&
                candidate.AgencyId == actor.AgencyId &&
                candidate.Person.AgencyId == actor.AgencyId,
                cancellationToken);
        if (note is null ||
            note.Status is not (NoteStatus.Logged or NoteStatus.Approved) ||
            note.NoteType != NoteType.Form ||
            note.FormId is not int formId ||
            note.FormType is not FormType formType ||
            note.EventDate is not DateTime activityDate)
            return null;
        var ownerIsCurrent = await context.Users.AsNoTracking().AnyAsync(owner =>
            owner.Id == note.Person.UserId && owner.AgencyId == actor.AgencyId &&
            (owner.Permissions & UserPermissions.CaseManagement) != 0,
            cancellationToken);
        if (!ownerIsCurrent)
            return null;
        var form = await context.Forms.AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.Id == formId && candidate.PersonId == note.PersonId &&
            candidate.Type == formType, cancellationToken);
        if (form is null || FormWorkBillingRules.IsRelease(form.Type.ToString()))
            return null;
        if (form.CompletedDate is DateTime completedOn)
        {
            if (completedOn.Date != activityDate.Date)
                return null;
        }
        else
        {
            var history = await context.FormAttestations.AsNoTracking()
                .Where(entry => entry.FormId == form.Id)
                .OrderByDescending(entry => entry.Id).Take(2)
                .ToListAsync(cancellationToken);
            if (history.Count != 2 ||
                history[0].Kind != FormAttestationKind.Revoked ||
                history[1].Kind != FormAttestationKind.Attested ||
                history[1].CompletedOn?.Date != activityDate.Date)
                return null;
        }
        var hasClaim = await context.ClaimLines.AsNoTracking()
            .AnyAsync(line => line.NoteId == noteId, cancellationToken);
        return new AdminFormNoteCorrectionTargetDto(note.Id, note.Revision,
            activityDate.Date, note.Status.Value.ToString(), form.Id,
            form.CompletedDate?.Date, form.DueDate.Date, hasClaim);
    }

    public async Task<Note> CorrectAsync(
        int noteId,
        int expectedRevision,
        DateTime correctedActivityDate,
        string reason,
        bool attestationConfirmed,
        CancellationToken cancellationToken = default)
    {
        var actor = sessionService.CurrentUser
            ?? throw new UnauthorizedAccessException("Sign in as an administrator to correct a submitted form note.");
        if (!actor.HasAdminPermissions)
            throw new UnauthorizedAccessException("Only an administrator can correct a submitted form note.");
        if (!attestationConfirmed)
            throw new ArgumentException(
                "Confirm that the corrected date was checked against the source record of the work.",
                nameof(attestationConfirmed));
        var explanation = reason?.Trim() ?? string.Empty;
        if (expectedRevision <= 0 || correctedActivityDate == default ||
            explanation.Length is < 1 or > 4_000)
            throw new ArgumentException(
                "Provide the current note revision, actual activity date, and an explanation of at most 4,000 characters.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService, cancellationToken);
        var ownerId = await (from sourceNote in context.Notes.AsNoTracking()
                             join person in context.People.AsNoTracking() on sourceNote.PersonId equals person.Id
                             join owner in context.Users.AsNoTracking() on person.UserId equals owner.Id
                             where sourceNote.Id == noteId && sourceNote.AgencyId == actor.AgencyId &&
                                   person.AgencyId == actor.AgencyId && owner.AgencyId == actor.AgencyId &&
                                   (owner.Permissions & UserPermissions.CaseManagement) != 0
                             select (int?)owner.Id).SingleOrDefaultAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException("The note is outside this agency's caseload.");
        await using var scheduleWrite = await ServiceTimeWriteScope.BeginAsync(
            context, actor.AgencyId, ownerId, cancellationToken);
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService, cancellationToken);
        var ownerIsCurrent = await context.Users.AsNoTracking().AnyAsync(owner =>
            owner.Id == ownerId && owner.AgencyId == actor.AgencyId &&
            (owner.Permissions & UserPermissions.CaseManagement) != 0,
            cancellationToken);
        if (!ownerIsCurrent)
            throw new UnauthorizedAccessException("The note's case manager is no longer in this agency.");
        var note = await context.Notes.Include(candidate => candidate.Person)
            .SingleOrDefaultAsync(candidate => candidate.Id == noteId &&
                candidate.AgencyId == actor.AgencyId &&
                candidate.Person.AgencyId == actor.AgencyId &&
                candidate.Person.UserId == ownerId, cancellationToken)
            ?? throw new UnauthorizedAccessException("The note is outside this agency's caseload.");
        if (note.Revision != expectedRevision)
            throw new NoteConcurrencyException();
        if (note.Status is not (NoteStatus.Logged or NoteStatus.Approved) ||
            note.NoteType != NoteType.Form || note.FormId is not int formId ||
            note.FormType is not FormType formType ||
            note.EventDate is not DateTime priorActivityDate)
            throw new InvalidOperationException(
                "Only a submitted note linked to an exact form obligation can be corrected here.");
        var form = await context.Forms.Include(candidate => candidate.Person)
            .SingleOrDefaultAsync(candidate => candidate.Id == formId &&
                candidate.PersonId == note.PersonId && candidate.Type == formType,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "The selected note has no matching form obligation.");
        if (FormWorkBillingRules.IsRelease(form.Type.ToString()))
            throw new InvalidOperationException("Release notes use their own authorization rules.");
        var wasRevoked = form.CompletedDate is null;
        if (!wasRevoked && form.CompletedDate?.Date != priorActivityDate.Date)
            throw new InvalidOperationException(
                "The note and current form attestation already disagree. Reconcile their source history before changing this date.");
        if (wasRevoked)
        {
            var history = await context.FormAttestations.AsNoTracking()
                .Where(entry => entry.FormId == form.Id)
                .OrderByDescending(entry => entry.Id).Take(2)
                .ToListAsync(cancellationToken);
            if (history.Count != 2 ||
                history[0].Kind != FormAttestationKind.Revoked ||
                history[1].Kind != FormAttestationKind.Attested ||
                history[1].CompletedOn?.Date != priorActivityDate.Date)
                throw new InvalidOperationException(
                    "The revoked form's last attested date does not match the note. Reconcile the source history before changing this date.");
        }
        var correctedDate = correctedActivityDate.Date;
        if (correctedDate == priorActivityDate.Date)
            throw new InvalidOperationException("Choose a date that differs from the current activity date.");
        if (await context.ClaimLines.AsNoTracking().AnyAsync(line => line.NoteId == noteId,
                cancellationToken))
            throw new InvalidOperationException(
                "This note already has a claim record. Review that claim through the billing correction workflow before changing its source date.");

        var effectiveDate = form.Person.EffectiveDate
            ?? throw new InvalidOperationException("The client has no effective date.");
        var cycle = FormAttestationRules.ResolveCycleForForm(
            effectiveDate, form.Type.ToString(), form.DueDate,
            form.TargetEffectiveDate == default ? null : form.TargetEffectiveDate)
            ?? throw new InvalidOperationException(
                "The selected form is not attached to a valid compliance cycle.");
        var settings = await context.Settings.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.AgencyId == actor.AgencyId,
                cancellationToken) ?? new Settings();
        var availableOn = FormDueDateCalculator.ComputeAvailableDateForDueDate(
            form.Type, form.DueDate, settings);
        var facts = await context.Forms.AsNoTracking()
            .Where(candidate => candidate.PersonId == note.PersonId)
            .Select(candidate => new FormFact(
                candidate.Id, candidate.PersonId, candidate.Type.ToString(),
                candidate.DueDate, candidate.CompletedDate, candidate.TargetEffectiveDate))
            .ToListAsync(cancellationToken);
        var today = BillingRules.MaineBusinessDate(TimeProvider.System.GetUtcNow());
        var decision = FormAttestationRules.Evaluate(
            form.Type.ToString(), correctedDate, cycle.CycleStart, today,
            AttestationActorKind.Supervisor, [], facts,
            targetEffectiveDate: form.TargetEffectiveDate == default
                ? null : form.TargetEffectiveDate,
            availableOn: availableOn);
        if (!decision.Accepted)
            throw new InvalidOperationException(decision.DateError ?? string.Join(" ",
                decision.UnmetPrerequisites.Select(item => item.Message)));

        var prerequisiteState = FormAttestationRules.NoPrerequisitesStateJson;
        if (form.Type == FormType.Reclassification)
        {
            var assessment = facts.FirstOrDefault(candidate =>
                candidate.FormType == FormType.ComprehensiveAssessment.ToString() &&
                candidate.TargetEffectiveDate?.Date == form.TargetEffectiveDate.Date &&
                candidate.CompletedDate is DateTime assessmentDate &&
                assessmentDate.Date <= correctedDate);
            if (assessment is null)
                throw new InvalidOperationException(
                    "Attest the Comprehensive Assessment for this annual target before correcting the Reclassification note.");
            prerequisiteState = FormAttestationRules.AssessmentPrerequisiteStateJson(
                assessment.FormId);
        }

        note.EventDate = correctedDate;
        note.FormDateCorrectionReason = explanation;
        await EnsureServiceTimeAvailableAsync(context, ownerId, note, cancellationToken);
        var recordedAtUtc = DateTime.UtcNow;
        if (!wasRevoked)
            form.RevokeAttestation(FormAttestation.Revoked(
                AttestationActorKind.Supervisor, actor.Id, recordedAtUtc, explanation));
        form.Attest(FormAttestation.Attested(
            correctedDate, AttestationActorKind.Supervisor, actor.Id, recordedAtUtc,
            evidenceNoteId: note.Id, prerequisiteStateJson: prerequisiteState,
            reason: explanation));
        await FormService.RecordLinkedNoteImpactFlagsAsync(
            context, form, priorActivityDate, correctedDate, explanation,
            recordedAtUtc, changedNote: note);
        note.Revision++;
        if (!wasRevoked)
            LocalAuditTrail.Record(context, actor, LocalAuditActions.FormAttestationRevoked,
                "Form", form.Id, JsonSerializer.Serialize(new
                {
                    priorDate = priorActivityDate.Date.ToString("yyyy-MM-dd"),
                    noteId = note.Id
                }));
        LocalAuditTrail.Record(context, actor, LocalAuditActions.FormAttested,
            "Form", form.Id, JsonSerializer.Serialize(new
            {
                completedOn = correctedDate.ToString("yyyy-MM-dd"),
                evidenceNoteId = note.Id
            }));
        LocalAuditTrail.Record(context, actor, LocalAuditActions.NoteFormDateCorrected,
            "Note", note.Id, JsonSerializer.Serialize(new
            {
                formId = form.Id,
                previousActivityDate = priorActivityDate.Date.ToString("yyyy-MM-dd"),
                correctedActivityDate = correctedDate.ToString("yyyy-MM-dd"),
                reason = explanation,
                attestationConfirmed = true
            }));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await scheduleWrite.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new NoteConcurrencyException(exception);
        }
        return note;
    }

    private static async Task EnsureServiceTimeAvailableAsync(
        SatiContext context, int ownerId, Note note, CancellationToken cancellationToken)
    {
        var candidate = ServiceTimeline.TryCreateBlock(
            note.Id, note.StartTime, note.Minutes, note.Status?.ToString());
        if (candidate is null)
            return;
        var windowProblem = ServiceTimeline.DescribeWindowViolation(
            candidate.StartMinutes, candidate.Minutes);
        if (windowProblem is not null)
            throw new InvalidOperationException(windowProblem);
        var dayStart = note.EventDate!.Value.Date;
        var blocks = (await context.Notes.Include(existing => existing.Person)
                .Where(existing => existing.Person.UserId == ownerId &&
                    existing.Person.AgencyId == note.AgencyId &&
                    existing.AgencyId == note.AgencyId &&
                    existing.EventDate >= dayStart &&
                    existing.EventDate < dayStart.AddDays(1))
                .ToListAsync(cancellationToken))
            .Select(existing => ServiceTimeline.TryCreateBlock(
                existing.Id, existing.StartTime, existing.Minutes,
                existing.Status?.ToString(),
                $"a note for {existing.Person.FullName}"))
            .OfType<ServiceBlock>();
        var conflicts = ServiceTimeline.FindConflicts(candidate, blocks);
        if (conflicts.Count > 0)
            throw new InvalidOperationException(
                "This service time overlaps time already recorded on this date. " +
                string.Join(" ", conflicts.Select(conflict => conflict.Reason)));
    }
}
