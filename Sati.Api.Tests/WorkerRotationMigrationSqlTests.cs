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
public sealed class WorkerRotationMigrationSqlTests
{
    private const string TargetMigration = "20261010042710_AddSignatureWorkRotation";
    private static readonly string[] Tables =
    [
        "ClearinghouseAgencyDispatchRotation", "ClearinghouseDispatchRotation",
        "ClearinghouseAccountPollRotation", "ClearinghouseAgencyPollRotation", "ClearinghousePollRotation",
        "SignatureWorkRotation", "SignatureAgencyWorkRotation"
    ];

    [SqlServerFact]
    public async Task PreflightRollbackApplyAndRerunPreserveWorkAndAdvancedPositions()
    {
        await using var fixture = await Fixture.CreateAsync();
        var retained = await fixture.BusinessStateAsync();
        Assert.Equal(new Result(false, true, 129), await fixture.RunAsync(preflight: true));
        await fixture.AssertPredecessorAsync(retained);
        Assert.Equal(new Result(true, false, 132), await fixture.RunAsync(rollback: true));
        await fixture.AssertPredecessorAsync(retained);
        Assert.Equal(new Result(true, false, 132), await fixture.RunAsync());
        await fixture.ExecuteAsync("""
            UPDATE dbo.ClearinghouseDispatchRotation SET LastAgencyId=42,Revision=7;
            UPDATE dbo.ClearinghousePollRotation SET LastAgencyId=43,Revision=8;
            UPDATE dbo.SignatureWorkRotation SET LastAgencyId=44,Revision=9;
            INSERT dbo.ClearinghouseAgencyDispatchRotation(AgencyId,LastAccountId,Revision)
              SELECT AgencyId,Id,3 FROM dbo.ClearinghouseAccounts;
            INSERT dbo.ClearinghouseAgencyPollRotation(AgencyId,LastAccountId,Revision)
              SELECT AgencyId,Id,4 FROM dbo.ClearinghouseAccounts;
            INSERT dbo.ClearinghouseAccountPollRotation(AgencyId,AccountId,LastFeedKind,Revision)
              SELECT AgencyId,Id,2,5 FROM dbo.ClearinghouseAccounts;
            INSERT dbo.SignatureAgencyWorkRotation(AgencyId,WorkKind,LastItemId,Revision)
              SELECT AgencyId,1,123,6 FROM dbo.ClearinghouseAccounts;
            """);
        var positions = await fixture.PositionsAsync();
        Assert.Equal(new Result(false, false, 132), await fixture.RunAsync());
        Assert.Equal(new Result(false, false, 132), await fixture.RunAsync(preflight: true));
        Assert.Equal(positions, await fixture.PositionsAsync());
        Assert.Equal(retained, await fixture.BusinessStateAsync());
    }

    [SqlServerTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task AcceptsExactContiguousMigrationPrefixesAndEfGeneratedObjects(int count)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ApplySourceAsync(count, recordHistory: true);
        Assert.Equal(new Result(false, count < 3, 129 + count), await fixture.RunAsync(preflight: true));
        Assert.Equal(new Result(count < 3, false, 132), await fixture.RunAsync());
    }

    [SqlServerFact]
    public async Task CompletesCompatibleUntrackedObjectsButRefusesMissingTrackedSeeds()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ApplySourceAsync(3, recordHistory: false);
        await fixture.ExecuteAsync("""
            DROP INDEX IX_SignatureAgencyWorkRotation_WorkKind ON dbo.SignatureAgencyWorkRotation;
            UPDATE dbo.SignatureWorkRotation SET LastAgencyId=123,Revision=15 WHERE WorkKind=2;
            DELETE dbo.SignatureWorkRotation WHERE WorkKind=3;
            """);
        Assert.Equal(new Result(false, true, 129), await fixture.RunAsync(preflight: true));
        Assert.Equal(new Result(true, false, 132), await fixture.RunAsync());
        Assert.Equal(15L, await fixture.ScalarAsync<long>("SELECT Revision FROM dbo.SignatureWorkRotation WHERE WorkKind=2;"));
        Assert.Equal(1L, await fixture.ScalarAsync<long>("SELECT Revision FROM dbo.SignatureWorkRotation WHERE WorkKind=3;"));
        await fixture.ExecuteAsync("DELETE dbo.SignatureWorkRotation WHERE WorkKind=3;");
        Assert.Equal(54012, (await Assert.ThrowsAsync<SqlException>(() => fixture.RunAsync())).Number);
        Assert.Equal(2, await fixture.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SignatureWorkRotation;"));
    }

