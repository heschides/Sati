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
    private static void MapAdminReleaseNoteCorrections(RouteGroupBuilder api)
    {
        api.MapGet("/admin/notes/{noteId:int}/release-date-correction-target", async Task<IResult> (
            int noteId, ClaimsPrincipal principal, ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions) return Results.Forbid();
            if (!await TenantAccess.IsCurrentActorAsync(db, actor, cancellationToken))
                return Results.Unauthorized();
            var row = await LoadAdminReleaseNoteAsync(db, actor, noteId, cancellationToken);
            if (row is null || !IsReleaseCorrectionNote(row.Note))
                return Results.NotFound();
            var note = row.Note;
            var release = await db.ReleaseObligations.AsNoTracking()
                .Include(item => item.Attestations)
                .SingleOrDefaultAsync(item => item.Id == note.ReleaseObligationId &&
                    item.PersonId == note.PersonId && item.AgencyId == actor.AgencyId,
                    cancellationToken);
            if (!IsReleaseCorrectionSourceValid(note, release))
                return Results.NotFound();
            var hasClaim = await db.ClaimLines.AsNoTracking()
                .AnyAsync(line => line.NoteId == noteId, cancellationToken);
            return Results.Ok(new AdminReleaseNoteCorrectionTargetDto(
                note.Id, note.Revision, release!.Id,
                release.RecipientDisplayName ?? "DHHS", note.EventDate!.Value.Date,
                release.CompletedOn, release.DueOn.Date,
                ContractMapper.NoteStatusName(note.Status) ?? string.Empty, hasClaim));
        });

        api.MapPost("/admin/notes/{noteId:int}/correct-release-date", async Task<IResult> (
            int noteId, AdminCorrectReleaseNoteDateRequest request,
            ClaimsPrincipal principal, ApiDbContext db, ApiClock clock,
            AuditTrail audit, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions) return Results.Forbid();
            if (!request.AttestationConfirmed || request.ExpectedRevision <= 0 ||
                request.CorrectedActivityDate == default ||
                string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["correction"] =
                        ["Confirm the source evidence, actual activity date, current note revision, and an explanation of at most 500 characters."]
                });
            var reason = request.Reason.Trim();
            var ownerId = await (from candidateNote in db.Notes.AsNoTracking()
                                 join person in db.People.AsNoTracking()
                                     on candidateNote.PersonId equals person.Id
                                 join owner in db.Users.AsNoTracking()
                                     on person.UserId equals owner.Id
                                 where candidateNote.Id == noteId && candidateNote.AgencyId == actor.AgencyId &&
                                     person.AgencyId == actor.AgencyId &&
                                     owner.AgencyId == actor.AgencyId &&
                                     (owner.Permissions & UserPermissions.CaseManagement) != 0
                                 select (int?)owner.Id).SingleOrDefaultAsync(cancellationToken);
            if (ownerId is not int caseManagerId) return Results.NotFound();
            await using var scheduleWrite = await ServiceTimeWriteScope.BeginAsync(
                db, actor.AgencyId, caseManagerId, cancellationToken);
            if (!await TenantAccess.IsCurrentActorAsync(db, actor, cancellationToken))
                return Results.Unauthorized();
            var row = await LoadAdminReleaseNoteAsync(db, actor, noteId, cancellationToken);
            if (row is null || row.Person.UserId != caseManagerId)
                return Results.NotFound();
            var note = row.Note;
            if (note.Revision != request.ExpectedRevision) return StaleNoteConflict();
            if (!IsReleaseCorrectionNote(note))
                return Results.Conflict(new ApiErrorDto("release_note_correction_unavailable",
                    "Only a submitted note linked to an exact release can be corrected here.", string.Empty));
            var release = await db.ReleaseObligations
                .Include(item => item.Attestations)
                .SingleOrDefaultAsync(item => item.Id == note.ReleaseObligationId &&
                    item.PersonId == note.PersonId && item.AgencyId == actor.AgencyId,
                    cancellationToken);
            if (!IsReleaseCorrectionSourceValid(note, release))
                return Results.Conflict(new ApiErrorDto("release_source_dates_disagree",
                    "The note and release history already disagree. Review the source records before correcting them.", string.Empty));
            var correctedDate = request.CorrectedActivityDate.Date;
            var priorDate = note.EventDate!.Value.Date;
            if (correctedDate == priorDate)
                return Results.Conflict(new ApiErrorDto("date_unchanged",
                    "Choose the actual work date, which must differ from the recorded date.", string.Empty));
            if (release!.WithdrawnOn is DateTime withdrawnOn && correctedDate > withdrawnOn.Date)
                return Results.Conflict(new ApiErrorDto("release_withdrawal_conflict",
                    "The corrected completion date cannot follow the retained withdrawal date.", string.Empty));
            if (correctedDate < release.AvailableOn.Date || correctedDate > clock.Today.Date)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["correctedActivityDate"] =
                        [$"Choose a date from {release.AvailableOn:MMM d, yyyy} through today."]
                });
            var claimLineIds = await db.ClaimLines.AsNoTracking()
                .Where(line => line.NoteId == noteId)
                .Select(line => line.Id).ToArrayAsync(cancellationToken);
            note.EventDate = correctedDate;
            var timeConflict = await FindReviewServiceTimeProblemAsync(
                db, row, actor.AgencyId,
                cancellationToken);
            if (timeConflict is not null) return timeConflict;
            var recordedAtUtc = clock.UtcNow.UtcDateTime;
            if (release.CompletedOn is not null)
                release.RevokeManualAttestation(actor.UserId, recordedAtUtc, reason);
            release.AttestManually(correctedDate, clock.Today,
                AttestationActorKind.Supervisor, actor.UserId, recordedAtUtc,
                reason, note.Id);
            foreach (var claimLineId in claimLineIds.Length == 0
                         ? new int?[] { null } : claimLineIds.Select(id => (int?)id))
                db.FormAttestationChangeReviewFlags.Add(
                    FormAttestationChangeReviewFlag.CreateRelease(
                        actor.AgencyId, note.PersonId, note.Id, release.Id,
                        claimLineId, note.EventDate, release.DueOn,
                        priorDate, correctedDate, reason,
                        requiresSupervisorAttention: true, recordedAtUtc));
            note.Revision++;
            audit.Record(actor, AuditActions.NoteReleaseDateCorrected, "Note", noteId,
                JsonSerializer.Serialize(new
                {
                    releaseObligationId = release.Id,
                    previousActivityDate = priorDate.ToString("yyyy-MM-dd"),
                    correctedActivityDate = correctedDate.ToString("yyyy-MM-dd"),
                    claimLineIds, reason, attestationConfirmed = true
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
            return Results.Ok(ContractMapper.ToNote(note, row.Person));
        });
    }

    private static Task<ReviewableNote?> LoadAdminReleaseNoteAsync(
        ApiDbContext db, Actor actor, int noteId, CancellationToken cancellationToken) =>
        (from note in db.Notes
               join person in db.People on note.PersonId equals person.Id
               join owner in db.Users on person.UserId equals owner.Id
               where note.Id == noteId && note.AgencyId == actor.AgencyId &&
                   person.AgencyId == actor.AgencyId && owner.AgencyId == actor.AgencyId &&
                   (owner.Permissions & UserPermissions.CaseManagement) != 0
               select new ReviewableNote(note, person))
            .SingleOrDefaultAsync(cancellationToken);

    private static bool IsReleaseCorrectionNote(ServerNote note) =>
        note.Status is NoteWorkflow.Logged or NoteWorkflow.Approved &&
        note.EventDate is not null && note.ReleaseObligationId is > 0 &&
        note.FormId is null &&
        NoteActivityRules.Has(note.Activities,
            ContractMapper.NoteTypeName(note.NoteType), NoteActivity.Form);

    private static bool IsReleaseCorrectionSourceValid(
        ServerNote note, ReleaseObligation? release)
    {
        if (release is null || note.EventDate?.Date is not DateTime activityDate ||
            note.FormType != (int)Enum.Parse<FormType>(
                ReleaseNoteLinkRules.FormTypeName(release.Category)))
            return false;
        var latest = release.Attestations.OrderByDescending(item => item.Id).FirstOrDefault();
        return latest is not null && latest.Source == ReleaseAttestationSource.Manual &&
            latest.EvidenceNoteId == note.Id && latest.CompletedOn.Date == activityDate &&
            (release.CompletedOn?.Date == activityDate ||
             release.CompletedOn is null && latest.RevokedAtUtc is not null);
    }
}
