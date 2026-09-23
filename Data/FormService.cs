using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;
using System.Text.Json;

namespace Sati.Data;

public sealed class FormService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService) : IFormService
{
    public async Task UpdateFormAsync(Form form)
    {
        var actor = CurrentCaseManager();
        await using var context = await contextFactory.CreateDbContextAsync();
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
        var stored = await LoadOwnedFormAsync(context, actor, form.Id);

        if (stored.CompletedDate?.Date != form.CompletedDate?.Date)
        {
            throw new InvalidOperationException(
                "A completion date can be changed only through an attestation or revocation.");
        }

        if (stored.OpenedDate?.Date != form.OpenedDate?.Date)
        {
            throw new InvalidOperationException(
                "An opening date can be recorded only through the audited form-opening workflow.");
        }

        form.OpenedDate = stored.OpenedDate;
    }

    public Task AttestAsync(Form form, DateTime completedOn, int? evidenceNoteId = null) =>
        AttestCoreAsync(
            form,
            completedOn,
            comprehensiveAssessmentCompletedOn: null,
            evidenceNoteId,
            confirmScheduledNoteConversion: false,
            scheduledNoteConversionToken: null);

    public Task AttestAsync(
        Form form,
        DateTime completedOn,
        int? evidenceNoteId,
        bool confirmScheduledNoteConversion,
        string? scheduledNoteConversionToken = null) =>
        AttestCoreAsync(form, completedOn, null, evidenceNoteId,
            confirmScheduledNoteConversion, scheduledNoteConversionToken);

    public Task AttestAsync(
        Form form,
        DateTime completedOn,
        int? evidenceNoteId,
        string? supervisorOverrideReason)
    {
        if (!string.IsNullOrWhiteSpace(supervisorOverrideReason))
            throw new NotSupportedException("Form prerequisite overrides are no longer supported.");

        return AttestAsync(form, completedOn, evidenceNoteId);
    }

    public Task AttestReclassificationAsync(
        Form form,
        DateTime reclassificationCompletedOn,
        DateTime? comprehensiveAssessmentCompletedOn,
        int? evidenceNoteId = null)
    {
        if (form.Type != FormType.Reclassification)
            throw new ArgumentException(
                "The combined attestation operation is only valid for Reclassification.",
                nameof(form));

        return AttestCoreAsync(
            form,
            reclassificationCompletedOn,
            comprehensiveAssessmentCompletedOn,
            evidenceNoteId,
            confirmScheduledNoteConversion: false,
            scheduledNoteConversionToken: null);
    }

    public Task AttestReclassificationAsync(
        Form form,
        DateTime reclassificationCompletedOn,
        DateTime? comprehensiveAssessmentCompletedOn,
        int? evidenceNoteId,
        bool confirmScheduledNoteConversion,
        string? scheduledNoteConversionToken = null) =>
        confirmScheduledNoteConversion
            ? throw new NotSupportedException(
                "Convert a Scheduled Reclassification note in the note editor before attesting.")
            : AttestReclassificationAsync(form, reclassificationCompletedOn,
                comprehensiveAssessmentCompletedOn, evidenceNoteId);

    private async Task AttestCoreAsync(
        Form form,
        DateTime completedOn,
        DateTime? comprehensiveAssessmentCompletedOn,
        int? evidenceNoteId,
        bool confirmScheduledNoteConversion,
        string? scheduledNoteConversionToken)
    {
        var actor = CurrentCaseManager();
        await using var context = await contextFactory.CreateDbContextAsync();
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var stored = await LoadOwnedFormAsync(context, actor, form.Id);
        if (stored.CompletedDate is not null)
            throw new InvalidOperationException(
                "This form already has a live attestation. Revoke it before recording a replacement.");

        var cycle = ResolveStoredCycle(stored);
        var settings = await context.Settings.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.AgencyId == actor.AgencyId)
            ?? new Settings();
        var availableOn = FormDueDateCalculator.ComputeAvailableDateForDueDate(
            stored.Type,
            stored.DueDate,
            settings);
        var completionDateError = FormAttestationRules.ValidateCompletionDate(
            completedOn,
            cycle.CycleStart,
            DateTime.Today,
            availableOn);
        if (completionDateError is not null)
            throw new ArgumentOutOfRangeException(nameof(completedOn), completionDateError);

