using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Models.Billing;

namespace Sati.Api.Infrastructure;

internal enum PollSelectionKind { Selected, Empty, Contended, LaneSkipped, Disabled }
internal sealed record PollSelection(PollSelectionKind Kind, int AgencyId = 0, Guid AccountId = default,
    Guid CheckpointId = default, ClearinghouseFeedKind FeedKind = default);

/// <summary>Short, persisted scheduling offers; receipt cursors are never written here.</summary>
internal sealed class ClearinghousePollSelector(IDbContextFactory<ApiDbContext> contexts, ClearinghouseDispatchGate gate)
{
    internal const string LockResource = "Sati.ClaimMdSandbox.PollSelection";
    internal const int MaximumOffersPerPass = 100;

    internal async Task<PollSelection> SelectAsync(IReadOnlyCollection<Guid> visited, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (visited.Count > MaximumOffersPerPass || visited.Any(x => x == Guid.Empty))
            throw new ArgumentException("Polling exclusions exceed the bounded pass.", nameof(visited));
        if (ExecutionStrategy.Current?.RetriesOnFailure == true)
            throw new InvalidOperationException("Polling selection cannot run inside a retrying execution scope.");
        if (!gate.IsRealSandboxEnabled) return new(PollSelectionKind.Disabled);
        var excluded = visited.ToArray();
        await using var db = await contexts.CreateDbContextAsync(token);
        if (!db.Database.IsSqlServer() && !(db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite" && gate.IsTestingIdentity))
            throw new InvalidOperationException("Polling selection requires SQL Server or the explicit Testing stand-in.");
        return await new SelectionSingleAttempt(db).ExecuteAsync(() => SelectOnceAsync(db, excluded, token));
    }

    private async Task<PollSelection> SelectOnceAsync(ApiDbContext db, Guid[] excluded, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            db.Database.IsSqlServer() ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable, token);
        if (!await AcquireAsync(db, token)) return new(PollSelectionKind.Contended);
        if (!gate.IsRealSandboxEnabled) return new(PollSelectionKind.Disabled);
        var global = await db.ClearinghousePollRotation.SingleOrDefaultAsync(x => x.Id == ClearinghousePollRotation.SingletonId, token)
            ?? throw new InvalidOperationException("Polling rotation is unavailable; migration review is required.");
        var feeds = from feed in db.ClearinghouseFeedCheckpoints
            join account in db.ClearinghouseAccounts on new { feed.AgencyId, Id = feed.AccountId } equals new { account.AgencyId, account.Id }
            where account.IsEnabled && account.IsTest && account.ConnectorKind == TradingPartnerKind.ClaimMd && account.SecretReference != null &&
                (feed.FeedKind == ClearinghouseFeedKind.Status || feed.FeedKind == ClearinghouseFeedKind.Era) &&
                feed.Cursor != null && feed.Cursor.Length > 0 && feed.Cursor.Length <= 20 && !excluded.Contains(feed.Id)
            select feed;
        var agencies = db.Agencies.Where(agency => agency.Id > 0 && feeds.Any(feed => feed.AgencyId == agency.Id));
        int? agencyId = global.LastAgencyId is { } lastAgency
            ? await agencies.Where(x => x.Id > lastAgency).OrderBy(x => x.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync(token)
            : await agencies.OrderBy(x => x.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync(token);
        if (agencyId is null && global.LastAgencyId is not null)
            agencyId = await agencies.OrderBy(x => x.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync(token);
        if (agencyId is null)
        { await VerifyOwnershipAsync(db, token); return new(PollSelectionKind.Empty); }

        global.LastAgencyId = agencyId; global.Revision = checked(global.Revision + 1);
        var agencyPivot = await db.ClearinghouseAgencyPollRotation.SingleOrDefaultAsync(x => x.AgencyId == agencyId, token);
        var accounts = db.ClearinghouseAccounts.Where(account => account.AgencyId == agencyId &&
            feeds.Any(feed => feed.AgencyId == account.AgencyId && feed.AccountId == account.Id));
        Guid? accountId = agencyPivot?.LastAccountId is { } lastAccount
            ? await accounts.Where(x => x.Id.CompareTo(lastAccount) > 0).OrderBy(x => x.Id).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(token)
            : await accounts.OrderBy(x => x.Id).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(token);
        if (accountId is null && agencyPivot?.LastAccountId is not null)
            accountId = await accounts.OrderBy(x => x.Id).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(token);
        PollSelection selected = new(PollSelectionKind.LaneSkipped);
        if (accountId is not null)
        {
            if (agencyPivot is null)
            {
                agencyPivot = new ClearinghouseAgencyPollRotation { AgencyId = agencyId.Value, LastAccountId = accountId };
                db.ClearinghouseAgencyPollRotation.Add(agencyPivot);
            }
            else { agencyPivot.LastAccountId = accountId; agencyPivot.Revision = checked(agencyPivot.Revision + 1); }
            var accountPivot = await db.ClearinghouseAccountPollRotation.SingleOrDefaultAsync(x =>
                x.AgencyId == agencyId && x.AccountId == accountId, token);
            var lane = feeds.Where(x => x.AgencyId == agencyId && x.AccountId == accountId);
            var checkpoint = accountPivot?.LastFeedKind is { } lastFeed
                ? await lane.Where(x => (int)x.FeedKind > (int)lastFeed).OrderBy(x => x.FeedKind)
                    .Select(x => new { x.Id, x.FeedKind }).FirstOrDefaultAsync(token)
                : await lane.OrderBy(x => x.FeedKind).Select(x => new { x.Id, x.FeedKind }).FirstOrDefaultAsync(token);
            if (checkpoint is null && accountPivot?.LastFeedKind is not null)
                checkpoint = await lane.OrderBy(x => x.FeedKind).Select(x => new { x.Id, x.FeedKind }).FirstOrDefaultAsync(token);
            if (checkpoint is not null)
            {
                if (accountPivot is null)
                    db.ClearinghouseAccountPollRotation.Add(new ClearinghouseAccountPollRotation
                    { AgencyId = agencyId.Value, AccountId = accountId.Value, LastFeedKind = checkpoint.FeedKind });
                else { accountPivot.LastFeedKind = checkpoint.FeedKind; accountPivot.Revision = checked(accountPivot.Revision + 1); }
                selected = new(PollSelectionKind.Selected, agencyId.Value, accountId.Value, checkpoint.Id, checkpoint.FeedKind);
            }
        }
        token.ThrowIfCancellationRequested();
        if (!gate.IsRealSandboxEnabled) return new(PollSelectionKind.Disabled);
        await db.SaveChangesAsync(token);
        await VerifyOwnershipAsync(db, token);
        await transaction.CommitAsync(token);
        return selected;
    }

    private static async Task<bool> AcquireAsync(ApiDbContext db, CancellationToken token)
    {
        if (!db.Database.IsSqlServer()) return true;
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock " +
            "@Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0; SELECT @result;";
        command.CommandTimeout = 10;
        var resource = command.CreateParameter(); resource.ParameterName = "@resource"; resource.Value = LockResource; command.Parameters.Add(resource);
        return OwnsLease(await command.ExecuteScalarAsync(token), token);
    }

    internal static bool OwnsLease(object? value, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (value is not int result)
            throw new InvalidOperationException("Polling selection admission returned no ownership result.");
        if (result == -1) return false;
        if (result is not (0 or 1)) { token.ThrowIfCancellationRequested(); throw new InvalidOperationException("Polling selection admission failed."); }
        return true;
    }

    private static async Task VerifyOwnershipAsync(ApiDbContext db, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!db.Database.IsSqlServer()) return;
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "SELECT APPLOCK_MODE('public', @resource, 'Transaction');"; command.CommandTimeout = 5;
        var resource = command.CreateParameter(); resource.ParameterName = "@resource"; resource.Value = LockResource; command.Parameters.Add(resource);
        if (await command.ExecuteScalarAsync(token) is not string mode || mode != "Exclusive")
            throw new InvalidOperationException("Polling selection ownership was lost.");
    }

    private sealed class SelectionSingleAttempt(ApiDbContext db) : ExecutionStrategy(db, 0, TimeSpan.Zero)
    { protected override bool ShouldRetryOn(Exception exception) => false; }
}
