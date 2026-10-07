using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Persistence.Migrations;
using Xunit;
namespace Sati.Api.Tests;

[Collection("Synthetic pipeline SQL Server")]
public sealed class RecordsGovernanceSqlTests
{
    [SqlServerFact]
    public async Task ControlledScriptRollsBackRerunsAndRefusesCorruptExistingSchema()
    {
        await using var database = new SyntheticPipelineDatabase(sqlServer: true); await database.InitializeAsync();
        await using var factory = new SyntheticPipelineFactory(database); await factory.SeedAsync();
        var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var db = new ApiDbContext(options); var migration = new AddRecordsGovernance { ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer" };
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.DownOperations, db.Model)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE dbo.__EFMigrationsHistory(MigrationId nvarchar(150) NOT NULL PRIMARY KEY, ProductVersion nvarchar(32) NOT NULL);
            DECLARE @n int=1; WHILE @n<=125 BEGIN
              INSERT dbo.__EFMigrationsHistory VALUES(CONCAT(N'20260000000000_Synthetic_',@n),N'10.0.5'); SET @n+=1;
            END;
            INSERT dbo.__EFMigrationsHistory VALUES(N'20261004204633_AddPayerBillingConfigurationVersions',N'10.0.5');
            """);
        var root = new DirectoryInfo(AppContext.BaseDirectory); while (root is not null && !File.Exists(Path.Combine(root.FullName, "SatiLogica.slnx"))) root = root.Parent;
        Assert.NotNull(root); var sql = await File.ReadAllTextAsync(Path.Combine(root.FullName, "scripts/Apply-RecordsGovernanceMigration.guarded.sql"));
        await using var connection = new SqlConnection(database.ConnectionString); await connection.OpenAsync();
        await Run(true); await Run(false); await Run(false);
        await using var corrupt = connection.CreateCommand(); corrupt.CommandText = "ALTER TABLE dbo.RecordsRetentionPlans ALTER COLUMN CandidatesJson nvarchar(999) NOT NULL;";
        await corrupt.ExecuteNonQueryAsync(); var error = await Assert.ThrowsAsync<SqlException>(() => Run(false)); Assert.Equal(53802, error.Number);
        async Task Run(bool rollback)
        { await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(); await using var command = connection.CreateCommand();
            command.Transaction = tx; command.CommandText = sql; command.Parameters.AddWithValue("@expectedDatabase", database.Name);
            await command.ExecuteNonQueryAsync(); command.CommandText = "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory;"; Assert.Equal(127, Convert.ToInt32(await command.ExecuteScalarAsync()));
            if (rollback) await tx.RollbackAsync(); else await tx.CommitAsync(); }
    }
    [SqlServerFact]
    public async Task MigrationImportsActivePersonHoldsWithoutChangingLegacyEvidenceAndRefusesLossyRollback()
    {
        await using var database = new SyntheticPipelineDatabase(sqlServer: true); await database.InitializeAsync();
        await using var factory = new SyntheticPipelineFactory(database); var users = await factory.SeedAsync();
        var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var db = new ApiDbContext(options); var migration = new AddRecordsGovernance { ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer" };
        await Apply(migration.DownOperations);
        var active = new ServerLegalHold { AgencyId = users.AgencyId, PersonId = users.FirstPersonId, Reason = "Synthetic legacy hold",
            PlacedByUserId = users.BillerId, PlacedAtUtc = DateTime.UtcNow.AddYears(-1), EffectiveAtUtc = DateTime.UtcNow.AddYears(-1) };
        var released = new ServerLegalHold { AgencyId = users.AgencyId, PersonId = users.FirstPersonId, Reason = "Synthetic historical release",
            PlacedByUserId = users.BillerId, IsReleased = true, ReleasedByUserId = users.AuthorId, ReleasedAtUtc = DateTime.UtcNow };
        db.LegalHolds.AddRange(active, released); await db.SaveChangesAsync();
        var before = System.Text.Json.JsonSerializer.Serialize(await db.LegalHolds.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync());
        await Apply(migration.UpOperations);
        var imported = Assert.Single(await db.Set<RecordsHold>().ToArrayAsync()); Assert.Equal(active.Id, imported.LegacyHoldId);
        Assert.Equal(users.FirstPersonId, imported.PersonId); Assert.False(imported.IsReleased);
        var placement = Assert.Single(await db.Set<RecordsHoldEvent>().ToArrayAsync()); Assert.Equal(active.Reason, placement.Reason);
        Assert.Equal(active.PlacedAtUtc, placement.RecordedAtUtc);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(await db.LegalHolds.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()));
        var failure = await Assert.ThrowsAsync<SqlException>(() => Apply(migration.DownOperations)); Assert.Equal(53804, failure.Number);
        async Task Apply(IReadOnlyList<Microsoft.EntityFrameworkCore.Migrations.Operations.MigrationOperation> operations)
        { await using var tx = await db.Database.BeginTransactionAsync();
            foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(operations, db.Model)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
            await tx.CommitAsync(); }
    }
    [SqlServerFact]
    public async Task AConcurrentHoldLocksTheAgencyAndDefeatsThePreparedPurge()
    {
        await using var database = new SyntheticPipelineDatabase(sqlServer: true); await database.InitializeAsync();
        await using var factory = new SyntheticPipelineFactory(database); var users = await factory.SeedAsync();
        var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlServer(database.ConnectionString).Options;
        var actor = new AgencyActor(users.BillerId, users.AgencyId, UserPermissions.Administration); var now = DateTime.UtcNow;
        RetentionPreviewDto preview; var store = new EmptyFixtureStore();
        await using (var db = new ApiDbContext(options))
        { await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var policy = await RecordsGovernanceWorkflow.PolicyAsync(db, actor, new(Guid.NewGuid(), RetentionRecordClass.Clinical, 0, 30, "Synthetic proposal"), (_, _) => { }, now);
            preview = await RecordsRetentionWorkflow.PreviewAsync(db, actor, policy.Id, store, new HashSet<int>(), now); await tx.CommitAsync(); }
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hold = Place(); await ready.Task.WaitAsync(TimeSpan.FromSeconds(15)); var purge = Execute();
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10); var waiting = false;
            do { await using var connection = new SqlConnection(database.ConnectionString); await connection.OpenAsync(); await using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_database_id=DB_ID() AND request_status='WAIT';";
                waiting = Convert.ToInt32(await command.ExecuteScalarAsync()) > 0; if (!waiting) await Task.Delay(25);
            } while (!waiting && DateTime.UtcNow < deadline && !purge.IsCompleted);
            Assert.True(waiting, "Purge must contend on the exact agency preservation lock while placement is uncommitted.");
        }
        finally { release.TrySetResult(); }
        await hold; var result = await purge; Assert.Contains("governance_changed_reprepare", result.Blockers);
        Assert.False(store.Deleted);
        async Task Place()
        { await using var db = new ApiDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await RecordsGovernanceWorkflow.HoldAsync(db, actor, new(Guid.NewGuid(), GovernanceHoldAction.Place, null, 0, PreservationScope.Agency,
                null, null, null, "Synthetic concurrent preservation"), _ => Task.FromResult(true), (_, _, _) => { }, now);
            ready.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(20)); await tx.CommitAsync(); }
        async Task<RetentionBatchResult> Execute()
        { await using var db = new ApiDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var result = await RecordsRetentionWorkflow.ExecuteBatchAsync(db, actor, preview.Id, Guid.NewGuid(), 0, store,
                new HashSet<int>(), RetentionExecutionMode.SyntheticFixture, true, now); await tx.CommitAsync(); return result; }
    }
    private sealed class EmptyFixtureStore : IRecordsRetentionStore
    {
        public bool Deleted { get; private set; }
        public Task<RetentionStoreInventory> InventoryAsync(DbContext db, int agencyId, CancellationToken ct) => Task.FromResult(new RetentionStoreInventory(true, true, [], []));
        public async Task<PreservationReceipt?> PreservationAsync(DbContext db, Guid planId, CancellationToken ct)
        { var plan = await db.Set<RecordsRetentionPlan>().SingleAsync(x => x.Id == planId, ct); return new(planId, plan.GovernanceRevision, "private-empty-fixture",
            Guid.NewGuid().ToString(), new string('A', 64), plan.PreparedAtUtc, true, true, true); }
        public Task DeleteAsync(DbContext db, IReadOnlyList<RetentionCandidate> candidates, CancellationToken ct) { Deleted = true; return Task.CompletedTask; }
    }
}
