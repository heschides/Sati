using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Models;
using Sati.Persistence.Migrations;
using Xunit;

namespace Sati.Api.Tests;

public sealed partial class SignatureWorkFairnessTests
{
    [SqlServerFact]
    public async Task ClaimReleaseSqlSignatureRotationContentionIsPhaseScopedAndReleasesBeforeBusinessWork()
    {
        await using var f = await SignatureWorkFixture.CreateAsync(true); await f.SeedAsync(f.A, packaged: false);
        await using var services = f.Services(); var selector = services.GetRequiredService<SignatureWorkSelector>();
        await using (var owner = f.Open())
        {
            await using var tx = await owner.Database.BeginTransactionAsync();
            await LockAsync(owner, SignatureWorkKind.Projection);
            Assert.Equal(SignatureSelectionKind.Contended, (await selector.SelectAsync(SignatureWorkKind.Projection, [], CancellationToken.None)).Kind);
            Assert.Equal(SignatureSelectionKind.Selected, (await selector.SelectAsync(SignatureWorkKind.Package, [], CancellationToken.None)).Kind);
            await using var verify = f.Open(); Assert.Equal(1, (await verify.SignatureWorkRotation.SingleAsync(x => x.WorkKind == SignatureWorkKind.Projection)).Revision);
        }
        Assert.Equal(SignatureSelectionKind.Selected, (await selector.SelectAsync(SignatureWorkKind.Projection, [], CancellationToken.None)).Kind);
        await using var after = f.Open(); await using var afterTx = await after.Database.BeginTransactionAsync();
        await LockAsync(after, SignatureWorkKind.Projection);
        Assert.Empty(f.BlobStore.Reads); Assert.Empty(f.Email.Sends);
    }

    [SqlServerFact]
    public async Task ClaimReleaseSqlSignatureRealOwnershipLossRejectsSelectedAndEmptyWithoutWrites()
    {
        foreach (var empty in new[] { false, true })
        {
            var loss = new OwnershipLoss(); await using var f = await SignatureWorkFixture.CreateAsync(true, loss);
            if (!empty) await f.SeedAsync(f.A);
            loss.Armed = true; await using var services = f.Services();
            await Assert.ThrowsAsync<InvalidOperationException>(() => services.GetRequiredService<SignatureWorkSelector>().SelectAsync(SignatureWorkKind.Projection, [], CancellationToken.None));
            Assert.True(loss.Fired); await using var db = f.Open();
            Assert.All(await db.SignatureWorkRotation.ToListAsync(), x => Assert.Equal(1, x.Revision));
            Assert.Empty(await db.SignatureAgencyWorkRotation.ToListAsync()); Assert.Empty(f.Email.Sends);
        }
    }

    [SqlServerFact]
    public async Task ClaimReleaseSqlSignatureCommitAmbiguityNeverReplaysAndRestartFollowsActualCommit()
    {
        foreach (var after in new[] { false, true })
        {
            var fault = new CommitFault(after); await using var f = await SignatureWorkFixture.CreateAsync(true, fault);
            await f.SeedAsync(f.A); await f.SeedAsync(f.B); fault.Armed = true;
            await using (var services = f.Services())
                await Assert.ThrowsAsync<InvalidOperationException>(() => services.GetRequiredService<SignatureWorkSelector>().SelectAsync(SignatureWorkKind.Projection, [], CancellationToken.None));
            Assert.Equal(1, fault.Fired); await using var db = f.Open();
            Assert.Equal(after ? 2 : 1, (await db.SignatureWorkRotation.SingleAsync(x => x.WorkKind == SignatureWorkKind.Projection)).Revision);
            Assert.Empty(await db.Set<SignatureComplianceProjection>().ToListAsync());
            await using var restarted = f.Services();
            Assert.Equal(after ? f.B : f.A, (await restarted.GetRequiredService<SignatureWorkSelector>().SelectAsync(SignatureWorkKind.Projection, [], CancellationToken.None)).AgencyId);
            Assert.Empty(f.Email.Sends); Assert.Empty(f.BlobStore.Reads);
        }
    }

