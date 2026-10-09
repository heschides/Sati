using System.Data;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Models.Billing;
using Sati.Persistence.Migrations;
using Xunit;

namespace Sati.Api.Tests;

[Collection("Synthetic pipeline SQL Server")]
public sealed class ClearinghouseReadinessMigrationSqlTests
{
    private const string TargetMigration = "20261009183720_AddClearinghousePreflightReadiness";
    private const string ReadinessTable = "dbo.ClearinghouseDispatchReadiness";

    [SqlServerFact]
    public async Task PreflightRollbackApplyAndRerunPreserveRetainedWorkAndReadiness()
    {
        await using var fixture = await MigrationFixture.CreateAsync();
        var retained = await fixture.ReadRetainedWorkAsync();
        var preflight = await fixture.RunAsync(preflightOnly: true);
        Assert.Equal(new MigrationResult(false, true, 128), preflight);
        await fixture.AssertPredecessorAsync(retained);

        Assert.Equal(new MigrationResult(true, false, 129), await fixture.RunAsync(rollback: true));
        await fixture.AssertPredecessorAsync(retained);

        Assert.Equal(new MigrationResult(true, false, 129), await fixture.RunAsync());
        Assert.Equal(retained, await fixture.ReadRetainedWorkAsync());
        await fixture.ExecuteAsync("""
            INSERT dbo.ClearinghouseDispatchReadiness
              (AgencyId, AccountId, Disposition, FailureCount, RecoveryCycleId, NextEligibleAtUtc,
               LastFailureAtUtc, SafeFailureCode, ValidatedAccountRevision, Revision)
            SELECT AgencyId, Id, 2, 1, '11111111-1111-1111-1111-111111111111',
                   '2026-10-09T12:01:00', '2026-10-09T12:00:00', N'account_key_unavailable', 0, 1
            FROM dbo.ClearinghouseAccounts;
            """);
        var readiness = await fixture.ScalarAsync<string>("SELECT * FROM dbo.ClearinghouseDispatchReadiness FOR JSON PATH;");
        Assert.Equal(new MigrationResult(false, false, 129), await fixture.RunAsync());
        Assert.Equal(new MigrationResult(false, false, 129), await fixture.RunAsync(preflightOnly: true));
        Assert.Equal(readiness, await fixture.ScalarAsync<string>("SELECT * FROM dbo.ClearinghouseDispatchReadiness FOR JSON PATH;"));
        Assert.Equal(retained, await fixture.ReadRetainedWorkAsync());
    }

    [SqlServerFact]
    public async Task RefusesWrongEnvironmentAndExpectedDatabaseBeforeCreatingSchema()
    {
        await using var fixture = await MigrationFixture.CreateAsync();
        var retained = await fixture.ReadRetainedWorkAsync();
        await fixture.ExecuteAsync("UPDATE dbo.SatiDatabaseIdentity SET EnvironmentName=N'Production' WHERE Id=1;");
        Assert.Equal(53910, (await Assert.ThrowsAsync<SqlException>(() => fixture.ExecuteRejectedCandidateAsync())).Number);
        await fixture.AssertPredecessorAsync(retained);
        await fixture.ExecuteAsync("UPDATE dbo.SatiDatabaseIdentity SET EnvironmentName=N'Demo' WHERE Id=1;");
        Assert.Equal(53910, (await Assert.ThrowsAsync<SqlException>(() => fixture.ExecuteRejectedCandidateAsync("SatiDemo"))).Number);
        await fixture.AssertPredecessorAsync(retained);
    }

