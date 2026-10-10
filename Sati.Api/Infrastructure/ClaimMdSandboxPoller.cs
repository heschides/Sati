using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Models.Billing;

namespace Sati.Api.Infrastructure;

/// <summary>Demo-only polling. Independent account/feed cursors advance with encrypted effects.</summary>
internal sealed class ClaimMdSandboxPoller(
    IDbContextFactory<ApiDbContext> contexts, ClaimMdSandboxConnector connector,
    IServiceScopeFactory scopes,
    ClearinghouseDispatchGate gate, Sati.Contracts.V1.EnvelopeProtector protector,
    IClaimMdSandboxCoordination coordination, IDemoWorkerResetCoordination resetCoordination,
    ILogger<ClaimMdSandboxPoller> logger, ClearinghousePollSelector selector) : BackgroundService
{
    internal async Task<int> PollOnceAsync(CancellationToken token)
    {
        if (!gate.IsRealSandboxEnabled) return 0;
        return await resetCoordination.RunAsync(
            innerToken => coordination.PollOnceAsync(PollAccountsAsync, innerToken), 0, token);
    }

    private async Task<int> PollAccountsAsync(CancellationToken token)
    {
        var visited = new HashSet<Guid>();
        var processed = 0;
        for (var offers = 0; offers < ClearinghousePollSelector.MaximumOffersPerPass; offers++)
        {
            token.ThrowIfCancellationRequested();
            if (!gate.IsRealSandboxEnabled) break;
            // Shared selection/discovery failure stops the pass, outside feed containment.
            var offer = await selector.SelectAsync(visited, token);
            if (offer.Kind is PollSelectionKind.Empty or PollSelectionKind.Contended or PollSelectionKind.Disabled) break;
            if (offer.Kind == PollSelectionKind.LaneSkipped) continue;
            if (!visited.Add(offer.CheckpointId)) throw new InvalidOperationException("Polling repeated a feed offer in one pass.");
            var current = await ReadFeedAsync(offer, token);
            if (current is null || !gate.IsRealSandboxEnabled || !gate.CanUseAccount(current.Value.Account)) continue;
            var (account, cursor) = current.Value;
            if (!IsInitializedCursor(cursor))
            {
                logger.LogWarning("Claim.MD feed {FeedKind} has no reviewed starting cursor for account {AccountId}.", offer.FeedKind, account.Id);
                continue;
            }
            try
            {
                processed += offer.FeedKind == ClearinghouseFeedKind.Status
                    ? await PollStatusAsync(account, cursor, token)
                    : await PollEraAsync(account, cursor, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                // XML may contain PHI or vendor text. Never log the exception or response.
                logger.LogError("Claim.MD feed {FeedKind} polling paused for account {AccountId}; cursor was not advanced.", offer.FeedKind, account.Id);
            }
            await Task.Delay(TimeSpan.FromSeconds(1), token);
        }
        return processed;
    }

    private async Task<(ClearinghouseAccount Account, string? Cursor)?> ReadFeedAsync(PollSelection offer, CancellationToken token)
    {
        await using var db = await contexts.CreateDbContextAsync(token);
        var account = await db.ClearinghouseAccounts.AsNoTracking().SingleOrDefaultAsync(row =>
            row.Id == offer.AccountId && row.AgencyId == offer.AgencyId && row.IsEnabled && row.IsTest &&
            row.ConnectorKind == Sati.Contracts.V1.TradingPartnerKind.ClaimMd, token);
        if (account is null) return null;
        var cursor = await db.ClearinghouseFeedCheckpoints.AsNoTracking()
            .Where(row => row.Id == offer.CheckpointId && row.AccountId == account.Id && row.AgencyId == account.AgencyId && row.FeedKind == offer.FeedKind)
            .Select(row => row.Cursor).SingleOrDefaultAsync(token);
        return (account, cursor);
    }

    private async Task<int> PollStatusAsync(ClearinghouseAccount account, string cursor, CancellationToken token)
    {
        if (!gate.IsRealSandboxEnabled) return 0;
        var page = await connector.GetStatusesAsync(account.SecretReference, cursor, token);
        if (page.Claims.Count == 0) return 0;
        await using var receiptDb = await contexts.CreateDbContextAsync(token);
        return await new ClaimMdStatusProcessor(receiptDb, protector)
            .ProcessAsync(account.Id, cursor, page, token) ? 1 : 0;
    }

    private async Task<int> PollEraAsync(ClearinghouseAccount account, string cursor, CancellationToken token)
    {
        // The listing can span pages, and the API does not promise ascending page order.
        // Reading only page one before moving the cursor could permanently skip an older ERA.
        var eras = new List<ClaimMdEraEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var pageNumber = 1; pageNumber <= 20; pageNumber++)
        {
            if (!gate.IsRealSandboxEnabled) return 0;
            var page = await connector.GetErasAsync(account.SecretReference, cursor, pageNumber, token);
            foreach (var era in page.Eras)
            {
                if (!seen.Add(era.EraId))
                    throw new InvalidOperationException("Claim.MD ERA listing repeated an ID across pages.");
                eras.Add(era);
            }
            if (page.Eras.Count < ClaimMdSandboxConnector.EraListPageSize) break;
            if (pageNumber == 20)
                throw new InvalidOperationException("Claim.MD ERA listing exceeds the bounded polling batch.");
        }
        // Listing order is not guaranteed. Validate the complete bounded listing, then
        // spend this feed's turn on just its oldest artifact. The next visit re-lists
        // from the committed receipt cursor; scheduling must never skip a failed ERA.
        var oldest = eras.OrderBy(row => decimal.Parse(row.EraId,
            System.Globalization.CultureInfo.InvariantCulture)).FirstOrDefault();
        if (oldest is null) return 0;
        if (decimal.Parse(oldest.EraId, System.Globalization.CultureInfo.InvariantCulture) <=
            decimal.Parse(cursor, System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("Claim.MD ERA pagination did not advance.");
        await Task.Delay(TimeSpan.FromSeconds(1), token);
        if (!gate.IsRealSandboxEnabled) return 0;
        var x12 = await connector.GetEra835Async(account.SecretReference, oldest.EraId, token);
        await using var scope = scopes.CreateAsyncScope();
        var intake = scope.ServiceProvider.GetRequiredService<ClaimResponseIngestion>();
        return await intake.ImportConnectorEraAsync(account.Id, oldest.EraId, cursor, x12, token) ? 1 : 0;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!gate.IsRealSandboxEnabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await PollOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogError("Claim.MD polling failed safely; no uncommitted cursor was advanced."); }
            try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static bool IsInitializedCursor([NotNullWhen(true)] string? cursor) =>
        cursor is { Length: > 0 and <= 20 } && cursor.All(char.IsAsciiDigit);
}
