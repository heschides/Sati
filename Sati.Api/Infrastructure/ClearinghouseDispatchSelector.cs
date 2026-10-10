using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Models.Billing;

namespace Sati.Api.Infrastructure;

internal enum DispatchSelectionKind { Selected, Empty, Contended, LaneSkipped, Disabled }
internal sealed record DispatchSelection(DispatchSelectionKind Kind, int AgencyId = 0,
    Guid AccountId = default, Guid DispatchId = default, DateTime? NextEligibleUtc = null);

/// <summary>Shared scheduling position, disposed before authoritative send admission and external I/O.</summary>
internal sealed class ClearinghouseDispatchSelector(
    IDbContextFactory<ApiDbContext> contexts, ClearinghouseDispatchGate gate, TimeProvider clock)
{
    internal const string LockResource = "Sati.ClearinghouseDispatch.Selection";

    internal async Task<DispatchSelection> SelectAsync(CancellationToken token)
    {
        if (ExecutionStrategy.Current?.RetriesOnFailure == true)
            throw new InvalidOperationException("Dispatch selection cannot run inside a retrying execution scope.");
        token.ThrowIfCancellationRequested();
        if (!gate.IsEnabled) return new(DispatchSelectionKind.Disabled);
        await using var db = await contexts.CreateDbContextAsync(token);
        if (!db.Database.IsSqlServer() && !(db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite" && gate.IsTestingIdentity))
            throw new InvalidOperationException("Dispatch selection requires SQL Server or the explicit testing stand-in.");
        return await new SelectionSingleAttempt(db).ExecuteAsync(() => SelectOnceAsync(db, token));
    }

    private IQueryable<ClearinghouseDispatch> EligibleQueued(ApiDbContext db, DateTime now) =>
        db.ClearinghouseDispatches.Where(row => row.State == ClearinghouseDispatchState.Queued)
            .Where(row => !db.ClearinghouseDispatchReadiness.Any(readiness =>
                readiness.AgencyId == row.AgencyId && readiness.AccountId == row.AccountId &&
                readiness.Disposition != ClearinghousePreflightDisposition.Ready &&
                !(gate.IsRealSandboxEnabled && readiness.Disposition == ClearinghousePreflightDisposition.Deferred &&
                    readiness.NextEligibleAtUtc <= now)));

    private async Task<DispatchSelection> SelectOnceAsync(ApiDbContext db, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            db.Database.IsSqlServer() ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable, token);
        if (!await AcquireAsync(db, token)) return new(DispatchSelectionKind.Contended);
        token.ThrowIfCancellationRequested();
        if (!gate.IsEnabled) return new(DispatchSelectionKind.Disabled);
        var global = await db.ClearinghouseDispatchRotation.SingleOrDefaultAsync(
            row => row.Id == ClearinghouseDispatchRotation.SingletonId, token)
            ?? throw new InvalidOperationException("Dispatch rotation state is unavailable; migration review is required.");
        var now = clock.GetUtcNow().UtcDateTime;
        var eligible = EligibleQueued(db, now);
        var agencies = db.Agencies.Where(agency => agency.Id > 0 && eligible.Any(row => row.AgencyId == agency.Id));
        int? agencyId = global.LastAgencyId is { } lastAgency
            ? await agencies.Where(agency => agency.Id > lastAgency).OrderBy(agency => agency.Id)
                .Select(agency => (int?)agency.Id).FirstOrDefaultAsync(token)
            : await agencies.OrderBy(agency => agency.Id).Select(agency => (int?)agency.Id).FirstOrDefaultAsync(token);
        if (agencyId is null && global.LastAgencyId is not null)
            agencyId = await agencies.OrderBy(agency => agency.Id).Select(agency => (int?)agency.Id).FirstOrDefaultAsync(token);
        if (agencyId is null)
        {
            var realSandbox = gate.IsRealSandboxEnabled;
            var deferred = from readiness in db.ClearinghouseDispatchReadiness
                where realSandbox && readiness.Disposition == ClearinghousePreflightDisposition.Deferred &&
                    readiness.NextEligibleAtUtc > now && db.ClearinghouseDispatches.Any(row =>
                        row.AgencyId == readiness.AgencyId && row.AccountId == readiness.AccountId &&
                        row.State == ClearinghouseDispatchState.Queued)
                select readiness.NextEligibleAtUtc;
            var next = await eligible.Select(_ => (DateTime?)now).Concat(deferred)
                .OrderBy(value => value).FirstOrDefaultAsync(token);
            await VerifyOwnershipAsync(db, token);
            return new(DispatchSelectionKind.Empty, NextEligibleUtc: next);
        }

        global.LastAgencyId = agencyId;
        global.Revision = checked(global.Revision + 1);
        var lane = await db.ClearinghouseAgencyDispatchRotation.SingleOrDefaultAsync(row => row.AgencyId == agencyId, token);
        var accounts = db.ClearinghouseAccounts.Where(account => account.AgencyId == agencyId &&
            eligible.Any(row => row.AgencyId == account.AgencyId && row.AccountId == account.Id));
        Guid? accountId = lane?.LastAccountId is { } lastAccount
            ? await accounts.Where(account => account.Id.CompareTo(lastAccount) > 0).OrderBy(account => account.Id)
                .Select(account => (Guid?)account.Id).FirstOrDefaultAsync(token)
            : await accounts.OrderBy(account => account.Id).Select(account => (Guid?)account.Id).FirstOrDefaultAsync(token);
        if (accountId is null && lane?.LastAccountId is not null)
            accountId = await accounts.OrderBy(account => account.Id).Select(account => (Guid?)account.Id).FirstOrDefaultAsync(token);
        Guid? dispatchId = null;
        if (accountId is not null)
        {
            dispatchId = await eligible.Where(row => row.AgencyId == agencyId && row.AccountId == accountId)
                .OrderBy(row => row.RequestedAtUtc).ThenBy(row => row.Id)
                .Select(row => (Guid?)row.Id).FirstOrDefaultAsync(token);
            if (lane is null)
            {
                lane = new ClearinghouseAgencyDispatchRotation { AgencyId = agencyId.Value, LastAccountId = accountId };
                db.ClearinghouseAgencyDispatchRotation.Add(lane);
            }
            else { lane.LastAccountId = accountId; lane.Revision = checked(lane.Revision + 1); }
        }
        await db.SaveChangesAsync(token);
        await VerifyOwnershipAsync(db, token);
        await transaction.CommitAsync(token);
        return dispatchId is null ? new(DispatchSelectionKind.LaneSkipped) :
            new(DispatchSelectionKind.Selected, agencyId.Value, accountId!.Value, dispatchId.Value);
    }

    private static async Task<bool> AcquireAsync(ApiDbContext db, CancellationToken token)
    {
        if (!db.Database.IsSqlServer()) return true; // Explicit Testing SQLite is only a transactional stand-in.
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock " +
            "@Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0; SELECT @result;";
        command.CommandTimeout = 10;
        var resource = command.CreateParameter(); resource.ParameterName = "@resource"; resource.Value = LockResource;
        command.Parameters.Add(resource);
        if (await command.ExecuteScalarAsync(token) is not int result)
            throw new InvalidOperationException("Dispatch selection admission returned no ownership result.");
        if (result == -1) return false;
        if (result is not (0 or 1)) { token.ThrowIfCancellationRequested(); throw new InvalidOperationException("Dispatch selection admission failed."); }
        return true;
    }

    private static async Task VerifyOwnershipAsync(ApiDbContext db, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!db.Database.IsSqlServer()) return;
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "SELECT APPLOCK_MODE('public', @resource, 'Transaction');";
        command.CommandTimeout = 5;
        var resource = command.CreateParameter(); resource.ParameterName = "@resource"; resource.Value = LockResource;
        command.Parameters.Add(resource);
        if (await command.ExecuteScalarAsync(token) is not string mode || mode != "Exclusive")
            throw new InvalidOperationException("Dispatch selection ownership was lost.");
    }

    private sealed class SelectionSingleAttempt(ApiDbContext db) : ExecutionStrategy(db, 0, TimeSpan.Zero)
    { protected override bool ShouldRetryOn(Exception exception) => false; }
}