    [SqlServerFact]
    public async Task RefusesWrongIdentityAndDatabaseBeforeAnySchemaChange()
    {
        await using var fixture = await Fixture.CreateAsync();
        var retained = await fixture.BusinessStateAsync();
        await fixture.ExecuteAsync("UPDATE dbo.SatiDatabaseIdentity SET EnvironmentName=N'Production' WHERE Id=1;");
        Assert.Equal(54010, (await Assert.ThrowsAsync<SqlException>(() => fixture.RunAsync())).Number);
        await fixture.AssertPredecessorAsync(retained);
        await fixture.ExecuteAsync("UPDATE dbo.SatiDatabaseIdentity SET EnvironmentName=N'Demo' WHERE Id=1;");
        Assert.Equal(54010, (await Assert.ThrowsAsync<SqlException>(() => fixture.RunAsync(expectedDatabase: "SatiDemo"))).Number);
        await fixture.AssertPredecessorAsync(retained);
    }

    [SqlServerFact]
    public async Task ExactHistoryGuardRejectsSameCountReplacementAndMutationProvesSensitivity()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ExecuteAsync("""
            UPDATE dbo.__EFMigrationsHistory SET MigrationId=N'20000101000000_UnreviewedReplacement'
            WHERE MigrationId=(SELECT MIN(MigrationId) FROM dbo.__EFMigrationsHistory);
            """);
        Assert.Equal(129, await fixture.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.__EFMigrationsHistory;"));
        Assert.Equal(54011, (await Assert.ThrowsAsync<SqlException>(() => fixture.RunAsync(preflight: true))).Number);
        // The identical unsafe fixture passes only when this specific guard is removed.
        Assert.Equal(new Result(false, true, 129), await fixture.RunAsync(preflight: true,
            sqlOverride: RemoveGuard(await SqlAsync(), "HISTORY")));
    }

