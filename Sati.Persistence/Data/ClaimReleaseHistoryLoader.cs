using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models.Billing;

namespace Sati.Data;

/// <summary>Common relational history reads after adapters have established the agency and source scope.</summary>
public static class ClaimReleaseHistoryLoader
{
    public const int MaximumRows = 10000;
    public const long MaximumRetainedCharacters = 16 * 1024 * 1024;
    public static ClaimReleaseHistoryProjection Incomplete() => new([], [], [], false);

    public static async Task<ClaimReleaseHistoryProjection> LoadAsync(DbContext db, int agencyId,
        IReadOnlyList<ClaimReleaseFile> files, IReadOnlyList<ClaimReleasePeriod> periods,
        IReadOnlyList<ClaimReleaseLine> lines, IReadOnlyList<ClaimReleaseEvent> events,
        IReadOnlyList<ClaimReleaseAudit> resolutions, bool complete, long? candidateGenerationId,
        CancellationToken token = default)
    {
        var fileIds = files.Select(row => row.Id).ToList();
        var periodIds = periods.Select(row => row.Id).ToList();
        var accounts = await db.Set<ClearinghouseAccount>().AsNoTracking()
            .Where(row => row.AgencyId == agencyId).Take(MaximumRows)
            .Select(row => new ClearinghouseAccount { Id = row.Id, AgencyId = row.AgencyId, ConnectorKind = row.ConnectorKind,
                IsTest = row.IsTest, ExternalAccountNumber = row.ExternalAccountNumber, ClaimNamespace = row.ClaimNamespace,
                TradingPartnerProfileVersion = row.TradingPartnerProfileVersion }).ToListAsync(token);
        var dispatches = await db.Set<ClearinghouseDispatch>().AsNoTracking()
            .Where(row => row.AgencyId == agencyId || fileIds.Contains(row.EdiGenerationId))
            .Take(MaximumRows).ToListAsync(token);
        var dispatchIds = dispatches.Select(row => row.Id).ToList();
        // The encrypted provider response is deliberately outside this projection.
        var attempts = await db.Set<ClearinghouseDispatchAttempt>().AsNoTracking()
            .Where(row => dispatchIds.Contains(row.DispatchId)).Take(MaximumRows)
            .Select(row => new ClearinghouseDispatchAttempt { Id = row.Id, DispatchId = row.DispatchId,
                AttemptNumber = row.AttemptNumber, FileName = row.FileName, ContentSha256 = row.ContentSha256,
                Outcome = row.Outcome, StartedAtUtc = row.StartedAtUtc, CompletedAtUtc = row.CompletedAtUtc }).ToListAsync(token);
        var corrections = await db.Set<ClaimCorrection>().AsNoTracking()
            .Where(row => row.AgencyId == agencyId || periodIds.Contains(row.BillingPeriodId))
            .Take(MaximumRows).ToListAsync(token);
        var correctionIds = corrections.Select(row => row.Id).ToList();
        var links = await db.Set<ClaimCorrectionSubmission>().AsNoTracking()
            .Where(row => fileIds.Contains(row.EdiGenerationId) || correctionIds.Contains(row.ClaimCorrectionId))
            .Take(MaximumRows).ToListAsync(token);
        var receiptQuery = db.Set<ClearinghouseResponseReceipt>().AsNoTracking()
            .Where(row => row.AgencyId == agencyId || row.Matches.Any(match => fileIds.Contains(match.EdiGenerationId)));
        if (await db.Set<ClearinghouseResponseMatch>().CountAsync(match => receiptQuery.Any(receipt => receipt.Id == match.ResponseId), token) >= MaximumRows)
            return Incomplete();
        var receipts = await receiptQuery
            .Take(MaximumRows).Select(row => new ClearinghouseResponseReceipt
            {
                Id = row.Id, AgencyId = row.AgencyId, Source = row.Source, AccountId = row.AccountId,
                ConnectorKind = row.ConnectorKind, FeedKind = row.FeedKind, Kind = row.Kind, IsTest = row.IsTest,
                Matches = row.Matches.ToList()
            }).ToListAsync(token);
        complete &= files.Count < MaximumRows && periods.Count < MaximumRows && lines.Count < MaximumRows &&
            events.Count < MaximumRows && resolutions.Count < MaximumRows && accounts.Count < MaximumRows &&
            dispatches.Count < MaximumRows && attempts.Count < MaximumRows && corrections.Count < MaximumRows &&
            links.Count < MaximumRows && receipts.Count < MaximumRows &&
            receipts.Sum(row => (long)row.Matches.Count) < MaximumRows;
        return OriginalClaimReleaseHistory.Project(agencyId, files, periods, lines, accounts, dispatches,
            attempts, corrections, links, receipts, events, resolutions, complete, candidateGenerationId);
    }
}
