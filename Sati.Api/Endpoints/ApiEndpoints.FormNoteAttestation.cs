using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    private sealed record AnnualPcpServerPlan(
        ServerForm Form,
        AnnualPcpNoteDecision Decision, FormProgressRequest? Progress = null);

    private sealed record AnnualPcpPreparation(
        SaveNoteRequest Request,
        AnnualPcpServerPlan? Plan,
        IResult? Problem);

    private static async Task<AnnualPcpPreparation> PrepareAnnualPcpNoteAsync(
        ApiDbContext db,
        Actor actor,
        SaveNoteRequest request,
        ApiClock clock,
        CancellationToken cancellationToken)
    {
        var isAnnual = request.FormProgress is not null || AnnualPcpNoteRules.IsAnnualSelection(
            request.IsAnnualPlan, request.FormType, request.FormId);
        if (!isAnnual)
        {
            return request.AnnualPcpAction == AnnualPcpProgressAction.None
                ? new(request, null, null)
                : new(request, null, FormNoteProblem(
                    "Only an Annual PCP note may advance an annual plan."));
        }

        if (request.FormId is not int formId)
            return new(request, null, FormNoteProblem(
                "Choose the Annual PCP plan year before saving."));
        var form = await db.Forms
            .Include(candidate => candidate.Attestations)
            .SingleOrDefaultAsync(candidate =>
                candidate.Id == formId &&
                candidate.PersonId == request.PersonId &&
                candidate.Type == request.FormType,
                cancellationToken);
        if (form is null)
            return new(request, null, FormNoteProblem(
                "The selected annual document no longer matches this note. Refresh the note."));

        var settings = await db.Settings.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.AgencyId == actor.AgencyId,
                cancellationToken)
            ?? new ServerSettings { AgencyId = actor.AgencyId };
        var availableOn = form.DueDate.Date.AddDays(
            -OpenDaysBefore(form.Type, settings));
        var explicitError = request.FormProgress is null ? null : !FormNoteAttestationRules.IsExactNonReleaseFormActivity(request.Activities, request.NoteType, request.FormType, request.FormId)
            ? "Document progress requires an exact Form activity." : FormProgressRules.Validate(request.FormProgress, form.Type, request.Status, availableOn, clock.Today, form.DueDate, form.TargetEffectiveDate, form.OpenedDate, form.CompletedDate);
        if (explicitError is not null) return new(request, null,
            explicitError == FormProgressRules.StaleStateMessage
                ? Results.Conflict(new ApiErrorDto("stale_form_progress", explicitError, string.Empty))
                : FormNoteProblem(explicitError));
        var decision = request.FormProgress is { } progress
            ? new AnnualPcpNoteDecision(true, false, request.EventDate!.Value.Date > form.DueDate.Date, progress.Action, string.Empty)
            : AnnualPcpNoteRules.Evaluate(
            true,
            form.Type,
            form.Id,
            request.Status,
            request.EventDate,
            availableOn,
            form.DueDate,
            form.OpenedDate,
            form.CompletedDate);
        var confirmationError = AnnualPcpNoteRules.ValidateConfirmation(
            decision, request.FormProgress?.Action ?? request.AnnualPcpAction);
        if (confirmationError is not null)
            return new(request, null, FormNoteProblem(confirmationError));

        if (decision.RequiredAction == AnnualPcpProgressAction.Open)
        {
            var dateError = FormOpeningRules.Validate(
                request.FormProgress?.OpenedOn ?? request.EventDate!.Value, availableOn, clock.Today);
            if (dateError is not null)
                return new(request, null, FormNoteProblem(dateError));
        }
        else if (decision.RequiredAction == AnnualPcpProgressAction.Complete)
        {
            var effectiveDate = await db.People.AsNoTracking()
                .Where(person => person.Id == request.PersonId &&
                                 person.AgencyId == actor.AgencyId)
                .Select(person => person.EffectiveDate)
                .SingleOrDefaultAsync(cancellationToken);
            if (effectiveDate is null)
                return new(request, null, FormNoteProblem(
                    "The client's effective date is required for annual document completion."));
            var cycle = FormAttestationRules.ResolveCycleForForm(
                effectiveDate.Value, form.Type, form.DueDate,
                form.TargetEffectiveDate == default ? null : form.TargetEffectiveDate);
            if (cycle is null)
                return new(request, null, FormNoteProblem(
                    "The selected annual document is not attached to a valid plan cycle."));
            var dateDecision = FormAttestationRules.Evaluate(
                form.Type, request.FormProgress?.CompletedOn ?? request.EventDate!.Value, cycle.Value.CycleStart,
                clock.Today, AttestationActorKind.CaseManager, [],
                [new FormFact(form.Id, form.PersonId, form.Type, form.DueDate,
                    form.CompletedDate, form.TargetEffectiveDate)],
                targetEffectiveDate: form.TargetEffectiveDate,
                availableOn: availableOn);
            if (!dateDecision.Accepted)
            {
                return new(request, null, FormNoteProblem(
                    dateDecision.DateError ?? string.Join(" ",
                        dateDecision.UnmetPrerequisites.Select(item => item.Message))));
            }
        }

        request = request with
        {
            IsAnnualPlan = form.Type == "PCP",
            IsUnbilled = request.IsUnbilled || (form.Type == "PCP" && decision.MustBeUnbilled)
        };
        return new(request, new AnnualPcpServerPlan(form, decision, request.FormProgress), null);
    }

    private static void ApplyAnnualPcpProgress(
        ApiDbContext db,
        Actor actor,
        ServerNote note,
        AnnualPcpServerPlan? plan,
        ApiClock clock,
        AuditTrail auditTrail)
    {
        if (plan is null || note.EventDate is not DateTime activityDate)
            return;
        var occurredOn = (plan.Progress?.CompletedOn ?? activityDate).Date;
        if (plan.Form.OpenedDate is null && plan.Progress?.OpenedOn is DateTime opening &&
            plan.Decision.RequiredAction == AnnualPcpProgressAction.Complete)
        {
            plan.Form.OpenedDate = opening.Date;
            auditTrail.Record(actor, AuditActions.FormOpened, "Form", plan.Form.Id,
                JsonSerializer.Serialize(new { formType = plan.Form.Type, openedOn = opening.Date.ToString("yyyy-MM-dd"), evidenceNoteId = note.Id }));
        }
        if (plan.Decision.RequiredAction == AnnualPcpProgressAction.Open)
        {
            occurredOn = (plan.Progress?.OpenedOn ?? activityDate).Date;
            plan.Form.OpenedDate = occurredOn;
            auditTrail.Record(actor, AuditActions.FormOpened, "Form", plan.Form.Id,
                JsonSerializer.Serialize(new
                {
                    formType = plan.Form.Type,
                    targetEffectiveDate = plan.Form.TargetEffectiveDate.ToString("yyyy-MM-dd"),
                    openedOn = occurredOn.ToString("yyyy-MM-dd"),
                    evidenceNoteId = note.Id
                }));
            return;
        }

        if (plan.Decision.RequiredAction != AnnualPcpProgressAction.Complete)
            return;
        plan.Form.ApplyAttestation(occurredOn);
        db.FormAttestations.Add(new ServerFormAttestation
        {
            FormId = plan.Form.Id,
            Kind = "Attested",
            CompletedOn = occurredOn,
            ActorKind = AttestationActorKind.CaseManager.ToString(),
            ActorUserId = actor.UserId,
            RecordedAtUtc = clock.UtcNow.UtcDateTime,
            EvidenceNoteId = note.Id,
            PrerequisiteStateJson = FormAttestationRules.NoPrerequisitesStateJson,
            Reason = plan.Progress is null ? "Annual PCP note confirmation." : "Explicit document progress confirmation."
        });
        auditTrail.Record(actor, AuditActions.FormAttested, "Form", plan.Form.Id,
            JsonSerializer.Serialize(new
            {
                formType = plan.Form.Type,
                targetEffectiveDate = plan.Form.TargetEffectiveDate.ToString("yyyy-MM-dd"),
                completedOn = occurredOn.ToString("yyyy-MM-dd"),
                evidenceNoteId = note.Id,
                annualPcpNote = plan.Form.Type == "PCP",
                explicitFormProgress = plan.Progress is not null
            }));
    }

    private static bool IsNonReleaseLoggedFormNote(SaveNoteRequest request) =>
        request.FormProgress is not null || FormNoteAttestationRules.AttestsExactFormOnLog(
            request.Status,
            request.Activities,
            request.NoteType,
            request.FormType,
            request.FormId,
            request.IsAnnualPlan);

    private static async Task<HashSet<int>> SafelyCancelledScheduledDuplicateNoteIdsAsync(
        ApiDbContext db,
        int agencyId,
        IEnumerable<(int Id, int? Status)> linkedNotes,
        CancellationToken cancellationToken)
    {
        var cancelledIds = linkedNotes
            .Where(note => note.Status == NoteWorkflow.Cancelled)
            .Select(note => note.Id)
            .Distinct()
            .ToArray();
        if (cancelledIds.Length == 0)
            return [];

        var noteIdsByResourceId = cancelledIds.ToDictionary(
            id => id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            id => id,
            StringComparer.Ordinal);
        var cancelledResourceIds = noteIdsByResourceId.Keys.ToArray();
        var auditedResourceIds = await db.AuditEvents.AsNoTracking()
            .Where(audit =>
                audit.AgencyId == agencyId &&
                audit.Action == ManualAttestationNoteRules.ScheduledDuplicateCancellationAuditAction &&
                audit.ResourceType == "Note" &&
                audit.ResourceId != null &&
                cancelledResourceIds.Contains(audit.ResourceId))
            .Select(audit => audit.ResourceId!)
            .Distinct()
            .ToListAsync(cancellationToken);

        return auditedResourceIds
            .Where(noteIdsByResourceId.ContainsKey)
            .Select(resourceId => noteIdsByResourceId[resourceId])
            .ToHashSet();
    }

    // Called inside the note's serializable write transaction, after the note has
    // an ID. Returning an error leaves the transaction uncommitted, so the note
    // and the form never disagree because one half of this operation succeeded.
    private static async Task<IResult?> AttestFormFromLoggedNoteAsync(
        ApiDbContext db,
        ServerNote note,
        Actor actor,
        ApiClock clock,
        AuditTrail auditTrail,
        CancellationToken cancellationToken,
        bool allowApprovedForAdminCorrection = false)
    {
        if (note.FormType is not int formType)
            return null;
        var formTypeName = ContractMapper.FormTypeName(formType);
        var exactFormActivity = FormNoteAttestationRules.IsExactNonReleaseFormActivity(
            note.Activities,
            ContractMapper.NoteTypeName(note.NoteType),
            formTypeName,
            note.FormId);
        var allowedStatus = FormNoteAttestationRules.AttestsExactFormOnLog(
                ContractMapper.NoteStatusName(note.Status),
                note.Activities,
                ContractMapper.NoteTypeName(note.NoteType),
                formTypeName,
                note.FormId) ||
            (allowApprovedForAdminCorrection && actor.HasAdminPermissions &&
             note.Status == (int)NoteStatus.Approved && exactFormActivity);
        if (!allowedStatus)
            return null;

        if (note.EventDate is not DateTime activityDate || note.FormId is not int formId)
            return FormNoteProblem("The submitted form note needs an activity date and an exact form obligation.");

        var form = await db.Forms.SingleOrDefaultAsync(candidate =>
            candidate.Id == formId && candidate.PersonId == note.PersonId &&
            candidate.Type == formTypeName, cancellationToken);
        if (form is null)
            return FormNoteProblem("The selected form obligation no longer matches this note. Refresh the note.");

        var person = await db.People.AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.Id == note.PersonId && candidate.AgencyId == actor.AgencyId,
            cancellationToken);
        if (person?.EffectiveDate is not DateTime effectiveDate)
            return FormNoteProblem("This client's effective date is needed to identify the form cycle.");

        var cycle = FormAttestationRules.ResolveCycleForForm(
            effectiveDate, form.Type, form.DueDate,
            form.TargetEffectiveDate == default ? null : form.TargetEffectiveDate);
        if (cycle is null)
            return FormNoteProblem("The selected form has no valid compliance cycle.");

        var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.AgencyId == actor.AgencyId, cancellationToken)
            ?? new ServerSettings { AgencyId = actor.AgencyId };
        var availableOn = form.DueDate.Date.AddDays(-OpenDaysBefore(form.Type, settings));
        var facts = await db.Forms.AsNoTracking()
            .Where(candidate => candidate.PersonId == note.PersonId)
            .Select(candidate => new FormFact(
                candidate.Id, candidate.PersonId, candidate.Type, candidate.DueDate,
                candidate.CompletedDate, candidate.TargetEffectiveDate))
            .ToListAsync(cancellationToken);
        var actorKind = actor.UserId == person.UserId
            ? AttestationActorKind.CaseManager
            : AttestationActorKind.Supervisor;
        var decision = FormAttestationRules.Evaluate(
            form.Type, activityDate, cycle.Value.CycleStart, clock.Today, actorKind,
            [], facts, targetEffectiveDate: form.TargetEffectiveDate == default
                ? null : form.TargetEffectiveDate, availableOn: availableOn);
        if (!decision.Accepted)
        {
            var message = decision.DateError ?? string.Join(" ",
                decision.UnmetPrerequisites.Select(item => item.Message));
            return FormNoteProblem(message);
        }

        var prerequisiteState = FormAttestationRules.NoPrerequisitesStateJson;
        if (form.Type == "Reclassification")
        {
            var assessment = await FindAssessmentForAnnualTargetAsync(
                db, note.PersonId, form, cycle.Value, cancellationToken);
            if (assessment?.CompletedDate is not DateTime assessmentDate ||
                assessmentDate.Date > activityDate.Date)
                return FormNoteProblem(
                    "Attest the Comprehensive Assessment for this annual target before logging the Reclassification note.");
            prerequisiteState = FormAttestationRules.AssessmentPrerequisiteStateJson(
                assessment.Id);
        }

        var previousDate = form.CompletedDate?.Date;
        var newDate = activityDate.Date;
        if (previousDate is DateTime previous && previous != newDate &&
            string.IsNullOrWhiteSpace(note.FormDateCorrectionReason))
            return FormNoteProblem(
                "Explain why this form's previously attested completion date is being corrected.");

        if (previousDate == newDate)
        {
            // Record the note as direct evidence even when a matching attestation
            // was already entered by hand or seeded earlier. The date is unchanged.
            var currentEvidence = await db.FormAttestations.AsNoTracking()
                .Where(entry => entry.FormId == form.Id && entry.Kind == "Attested")
                .OrderByDescending(entry => entry.Id)
                .Select(entry => entry.EvidenceNoteId)
                .FirstOrDefaultAsync(cancellationToken);
            if (currentEvidence == note.Id)
                return null;
        }

        var recordedAtUtc = clock.UtcNow.UtcDateTime;
        if (previousDate is DateTime oldDate && oldDate != newDate)
        {
            db.FormAttestations.Add(new ServerFormAttestation
            {
                FormId = form.Id,
                Kind = "Revoked",
                ActorKind = actorKind.ToString(),
                ActorUserId = actor.UserId,
                RecordedAtUtc = recordedAtUtc,
                Reason = note.FormDateCorrectionReason!.Trim()
            });
            auditTrail.Record(actor, AuditActions.FormAttestationRevoked, "Form", form.Id,
                JsonSerializer.Serialize(new { priorDate = oldDate.ToString("yyyy-MM-dd"),
                    reason = note.FormDateCorrectionReason!.Trim(), noteId = note.Id }));
            await RecordApiLinkedNoteImpactFlagsAsync(db, form, actor.AgencyId,
                oldDate, newDate, note.FormDateCorrectionReason!, recordedAtUtc,
                cancellationToken, note);
        }

        form.ApplyAttestation(newDate);
        db.FormAttestations.Add(new ServerFormAttestation
        {
            FormId = form.Id,
            Kind = "Attested",
            CompletedOn = newDate,
            ActorKind = actorKind.ToString(),
            ActorUserId = actor.UserId,
            RecordedAtUtc = recordedAtUtc,
            EvidenceNoteId = note.Id,
            PrerequisiteStateJson = prerequisiteState
        });
        auditTrail.Record(actor, AuditActions.FormAttested, "Form", form.Id,
            JsonSerializer.Serialize(new { formType = form.Type,
                completedOn = newDate.ToString("yyyy-MM-dd"), evidenceNoteId = note.Id,
                priorDate = previousDate?.ToString("yyyy-MM-dd"),
                correctionReason = note.FormDateCorrectionReason }));
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    private static IResult FormNoteProblem(string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["formId"] = [message]
        }, statusCode: StatusCodes.Status422UnprocessableEntity);

    private static async Task RecordApiLinkedNoteImpactFlagsAsync(
        ApiDbContext db,
        ServerForm form,
        int agencyId,
        DateTime? previousCompletedOn,
        DateTime? revisedCompletedOn,
        string reason,
        DateTime recordedAtUtc,
        CancellationToken cancellationToken,
        ServerNote? changedNote = null)
    {
        if (FormWorkBillingRules.IsRelease(form.Type))
            return;

        var notes = await db.Notes.AsNoTracking()
            .Where(note => note.FormId == form.Id &&
                           note.PersonId == form.PersonId &&
                           note.AgencyId == agencyId)
            .ToListAsync(cancellationToken);
        if (changedNote is { Id: > 0 })
        {
            notes.RemoveAll(note => note.Id == changedNote.Id);
            if (changedNote.FormId == form.Id &&
                changedNote.PersonId == form.PersonId &&
                changedNote.AgencyId == agencyId)
                notes.Add(changedNote);
        }

        if (notes.Count == 0)
            return;
        var noteIds = notes.Select(note => note.Id).ToArray();
        var claims = await db.ClaimLines.AsNoTracking()
            .Where(line => noteIds.Contains(line.NoteId))
            .Select(line => new { line.Id, line.NoteId })
            .ToListAsync(cancellationToken);
        var claimsByNote = claims.ToLookup(line => line.NoteId);

        foreach (var note in notes)
        {
            var claimIds = claimsByNote[note.Id].Select(line => (int?)line.Id).ToArray();
            var impact = FormAttestationImpactRules.Evaluate(
                note.Status, claimIds.Length > 0, note.EventDate,
                previousCompletedOn, revisedCompletedOn, form.DueDate);
            if (!impact.RequiresSupervisorAttention && !impact.RequiresBillingAttention)
                continue;

            foreach (var claimLineId in claimIds.Length == 0
                         ? new int?[] { null } : claimIds)
            {
                db.FormAttestationChangeReviewFlags.Add(
                    FormAttestationChangeReviewFlag.Create(
                        agencyId, form.PersonId, note.Id, form.Id,
                        claimLineId, note.EventDate, form.DueDate,
                        previousCompletedOn, revisedCompletedOn, reason,
                        impact, recordedAtUtc));
            }
        }
    }
}
