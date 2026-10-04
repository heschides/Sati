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
    ILogger<ClaimMdSandboxPoller> logger) : BackgroundService
{
    internal async Task<int> PollOnceAsync(CancellationToken token)
    {
        if (!gate.IsRealSandboxEnabled) return 0;
        return await resetCoordination.RunAsync(
            innerToken => coordination.PollOnceAsync(PollAccountsAsync, innerToken), 0, token);
    }

    private async Task<int> PollAccountsAsync(CancellationToken token)
    {
        await using var db = await contexts.CreateDbContextAsync(token);
        var accounts = await db.ClearinghouseAccounts.AsNoTracking().Where(row =>
            row.IsEnabled && row.IsTest && row.ConnectorKind == Sati.Contracts.V1.TradingPartnerKind.ClaimMd &&
            row.SecretReference != null).OrderBy(row => row.Id).ToListAsync(token);
        var processed = 0;
        foreach (var account in accounts)
        {
            try
            {
                processed += await PollStatusAsync(account, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                // XML may contain PHI or vendor text. Never log the exception or response.
                logger.LogError("Claim.MD status polling paused for account {AccountId}; cursor was not advanced.", account.Id);
            }
            await Task.Delay(TimeSpan.FromSeconds(1), token);
            try { processed += await PollEraAsync(account, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                logger.LogError("Claim.MD ERA polling paused for account {AccountId}; cursor was not advanced.", account.Id);
            }
            await Task.Delay(TimeSpan.FromSeconds(1), token);
        }
        return processed;
    }

    private async Task<int> PollStatusAsync(ClearinghouseAccount account, CancellationToken token)
    {
        await using var db = await contexts.CreateDbContextAsync(token);
        var cursor = await db.ClearinghouseFeedCheckpoints.AsNoTracking()
            .Where(row => row.AccountId == account.Id && row.AgencyId == account.AgencyId &&
                row.FeedKind == ClearinghouseFeedKind.Status)
            .Select(row => row.Cursor).SingleOrDefaultAsync(token);
        if (!IsInitializedCursor(cursor))
        {
            logger.LogWarning("Claim.MD status feed has no reviewed starting cursor for account {AccountId}.", account.Id);
            return 0;
        }
        var page = await connector.GetStatusesAsync(account.SecretReference, cursor, token);
        if (page.Claims.Count == 0) return 0;
        await using var receiptDb = await contexts.CreateDbContextAsync(token);
        return await new ClaimMdStatusProcessor(receiptDb, protector)
            .ProcessAsync(account.Id, cursor, page, token) ? 1 : 0;
    }

    private async Task<int> PollEraAsync(ClearinghouseAccount account, CancellationToken token)
    {
        await using var db = await contexts.CreateDbContextAsync(token);
        var cursor = await db.ClearinghouseFeedCheckpoints.AsNoTracking()
            .Where(row => row.AccountId == account.Id && row.AgencyId == account.AgencyId &&
                row.FeedKind == ClearinghouseFeedKind.Era)
            .Select(row => row.Cursor).SingleOrDefaultAsync(token);
        if (!IsInitializedCursor(cursor))
        {
            logger.LogWarning("Claim.MD ERA feed has no reviewed starting cursor for account {AccountId}.", account.Id);
            return 0;
        }
        // The listing can span pages, and the API does not promise ascending page order.
        // Reading only page one before moving the cursor could permanently skip an older ERA.
        var eras = new List<ClaimMdEraEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var pageNumber = 1; pageNumber <= 20; pageNumber++)
        {
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
        var processed = 0;
        foreach (var era in eras.OrderBy(row => decimal.Parse(row.EraId,
                     System.Globalization.CultureInfo.InvariantCulture)))
        {
            if (decimal.Parse(era.EraId, System.Globalization.CultureInfo.InvariantCulture) <=
                decimal.Parse(cursor, System.Globalization.CultureInfo.InvariantCulture))
                throw new InvalidOperationException("Claim.MD ERA pagination did not advance.");
            await Task.Delay(TimeSpan.FromSeconds(1), token);
            var x12 = await connector.GetEra835Async(account.SecretReference, era.EraId, token);
            await using var scope = scopes.CreateAsyncScope();
            var intake = scope.ServiceProvider.GetRequiredService<ClaimResponseIngestion>();
            if (!await intake.ImportConnectorEraAsync(account.Id, era.EraId, cursor, x12, token)) break;
            cursor = era.EraId;
            processed++;
        }
        return processed;
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