        var actorKind = actor.Id == stored.Person.UserId
            ? AttestationActorKind.CaseManager
            : AttestationActorKind.Supervisor;
        var formFacts = await LoadFormFactsAsync(context, stored.PersonId);
        Form? assessment = null;
        FormAttestation? impliedAssessmentAttestation = null;

        if (stored.Type == FormType.Reclassification)
        {
            assessment = await FindAssessmentForSameAnnualObligationAsync(
                context,
                stored,
                cycle);
            if (assessment is null)
            {
                throw new InvalidOperationException(
                    "The Comprehensive Assessment obligation for this annual effective date is missing. Refresh the consumer's compliance forms before attesting the Reclassification.");
            }

            if (assessment.CompletedDate is null)
            {
                if (comprehensiveAssessmentCompletedOn is not DateTime assessmentCompletedOn)
                {
                    throw new InvalidOperationException(
                        "Enter the actual Comprehensive Assessment completion date. A completed Reclassification implies that its Comprehensive Assessment was completed.");
                }

                var assessmentDateError = FormAttestationRules.ValidateAssessmentCompletionDate(
                    assessmentCompletedOn,
                    completedOn,
                    cycle.CycleStart,
                    DateTime.Today,
                    FormDueDateCalculator.ComputeAvailableDateForDueDate(
                        assessment.Type,
                        assessment.DueDate,
                        settings));
                if (assessmentDateError is not null)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(comprehensiveAssessmentCompletedOn),
                        assessmentDateError);
                }

