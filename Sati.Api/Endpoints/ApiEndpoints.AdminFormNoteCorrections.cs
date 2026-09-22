using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;

namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    private static void MapAdminFormNoteCorrections(RouteGroupBuilder api)
    {
        api.MapGet("/admin/notes/{noteId:int}/form-date-correction-target", async Task<IResult> (
            int noteId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();
            if (!await TenantAccess.IsCurrentActorAsync(db, actor, cancellationToken))
                return Results.Unauthorized();
            var row = await (from note in db.Notes.AsNoTracking()
                             join person in db.People.AsNoTracking() on note.PersonId equals person.Id
                             join owner in db.Users.AsNoTracking() on person.UserId equals owner.Id
                             where note.Id == noteId && note.AgencyId == actor.AgencyId &&
                                   person.AgencyId == actor.AgencyId &&
                                   owner.AgencyId == actor.AgencyId &&
                                   (owner.Permissions & UserPermissions.CaseManagement) != 0
                             select new { Note = note, Person = person })
                .SingleOrDefaultAsync(cancellationToken);
            if (row is null ||
                (row.Note.Status != (int)NoteStatus.Logged &&
                 row.Note.Status != (int)NoteStatus.Approved) ||
                row.Note.NoteType != (int)NoteType.Form ||
                row.Note.FormId is not int formId ||
                row.Note.FormType is not int formType ||
                row.Note.EventDate is not DateTime activityDate)
                return Results.NotFound();
            var form = await db.Forms.AsNoTracking().SingleOrDefaultAsync(candidate =>
                candidate.Id == formId && candidate.PersonId == row.Person.Id &&
                candidate.Type == ((FormType)formType).ToString(), cancellationToken);
            if (form is null || FormWorkBillingRules.IsRelease(form.Type))
                return Results.NotFound();
            if (form.CompletedDate is DateTime completedOn)
            {
                if (completedOn.Date != activityDate.Date)
                    return Results.NotFound();
            }
            else
            {
                var history = await db.FormAttestations.AsNoTracking()
                    .Where(entry => entry.FormId == form.Id)
                    .OrderByDescending(entry => entry.Id)
                    .Take(2).ToListAsync(cancellationToken);
                if (history.Count != 2 || history[0].Kind != "Revoked" ||
                    history[1].Kind != "Attested" ||
                    history[1].CompletedOn?.Date != activityDate.Date)
                    return Results.NotFound();
            }
            var hasClaim = await db.ClaimLines.AsNoTracking()
                .AnyAsync(line => line.NoteId == noteId, cancellationToken);
            return Results.Ok(new AdminFormNoteCorrectionTargetDto(
                row.Note.Id, row.Note.Revision, activityDate.Date,
                ContractMapper.NoteStatusName(row.Note.Status) ?? string.Empty, form.Id,
                form.CompletedDate?.Date, form.DueDate.Date, hasClaim));
        });

        api.MapPost("/admin/notes/{noteId:int}/correct-form-date", async Task<IResult> (
            int noteId,
            AdminCorrectFormNoteDateRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions)
                return Results.Forbid();
            if (!request.AttestationConfirmed)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["attestationConfirmed"] = ["Confirm that the corrected date was checked against the source record of the work."]
                });
            }
            var reason = request.Reason?.Trim() ?? string.Empty;
            if (request.ExpectedRevision <= 0 ||
                request.CorrectedActivityDate == default ||
                reason.Length is < 1 or > 4_000)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["correction"] = ["Provide the current note revision, actual activity date, and an explanation of at most 4,000 characters."]
                });
            }

            var ownerId = await (from note in db.Notes.AsNoTracking()
                                 join person in db.People.AsNoTracking() on note.PersonId equals person.Id
                                 join owner in db.Users.AsNoTracking() on person.UserId equals owner.Id
                                 where note.Id == noteId &&
                                       note.AgencyId == actor.AgencyId &&
                                       person.AgencyId == actor.AgencyId &&
                                       owner.AgencyId == actor.AgencyId &&
                                       (owner.Permissions & UserPermissions.CaseManagement) != 0
                                 select (int?)owner.Id).SingleOrDefaultAsync(cancellationToken);
            if (ownerId is not int caseManagerId)
                return Results.NotFound();

            await using var scheduleWrite = await ServiceTimeWriteScope.BeginAsync(
                db, actor.AgencyId, caseManagerId, cancellationToken);
            if (!await TenantAccess.IsCurrentActorAsync(db, actor, cancellationToken))
                return Results.Unauthorized();

            var row = await (from note in db.Notes
                             join person in db.People on note.PersonId equals person.Id
                             join owner in db.Users on person.UserId equals owner.Id
                             where note.Id == noteId &&
                                   note.AgencyId == actor.AgencyId &&
                                   person.AgencyId == actor.AgencyId &&
                                   owner.AgencyId == actor.AgencyId &&
                                   owner.Id == caseManagerId &&
                                   (owner.Permissions & UserPermissions.CaseManagement) != 0
                             select new ReviewableNote(note, person))
                .SingleOrDefaultAsync(cancellationToken);
            if (row is null)
                return Results.NotFound();
            if (row.Note.Revision != request.ExpectedRevision)
                return StaleNoteConflict();
            if ((row.Note.Status != (int)NoteStatus.Logged &&
                 row.Note.Status != (int)NoteStatus.Approved) ||
                row.Note.NoteType != (int)NoteType.Form ||
                row.Note.FormId is not int formId ||
                row.Note.EventDate is not DateTime priorActivityDate ||
                row.Note.FormType is not int)
            {
                return Results.Conflict(new ApiErrorDto("form_note_correction_unavailable",
                    "Only a submitted note linked to an exact form obligation can be corrected here.", string.Empty));
            }
            var form = await db.Forms.SingleOrDefaultAsync(candidate =>
                candidate.Id == formId && candidate.PersonId == row.Person.Id &&
                candidate.Type == ((FormType)row.Note.FormType.Value).ToString(),
                cancellationToken);
            if (form is null || FormWorkBillingRules.IsRelease(form.Type))
                return Results.Conflict(new ApiErrorDto("form_note_correction_unavailable",
                    "The selected note has no eligible non-release form obligation.", string.Empty));
            var wasRevoked = form.CompletedDate is null;
            if (!wasRevoked && form.CompletedDate?.Date != priorActivityDate.Date)
                return Results.Conflict(new ApiErrorDto("source_dates_disagree",
                    "The note and current form attestation already disagree. Reconcile their source history before changing this date.", string.Empty));
            if (wasRevoked)
            {
                var history = await db.FormAttestations.AsNoTracking()
                    .Where(entry => entry.FormId == form.Id)
                    .OrderByDescending(entry => entry.Id)
                    .Take(2).ToListAsync(cancellationToken);
                if (history.Count != 2 || history[0].Kind != "Revoked" ||
                    history[1].Kind != "Attested" ||
                    history[1].CompletedOn?.Date != priorActivityDate.Date)
                    return Results.Conflict(new ApiErrorDto("source_dates_disagree",
                        "The revoked form's last attested date does not match the note. Reconcile the source history before changing this date.", string.Empty));
            }
            var correctedDate = request.CorrectedActivityDate.Date;
            if (correctedDate == priorActivityDate.Date)
                return Results.Conflict(new ApiErrorDto("date_unchanged",
                    "Choose a corrected activity date that differs from the current date.", string.Empty));
            if (await db.ClaimLines.AsNoTracking().AnyAsync(line => line.NoteId == noteId,
                    cancellationToken))
                return Results.Conflict(new ApiErrorDto("claim_correction_required",
                    "This note already has a claim record. Review that claim through the billing correction workflow before changing its source date.", string.Empty));

            row.Note.EventDate = correctedDate;
            row.Note.FormDateCorrectionReason = reason;
            var timeConflict = await FindReviewServiceTimeProblemAsync(
                db, row, actor.AgencyId, cancellationToken);
            if (timeConflict is not null)
                return timeConflict;
            var attestationProblem = await AttestFormFromLoggedNoteAsync(
                db, row.Note, actor, clock, auditTrail, cancellationToken,
                allowApprovedForAdminCorrection: true);
            if (attestationProblem is not null)
                return attestationProblem;
            if (wasRevoked)
            {
                await RecordApiLinkedNoteImpactFlagsAsync(
                    db, form, actor.AgencyId, priorActivityDate.Date,
                    correctedDate, reason, clock.UtcNow.UtcDateTime,
                    cancellationToken, row.Note);
            }

            row.Note.Revision++;
            auditTrail.Record(actor, AuditActions.NoteFormDateCorrected, "Note", noteId,
                JsonSerializer.Serialize(new
                {
                    formId,
                    previousActivityDate = priorActivityDate.Date.ToString("yyyy-MM-dd"),
                    correctedActivityDate = correctedDate.ToString("yyyy-MM-dd"),
                    reason,
                    attestationConfirmed = true
                }));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await scheduleWrite.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleNoteConflict();
            }
            return Results.Ok(ContractMapper.ToNote(row.Note, row.Person));
        });
    }
}
