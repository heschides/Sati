using Microsoft.EntityFrameworkCore;

namespace Sati.Data;

/// <summary>Transitional local EF adapter; introduces no local transport or response writer.</summary>
public static class LocalClaimReleaseHistory
{
    public static async Task<ClaimReleaseHistoryProjection> LoadAsync(SatiContext db, int agencyId,
        long? candidateGenerationId = null, CancellationToken token = default)
    {
        if (agencyId <= 0 || db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Claim release history requires trusted agency scope and an owned decision transaction.");
        var periods = await db.BillingPeriods.AsNoTracking().Where(row => row.User.AgencyId == agencyId)
            .Take(ClaimReleaseHistoryLoader.MaximumRows).Select(row => new ClaimReleasePeriod(row.Id, row.User.AgencyId)).ToListAsync(token);
        var periodIds = periods.Select(row => row.Id).ToList();
        var fileQuery = db.EdiGenerations.AsNoTracking().Where(row => row.AgencyId == agencyId || periodIds.Contains(row.BillingPeriodId));
        if (await fileQuery.CountAsync(token) >= ClaimReleaseHistoryLoader.MaximumRows ||
            await fileQuery.SumAsync(row => (long)row.Content.Length, token) > ClaimReleaseHistoryLoader.MaximumRetainedCharacters)
            return ClaimReleaseHistoryLoader.Incomplete();
        var files = await fileQuery.Select(row => new ClaimReleaseFile(row.Id, row.AgencyId, row.BillingPeriodId,
            row.IsTest, row.IsCorrection, row.ControlNumber, row.FileName, row.Content)).ToListAsync(token);
        var lines = await db.ClaimLines.AsNoTracking().Where(row => periodIds.Contains(row.BillingPeriodId) || row.Note.AgencyId == agencyId)
            .Take(ClaimReleaseHistoryLoader.MaximumRows).Select(row => new ClaimReleaseLine(row.Id, row.BillingPeriodId,
                row.NoteId, row.Note.AgencyId, row.Note.Person.AgencyId, row.Note.Person.User!.AgencyId, row.ChargeAmount)).ToListAsync(token);
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
}
