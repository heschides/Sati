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
            evidenceNoteId);

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
            evidenceNoteId);
    }

    private async Task AttestCoreAsync(
        Form form,
        DateTime completedOn,
        DateTime? comprehensiveAssessmentCompletedOn,
        int? evidenceNoteId)
    {
        var actor = CurrentCaseManager();
        await using var context = await contextFactory.CreateDbContextAsync();
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
        await using var transaction = await context.Database.BeginTransactionAsync();
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

                impliedAssessmentAttestation = FormAttestation.Attested(
                    assessmentCompletedOn,
                    actorKind,
                    actor.Id,
                    DateTime.UtcNow,
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

        await EnsureEvidenceIsValidAsync(context, stored, cycle, evidenceNoteId);

        var recordedAtUtc = DateTime.UtcNow;
        var prerequisiteStateJson = assessment is null
            ? FormAttestationRules.NoPrerequisitesStateJson
            : FormAttestationRules.AssessmentPrerequisiteStateJson(assessment.Id);
        var attestation = FormAttestation.Attested(
            completedOn,
            actorKind,
            actor.Id,
            recordedAtUtc,
            evidenceNoteId,
            prerequisiteStateJson);
        stored.Attest(attestation);

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
            evidenceNoteId,
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
        var stored = await LoadOwnedFormAsync(context, actor, form.Id);
        if (stored.CompletedDate is null)
            return;

        var actorKind = actor.Id == stored.Person.UserId
            ? AttestationActorKind.CaseManager
            : AttestationActorKind.Supervisor;
        var revocation = FormAttestation.Revoked(
            actorKind, actor.Id, DateTime.UtcNow, reason);
        stored.RevokeAttestation(revocation);
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

    private static async Task EnsureEvidenceIsValidAsync(
        SatiContext context,
        Form form,
        (DateTime CycleStart, DateTime CycleEnd) cycle,
        int? evidenceNoteId)
    {
        if (evidenceNoteId is not int noteId)
            return;

        var evidenceIsValid = await context.Notes.AsNoTracking().AnyAsync(note =>
            note.Id == noteId &&
            note.PersonId == form.PersonId &&
            note.FormType == form.Type &&
            note.EventDate != null &&
            note.EventDate.Value.Date >= cycle.CycleStart.Date &&
            note.EventDate.Value.Date < cycle.CycleEnd.Date &&
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
