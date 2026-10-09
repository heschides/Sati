using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Data;

namespace Sati.Api.Infrastructure;

internal static class ApiClaimReleaseHistory
{
    public static async Task<ClaimReleaseHistoryProjection> LoadAsync(ApiDbContext db, int agencyId,
        long? candidateGenerationId, CancellationToken token)
    {
        if (agencyId <= 0 || db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Claim release history requires trusted agency scope and an owned decision transaction.");
        return await LoadCoreAsync(db, agencyId, candidateGenerationId, token);
    }

    // Display is advisory. Every command repeats the projection under owned admission.
    public static Task<ClaimReleaseHistoryProjection> LoadForDisplayAsync(ApiDbContext db, int agencyId,
        CancellationToken token)
    {
        if (agencyId <= 0) throw new ArgumentOutOfRangeException(nameof(agencyId));
        return LoadCoreAsync(db, agencyId, null, token);
    }

    private static async Task<ClaimReleaseHistoryProjection> LoadCoreAsync(ApiDbContext db, int agencyId,
        long? candidateGenerationId, CancellationToken token)
    {
        var periods = await (from period in db.BillingPeriods.AsNoTracking()
            join owner in db.Users.AsNoTracking() on period.UserId equals owner.Id
            where owner.AgencyId == agencyId
            select new ClaimReleasePeriod(period.Id, owner.AgencyId)).Take(ClaimReleaseHistoryLoader.MaximumRows).ToListAsync(token);
        var periodIds = periods.Select(row => row.Id).ToList();
        var fileQuery = db.EdiGenerations.AsNoTracking().Where(row => row.AgencyId == agencyId || periodIds.Contains(row.BillingPeriodId));
        if (await fileQuery.CountAsync(token) >= ClaimReleaseHistoryLoader.MaximumRows ||
            await fileQuery.SumAsync(row => (long)row.Content.Length, token) > ClaimReleaseHistoryLoader.MaximumRetainedCharacters)
            return ClaimReleaseHistoryLoader.Incomplete();
        var files = await fileQuery.Select(row => new ClaimReleaseFile(row.Id, row.AgencyId, row.BillingPeriodId,
            row.IsTest, row.IsCorrection, row.ControlNumber, row.FileName, row.Content)).ToListAsync(token);
        var lines = await (from line in db.ClaimLines.AsNoTracking()
            join note in db.Notes.AsNoTracking() on line.NoteId equals note.Id
            join person in db.People.AsNoTracking() on note.PersonId equals person.Id
            join owner in db.Users.AsNoTracking() on person.UserId equals owner.Id
            where periodIds.Contains(line.BillingPeriodId) || note.AgencyId == agencyId
            select new ClaimReleaseLine(line.Id, line.BillingPeriodId, line.NoteId, note.AgencyId,
                person.AgencyId, owner.AgencyId, line.ChargeAmount)).Take(ClaimReleaseHistoryLoader.MaximumRows).ToListAsync(token);
        var fileIds = files.Select(row => row.Id).ToList();
        var events = await db.BillingSubmissionEvents.AsNoTracking().Where(row => row.AgencyId == agencyId ||
            row.EdiGenerationId.HasValue && fileIds.Contains(row.EdiGenerationId.Value))
            .Take(ClaimReleaseHistoryLoader.MaximumRows).Select(row => new ClaimReleaseEvent(row.AgencyId,
                row.BillingPeriodId, row.EdiGenerationId, row.Stage, row.ResponseCode, row.OccurredAtUtc)).ToListAsync(token);
        var resolutions = await db.AuditEvents.AsNoTracking().Where(row => row.AgencyId == agencyId &&
            row.Action == "billing-clearinghouse.dispatch-reconciled")
            .Take(ClaimReleaseHistoryLoader.MaximumRows).Select(row => new ClaimReleaseAudit(row.AgencyId, row.MetadataJson)).ToListAsync(token);
        return await ClaimReleaseHistoryLoader.LoadAsync(db, agencyId, files, periods, lines, events,
            resolutions, true, candidateGenerationId, token);
    }

    public static OriginalClaimReleaseDecision Evaluate(ClaimReleaseHistoryProjection history,
        int periodId, IReadOnlySet<int> noteIds, OriginalClaimReleaseOperation operation,
        long? generationId = null, Guid? dispatchId = null) => OriginalClaimReleaseRules.Evaluate(
            periodId, noteIds, operation, history.Facts, history.Defects, history.Complete, generationId, dispatchId);
}
