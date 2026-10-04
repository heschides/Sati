using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Api.Tests;

public sealed class DemoExternalClearinghouseGuardSqlTests
{
    [SqlServerFact]
    public async Task OwnerExecutedGuardWorksWithDeniedBaselineAccessAndProtectsLiveAndBaselineHistory()
    {
        await using var database = new SyntheticPipelineDatabase(sqlServer: true);
        await database.InitializeAsync();
        await using var db = new ApiDbContext(database.Options());
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        var repository = RepositoryRoot();
        var helper = File.ReadAllText(Path.Combine(repository,
            "Sati.DemoRefresh", "Shared", "ExternalClearinghouseResetGuard.ps1"));
        var opening = helper.IndexOf("return @\"", StringComparison.Ordinal);
        var closing = helper.LastIndexOf("\"@", StringComparison.Ordinal);
        Assert.True(opening >= 0 && closing > opening);
        var guard = helper[(opening + "return @\"".Length)..closing]
            .Replace("$requireBaselineFlag", "1", StringComparison.Ordinal);
        var initializer = File.ReadAllText(Path.Combine(repository, "scripts", "Initialize-DemoFullReset.ps1"));
        var procedureStart = initializer.IndexOf("CREATE OR ALTER PROCEDURE dbo.SatiAssertCanonicalResetAllowed",
            StringComparison.Ordinal);
        var procedureEnd = initializer.IndexOf("\"@", procedureStart, StringComparison.Ordinal);
        Assert.True(procedureStart >= 0 && procedureEnd > procedureStart);
        // Execute the actual installer procedure, changing only its environment assertion
        // to this fixture's owned synthetic name and substituting its shared SQL source.
        var procedure = initializer[procedureStart..procedureEnd]
            .Replace("$restoreExternalGuardSql", guard, StringComparison.Ordinal)
            .Replace("DB_NAME() <> N'SatiDemo'", $"DB_NAME() <> N'{database.Name}'", StringComparison.Ordinal);
        await ExecuteAsync(connection, "CREATE SCHEMA demo_baseline AUTHORIZATION dbo;");
        foreach (var table in new[] { "ClearinghouseAccounts", "ClearinghouseDispatches",
                     "ClearinghouseDispatchAttempts", "ClearinghouseResponseReceipts", "ClearinghouseResponseMatches",
                     "ClearinghouseFeedCheckpoints", "AuditEvents" })
            await ExecuteAsync(connection, $"SELECT * INTO demo_baseline.[{table}] FROM dbo.[{table}];");
        db.DatabaseIdentities.Add(new ServerDatabaseIdentity
            { EnvironmentName = "Demo", InstanceId = Guid.NewGuid(), CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await ExecuteAsync(connection, procedure);
        await ExecuteAsync(connection, """
            CREATE USER SatiResetGuardTest WITHOUT LOGIN;
            DENY SELECT, INSERT, UPDATE, DELETE ON SCHEMA::demo_baseline TO SatiResetGuardTest;
            GRANT EXECUTE ON dbo.SatiAssertCanonicalResetAllowed TO SatiResetGuardTest;
            """);
        var denied = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsResetAsync(connection,
            "SELECT COUNT(*) FROM demo_baseline.ClearinghouseAccounts;"));
        Assert.Equal(229, denied.Number);
        await AssertAllowedAsync(connection);

        var agency = new ServerAgency { Name = "Isolated guard test" };
        db.Agencies.Add(agency);
        await db.SaveChangesAsync();
        var account = new ClearinghouseAccount
        {
            Id = Guid.NewGuid(), AgencyId = agency.Id, ConnectorKind = TradingPartnerKind.ClaimMd,
            IsEnabled = false, IsTest = true, ExternalAccountNumber = "SYNTHETIC",
            ClaimNamespace = "GUARD001", TradingPartnerProfileVersion = TradingPartnerProfile.CurrentVersion,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        db.ClearinghouseAccounts.Add(account);
        await db.SaveChangesAsync();
        await AssertAllowedAsync(connection); // Pure fake account remains resettable.
        await ExecuteAsync(connection,
            "UPDATE dbo.ClearinghouseAccounts SET SecretReference=N'CLAIMMD_SANDBOX_KEY_GUARD001';");
        await AssertBlockedAsync(connection); // Disabled is not unlinked.
        await ExecuteAsync(connection,
            "INSERT demo_baseline.ClearinghouseAccounts SELECT * FROM dbo.ClearinghouseAccounts; " +
            "UPDATE dbo.ClearinghouseAccounts SET SecretReference=NULL;");
        await AssertBlockedAsync(connection); // Baseline cannot restore vendor routing.
        await ExecuteAsync(connection, "DELETE demo_baseline.ClearinghouseAccounts;");
        await AssertAllowedAsync(connection);

        db.AuditEvents.Add(new ServerAuditEvent
        {
            AgencyId = agency.Id, ActorUserId = 0, ResourceType = "ClearinghouseAccount",
            Action = "billing-clearinghouse.claimmd-test-account-onboarded"
        });
        await db.SaveChangesAsync();
        await AssertBlockedAsync(connection); // Deleted/disabled metadata cannot erase the linkage evidence.
    }

    private static Task AssertAllowedAsync(SqlConnection connection) =>
        ExecuteAsResetAsync(connection, "EXEC dbo.SatiAssertCanonicalResetAllowed;");

    private static async Task AssertBlockedAsync(SqlConnection connection)
    {
        var exception = await Assert.ThrowsAsync<SqlException>(() => AssertAllowedAsync(connection));
        Assert.Equal(51012, exception.Number);
        Assert.Contains("DemoResetBlockedByExternalClearinghouseState", exception.Message);
    }

    private static Task ExecuteAsResetAsync(SqlConnection connection, string command) =>
        ExecuteAsync(connection, "EXECUTE AS USER = N'SatiResetGuardTest'; BEGIN TRY " + command +
            " REVERT; END TRY BEGIN CATCH IF @@TRANCOUNT > 0 ROLLBACK; REVERT; THROW; END CATCH;");

    private static async Task ExecuteAsync(SqlConnection connection, string text)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = text;
        command.CommandTimeout = 30;
        await command.ExecuteNonQueryAsync();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Sati repository root was not found.");
    }
}