                var assessmentEvidenceNoteId = await ResolveManualAttestationNoteAsync(
                    context, actor, assessment, cycle, assessmentCompletedOn, null,
                    confirmScheduledNoteConversion: false,
                    scheduledNoteConversionToken: null);
                int? citedAssessmentEvidenceNoteId = assessmentCompletedOn.Date <= cycle.CycleEnd.Date
                    ? assessmentEvidenceNoteId : null;
                await EnsureEvidenceIsValidAsync(context, assessment, cycle,
                    assessmentCompletedOn, citedAssessmentEvidenceNoteId);
                impliedAssessmentAttestation = FormAttestation.Attested(
                    assessmentCompletedOn,
                    actorKind,
                    actor.Id,
                    DateTime.UtcNow,
                    citedAssessmentEvidenceNoteId,
                    prerequisiteStateJson: FormAttestationRules.NoPrerequisitesStateJson);
                assessment.Attest(impliedAssessmentAttestation);
                formFacts = formFacts
                    .Where(fact => fact.FormId != assessment.Id)
                    .Append(ToFormFact(assessment))
                    .ToList();
            }
            else if (comprehensiveAssessmentCompletedOn is not null)
            {
                throw new InvalidOperationException(
                    "The Comprehensive Assessment already has an attestation. Do not enter a replacement date unless that attestation is revoked first.");
            }
        }
        else if (comprehensiveAssessmentCompletedOn is not null)
        {
            throw new ArgumentException(
                "A Comprehensive Assessment completion date can be supplied only with a Reclassification attestation.",
                nameof(comprehensiveAssessmentCompletedOn));
        }

        DateTime? targetEffectiveDate = stored.TargetEffectiveDate == default
            ? null
            : stored.TargetEffectiveDate;
        var decision = FormAttestationRules.Evaluate(
            stored.Type.ToString(),
            completedOn,
            cycle.CycleStart,
            DateTime.Today,
            actorKind,
            [],
            formFacts,
            targetEffectiveDate: targetEffectiveDate,
            availableOn: availableOn);
        if (!decision.Accepted)
        {
            if (decision.DateError is not null)
                throw new ArgumentOutOfRangeException(nameof(completedOn), decision.DateError);
            throw new InvalidOperationException(string.Join(" ",
                decision.UnmetPrerequisites.Select(prerequisite => prerequisite.Message)));
        }

        evidenceNoteId = await ResolveManualAttestationNoteAsync(
            context, actor, stored, cycle, completedOn, evidenceNoteId,
            confirmScheduledNoteConversion && stored.Type != FormType.Reclassification,
            scheduledNoteConversionToken);
        // A note on the target effective date still documents this exact form.
        // Later overdue notes retain their FormId link but are not cited by the
        // historical in-cycle evidence field.
        var citedEvidenceNoteId = completedOn.Date <= cycle.CycleEnd.Date
            ? evidenceNoteId : null;
        await EnsureEvidenceIsValidAsync(context, stored, cycle, completedOn, citedEvidenceNoteId);

        var recordedAtUtc = DateTime.UtcNow;
        var prerequisiteStateJson = assessment is null
            ? FormAttestationRules.NoPrerequisitesStateJson
            : FormAttestationRules.AssessmentPrerequisiteStateJson(assessment.Id);
        var precedingEntry = stored.Attestations.OrderByDescending(entry => entry.Id).FirstOrDefault();
        var correctionReason = precedingEntry?.Kind == FormAttestationKind.Revoked
            ? precedingEntry.Reason
            : null;
        var attestation = FormAttestation.Attested(
            completedOn,
            actorKind,
            actor.Id,
            recordedAtUtc,
            citedEvidenceNoteId,
            prerequisiteStateJson);
        stored.Attest(attestation);
        if (!string.IsNullOrWhiteSpace(correctionReason))
        {
            await RecordLinkedNoteImpactFlagsAsync(
                context, stored, previousCompletedOn: null, revisedCompletedOn: completedOn,
                correctionReason, recordedAtUtc);
        }

        if (impliedAssessmentAttestation is not null)
        {
            RecordAttestationAudit(
                context,
                actor,
                assessment!,
                actorKind,
                cycle,
                impliedAssessmentAttestation.CompletedOn!.Value,
                impliedByReclassificationFormId: stored.Id);
        }
        RecordAttestationAudit(
            context,
            actor,
            stored,
            actorKind,
            cycle,
            completedOn,
            comprehensiveAssessmentFormId: assessment?.Id);

        try
        {
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new InvalidOperationException(
                "This form's attestation changed in another session. Refresh it and try again.",
                exception);
        }

        form.Attest(FormAttestation.Attested(
            stored.CompletedDate!.Value,
            actorKind,
            actor.Id,
            recordedAtUtc,
            citedEvidenceNoteId,
            prerequisiteStateJson));
    }

    public async Task<FormPrerequisiteStatusDto> GetPrerequisiteStatusAsync(Form form)
    {
        var actor = CurrentCaseManager();
        await using var context = await contextFactory.CreateDbContextAsync();
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
        var stored = await LoadOwnedFormAsync(context, actor, form.Id);
        var prerequisite = FormAttestationRules.PrerequisiteFor(stored.Type.ToString());
        if (prerequisite == PrerequisiteKind.None)
        {
            return new FormPrerequisiteStatusDto(
                prerequisite.ToString(),
                true,
                "Attestation is sufficient; no separate document prerequisite applies.",
                [],
                CanSupervisorOverride: false);
        }

        var cycle = ResolveStoredCycle(stored);
        var assessment = await FindAssessmentForSameAnnualObligationAsync(
            context,
            stored,
            cycle);
        var isSatisfied = assessment?.CompletedDate is not null;
        return new FormPrerequisiteStatusDto(
            prerequisite.ToString(),
            isSatisfied,
            isSatisfied
                ? "The Comprehensive Assessment for this annual effective date is already attested."
                : "A completed Reclassification implies a completed Comprehensive Assessment. Enter the actual assessment completion date; Sati will save two separate attestations together.",
            [],
            CanSupervisorOverride: false);
    }

    public async Task<IReadOnlyList<FormAttestationHistoryDto>> GetAttestationHistoryAsync(Form form)
    {
        var actor = CurrentCaseManager();
        await using var context = await contextFactory.CreateDbContextAsync();
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
        var stored = await LoadOwnedFormAsync(context, actor, form.Id);
        var rows = await (
            from entry in context.FormAttestations.AsNoTracking()
            join user in context.Users.AsNoTracking()
                on entry.ActorUserId equals (int?)user.Id into actorUsers
            from user in actorUsers.DefaultIfEmpty()
            where entry.FormId == stored.Id
            orderby entry.RecordedAtUtc descending, entry.Id descending
            select new
            {
                entry.Id,
                entry.FormId,
                entry.Kind,
                entry.CompletedOn,
                entry.ActorKind,
                entry.ActorUserId,
                ActorDisplayName = user == null ? "Sati" : user.DisplayName,
                entry.RecordedAtUtc,
                entry.EvidenceNoteId,
                entry.Reason
            }).ToListAsync();

        return rows.Select(entry => new FormAttestationHistoryDto(
            entry.Id,
            entry.FormId,
            entry.Kind.ToString(),
            entry.CompletedOn,
            entry.ActorKind.ToString(),
            entry.ActorUserId,
            entry.ActorDisplayName,
            entry.RecordedAtUtc,
            entry.EvidenceNoteId,
            entry.Reason)).ToList();
    }

    public async Task<DocumentArtifactDto> RecordExternalPrerequisiteAsync(Form form, string note)
    {
        var actor = CurrentCaseManager();
        await using var context = await contextFactory.CreateDbContextAsync();
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
        await using var transaction = await context.Database.BeginTransactionAsync();
        var stored = await LoadOwnedFormAsync(context, actor, form.Id);
        var entry = AnnualDocumentCatalog.ForFormType(stored.Type.ToString())
            ?? throw new InvalidOperationException("This form does not have an external-document prerequisite.");
        var cycle = ResolveStoredCycle(stored);
        var artifact = await DocumentArtifactStore.StageExternalAsync(
            context, stored.PersonId, actor.AgencyId, entry.Kind, cycle.CycleStart,
            DateTime.UtcNow, actor.Id, note, default);
        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.DocumentRecordedExternal,
            "Person",
            stored.PersonId,
            JsonSerializer.Serialize(new
            {
                kind = entry.Kind.ToString(),
                cycleStart = cycle.CycleStart.ToString("yyyy-MM-dd")
            }));
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return DocumentArtifactStore.ToDto(artifact);
    }

    public async Task RevokeAttestationAsync(Form form, string reason)
    {
        var actor = CurrentCaseManager();
        await using var context = await contextFactory.CreateDbContextAsync();
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var stored = await LoadOwnedFormAsync(context, actor, form.Id);
        if (stored.CompletedDate is null)
            return;

        var actorKind = actor.Id == stored.Person.UserId
            ? AttestationActorKind.CaseManager
            : AttestationActorKind.Supervisor;
        var previousCompletedOn = stored.CompletedDate;
        var revocation = FormAttestation.Revoked(
            actorKind, actor.Id, DateTime.UtcNow, reason);
        stored.RevokeAttestation(revocation);
        await RecordLinkedNoteImpactFlagsAsync(
            context, stored, previousCompletedOn, revisedCompletedOn: null,
            reason, revocation.RecordedAtUtc);
        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.FormAttestationRevoked,
            "Form",
            stored.Id,
            JsonSerializer.Serialize(new
            {
                formType = stored.Type.ToString(),
                actorKind = actorKind.ToString()
            }));
        try
        {
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new InvalidOperationException(
                "This form's attestation changed in another session. Refresh it and try again.",
                exception);
        }
        form.RevokeAttestation(FormAttestation.Revoked(
            actorKind,
            actor.Id,
            revocation.RecordedAtUtc,
            reason));
    }

    public async Task OpenFormAsync(Form form)
    {
        await OpenFormAsync(form, DateTime.Today);
    }

    public async Task OpenFormAsync(Form form, DateTime openedOn)
    {
        var actor = CurrentCaseManager();
        await using var context = await contextFactory.CreateDbContextAsync();
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
        var stored = await LoadOwnedFormAsync(context, actor, form.Id);
        if (stored.OpenedDate is not null)
        {
            if (stored.OpenedDate.Value.Date == openedOn.Date)
            {
                form.OpenedDate = stored.OpenedDate;
                return;
            }

            throw new InvalidOperationException(
                "This form already has an opening date. Correcting an opening date requires an audited correction workflow.");
        }

        var settings = await context.Settings.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.AgencyId == actor.AgencyId)
            ?? new Settings { AgencyId = actor.AgencyId };
        var availableOn = FormDueDateCalculator.ComputeAvailableDateForDueDate(
            stored.Type,
            stored.DueDate,
            settings);
        var dateError = FormOpeningRules.Validate(openedOn, availableOn, DateTime.Today);
        if (dateError is not null)
            throw new ArgumentOutOfRangeException(nameof(openedOn), dateError);

        stored.OpenedDate = openedOn.Date;
        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.FormOpened,
            "Form",
            stored.Id,
            JsonSerializer.Serialize(new
            {
                formType = stored.Type.ToString(),
                targetEffectiveDate = stored.TargetEffectiveDate == default
                    ? null
                    : stored.TargetEffectiveDate.ToString("yyyy-MM-dd"),
                openedOn = openedOn.Date.ToString("yyyy-MM-dd"),
                recordedAtUtc = DateTime.UtcNow
            }));
        await context.SaveChangesAsync();
        form.OpenedDate = stored.OpenedDate;
    }

    public async Task DeleteFormsAsync(IEnumerable<Form> forms)
    {
        var actor = CurrentCaseManager();
        await using var context = await contextFactory.CreateDbContextAsync();
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
        // Even an empty request must validate the persisted actor. The session's
        // capabilities alone may be stale after an administrator changes access.
        if (!actor.HasCaseManagerPermissions ||
            !await LocalTenantAccess.IsCurrentActorAsync(context, actor))
        {
            throw new UnauthorizedAccessException("A current case manager account is required.");
        }

        ArgumentNullException.ThrowIfNull(forms);
        var ids = forms.Select(candidate => candidate.Id).Where(id => id > 0).Distinct()
            .Take(FormRetentionRules.MaximumRequestIds + 1).ToList();
        if (ids.Count > FormRetentionRules.MaximumRequestIds)
            throw new ArgumentException(FormRetentionRules.RequestLimitMessage, nameof(forms));
        if (ids.Count == 0)
            return;

        var ownedIds = await (from stored in context.Forms.AsNoTracking()
                              join person in context.People.AsNoTracking() on stored.PersonId equals person.Id
                              join owner in context.Users.AsNoTracking() on person.UserId equals owner.Id
                              where ids.Contains(stored.Id) &&
                                    person.UserId == actor.Id && person.AgencyId == actor.AgencyId &&
                                    owner.AgencyId == actor.AgencyId &&
                                    owner.Role == actor.Role && owner.Permissions == actor.Permissions
                              select stored.Id).ToListAsync();
        if (ownedIds.Count != ids.Count)
            throw new UnauthorizedAccessException("One or more forms are outside the signed-in caseload.");

        // Do not infer deletion safety from today's requirements or missing
        // attestations: removing an overdue row would erase the billing block.
        throw new InvalidOperationException(FormRetentionRules.Message);
    }

    private User CurrentCaseManager()
    {
        var actor = sessionService.CurrentUser
            ?? throw new UnauthorizedAccessException("A signed-in case manager is required.");
        if (!actor.HasCaseManagerPermissions && !actor.HasSupervisorPermissions)
            throw new UnauthorizedAccessException("A case manager or supervisor account is required.");
        return actor;
    }

    private static async Task<Form> LoadOwnedFormAsync(SatiContext context, User actor, int formId)
    {
        var form = await context.Forms
            .Include(candidate => candidate.Person)
            .Include(candidate => candidate.Attestations)
            .SingleOrDefaultAsync(candidate =>
                candidate.Id == formId &&
                candidate.Person.AgencyId == actor.AgencyId)
            ?? throw new UnauthorizedAccessException("That form is outside the signed-in agency.");
        if (!await LocalTenantAccess.CanAccessUserAsync(context, actor, form.Person.UserId))
            throw new UnauthorizedAccessException("That form is outside the signed-in caseload.");
        return form;
    }

    private static async Task<List<FormFact>> LoadFormFactsAsync(SatiContext context, int personId) =>
        await context.Forms.AsNoTracking()
            .Where(form => form.PersonId == personId)
            .Select(form => new FormFact(
                form.Id,
                form.PersonId,
                form.Type.ToString(),
                form.DueDate,
                form.CompletedDate,
                form.TargetEffectiveDate))
            .ToListAsync();

    private static FormFact ToFormFact(Form form) => new(
        form.Id,
        form.PersonId,
        form.Type.ToString(),
        form.DueDate,
        form.CompletedDate,
        form.TargetEffectiveDate);

    private static (DateTime CycleStart, DateTime CycleEnd) ResolveStoredCycle(Form form)
    {
        var effectiveDate = form.Person.EffectiveDate
            ?? throw new InvalidOperationException("The consumer has no effective date.");
        return FormAttestationRules.ResolveCycleForForm(
                effectiveDate,
                form.Type.ToString(),
                form.DueDate,
                form.TargetEffectiveDate == default ? null : form.TargetEffectiveDate)
            ?? throw new InvalidOperationException(
                "The form is not attached to a valid compliance cycle.");
    }

    private static async Task<Form?> FindAssessmentForSameAnnualObligationAsync(
        SatiContext context,
        Form reclassification,
        (DateTime CycleStart, DateTime CycleEnd) cycle)
    {
        var candidates = await context.Forms
            .Include(candidate => candidate.Attestations)
            .Where(candidate =>
                candidate.PersonId == reclassification.PersonId &&
                candidate.Type == FormType.ComprehensiveAssessment)
            .OrderByDescending(candidate => candidate.DueDate)
            .ThenByDescending(candidate => candidate.Id)
            .ToListAsync();

        if (reclassification.TargetEffectiveDate != default)
        {
            var exact = candidates.FirstOrDefault(candidate =>
                candidate.TargetEffectiveDate != default &&
                candidate.TargetEffectiveDate.Date == reclassification.TargetEffectiveDate.Date);
            if (exact is not null)
                return exact;

            // Compatibility during a rolling upgrade: only a legacy row with no
            // explicit identity may be matched by its deadline window.
            return candidates.FirstOrDefault(candidate =>
                candidate.TargetEffectiveDate == default &&
                candidate.DueDate.Date > cycle.CycleStart.Date &&
                candidate.DueDate.Date <= cycle.CycleEnd.Date);
        }

        return candidates.FirstOrDefault(candidate =>
            candidate.DueDate.Date > cycle.CycleStart.Date &&
            candidate.DueDate.Date <= cycle.CycleEnd.Date);
    }

    internal static async Task RecordLinkedNoteImpactFlagsAsync(
        SatiContext context,
        Form form,
        DateTime? previousCompletedOn,
        DateTime? revisedCompletedOn,
        string reason,
        DateTime recordedAtUtc,
        Note? changedNote = null)
    {
        // Release authorization has its own rules. A release completed after its
        // due date must not acquire the form-work billing hold.
        if (FormWorkBillingRules.IsRelease(form.Type.ToString()))
            return;

        var agencyId = form.Person.AgencyId ?? throw new InvalidOperationException("The form is not attached to an agency.");

        var linkedNotes = await context.Notes.AsNoTracking()
            .Where(note => note.FormId == form.Id &&
                           note.PersonId == form.PersonId &&
                           note.AgencyId == form.Person.AgencyId)
            .ToListAsync();
        if (changedNote is { Id: > 0 })
        {
            linkedNotes.RemoveAll(note => note.Id == changedNote.Id);
            if (changedNote.FormId == form.Id &&
                changedNote.PersonId == form.PersonId &&
                changedNote.AgencyId == agencyId)
                linkedNotes.Add(changedNote);
        }
        if (linkedNotes.Count == 0)
            return;

        var noteIds = linkedNotes.Select(note => note.Id).ToArray();
        var claimLines = await context.ClaimLines.AsNoTracking()
            .Where(line => noteIds.Contains(line.NoteId))
            .Select(line => new { line.Id, line.NoteId })
            .ToListAsync();
        var claimsByNote = claimLines.ToLookup(line => line.NoteId);

        foreach (var note in linkedNotes)
        {
            var claims = claimsByNote[note.Id].ToArray();
            var impact = FormAttestationImpactRules.Evaluate(
                (int?)note.Status, claims.Length > 0, note.EventDate,
                previousCompletedOn, revisedCompletedOn, form.DueDate);
            if (!impact.RequiresSupervisorAttention && !impact.RequiresBillingAttention)
                continue;

            var claimLineIds = claims.Length == 0
                ? new int?[] { null }
                : claims.Select(line => (int?)line.Id).ToArray();
            foreach (var claimLineId in claimLineIds)
            {
                context.FormAttestationChangeReviewFlags.Add(
                    FormAttestationChangeReviewFlag.Create(
                        agencyId, form.PersonId, note.Id, form.Id,
                        claimLineId, note.EventDate, form.DueDate,
                        previousCompletedOn, revisedCompletedOn,
                        reason, impact, recordedAtUtc));
            }
        }
    }
    private static async Task<int> ResolveManualAttestationNoteAsync(
        SatiContext context,
        User actor,
        Form form,
        (DateTime CycleStart, DateTime CycleEnd) cycle,
        DateTime completedOn,
        int? explicitEvidenceNoteId,
        bool confirmScheduledNoteConversion,
        string? scheduledNoteConversionToken)
    {
        var linked = await context.Notes
            .Where(note => note.PersonId == form.PersonId && note.FormId == form.Id &&
                           note.AgencyId == actor.AgencyId)
            .ToListAsync();
        if (linked.Count > 1)
            throw new InvalidOperationException(
                "Several notes are linked to this form. Have a supervisor review the exact evidence before attesting.");
        if (linked.Count == 1)
        {
            var note = linked[0];
            if (explicitEvidenceNoteId is int citedId && citedId != note.Id)
                throw new InvalidOperationException(
                    "The selected evidence note differs from the note already linked to this form.");
            var hasClaimLine = await context.ClaimLines.AsNoTracking()
                .AnyAsync(line => line.NoteId == note.Id);
            if (form.Type != FormType.Reclassification &&
                ManualAttestationNoteRules.CanConvertScheduledFormNote(
                    (int?)note.Status, note.NoteType?.ToString(), (int?)note.Activities,
                    hasClaimLine))
            {
                if (!confirmScheduledNoteConversion)
                    throw new ScheduledFormNoteConversionRequiredException(
                        ManualAttestationNoteRules.ScheduledConversionPrompt(
                            Person.FormDisplayName(form.Type), note.EventDate, completedOn),
                        ManualAttestationNoteRules.ScheduledConversionToken(
                            note.Id, note.Revision, note.EventDate, completedOn));

                if (scheduledNoteConversionToken !=
                    ManualAttestationNoteRules.ScheduledConversionToken(
                        note.Id, note.Revision, note.EventDate, completedOn))
                    throw new InvalidOperationException(
                        ManualAttestationNoteRules.ScheduledNoteChangedMessage);

                var plannedOn = note.EventDate;
                note.EventDate = completedOn.Date;
                note.Status = NoteStatus.Pending;
                note.Revision++;
                LocalAuditTrail.Record(context, actor, LocalAuditActions.NoteUpdated,
                    "Note", note.Id, JsonSerializer.Serialize(new
                    {
                        source = "form-attestation",
                        plannedOn = plannedOn?.ToString("yyyy-MM-dd"),
                        actualWorkDate = completedOn.Date.ToString("yyyy-MM-dd"),
                        newStatus = "Pending"
                    }));
                await context.SaveChangesAsync();
                return note.Id;
            }

            if (confirmScheduledNoteConversion)
                throw new InvalidOperationException(
                    ManualAttestationNoteRules.ScheduledNoteChangedMessage);

            var conflict = ManualAttestationNoteRules.Conflict(
                completedOn, note.EventDate, (int?)note.Status, hasClaimLine);
            if (conflict is not null)
                throw new InvalidOperationException(conflict);
            return note.Id;
        }

        if (confirmScheduledNoteConversion)
            throw new InvalidOperationException(
                ManualAttestationNoteRules.ScheduledNoteChangedMessage);

        if (explicitEvidenceNoteId is int evidenceNoteId)
            return evidenceNoteId;

        var legacyCandidateExists = await context.Notes.AsNoTracking().AnyAsync(note =>
            note.PersonId == form.PersonId && note.FormId == null &&
            note.FormType == form.Type && note.EventDate != null &&
            note.EventDate.Value.Date >= cycle.CycleStart.Date &&
            note.EventDate.Value.Date <= cycle.CycleEnd.Date);
        if (legacyCandidateExists)
            throw new InvalidOperationException(
                ManualAttestationNoteRules.UnlinkedFormNoteMessage);

        var draft = Note.Create(
            $"Draft: document completion of {Person.FormDisplayName(form.Type)}.",
            completedOn.Date, NoteStatus.Pending, null, form.PersonId,
            form.Type, NoteType.Form, form.Id);
        draft.Activities = NoteActivity.Form;
        draft.AgencyId = actor.AgencyId;
        context.Notes.Add(draft);
        LocalAuditTrail.Record(context, actor, LocalAuditActions.NoteCreated, "Note");
        await context.SaveChangesAsync();
        return draft.Id;
    }

    private static async Task EnsureEvidenceIsValidAsync(
        SatiContext context,
        Form form,
        (DateTime CycleStart, DateTime CycleEnd) cycle,
        DateTime completedOn,
        int? evidenceNoteId)
    {
        if (evidenceNoteId is not int noteId)
            return;

        var requiresExactCompletion = !FormWorkBillingRules.IsRelease(form.Type.ToString());
        var evidenceIsValid = await context.Notes.AsNoTracking().AnyAsync(note =>
            note.Id == noteId &&
            note.PersonId == form.PersonId &&
            note.FormType == form.Type &&
            (!requiresExactCompletion || note.FormId == form.Id) &&
            note.EventDate != null &&
            (!requiresExactCompletion || note.EventDate.Value.Date == completedOn.Date) &&
            note.EventDate.Value.Date >= cycle.CycleStart.Date &&
            note.EventDate.Value.Date <= cycle.CycleEnd.Date &&
            (note.Status == NoteStatus.Pending ||
             note.Status == NoteStatus.Logged ||
             note.Status == NoteStatus.Approved));
        if (!evidenceIsValid)
            throw new ArgumentException(
                "The cited note is not matching form evidence.",
                nameof(evidenceNoteId));
    }

    private static void RecordAttestationAudit(
        SatiContext context,
        User actor,
        Form form,
        AttestationActorKind actorKind,
        (DateTime CycleStart, DateTime CycleEnd) cycle,
        DateTime completedOn,
        int? comprehensiveAssessmentFormId = null,
        int? impliedByReclassificationFormId = null) =>
        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.FormAttested,
            "Form",
            form.Id,
            JsonSerializer.Serialize(new
            {
                formType = form.Type.ToString(),
                targetEffectiveDate = form.TargetEffectiveDate == default
                    ? null
                    : form.TargetEffectiveDate.ToString("yyyy-MM-dd"),
                cycleStart = cycle.CycleStart.ToString("yyyy-MM-dd"),
                completedOn = completedOn.Date.ToString("yyyy-MM-dd"),
                actorKind = actorKind.ToString(),
                comprehensiveAssessmentFormId,
                impliedByReclassificationFormId
            }));
}
