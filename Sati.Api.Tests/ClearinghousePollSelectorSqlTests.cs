using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Models.Billing;
using Sati.Persistence.Migrations;
using Xunit;

namespace Sati.Api.Tests;

public sealed partial class ClearinghousePollSelectorTests
{
    [Fact]
    public Task PollingAccountReplacementRetainsAgencyPositionAndStartsOnlyNewFeed() => ReplacementAsync(false);
    [SqlServerFact]
    public Task ClaimReleaseSqlPollingAccountReplacementRetainsAgencyPositionAndStartsOnlyNewFeed() => ReplacementAsync(true);

    private static async Task ReplacementAsync(bool sql)
    {
        await using var fixture = await PollFixture.CreateAsync(sql);
        var first = await fixture.Selector().SelectAsync([], CancellationToken.None);
        Guid replacement;
        await using (var db = fixture.Open())
        {
            var old = await db.ClearinghouseAccounts.SingleAsync(x => x.AgencyId == fixture.A && x.IsEnabled);
            old.IsEnabled = false; old.Revision++; await db.SaveChangesAsync();
            var next = await db.ClearinghouseAccounts.Where(x => x.AgencyId == fixture.A && x.Id != old.Id).OrderBy(x => x.Id).FirstAsync();
            next.IsEnabled = true; next.Revision++; await db.SaveChangesAsync(); replacement = next.Id;
        }
        Assert.Equal(fixture.B, (await fixture.Selector().SelectAsync([], CancellationToken.None)).AgencyId);
        var offered = await fixture.Selector().SelectAsync([], CancellationToken.None);
        Assert.Equal(fixture.A, offered.AgencyId); Assert.Equal(replacement, offered.AccountId);
        Assert.Equal(ClearinghouseFeedKind.Status, offered.FeedKind); Assert.NotEqual(first.CheckpointId, offered.CheckpointId);
        await using var retained = fixture.Open();
        Assert.Equal(2, await retained.ClearinghouseAccountPollRotation.CountAsync(x => x.AgencyId == fixture.A));
        Assert.All(await retained.ClearinghouseFeedCheckpoints.ToListAsync(), x => Assert.Equal("0", x.Cursor));
    }

