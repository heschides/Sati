using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Persistence.Migrations;
using Sati.TestFixtures;
using Xunit;

namespace Sati.Api.Tests;
[Collection("Synthetic pipeline SQL Server")]
public class PayerBillingSqlTests
{
    [SqlServerFact] public async Task GuardedScriptRollsBackRerunsAndRejectsIncompatibleExistingSchema()
    {
        await using var database = new SyntheticPipelineDatabase(sqlServer: true); await database.InitializeAsync();
        await using var factory = new SyntheticPipelineFactory(database); await factory.SeedAsync();
        var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var db = new ApiDbContext(options);
        var migration = new AddPayerBillingConfigurationVersions { ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer" };
        await ApplyAsync(db, migration.DownOperations);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE dbo.__EFMigrationsHistory(MigrationId nvarchar(150) NOT NULL PRIMARY KEY, ProductVersion nvarchar(32) NOT NULL);
            DECLARE @n int=1; WHILE @n<=124 BEGIN
              INSERT dbo.__EFMigrationsHistory VALUES(CONCAT(N'20260000000000_Synthetic_',@n),N'10.0.5'); SET @n+=1;
            END;
            INSERT dbo.__EFMigrationsHistory VALUES(N'20261004120026_AddNoteAmendments',N'10.0.5');
            """);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SatiLogica.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var sql = await File.ReadAllTextAsync(Path.Combine(root.FullName, "scripts/Apply-PayerBillingMigration.guarded.sql"));
        await using var connection = new SqlConnection(database.ConnectionString); await connection.OpenAsync();
        await Run(true); await Run(false); await Run(false);
        await using var corrupt = connection.CreateCommand();
        corrupt.CommandText = "ALTER TABLE dbo.PayerBillingConfigurationVersions ALTER COLUMN ConfigurationJson nvarchar(999) NOT NULL;"; await corrupt.ExecuteNonQueryAsync();
        var error = await Assert.ThrowsAsync<SqlException>(() => Run(false)); Assert.Equal(53702, error.Number);
        async Task Run(bool rollback)
        {
            await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(); await using var command = connection.CreateCommand();
            command.Transaction = tx; command.CommandText = sql; command.Parameters.AddWithValue("@expectedDatabase", database.Name);
            await command.ExecuteNonQueryAsync(); command.CommandText = "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory;";
            Assert.Equal(126, Convert.ToInt32(await command.ExecuteScalarAsync()));
            if (rollback) await tx.RollbackAsync(); else await tx.CommitAsync();
        }
    }
    [SqlServerFact] public async Task AdditiveMigrationPreservesLegacyClaimsAndRefusesRollbackAfterPublication()
    {
        await using var database = new SyntheticPipelineDatabase(sqlServer: true); await database.InitializeAsync();
        await using var factory = new SyntheticPipelineFactory(database); var users = await factory.SeedAsync();
        var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var db = new ApiDbContext(options);
        var migration = new AddPayerBillingConfigurationVersions { ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer" };
        var legacyJson = ProfessionalClaimSnapshotCodec.Serialize(PayerBillingSynthetic.Legacy(users.AgencyId, users.FirstPersonId));
        var note = new ServerNote { AgencyId = users.AgencyId, PersonId = users.FirstPersonId, Narrative = "Synthetic migration record", Status = 6, EventDate = new(2026, 8, 1), Minutes = 15 };
        db.Notes.Add(note); await db.SaveChangesAsync();
        var period = new ServerBillingPeriod { UserId = users.AuthorId, Year = 2026, Month = 8 };
        period.Lines.Add(new() { NoteId = note.Id, DateOfService = new(2026, 8, 1), ProcedureCode = "G9012", Units = 1, ChargeAmount = 25, ClaimSnapshotJson = legacyJson });
        db.BillingPeriods.Add(period); await db.SaveChangesAsync();
        await Apply(migration.DownOperations); await Apply(migration.UpOperations);
        Assert.Equal(legacyJson, (await db.ClaimLines.SingleAsync()).ClaimSnapshotJson);
        // Imported financial evidence must prevent a downgrade even when this table is empty.
        var version = PayerBillingSynthetic.Version(users.AgencyId);
        var v2 = PayerBillingRules.Freeze(PayerBillingSynthetic.Legacy(users.AgencyId, users.FirstPersonId),
            version, PayerBillingSynthetic.Preparation(version, users.FirstPersonId), users.BillerId,
            DateTime.UtcNow, new(2026, 8, 1));
        var line = await db.ClaimLines.SingleAsync();
        line.ClaimSnapshotJson = ProfessionalClaimSnapshotCodec.Serialize(v2); await db.SaveChangesAsync();
        var snapshotError = await Assert.ThrowsAsync<SqlException>(() => Apply(migration.DownOperations));
        Assert.Equal(51032, snapshotError.Number);
        line.ClaimSnapshotJson = legacyJson; await db.SaveChangesAsync();
        var actor = new AgencyActor(users.BillerId, users.AgencyId, UserPermissions.AllAgencyPermissions);
        await using (var tx = await PayerBillingStore.BeginPublicationAsync(db, users.AgencyId))
        {
            await PayerBillingStore.AppendAsync(db, actor, new(Guid.NewGuid(), 0, PayerBillingSynthetic.Configuration()), DateTime.UtcNow, _ => { });
            await tx.CommitAsync();
        }
        var error = await Assert.ThrowsAsync<SqlException>(() => Apply(migration.DownOperations)); Assert.Equal(51032, error.Number);
        Assert.Single(await db.PayerBillingConfigurationVersions.ToListAsync());
        Assert.Equal(legacyJson, (await db.ClaimLines.AsNoTracking().SingleAsync()).ClaimSnapshotJson);
        async Task Apply(IReadOnlyList<Microsoft.EntityFrameworkCore.Migrations.Operations.MigrationOperation> operations)
        { await ApplyAsync(db, operations); }
    }
    private static async Task ApplyAsync(ApiDbContext db, IReadOnlyList<Microsoft.EntityFrameworkCore.Migrations.Operations.MigrationOperation> operations)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            foreach (var generated in db.GetService<IMigrationsSqlGenerator>().Generate(operations, db.Model))
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = generated.CommandText; await command.ExecuteNonQueryAsync();
            }
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
    [SqlServerFact] public async Task TwoConnectionsSerializePublicationAndStaleWriterMakesNoVersion()
    {
        await using var database = new SyntheticPipelineDatabase(sqlServer: true); await database.InitializeAsync();
        await using var factory = new SyntheticPipelineFactory(database); var users = await factory.SeedAsync();
        var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlServer(database.ConnectionString).Options;
        var actor = new AgencyActor(users.BillerId, users.AgencyId, UserPermissions.AllAgencyPermissions);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Write(true); await held.Task.WaitAsync(TimeSpan.FromSeconds(20)); var second = Write(false);
        try
        {
            var end = DateTime.UtcNow.AddSeconds(10);
            while (!await database.HasWaitingApplicationLockAsync() && DateTime.UtcNow < end) await Task.Delay(25);
            Assert.True(await database.HasWaitingApplicationLockAsync(), "The second writer must wait on the database-owned payer publication lock.");
        }
        finally { release.TrySetResult(); }
        Assert.Null(await first); Assert.IsType<PayerBillingConflictException>(await second);
        await using var verify = new ApiDbContext(options); Assert.Single(await verify.PayerBillingConfigurationVersions.ToListAsync());
        async Task<Exception?> Write(bool pause)
        {
            try
            {
                await using var db = new ApiDbContext(options); await using var tx = await PayerBillingStore.BeginPublicationAsync(db, users.AgencyId);
                await PayerBillingStore.AppendAsync(db, actor, new(Guid.NewGuid(), 0, PayerBillingSynthetic.Configuration() with { EffectiveOn = pause ? new(2026, 4, 28) : new(2026, 5, 1) }), DateTime.UtcNow, _ => { });
                if (pause) { held.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
                await tx.CommitAsync(); return null;
            }
            catch (Exception e) { if (pause) held.TrySetException(e); return e; }
        }
    }
}