    [SqlServerFact]
    public async Task RefusesSubstitutedMigrationEvenWhenCountAndLatestPredecessorMatch()
    {
        await using var fixture = await MigrationFixture.CreateAsync();
        var retained = await fixture.ReadRetainedWorkAsync();
        await fixture.ExecuteAsync("""
            UPDATE dbo.__EFMigrationsHistory SET MigrationId=N'20000101000000_UnreviewedSyntheticReplacement'
            WHERE MigrationId=(SELECT MIN(MigrationId) FROM dbo.__EFMigrationsHistory);
            """);
        Assert.Equal(128, await fixture.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.__EFMigrationsHistory;"));
        Assert.Equal("20261007111016_AddAssessmentReviewCycles",
            await fixture.ScalarAsync<string>("SELECT MAX(MigrationId) FROM dbo.__EFMigrationsHistory;"));
        Assert.Equal(53911, (await Assert.ThrowsAsync<SqlException>(() => fixture.ExecuteRejectedCandidateAsync())).Number);
        await fixture.AssertPredecessorAsync(retained);
    }

    [SqlServerFact]
    public async Task RefusesWeakenedExistingCheckWithoutRepairingOrRecordingHistory()
    {
        await using var fixture = await MigrationFixture.CreateAsync();
        await fixture.RunAsync();
        var retained = await fixture.ReadRetainedWorkAsync();
        await fixture.ExecuteAsync("""
            ALTER TABLE dbo.ClearinghouseDispatchReadiness DROP CONSTRAINT CK_ClearinghouseDispatchReadiness_State;
            ALTER TABLE dbo.ClearinghouseDispatchReadiness WITH CHECK ADD CONSTRAINT CK_ClearinghouseDispatchReadiness_State CHECK (AgencyId > 0);
            """);
        var weakened = await fixture.ScalarAsync<string>("SELECT definition FROM sys.check_constraints WHERE name=N'CK_ClearinghouseDispatchReadiness_State';");
        Assert.Equal(53912, (await Assert.ThrowsAsync<SqlException>(() => fixture.ExecuteRejectedCandidateAsync())).Number);
        Assert.Equal(weakened, await fixture.ScalarAsync<string>("SELECT definition FROM sys.check_constraints WHERE name=N'CK_ClearinghouseDispatchReadiness_State';"));
        Assert.Equal(129, await fixture.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.__EFMigrationsHistory;"));
        Assert.Equal(retained, await fixture.ReadRetainedWorkAsync());
    }

    [SqlServerFact]
    public async Task RefusesUntrustedExistingTenantForeignKey()
    {
        await using var fixture = await MigrationFixture.CreateAsync();
        await fixture.RunAsync();
        var retained = await fixture.ReadRetainedWorkAsync();
        await fixture.ExecuteAsync("ALTER TABLE dbo.ClearinghouseDispatchReadiness NOCHECK CONSTRAINT FK_ClearinghouseDispatchReadiness_ClearinghouseAccounts_AgencyId_AccountId;");
        Assert.Equal(53912, (await Assert.ThrowsAsync<SqlException>(() => fixture.ExecuteRejectedCandidateAsync(preflightOnly: true))).Number);
        Assert.Equal(1, await fixture.ScalarAsync<int>("SELECT CONVERT(int,is_not_trusted) FROM sys.foreign_keys WHERE name=N'FK_ClearinghouseDispatchReadiness_ClearinghouseAccounts_AgencyId_AccountId';"));
        Assert.Equal(retained, await fixture.ReadRetainedWorkAsync());
    }

    [SqlServerFact]
    public async Task RefusesExistingDispatchIndexWithDifferentOrderingAndMissingIncludedColumn()
    {
        await using var fixture = await MigrationFixture.CreateAsync();
        await fixture.RunAsync();
        var retained = await fixture.ReadRetainedWorkAsync();
        await fixture.ExecuteAsync("""
            DROP INDEX IX_ClearinghouseDispatches_State_RequestedAtUtc_Id ON dbo.ClearinghouseDispatches;
            CREATE INDEX IX_ClearinghouseDispatches_State_RequestedAtUtc_Id
              ON dbo.ClearinghouseDispatches(State, RequestedAtUtc DESC, Id) INCLUDE(AgencyId);
            """);
        Assert.Equal(53912, (await Assert.ThrowsAsync<SqlException>(() => fixture.ExecuteRejectedCandidateAsync())).Number);
        Assert.Equal(retained, await fixture.ReadRetainedWorkAsync());
    }

    [SqlServerFact]
    public async Task CompatibleUntrackedSchemaIsCompletedButMissingTrackedSchemaIsRejected()
    {
        await using var fixture = await MigrationFixture.CreateAsync();
        await fixture.ApplySourceUpAsync();
        await fixture.ExecuteAsync("DROP INDEX IX_ClearinghouseDispatchReadiness_Disposition_NextEligibleAtUtc ON dbo.ClearinghouseDispatchReadiness;");
        Assert.Equal(new MigrationResult(false, true, 128), await fixture.RunAsync(preflightOnly: true));
        Assert.Equal(new MigrationResult(true, false, 129), await fixture.RunAsync());
        await fixture.ExecuteAsync("DROP INDEX IX_ClearinghouseDispatchReadiness_Disposition_NextEligibleAtUtc ON dbo.ClearinghouseDispatchReadiness;");
        Assert.Equal(53912, (await Assert.ThrowsAsync<SqlException>(() => fixture.ExecuteRejectedCandidateAsync())).Number);
        Assert.Equal(129, await fixture.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.__EFMigrationsHistory;"));
    }

    [SqlServerFact]
    public async Task FailureWhileRecordingHistoryRollsBackCreatedTableAndIndexes()
    {
        await using var fixture = await MigrationFixture.CreateAsync();
        var retained = await fixture.ReadRetainedWorkAsync();
        // This private-fixture trigger fails after schema creation, proving the caller's
        // transaction protects both the objects and history from a late failure.
        await fixture.ExecuteAsync("""
            CREATE TRIGGER dbo.SyntheticRejectReadinessHistory ON dbo.__EFMigrationsHistory AFTER INSERT AS
            BEGIN
              IF EXISTS(SELECT 1 FROM inserted WHERE MigrationId=N'20261009183720_AddClearinghousePreflightReadiness')
                THROW 53990, 'Synthetic late migration failure.', 1;
            END;
            """);
        Assert.Equal(53990, (await Assert.ThrowsAsync<SqlException>(() => fixture.RunAsync())).Number);
        await fixture.AssertPredecessorAsync(retained);
    }

    [SqlServerFact]
    public async Task RefusesExecutionWithoutCallerTransaction()
    {
        await using var fixture = await MigrationFixture.CreateAsync();
        var retained = await fixture.ReadRetainedWorkAsync();
        await using var connection = new SqlConnection(fixture.Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = await ReadGuardedSqlAsync();
        command.Parameters.Add("@expectedDatabase", SqlDbType.NVarChar, 128).Value = fixture.Database.Name;
        command.Parameters.Add("@preflightOnly", SqlDbType.Bit).Value = false;
        Assert.Equal(53910, (await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync())).Number);
        await fixture.AssertPredecessorAsync(retained);
    }

    private static async Task<string> ReadGuardedSqlAsync()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SatiLogica.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        return await File.ReadAllTextAsync(Path.Combine(root.FullName,
            "scripts/Apply-ClearinghousePreflightReadinessMigration.guarded.sql"));
    }

    private sealed record MigrationResult(bool MigrationWasApplied, bool ChangesRequired, int MigrationCount);
    private sealed record RetainedWork(string Content, Guid DispatchId, ClearinghouseDispatchState State, long Revision);

    private sealed class MigrationFixture : IAsyncDisposable
    {
        public SyntheticPipelineDatabase Database { get; } = new(sqlServer: true);
        private ApiDbContext OpenDatabase() => new(new DbContextOptionsBuilder<ApiDbContext>()
            .UseSqlServer(Database.ConnectionString).Options);

        public static async Task<MigrationFixture> CreateAsync()
        {
            var fixture = new MigrationFixture();
            try
            {
                await fixture.Database.InitializeAsync();
                await using var factory = new SyntheticPipelineFactory(fixture.Database) { DisableDispatchWorker = true };
                var actors = await factory.SeedAsync();
                await using var db = fixture.OpenDatabase();
                var period = new ServerBillingPeriod { UserId = actors.AuthorId, Year = 2026, Month = 10, Status = 1 };
                db.BillingPeriods.Add(period);
                var account = new ClearinghouseAccount
                {
                    Id = Guid.NewGuid(), AgencyId = actors.AgencyId, ConnectorKind = TradingPartnerKind.ClaimMd,
                    IsTest = true, IsEnabled = true, ExternalAccountNumber = "READINESSTEST", ClaimNamespace = "TEST",
                    TradingPartnerProfileVersion = TradingPartnerProfile.CurrentVersion,
                    CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
                };
                db.ClearinghouseAccounts.Add(account);
                await db.SaveChangesAsync();
                var generation = new ServerEdiGeneration
                {
                    AgencyId = actors.AgencyId, ActorUserId = actors.BillerId, BillingPeriodId = period.Id,
                    IdempotencyKey = Guid.NewGuid().ToString("N"), IsTest = true, ControlNumber = "123456789",
                    FileName = "synthetic-readiness.CMDTEST.txt", Content = "Synthetic retained EDI\r\n~ unchanged bytes."
                };
                db.EdiGenerations.Add(generation);
                await db.SaveChangesAsync();
                db.ClearinghouseDispatches.Add(new ClearinghouseDispatch
                {
                    Id = Guid.NewGuid(), AgencyId = actors.AgencyId, AccountId = account.Id,
                    EdiGenerationId = generation.Id, RequestingUserId = actors.BillerId,
                    RequestedAtUtc = DateTime.UtcNow, State = ClearinghouseDispatchState.Queued,
                    TradingPartnerProfileVersion = TradingPartnerProfile.CurrentVersion, Revision = 7
                });
                await db.SaveChangesAsync();
                var migration = new AddClearinghousePreflightReadiness { ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer" };
                foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.DownOperations, db.Model))
                    await db.Database.ExecuteSqlRawAsync(command.CommandText);
                var migrations = typeof(AddClearinghousePreflightReadiness).Assembly.GetTypes()
                    .Select(type => type.GetCustomAttribute<MigrationAttribute>()?.Id)
                    .OfType<string>().Order(StringComparer.Ordinal).ToArray();
                Assert.Equal(129, migrations.Length);
                Assert.Equal(TargetMigration, migrations[^1]);
                await fixture.ExecuteAsync("CREATE TABLE dbo.__EFMigrationsHistory(MigrationId nvarchar(150) NOT NULL PRIMARY KEY, ProductVersion nvarchar(32) NOT NULL);");
                foreach (var id in migrations[..^1])
                    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.__EFMigrationsHistory(MigrationId,ProductVersion) VALUES({id},N'10.0.5');");
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public async Task ApplySourceUpAsync()
        {
            await using var db = OpenDatabase();
            var migration = new AddClearinghousePreflightReadiness { ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer" };
            foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, db.Model))
                await db.Database.ExecuteSqlRawAsync(command.CommandText);
        }

        public async Task<MigrationResult> RunAsync(bool rollback = false, bool preflightOnly = false)
        {
            await using var connection = new SqlConnection(Database.ConnectionString);
            await connection.OpenAsync();
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 60;
            command.CommandText = await ReadGuardedSqlAsync();
            command.Parameters.Add("@expectedDatabase", SqlDbType.NVarChar, 128).Value = Database.Name;
            command.Parameters.Add("@preflightOnly", SqlDbType.Bit).Value = preflightOnly;
            MigrationResult result;
            await using (var reader = await command.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                result = new(Convert.ToBoolean(reader["MigrationWasApplied"]), Convert.ToBoolean(reader["ChangesRequired"]),
                    Convert.ToInt32(reader["MigrationCount"]));
            }
            command.CommandText = "SELECT APPLOCK_MODE(N'public',N'SatiDemo.FullReset',N'Transaction');";
            Assert.Equal("Exclusive", await command.ExecuteScalarAsync());
            if (rollback) await transaction.RollbackAsync(); else await transaction.CommitAsync();
            return result;
        }

        public async Task ExecuteRejectedCandidateAsync(string? expectedDatabase = null, bool preflightOnly = false)
        {
            await using var connection = new SqlConnection(Database.ConnectionString);
            await connection.OpenAsync();
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = await ReadGuardedSqlAsync();
            command.Parameters.Add("@expectedDatabase", SqlDbType.NVarChar, 128).Value = expectedDatabase ?? Database.Name;
            command.Parameters.Add("@preflightOnly", SqlDbType.Bit).Value = preflightOnly;
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }

        public async Task AssertPredecessorAsync(RetainedWork retained)
        {
            Assert.Equal(128, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.__EFMigrationsHistory;"));
            Assert.Equal(0, await ScalarAsync<int>($"SELECT COUNT(*) FROM sys.tables WHERE object_id=OBJECT_ID(N'{ReadinessTable}');"));
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.ClearinghouseDispatches') AND name=N'IX_ClearinghouseDispatches_State_RequestedAtUtc_Id';"));
            Assert.Equal(retained, await ReadRetainedWorkAsync());
        }

        public async Task<RetainedWork> ReadRetainedWorkAsync()
        {
            await using var db = OpenDatabase();
            var dispatch = await db.ClearinghouseDispatches.AsNoTracking().SingleAsync();
            var generation = await db.EdiGenerations.AsNoTracking().SingleAsync();
            return new(generation.Content, dispatch.Id, dispatch.State, dispatch.Revision);
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqlConnection(Database.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        public async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = new SqlConnection(Database.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
        }

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }
}
