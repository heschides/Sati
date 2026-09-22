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
    private static bool IsNonReleaseLoggedFormNote(SaveNoteRequest request) =>
        string.Equals(request.Status, "Logged", StringComparison.Ordinal) &&
        string.Equals(request.NoteType, "Form", StringComparison.Ordinal) &&
        request.FormId is > 0 &&
        !FormNoteLinkRules.IsRelease(request.FormType);

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
        var allowedStatus = note.Status == (int)NoteStatus.Logged ||
            (allowApprovedForAdminCorrection && actor.HasAdminPermissions &&
             note.Status == (int)NoteStatus.Approved);
        if (!allowedStatus ||
            note.NoteType != (int)NoteType.Form ||
            note.FormType is not int formType ||
            FormNoteLinkRules.IsRelease(((FormType)formType).ToString()))
            return null;

        if (note.EventDate is not DateTime activityDate || note.FormId is not int formId)
            return FormNoteProblem("The submitted form note needs an activity date and an exact form obligation.");

        var form = await db.Forms.SingleOrDefaultAsync(candidate =>
            candidate.Id == formId && candidate.PersonId == note.PersonId &&
            candidate.Type == ((FormType)formType).ToString(), cancellationToken);
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