    [SqlServerFact]
    public async Task RefusesRecordedRotationHistoryGap()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ExecuteAsync($"INSERT dbo.__EFMigrationsHistory VALUES(N'{TargetMigration}',N'10.0.5');");
        Assert.Equal(54011, (await Assert.ThrowsAsync<SqlException>(() => fixture.RunAsync(preflight: true))).Number);
    }

    [SqlServerTheory]
    [InlineData("CHECK")]
    [InlineData("FK")]
    [InlineData("INDEX")]
    [InlineData("COLUMN")]
    public async Task RejectsSemanticDriftAndRemovedGuardAcceptsSameUnsafeFixture(string guard)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.RunAsync();
        var retained = await fixture.BusinessStateAsync();
        var mutate = guard switch
        {
            "CHECK" => """
                ALTER TABLE dbo.SignatureWorkRotation DROP CONSTRAINT CK_SignatureWorkRotation_Scope;
                ALTER TABLE dbo.SignatureWorkRotation WITH CHECK ADD CONSTRAINT CK_SignatureWorkRotation_Scope CHECK (WorkKind>0);
                """,
            "FK" => "ALTER TABLE dbo.ClearinghouseAccountPollRotation NOCHECK CONSTRAINT FK_ClearinghouseAccountPollRotation_ClearinghouseAccounts_AgencyId_AccountId;",
            "INDEX" => """
                DROP INDEX IX_ClearinghouseDispatches_QueuedLane ON dbo.ClearinghouseDispatches;
                CREATE INDEX IX_ClearinghouseDispatches_QueuedLane ON dbo.ClearinghouseDispatches(AgencyId,AccountId,RequestedAtUtc,Id) WHERE State=2;
                """,
            "COLUMN" => "ALTER TABLE dbo.SignatureWorkRotation ADD UnexpectedColumn int NULL;",
            _ => throw new ArgumentOutOfRangeException(nameof(guard))
        };
        await fixture.ExecuteAsync(mutate);
        Assert.Equal(54012, (await Assert.ThrowsAsync<SqlException>(() => fixture.RunAsync(preflight: true))).Number);
        Assert.Equal(new Result(false, false, 132), await fixture.RunAsync(preflight: true,
            sqlOverride: RemoveGuard(await SqlAsync(), guard)));
        Assert.Equal(retained, await fixture.BusinessStateAsync());
    }

    [SqlServerFact]
    public async Task DuplicateCheckpointRowsAreRejectedWithoutCleanupAndGuardMutationProvesDetection()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ExecuteAsync("""
            DROP INDEX IX_ClearinghouseFeedCheckpoints_AccountId_FeedKind ON dbo.ClearinghouseFeedCheckpoints;
            INSERT dbo.ClearinghouseFeedCheckpoints(Id,AgencyId,AccountId,FeedKind,[Cursor],LastReceiptId,UpdatedAtUtc,Revision)
              SELECT NEWID(),AgencyId,Id,1,N'123',NULL,'2026-10-10T12:00:00',3 FROM dbo.ClearinghouseAccounts;
            INSERT dbo.ClearinghouseFeedCheckpoints(Id,AgencyId,AccountId,FeedKind,[Cursor],LastReceiptId,UpdatedAtUtc,Revision)
              SELECT NEWID(),AgencyId,Id,1,N'456',NULL,'2026-10-10T12:01:00',4 FROM dbo.ClearinghouseAccounts;
            """);
        var retained = await fixture.BusinessStateAsync();
        Assert.Equal(54012, (await Assert.ThrowsAsync<SqlException>(() => fixture.RunAsync(preflight: true))).Number);
        Assert.Equal(new Result(false, true, 129), await fixture.RunAsync(preflight: true,
            sqlOverride: RemoveGuard(await SqlAsync(), "DUPLICATE")));
        Assert.Equal(54012, (await Assert.ThrowsAsync<SqlException>(() => fixture.RunAsync())).Number);
        await fixture.AssertPredecessorAsync(retained);
        Assert.Equal(2, await fixture.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ClearinghouseFeedCheckpoints;"));
    }

    [SqlServerFact]
    public async Task RefusesEnabledRotationTriggerBeforeSeedAdoptionAndMutationProvesDetection()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ApplySourceAsync(3, recordHistory: false);
        var retained = await fixture.BusinessStateAsync();
        await fixture.ExecuteAsync("DELETE dbo.SignatureWorkRotation WHERE WorkKind=3;");
        await fixture.ExecuteAsync("""
            CREATE TRIGGER dbo.SyntheticUnexpectedRotationTrigger ON dbo.SignatureWorkRotation AFTER INSERT AS
            BEGIN
              THROW 54091, 'Synthetic unreviewed seed side effect.', 1;
            END;
            """);
        Assert.Equal(54012, (await Assert.ThrowsAsync<SqlException>(() => fixture.RunAsync(preflight: true))).Number);
        Assert.Equal(new Result(false, true, 129), await fixture.RunAsync(preflight: true,
            sqlOverride: RemoveGuard(await SqlAsync(), "TRIGGER")));
        Assert.Equal(54012, (await Assert.ThrowsAsync<SqlException>(() => fixture.RunAsync())).Number);
        Assert.Equal(2, await fixture.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SignatureWorkRotation;"));
        Assert.Equal(129, await fixture.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.__EFMigrationsHistory;"));
        Assert.Equal(retained, await fixture.BusinessStateAsync());
    }

    [SqlServerFact]
    public async Task LateHistoryFailureRollsBackAllSevenTablesIndexesAndSeeds()
    {
        await using var fixture = await Fixture.CreateAsync();
        var retained = await fixture.BusinessStateAsync();
        await fixture.ExecuteAsync($"""
            CREATE TRIGGER dbo.SyntheticRejectRotationHistory ON dbo.__EFMigrationsHistory AFTER INSERT AS
            BEGIN
              IF EXISTS(SELECT 1 FROM inserted WHERE MigrationId=N'{TargetMigration}')
                THROW 54090, 'Synthetic late rotation migration failure.', 1;
            END;
            """);
        Assert.Equal(54090, (await Assert.ThrowsAsync<SqlException>(() => fixture.RunAsync())).Number);
        await fixture.AssertPredecessorAsync(retained);
    }

    [SqlServerFact]
    public async Task RefusesExecutionWithoutCallerTransaction()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var connection = new SqlConnection(fixture.Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = await SqlAsync();
        AddParameters(command, fixture.Database.Name, false);
        Assert.Equal(54010, (await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync())).Number);
    }

    private static string RemoveGuard(string sql, string guard)
    {
        var start = sql.IndexOf($"-- {guard}_GUARD_BEGIN", StringComparison.Ordinal);
        var end = sql.IndexOf($"-- {guard}_GUARD_END", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        // A removed guard can be the only statement in a BEGIN/END block.
        // Keep valid SQL so the comparison tests the absent protection, not parsing.
        return sql.Remove(start, end - start).Insert(start, "PRINT N'Synthetic guard removed';\n");
    }

    private static async Task<string> SqlAsync()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SatiLogica.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        return await File.ReadAllTextAsync(Path.Combine(root.FullName, "scripts/Apply-WorkerRotationMigrations.guarded.sql"));
    }

    private static void AddParameters(SqlCommand command, string database, bool preflight)
    {
        command.Parameters.Add("@expectedDatabase", SqlDbType.NVarChar, 128).Value = database;
        command.Parameters.Add("@preflightOnly", SqlDbType.Bit).Value = preflight;
    }

    private sealed record Result(bool MigrationWasApplied, bool ChangesRequired, int MigrationCount);

    private sealed class Fixture : IAsyncDisposable
    {
        public SyntheticPipelineDatabase Database { get; } = new(sqlServer: true);
        private ApiDbContext OpenDatabase() => new(new DbContextOptionsBuilder<ApiDbContext>().UseSqlServer(Database.ConnectionString).Options);
        private static Migration[] SourceMigrations() =>
        [
            new AddClearinghouseDispatchRotation { ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer" },
            new AddClearinghousePollRotation { ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer" },
            new AddSignatureWorkRotation { ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer" }
        ];

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
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
                    IsTest = true, IsEnabled = true, ExternalAccountNumber = "ROTATIONTEST", ClaimNamespace = "TEST",
                    TradingPartnerProfileVersion = TradingPartnerProfile.CurrentVersion,
                    CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
                };
                db.ClearinghouseAccounts.Add(account);
                await db.SaveChangesAsync();
                var generation = new ServerEdiGeneration
                {
                    AgencyId = actors.AgencyId, ActorUserId = actors.BillerId, BillingPeriodId = period.Id,
                    IdempotencyKey = Guid.NewGuid().ToString("N"), IsTest = true, ControlNumber = "123456789",
                    FileName = "synthetic-rotation.CMDTEST.txt", Content = "Synthetic retained EDI\r\n~ unchanged bytes."
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
                foreach (var migration in SourceMigrations().Reverse())
                    foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.DownOperations, db.Model))
                        await db.Database.ExecuteSqlRawAsync(command.CommandText);
                var migrations = typeof(AddSignatureWorkRotation).Assembly.GetTypes()
                    .Select(type => type.GetCustomAttribute<MigrationAttribute>()?.Id).OfType<string>()
                    .Where(id => StringComparer.Ordinal.Compare(id, TargetMigration) <= 0).Order(StringComparer.Ordinal).ToArray();
                Assert.Equal(132, migrations.Length);
                await fixture.ExecuteAsync("CREATE TABLE dbo.__EFMigrationsHistory(MigrationId nvarchar(150) NOT NULL PRIMARY KEY,ProductVersion nvarchar(32) NOT NULL);");
                foreach (var id in migrations[..^3])
                    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.__EFMigrationsHistory VALUES({id},N'10.0.5');");
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public async Task ApplySourceAsync(int count, bool recordHistory)
        {
            await using var db = OpenDatabase();
            foreach (var migration in SourceMigrations().Take(count))
            {
                foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, db.Model))
                    await db.Database.ExecuteSqlRawAsync(command.CommandText);
                if (recordHistory)
                {
                    var id = migration.GetType().GetCustomAttribute<MigrationAttribute>()!.Id;
                    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.__EFMigrationsHistory VALUES({id},N'10.0.5');");
                }
            }
        }

        public async Task<Result> RunAsync(bool rollback = false, bool preflight = false, string? expectedDatabase = null, string? sqlOverride = null)
        {
            await using var connection = new SqlConnection(Database.ConnectionString);
            await connection.OpenAsync();
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 90;
            command.CommandText = sqlOverride ?? await SqlAsync();
            AddParameters(command, expectedDatabase ?? Database.Name, preflight);
            Result result;
            await using (var reader = await command.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                result = new(reader.GetBoolean(0), reader.GetBoolean(1), reader.GetInt32(2));
            }
            command.CommandText = "SELECT APPLOCK_MODE(N'public',N'SatiDemo.FullReset',N'Transaction');";
            Assert.Equal("Exclusive", await command.ExecuteScalarAsync());
            if (rollback) await transaction.RollbackAsync(); else await transaction.CommitAsync();
            return result;
        }

        public async Task AssertPredecessorAsync(string retained)
        {
            Assert.Equal(129, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.__EFMigrationsHistory;"));
            foreach (var table in Tables)
                Assert.Equal(0, await ScalarAsync<int>($"SELECT COUNT(*) FROM sys.tables WHERE object_id=OBJECT_ID(N'dbo.{table}');"));
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE name IN (N'IX_ClearinghouseDispatches_QueuedLane',N'IX_ClearinghouseFeedCheckpoints_AgencyId_AccountId_FeedKind',N'IX_ClearinghouseAccounts_AgencyId_IsEnabled_IsTest_ConnectorKind_Id',N'IX_SignatureOutbox_AgencyId_Id',N'IX_SignatureCompletions_AgencyId_Id',N'IX_SignatureAgencyWorkRotation_WorkKind');"));
            Assert.Equal(retained, await BusinessStateAsync());
        }

        public async Task<string> BusinessStateAsync()
        {
            var values = new List<string>();
            foreach (var table in new[] { "ClearinghouseAccounts", "ClearinghouseDispatches", "EdiGenerations", "ClearinghouseFeedCheckpoints", "ClearinghouseDispatchReadiness" })
                values.Add(await ScalarAsync<string>($"SELECT * FROM dbo.{table} ORDER BY 1 FOR JSON PATH,INCLUDE_NULL_VALUES;"));
            return string.Join("\n", values);
        }

        public async Task<string> PositionsAsync()
        {
            var values = new List<string>();
            foreach (var table in Tables)
                values.Add(await ScalarAsync<string>($"SELECT * FROM dbo.{table} ORDER BY 1 FOR JSON PATH,INCLUDE_NULL_VALUES;"));
            return string.Join("\n", values);
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
