using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

public sealed partial class ReleaseObligationService
{
    public async Task<AdminReleaseNoteCorrectionTargetDto?> GetAdminCorrectionTargetAsync(
        int noteId, CancellationToken cancellationToken = default)
    {
        var actor = RequireAdmin();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService, cancellationToken);
        var note = await context.Notes.AsNoTracking()
            .Include(item => item.Person)
            .SingleOrDefaultAsync(item => item.Id == noteId &&
                item.AgencyId == actor.AgencyId && item.Person.AgencyId == actor.AgencyId,
                cancellationToken);
        if (note is null || !IsAdminReleaseCorrectionNote(note) ||
            !await OwnerIsCurrentAsync(context, actor, note.Person.UserId, cancellationToken))
            return null;
        var release = await context.ReleaseObligations.AsNoTracking()
            .Include(item => item.Attestations)
            .SingleOrDefaultAsync(item => item.Id == note.ReleaseObligationId &&
                item.PersonId == note.PersonId && item.AgencyId == actor.AgencyId,
                cancellationToken);
        if (!IsAdminReleaseCorrectionSourceValid(note, release)) return null;
        var hasClaim = await context.ClaimLines.AsNoTracking()
            .AnyAsync(line => line.NoteId == noteId, cancellationToken);
        return new AdminReleaseNoteCorrectionTargetDto(
            note.Id, note.Revision, release!.Id,
            release.RecipientDisplayName ?? "DHHS", note.EventDate!.Value.Date,
            release.CompletedOn, release.DueOn.Date,
            note.Status!.Value.ToString(), hasClaim);
    }

    public async Task CorrectNoteDateAsAdminAsync(
        int noteId, int expectedRevision, DateTime correctedActivityDate,
        string reason, bool attestationConfirmed,
        CancellationToken cancellationToken = default)
    {
        var actor = RequireAdmin();
        var explanation = reason?.Trim() ?? string.Empty;
        if (!attestationConfirmed || expectedRevision <= 0 ||
            correctedActivityDate == default || explanation.Length is < 1 or > 500)
            throw new ArgumentException(
                "Confirm the source evidence, actual date, current note revision, and an explanation of at most 500 characters.");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService, cancellationToken);
        var ownerId = await (from sourceNote in context.Notes.AsNoTracking()
                             join person in context.People.AsNoTracking()
                                 on sourceNote.PersonId equals person.Id
                             join owner in context.Users.AsNoTracking()
                                 on person.UserId equals owner.Id
                             where sourceNote.Id == noteId &&
                                 sourceNote.AgencyId == actor.AgencyId &&
                                 person.AgencyId == actor.AgencyId &&
                                 owner.AgencyId == actor.AgencyId &&
                                 (owner.Permissions & UserPermissions.CaseManagement) != 0
                             select (int?)owner.Id).SingleOrDefaultAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException("The note is outside this agency.");
        await using var scheduleWrite = await ServiceTimeWriteScope.BeginAsync(
            context, actor.AgencyId, ownerId, cancellationToken);
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService, cancellationToken);
        if (!await OwnerIsCurrentAsync(context, actor, ownerId, cancellationToken))
            throw new UnauthorizedAccessException("The note's case manager is no longer in this agency.");
        var note = await context.Notes.Include(item => item.Person)
            .SingleOrDefaultAsync(item => item.Id == noteId &&
                item.AgencyId == actor.AgencyId && item.Person.AgencyId == actor.AgencyId &&
                item.Person.UserId == ownerId, cancellationToken)
            ?? throw new UnauthorizedAccessException("The note is outside this agency.");
        if (note.Revision != expectedRevision) throw new NoteConcurrencyException();
        if (!IsAdminReleaseCorrectionNote(note))
            throw new InvalidOperationException(
                "Only a submitted note linked to an exact release can be corrected here.");
        var release = await context.ReleaseObligations
            .Include(item => item.Attestations)
            .SingleOrDefaultAsync(item => item.Id == note.ReleaseObligationId &&
                item.PersonId == note.PersonId && item.AgencyId == actor.AgencyId,
                cancellationToken);
        if (!IsAdminReleaseCorrectionSourceValid(note, release))
            throw new InvalidOperationException(
                "The note and release history already disagree. Review the source records first.");
        var priorDate = note.EventDate!.Value.Date;
        var correctedDate = correctedActivityDate.Date;
        if (correctedDate == priorDate)
            throw new InvalidOperationException("Choose an actual date different from the recorded date.");
        if (release!.WithdrawnOn is DateTime withdrawnOn && correctedDate > withdrawnOn.Date)
            throw new InvalidOperationException(
                "The corrected completion date cannot follow the retained withdrawal date.");
        if (correctedDate < release.AvailableOn.Date || correctedDate > DateTime.Today.Date)
            throw new ArgumentOutOfRangeException(nameof(correctedActivityDate),
                $"Choose a date from {release.AvailableOn:MMM d, yyyy} through today.");
        var claimLineIds = await context.ClaimLines.AsNoTracking()
            .Where(line => line.NoteId == noteId)
            .Select(line => line.Id).ToArrayAsync(cancellationToken);
        note.EventDate = correctedDate;
        await AdminFormNoteCorrectionService.EnsureServiceTimeAvailableAsync(
            context, ownerId, note, cancellationToken);
        var recordedAtUtc = DateTime.UtcNow;
        if (release.CompletedOn is not null)
            release.RevokeManualAttestation(actor.Id, recordedAtUtc, explanation);
        release.AttestManually(correctedDate, DateTime.Today,
            AttestationActorKind.Supervisor, actor.Id, recordedAtUtc,
            explanation, note.Id);
        foreach (var claimLineId in claimLineIds.Length == 0
                     ? new int?[] { null } : claimLineIds.Select(id => (int?)id))
            context.FormAttestationChangeReviewFlags.Add(
                FormAttestationChangeReviewFlag.CreateRelease(
                    actor.AgencyId, note.PersonId, note.Id, release.Id,
                    claimLineId, note.EventDate, release.DueOn,
                    priorDate, correctedDate, explanation,
                    requiresSupervisorAttention: true, recordedAtUtc));
        note.Revision++;
        LocalAuditTrail.Record(context, actor, LocalAuditActions.NoteReleaseDateCorrected,
            "Note", note.Id, JsonSerializer.Serialize(new
            {
                releaseObligationId = release.Id,
                previousActivityDate = priorDate.ToString("yyyy-MM-dd"),
                correctedActivityDate = correctedDate.ToString("yyyy-MM-dd"),
                claimLineIds, reason = explanation, attestationConfirmed = true
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
    }

    private User RequireAdmin()
    {
        var actor = sessionService.CurrentUser
            ?? throw new UnauthorizedAccessException("Sign in as an administrator.");
        if (!actor.HasAdminPermissions)
            throw new UnauthorizedAccessException("Only an administrator can correct a submitted release note.");
        return actor;
    }

    private static Task<bool> OwnerIsCurrentAsync(
        SatiContext context, User actor, int ownerId,
        CancellationToken cancellationToken) =>
        context.Users.AsNoTracking().AnyAsync(owner =>
            owner.Id == ownerId && owner.AgencyId == actor.AgencyId &&
            (owner.Permissions & UserPermissions.CaseManagement) != 0,
            cancellationToken);

    private static bool IsAdminReleaseCorrectionNote(Note note) =>
        note.Status is NoteStatus.Logged or NoteStatus.Approved &&
        note.EventDate is not null && note.ReleaseObligationId is > 0 &&
        note.FormId is null &&
        NoteActivityRules.Has(note.Activities, note.NoteType?.ToString(), NoteActivity.Form);

    private static bool IsAdminReleaseCorrectionSourceValid(
        Note note, ReleaseObligation? release)
    {
        if (release is null || note.EventDate?.Date is not DateTime activityDate ||
            note.FormType?.ToString() != ReleaseNoteLinkRules.FormTypeName(release.Category))
            return false;
        var latest = release.Attestations.OrderByDescending(item => item.Id).FirstOrDefault();
        return latest is not null && latest.Source == ReleaseAttestationSource.Manual &&
            latest.EvidenceNoteId == note.Id && latest.CompletedOn.Date == activityDate &&
            (release.CompletedOn?.Date == activityDate ||
             release.CompletedOn is null && latest.RevokedAtUtc is not null);
    }
}