    [SqlServerFact]
    public async Task ClaimReleaseSqlSignatureAgencyAndItemPositionsSurviveFreshSqlConnections()
    {
        await using var f = await SignatureWorkFixture.CreateAsync(true);
        var a1 = await f.SeedAsync(f.A, packaged: false); var a2 = await f.SeedAsync(f.A, packaged: false); var b = await f.SeedAsync(f.B, packaged: false);
        foreach (var kind in Enum.GetValues<SignatureWorkKind>())
        foreach (var item in new[] { a1, b, a2, b, a1 })
        {
            await using var services = f.Services();
            var offer = await services.GetRequiredService<SignatureWorkSelector>().SelectAsync(kind, [], CancellationToken.None);
            Assert.Equal(item.Agency, offer.AgencyId); Assert.Equal(kind == SignatureWorkKind.Mail ? item.Mail : item.Completion, offer.ItemId);
        }
        await using var db = f.Open(); Assert.All(await db.SignatureRequests.ToListAsync(), x => Assert.Equal(5, x.Revision));
    }

    [SqlServerFact]
    public async Task ClaimReleaseSqlSignatureRotationMigrationIsAdditiveAndUsedDownRefuses()
    {
        await using var f = await SignatureWorkFixture.CreateAsync(true); await f.SeedAsync(f.A);
        await using var db = f.Open(); var migration = new AddSignatureWorkRotation { ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer" };
        Assert.DoesNotContain(migration.UpOperations, x => x is DropTableOperation or DropIndexOperation or AlterColumnOperation);
        Assert.Equal(2, migration.UpOperations.OfType<CreateTableOperation>().Count());
        Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        async Task Apply(IReadOnlyList<MigrationOperation> operations)
        {
            await new SingleAttempt(db).ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(operations, db.Model))
                    await db.Database.ExecuteSqlRawAsync(command.CommandText);
                await tx.CommitAsync();
            });
        }
        await Apply(migration.DownOperations); await Apply(migration.UpOperations);
        Assert.Equal(3, await db.SignatureWorkRotation.CountAsync());
        var invalid = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT dbo.SignatureAgencyWorkRotation (AgencyId,WorkKind,LastItemId,Revision) VALUES (2147483647,1,1,1)"));
        Assert.Equal(547, invalid.Number);
        await using var services = f.Services(); await services.GetRequiredService<SignatureWorkSelector>().SelectAsync(SignatureWorkKind.Projection, [], CancellationToken.None);
        Assert.Equal(51047, (await Assert.ThrowsAsync<SqlException>(() => Apply(migration.DownOperations))).Number);
        Assert.Equal(3, await db.SignatureWorkRotation.CountAsync()); Assert.Single(await db.SignatureAgencyWorkRotation.ToListAsync());
        Assert.Single(await db.SignatureCompletions.ToListAsync());
    }

    private static Task LockAsync(ApiDbContext db, SignatureWorkKind kind) => db.Database.ExecuteSqlRawAsync(
        "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={0}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=0; IF @r < 0 THROW 51000, 'Synthetic lock failed', 1;",
        SignatureWorkSelector.Resource(kind));
    private sealed class SingleAttempt(ApiDbContext db) : ExecutionStrategy(db, 0, TimeSpan.Zero)
    { protected override bool ShouldRetryOn(Exception exception) => false; }
    private sealed class OwnershipLoss : DbCommandInterceptor
    {
        internal bool Armed, Fired;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains("FROM [Agencies]", StringComparison.Ordinal))
            {
                Armed = false; Fired = true;
                await using var release = command.Connection!.CreateCommand(); release.Transaction = command.Transaction;
                release.CommandText = "EXEC sys.sp_releaseapplock @Resource='Sati.Signatures.WorkSelection:1', @LockOwner='Transaction';";
                await release.ExecuteNonQueryAsync(cancellationToken);
            }
            return result;
        }
    }
    private sealed class CommitFault(bool after) : DbTransactionInterceptor
    {
        internal bool Armed; internal int Fired;
        private void Fail(DbContext? db, bool committed)
        {
            if (!Armed || after != committed || db?.ChangeTracker.Entries<SignatureWorkRotation>().Any() != true) return;
            Armed = false; Fired++; throw new InvalidOperationException("Synthetic signature commit acknowledgement failure.");
        }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        { Fail(eventData.Context, false); return ValueTask.FromResult(result); }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { Fail(eventData.Context, true); return Task.CompletedTask; }
    }
}
