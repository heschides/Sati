using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models.Billing;
using Sati.Persistence.Migrations;
using Xunit;

namespace Sati.Api.Tests;

public sealed class ClearinghousePhase2PersistenceTests
{
    [Fact]
    public async Task OnlyOneEnabledAccountPerAgencyPartnerAndEnvironment()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Context();
        db.ClearinghouseAccounts.Add(Account(1, "FIRST"));
        await db.SaveChangesAsync();
        db.ClearinghouseAccounts.Add(Account(1, "SECOND"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task DispatchCannotPointToAnotherAgencysAccountOrGeneration()
    {
        await using var fixture = await Fixture.CreateAsync();
        var foreignAccount = Account(2, "FOREIGN");
        await using (var db = fixture.Context())
        {
            db.ClearinghouseAccounts.Add(foreignAccount);
            await db.SaveChangesAsync();
        }
        await using (var db = fixture.Context())
        {
            db.ClearinghouseDispatches.Add(Dispatch(1, foreignAccount.Id, fixture.GenerationId));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        var localAccount = Account(1, "LOCAL");
        await using (var db = fixture.Context())
        {
            db.ClearinghouseAccounts.Add(localAccount);
            await db.SaveChangesAsync();
        }
        await using (var db = fixture.Context())
        {
            db.ClearinghouseDispatches.Add(Dispatch(2, foreignAccount.Id, fixture.GenerationId));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task OneRetainedGenerationHasOnlyOneDispatchAndOneAttemptNumber()
    {
        await using var fixture = await Fixture.CreateAsync();
        var account = Account(1, "CLAIMONE");
        var dispatch = Dispatch(1, account.Id, fixture.GenerationId);
        await using (var db = fixture.Context())
        {
            db.ClearinghouseAccounts.Add(account);
            db.ClearinghouseDispatches.Add(dispatch);
            await db.SaveChangesAsync();
        }
        await using (var db = fixture.Context())
        {
            db.ClearinghouseDispatches.Add(Dispatch(1, account.Id, fixture.GenerationId));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = fixture.Context())
        {
            db.ClearinghouseDispatchAttempts.Add(Attempt(dispatch.Id));
            await db.SaveChangesAsync();
        }
        await using (var db = fixture.Context())
        {
            db.ClearinghouseDispatchAttempts.Add(Attempt(dispatch.Id));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task AttemptAndDispatchIdentityCannotBeRewritten()
    {
        await using var fixture = await Fixture.CreateAsync();
        var account = Account(1, "CLAIMONE");
        var dispatch = Dispatch(1, account.Id, fixture.GenerationId);
        await using (var db = fixture.Context())
        {
            db.ClearinghouseAccounts.Add(account);
            db.ClearinghouseDispatches.Add(dispatch);
            db.ClearinghouseDispatchAttempts.Add(Attempt(dispatch.Id));
            await db.SaveChangesAsync();
        }
        await using (var db = fixture.Context())
        {
            (await db.ClearinghouseDispatchAttempts.SingleAsync()).ContentSha256 = new string('0', 64);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using (var db = fixture.Context())
        {
            var retained = await db.ClearinghouseDispatches.SingleAsync();
            retained.AccountId = Guid.NewGuid();
            retained.Revision++;
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task DispatchRevisionDetectsConcurrentUpdates()
    {
        await using var fixture = await Fixture.CreateAsync();
        var account = Account(1, "CLAIMONE");
        var dispatch = Dispatch(1, account.Id, fixture.GenerationId);
        await using (var seed = fixture.Context())
        {
            seed.ClearinghouseAccounts.Add(account);
            seed.ClearinghouseDispatches.Add(dispatch);
            await seed.SaveChangesAsync();
        }
        await using var first = fixture.Context();
        await using var second = fixture.Context();
        var firstRow = await first.ClearinghouseDispatches.SingleAsync();
        var secondRow = await second.ClearinghouseDispatches.SingleAsync();
        firstRow.State = ClearinghouseDispatchState.Sending;
        firstRow.Revision++;
        await first.SaveChangesAsync();
        secondRow.State = ClearinghouseDispatchState.CancelledBeforeSend;
        secondRow.Revision++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task OutcomeUnknownCannotBeSentAgainAndAccountNamespaceCannotChange()
    {
        await using var fixture = await Fixture.CreateAsync();
        var account = Account(1, "CLAIMONE");
        var dispatch = Dispatch(1, account.Id, fixture.GenerationId);
        dispatch.State = ClearinghouseDispatchState.OutcomeUnknown;
        await using (var seed = fixture.Context())
        {
            seed.ClearinghouseAccounts.Add(account);
            seed.ClearinghouseDispatches.Add(dispatch);
            await seed.SaveChangesAsync();
        }
        await using (var db = fixture.Context())
        {
            var retained = await db.ClearinghouseDispatches.SingleAsync();
            retained.State = ClearinghouseDispatchState.Sending;
            retained.Revision++;
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using (var db = fixture.Context())
        {
            var retained = await db.ClearinghouseAccounts.SingleAsync();
            retained.ClaimNamespace = "CHANGED";
            retained.Revision++;
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task ConnectorReceiptNeedsArtifactProvenanceAndManualReceiptNeedsHumanActor()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.Context())
        {
            db.ClearinghouseResponseReceipts.Add(new ClearinghouseResponseReceipt
            {
                Id = Guid.NewGuid(), AgencyId = 1, Source = ClearinghouseReceiptSource.Connector
            });
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using (var db = fixture.Context())
        {
            db.ClearinghouseResponseReceipts.Add(new ClearinghouseResponseReceipt
            {
                Id = Guid.NewGuid(), AgencyId = 1, Source = ClearinghouseReceiptSource.Manual
            });
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public void DesktopAndApiMapTheSameClearinghouseSchema()
    {
        using var local = new SatiContext(new DbContextOptionsBuilder<SatiContext>()
            .UseSqlite("Data Source=:memory:").Options);
        using var server = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        foreach (var type in new[] { typeof(ClearinghouseAccount), typeof(ClearinghouseDispatch),
                     typeof(ClearinghouseDispatchAttempt), typeof(ClearinghouseFeedCheckpoint),
                     typeof(ClearinghouseResponseReceipt) })
        {
            var desktopEntity = local.Model.FindEntityType(type)!;
            var apiEntity = server.Model.FindEntityType(type)!;
            Assert.Equal(desktopEntity.GetTableName(), apiEntity.GetTableName());
            Assert.Equal(desktopEntity.GetProperties().Select(x => x.Name).Order(),
                apiEntity.GetProperties().Select(x => x.Name).Order());
            Assert.Equal(desktopEntity.GetIndexes().Select(x =>
                    (string.Join(',', x.Properties.Select(p => p.Name)), x.IsUnique, x.GetFilter())).OrderBy(x => x.Item1),
                apiEntity.GetIndexes().Select(x =>
                    (string.Join(',', x.Properties.Select(p => p.Name)), x.IsUnique, x.GetFilter())).OrderBy(x => x.Item1));
            Assert.Equal(desktopEntity.GetForeignKeys().Select(x =>
                    (string.Join(',', x.Properties.Select(p => p.Name)), x.PrincipalEntityType.GetTableName())).OrderBy(x => x.Item1),
                apiEntity.GetForeignKeys().Select(x =>
                    (string.Join(',', x.Properties.Select(p => p.Name)), x.PrincipalEntityType.GetTableName())).OrderBy(x => x.Item1));
        }
    }

    [Fact]
    public void MigrationAddsOnlyScopedTablesAndProvenanceWithoutErasingData()
    {
        var operations = new AddClearinghouseDispatchFoundation().UpOperations;
        Assert.DoesNotContain(operations, operation => operation is DropTableOperation or DropColumnOperation or DeleteDataOperation or UpdateDataOperation);
        Assert.Contains(operations.OfType<CreateTableOperation>(), x => x.Name == "ClearinghouseDispatchAttempts");
        Assert.Contains(operations.OfType<AddColumnOperation>(), x => x.Table == "ClearinghouseResponseReceipts" && x.Name == "Source");
        Assert.Contains(operations.OfType<CreateIndexOperation>(), x => x.Table == "ClearinghouseDispatches" && x.IsUnique && x.Columns.SequenceEqual(["EdiGenerationId"]));
    }

    [Fact]
    public void MigrationRefusesRollbackWhenNewFinancialEvidenceExists()
    {
        using var db = new SatiContext(new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=SatiMigrationScriptOnly;Integrated Security=true")
            .Options);
        var migrations = db.Database.GetMigrations().ToList();
        var migrationId = Assert.Single(migrations,
            id => id.EndsWith("AddClearinghouseDispatchFoundation", StringComparison.Ordinal));
        Assert.EndsWith("AddClearinghouseDispatchFoundation", migrationId);
        var migrationIndex = migrations.IndexOf(migrationId);
        Assert.True(migrationIndex > 0);
        var previousId = migrations[migrationIndex - 1];
        var rollback = db.GetService<IMigrator>().GenerateScript(migrationId, previousId);
        Assert.Contains("ClearinghouseDispatchAttempts", rollback);
        Assert.Contains("prevents rollback", rollback);
        Assert.Contains("THROW 51001", rollback);
    }

    private static ClearinghouseAccount Account(int agencyId, string claimNamespace) => new()
    {
        Id = Guid.NewGuid(), AgencyId = agencyId, ConnectorKind = TradingPartnerKind.ClaimMd,
        IsTest = true, ExternalAccountNumber = $"ACCOUNT-{agencyId}", ClaimNamespace = claimNamespace,
        TradingPartnerProfileVersion = TradingPartnerProfile.CurrentVersion, IsEnabled = true,
        CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
    };

    private static ClearinghouseDispatch Dispatch(int agencyId, Guid accountId, long generationId) => new()
    {
        Id = Guid.NewGuid(), AgencyId = agencyId, AccountId = accountId,
        EdiGenerationId = generationId, RequestingUserId = agencyId == 1 ? 7 : 8,
        RequestedAtUtc = DateTime.UtcNow, State = ClearinghouseDispatchState.Queued,
        TradingPartnerProfileVersion = TradingPartnerProfile.CurrentVersion
    };

    private static ClearinghouseDispatchAttempt Attempt(Guid dispatchId) => new()
    {
        Id = Guid.NewGuid(), DispatchId = dispatchId, AttemptNumber = 1,
        StartedAtUtc = DateTime.UtcNow.AddSeconds(-1), CompletedAtUtc = DateTime.UtcNow,
        ContentSha256 = new string('A', 64), FileName = "synthetic.837",
        Outcome = ClearinghouseAttemptOutcome.OutcomeUnknown
    };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public long GenerationId { get; private set; }
        private Fixture(SqliteConnection connection) => _connection = connection;
        public ApiDbContext Context() => new(new DbContextOptionsBuilder<ApiDbContext>()
            .UseSqlite(_connection).Options);

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var fixture = new Fixture(connection);
            await using var db = fixture.Context();
            await db.Database.EnsureCreatedAsync();
            db.Agencies.AddRange(new ServerAgency { Id = 1, Name = "Synthetic One" },
                new ServerAgency { Id = 2, Name = "Synthetic Two" });
            db.Users.AddRange(new ServerUser { Id = 7, AgencyId = 1, Username = "one", Role = "Admin" },
                new ServerUser { Id = 8, AgencyId = 2, Username = "two", Role = "Admin" });
            db.BillingPeriods.Add(new ServerBillingPeriod { Id = 77, UserId = 7, Year = 2026, Month = 9 });
            var generation = new ServerEdiGeneration { AgencyId = 1, ActorUserId = 7, BillingPeriodId = 77,
                IdempotencyKey = "synthetic-first", IsTest = true, FileName = "synthetic.837", Content = "TEST" };
            db.EdiGenerations.Add(generation);
            await db.SaveChangesAsync();
            fixture.GenerationId = generation.Id;
            return fixture;
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
}