    [SqlServerFact]
    public async Task ClaimReleaseSqlPollingSelectorLockResultsAndLostOwnershipFailClosed()
    {
        foreach (var empty in new[] { false, true })
        {
            var loss = new PollOwnershipLoss();
            await using var fixture = await PollFixture.CreateAsync(true, loss);
            if (empty)
            {
                await using var db = fixture.Open();
                await db.Database.ExecuteSqlRawAsync("UPDATE ClearinghouseAccounts SET IsEnabled = 0, Revision = Revision + 1");
            }
            loss.Armed = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Selector().SelectAsync([], CancellationToken.None));
            Assert.True(loss.Fired);
            await using var verify = fixture.Open(); Assert.Equal(1, (await verify.ClearinghousePollRotation.SingleAsync()).Revision);
            Assert.Empty(await verify.ClearinghouseAccountPollRotation.ToListAsync());
        }
    }

    [SqlServerFact]
    public async Task ClaimReleaseSqlPollingSharedSelectorContentionAndDisposalPreservePosition()
    {
        await using var fixture = await PollFixture.CreateAsync(true);
        await using (var owner = fixture.Open())
        {
            await using var transaction = await owner.Database.BeginTransactionAsync();
            await owner.Database.ExecuteSqlRawAsync(
                "DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource='Sati.ClaimMdSandbox.PollSelection', " +
                "@LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=0; IF @r < 0 THROW 51000, 'Synthetic lock failed', 1;");
            Assert.Equal(PollSelectionKind.Contended, (await fixture.Selector().SelectAsync([], CancellationToken.None)).Kind);
            await using var verify = fixture.Open();
            Assert.Equal(1, (await verify.ClearinghousePollRotation.SingleAsync()).Revision);
        }
        Assert.Equal(PollSelectionKind.Selected, (await fixture.Selector().SelectAsync([], CancellationToken.None)).Kind);
        // A separate connection immediately acquires the same lock after selection disposal.
        await using var after = fixture.Open(); await using var afterTransaction = await after.Database.BeginTransactionAsync();
        await after.Database.ExecuteSqlRawAsync(
            "DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource='Sati.ClaimMdSandbox.PollSelection', " +
            "@LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=0; IF @r < 0 THROW 51000, 'Selection retained its lock', 1;");
    }

    [SqlServerFact]
    public async Task ClaimReleaseSqlPollingFailedOrAmbiguousCommitDoesNotReplayAndRestartRetainsActualPosition()
    {
        foreach (var after in new[] { false, true })
        {
            var fault = new PollCommitFault(after);
            await using var fixture = await PollFixture.CreateAsync(true, fault);
            fault.Armed = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Selector().SelectAsync([], CancellationToken.None));
            Assert.Equal(1, fault.Fired);
            await using var db = fixture.Open();
            Assert.Equal(after ? 2 : 1, (await db.ClearinghousePollRotation.SingleAsync()).Revision);
            Assert.Empty(await db.ClearinghouseResponseReceipts.ToListAsync());
            var resumed = await fixture.Selector().SelectAsync([], CancellationToken.None);
            Assert.Equal(after ? fixture.B : fixture.A, resumed.AgencyId);
        }
    }

    [SqlServerFact]
    public async Task ClaimReleaseSqlPollingMigrationIsAdditiveAndUsedRollbackRefuses()
    {
        await using var fixture = await PollFixture.CreateAsync(true);
        await using var db = fixture.Open();
        var migration = new AddClearinghousePollRotation { ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer" };
        Assert.DoesNotContain(migration.UpOperations, x => x is Microsoft.EntityFrameworkCore.Migrations.Operations.DropIndexOperation or
            Microsoft.EntityFrameworkCore.Migrations.Operations.DropTableOperation or Microsoft.EntityFrameworkCore.Migrations.Operations.AlterColumnOperation);
        async Task Apply(IReadOnlyList<Microsoft.EntityFrameworkCore.Migrations.Operations.MigrationOperation> operations)
        {
            await new SyntheticSingleAttempt(db).ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(operations, db.Model))
                    await db.Database.ExecuteSqlRawAsync(command.CommandText);
                await transaction.CommitAsync();
            });
        }
        await Apply(migration.DownOperations); await Apply(migration.UpOperations);
        Assert.Equal(1, (await db.ClearinghousePollRotation.SingleAsync()).Revision);
        var invalid = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT dbo.ClearinghouseAccountPollRotation (AgencyId,AccountId,LastFeedKind,Revision) VALUES (2147483647,'00000000-0000-0000-0000-000000000001',1,1)"));
        Assert.Equal(547, invalid.Number);
        await fixture.Selector().SelectAsync([], CancellationToken.None);
        var used = await Assert.ThrowsAsync<SqlException>(() => Apply(migration.DownOperations)); Assert.Equal(51046, used.Number);
        Assert.Equal(2, (await db.ClearinghousePollRotation.AsNoTracking().SingleAsync()).Revision);
        Assert.All(await db.ClearinghouseFeedCheckpoints.ToListAsync(), x => Assert.Equal("0", x.Cursor));
        Assert.Empty(await db.ClearinghouseResponseReceipts.ToListAsync());
    }

    private sealed class SyntheticSingleAttempt(ApiDbContext db) : ExecutionStrategy(db, 0, TimeSpan.Zero)
    { protected override bool ShouldRetryOn(Exception exception) => false; }

    private sealed class PollOwnershipLoss : DbCommandInterceptor
    {
        public bool Armed { get; set; } public bool Fired { get; private set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains("FROM [Agencies]", StringComparison.Ordinal))
            {
                Armed = false; Fired = true;
                await using var release = command.Connection!.CreateCommand(); release.Transaction = command.Transaction;
                release.CommandText = "EXEC sys.sp_releaseapplock @Resource='Sati.ClaimMdSandbox.PollSelection', @LockOwner='Transaction';";
                await release.ExecuteNonQueryAsync(cancellationToken);
            }
            return result;
        }
    }
    private sealed class PollCommitFault(bool after) : DbTransactionInterceptor
    {
        public bool Armed { get; set; } public int Fired { get; private set; }
        private void Fail(DbContext? db, bool committed)
        {
            if (!Armed || after != committed || db?.ChangeTracker.Entries<ClearinghousePollRotation>().Any() != true) return;
            Armed = false; Fired++; throw new InvalidOperationException("Synthetic polling commit acknowledgement failure.");
        }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        { Fail(eventData.Context, false); return ValueTask.FromResult(result); }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { Fail(eventData.Context, true); return Task.CompletedTask; }
    }
}
