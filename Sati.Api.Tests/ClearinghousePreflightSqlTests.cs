using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Sati.Models.Billing;
using Sati.Persistence.Migrations;
using Xunit;

namespace Sati.Api.Tests;

public sealed partial class ClearinghouseDispatchApiTests
{
    [SqlServerFact]
    public async Task ClaimReleaseSqlLostAccountLeaseIsDetectedAndCancellationReleasesOwnership()
    {
        await using var database = new SyntheticPipelineDatabase(sqlServer: true);
        await database.InitializeAsync();
        var factory = new PreflightLeaseFactory(database);
        var coordination = new SqlDemoWorkerResetCoordination(factory,
            Options.Create(new SatiApiOptions { ExpectedEnvironment = "Demo", ExpectedDatabaseName = "SatiDemo" }));
        var accountId = Guid.NewGuid();
        await coordination.RunAccountPreflightAsync(1, accountId, async (lease, token) =>
        {
            await lease.VerifyAsync(token);
            await factory.Last!.Database.ExecuteSqlInterpolatedAsync($"EXEC sys.sp_releaseapplock @Resource = {"Sati.ClearinghousePreflight:1:" + accountId.ToString("N")}, @LockOwner = 'Session';");
            try { await Assert.ThrowsAsync<InvalidOperationException>(() => lease.VerifyAsync(token)); }
            finally
            {
                // Reacquire even on a failed assertion so cleanup cannot mask the proof.
                await factory.Last.Database.ExecuteSqlInterpolatedAsync($"EXEC sys.sp_getapplock @Resource = {"Sati.ClearinghousePreflight:1:" + accountId.ToString("N")}, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0;");
            }
            return true;
        }, false, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordination.RunAccountPreflightAsync<bool>(1, accountId,
            (_, token) => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult(true); }, false, cancellation.Token));
        Assert.True(await coordination.RunAccountPreflightAsync(1, accountId,
            async (lease, token) => { await lease.VerifyAsync(token); return true; }, false, CancellationToken.None));
    }

    private sealed class PreflightLeaseFactory(SyntheticPipelineDatabase database) : IDbContextFactory<ApiDbContext>
    {
        public ApiDbContext? Last { get; private set; }
        public ApiDbContext CreateDbContext() => Last = new ApiDbContext(database.Options());
    }

    [SqlServerFact]
    public async Task ClaimReleaseSqlAccountPreflightExcludesSameAccountButNotOtherAccountsOrDecisionLocks()
    {
        var keys = new ControlledPreflightKeys { Available = true }; var wrapper = new ControlledPreflightWrapper();
        await using var fixture = await CreatePreflightFixtureAsync(keys, wrapper, sqlServer: true);
        var first = SqlPreflightCoordination(fixture.Factory);
        await using var secondHost = new SyntheticPipelineFactory(fixture.Database, fixture.Factory.Vault)
            { EnableSyntheticDispatch = true, DisableDispatchWorker = true };
        var second = SqlPreflightCoordination(secondHost);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wrapper.BeforeWrap = async () =>
        {
            Assert.False(await fixture.Database.HasGrantedClaimReleaseLockAsync());
            entered.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(30));
        };
        var connector = new IsolationRecordingConnector();
        var work = CreateIsolationWorker(fixture.Factory, keys, connector, coordination: first).ProcessOneAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            var sameEntered = false;
            Assert.False(await second.RunAccountPreflightAsync(fixture.Actors.AgencyId, fixture.AccountId,
                (_, _) => { sameEntered = true; return Task.FromResult(true); }, false, CancellationToken.None));
            Assert.False(sameEntered);
            Assert.True(await second.RunAccountPreflightAsync(fixture.Actors.AgencyId, Guid.NewGuid(),
                async (lease, token) => { await lease.VerifyAsync(token); return true; }, false, CancellationToken.None));
            Assert.False(await CreateIsolationWorker(secondHost, keys, connector, coordination: second).ProcessOneAsync(CancellationToken.None));
        }
        finally { release.TrySetResult(); }
        Assert.True(await work); Assert.Single(connector.Dispatches);
        Assert.True(await second.RunAccountPreflightAsync(fixture.Actors.AgencyId, fixture.AccountId,
            async (lease, token) => { await lease.VerifyAsync(token); return true; }, false, CancellationToken.None));
    }

    [SqlServerFact]
    public async Task ClaimReleaseSqlReadinessUpgradeConstraintsAndUnresolvedRollbackGuardPreserveWork()
    {
        await using var fixture = await Fixture.CreateAsync(sqlServer: true);
        await fixture.GenerateAsync(fixture.AccountId);
        await using var db = fixture.Factory.OpenDatabase();
        var migration = new AddClearinghousePreflightReadiness { ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer" };
        var generator = db.GetService<IMigrationsSqlGenerator>();
        async Task Apply(IReadOnlyList<MigrationOperation> operations)
        { foreach (var command in generator.Generate(operations, db.Model)) await db.Database.ExecuteSqlRawAsync(command.CommandText); }
        var bytes = (await db.EdiGenerations.SingleAsync()).Content;
        await Apply(migration.DownOperations); await Apply(migration.UpOperations);
        Assert.Empty(await db.ClearinghouseDispatchReadiness.ToListAsync());
        Assert.Equal(bytes, (await db.EdiGenerations.SingleAsync()).Content);
        async Task BadInsert(int agencyId, int disposition, int count, string? code, DateTime? due)
        {
            var failure = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ClearinghouseDispatchReadiness (AgencyId, AccountId, Disposition, FailureCount, RecoveryCycleId, NextEligibleAtUtc, LastFailureAtUtc, SafeFailureCode, ValidatedAccountRevision, Revision) VALUES ({agencyId}, {fixture.AccountId}, {disposition}, {count}, {Guid.NewGuid()}, {due}, {DateTime.UtcNow}, {code}, {0L}, {1L})"));
            Assert.Equal(547, failure.Number);
        }
        await BadInsert(fixture.Actors.AgencyId, 2, 1, null, DateTime.UtcNow.AddMinutes(1));
        await BadInsert(fixture.Actors.AgencyId, 2, 5, "account_key_unavailable", DateTime.UtcNow.AddMinutes(1));
        await BadInsert(fixture.Actors.AgencyId + 1000, 2, 1, "account_key_unavailable", DateTime.UtcNow.AddMinutes(1));
        var row = new ClearinghouseDispatchReadiness { AgencyId = fixture.Actors.AgencyId, AccountId = fixture.AccountId };
        row.Apply(ClearinghousePreflightRules.MissingKey(fixture.AccountId, null, Guid.NewGuid(), DateTime.UtcNow), 0);
        db.ClearinghouseDispatchReadiness.Add(row); await db.SaveChangesAsync();
        var rollback = await Assert.ThrowsAsync<SqlException>(() => Apply(migration.DownOperations));
        Assert.Equal(51044, rollback.Number);
        Assert.Single(await db.ClearinghouseDispatchReadiness.ToListAsync());
        Assert.Equal(bytes, (await db.EdiGenerations.SingleAsync()).Content);
        row.Revision++; // A valid state cannot be silently deleted through either context.
        db.ClearinghouseDispatchReadiness.Remove(row);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
}
