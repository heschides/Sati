using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Api.Tests;

public sealed class DemoWatchdogSchemaTests
{
    [Fact]
    public void ReadOnlyQueriesNameMappedTablesColumnsAndPersistedStates()
    {
        using var db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>()
            .UseSqlServer("Server=(localdb)\\SatiUnusedTestPlaceholder;Database=SatiWatchdogSchemaOnly;Trusted_Connection=True")
            .Options);
        var model = db.Model;
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(),
            "Sati.DemoRefresh", "Shared", "DemoWatchdog.ps1"));

        // The SQL text is executed outside EF. Keep its table and column names
        // tied to the actual API/EF schema, rather than a mock query result.
        var mappedTables = model.GetEntityTypes().Select(entity => entity.GetTableName())
            .Where(name => name is not null).ToHashSet(StringComparer.Ordinal);
        var queriedTables = Regex.Matches(script, @"dbo\.(\w+)")
            .Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(queriedTables);
        Assert.All(queriedTables, table => Assert.Contains(table, mappedTables));

        AssertColumns<ServerDatabaseIdentity>(model, "SatiDatabaseIdentity", "Id", "EnvironmentName");
        AssertColumns<ServerAuditEvent>(model, "AuditEvents", "Action", "OccurredAtUtc", "Id");
        AssertColumns<ServerNote>(model, "Notes", "AgencyId", "PersonId", "Status", "EventDate");
        AssertColumns<ServerPerson>(model, "People", "AgencyId", "Id");
        AssertColumns<ServerSettings>(model, "Settings", "AgencyId", "AbandonedAfterDays",
            "IsInternalElectronicSignatureEnabled");
        AssertColumns<ClearinghouseAccount>(model, "ClearinghouseAccounts", "AgencyId", "Id", "IsEnabled", "IsTest");
        AssertColumns<ClearinghouseDispatch>(model, "ClearinghouseDispatches", "AgencyId", "AccountId", "State", "RequestedAtUtc");
        AssertColumns<Sati.Models.SignatureCompletion>(model, "SignatureCompletions", "AgencyId", "Id", "SignedAtUtc");
        AssertColumns<Sati.Models.SignaturePackage>(model, "SignaturePackages", "AgencyId", "CompletionId");
        AssertColumns<Sati.Models.SignatureOutbox>(model, "SignatureOutbox", "AgencyId", "Purpose", "State");

        Assert.Equal(1, NoteWorkflow.Pending);
        Assert.Equal(1, (int)ClearinghouseDispatchState.Queued);
        Assert.Equal(2, (int)ClearinghouseDispatchState.Sending);
        Assert.Equal(5, (int)ClearinghouseDispatchState.OutcomeUnknown);
        Assert.Contains("n.Status = 1", script);
        Assert.Contains("d.State IN (1, 2)", script);
        Assert.Contains("d.State = 5", script);
        Assert.Contains("o.Purpose IN (N'Invitation', N'Receipt')", script);
        Assert.Contains("o.State IN (N'NeedsReview', N'Failed')", script);
        Assert.Contains("CASE WHEN s.AbandonedAfterDays > 0 THEN s.AbandonedAfterDays ELSE 7 END + 2", script);
        Assert.Contains("n.EventDate < DATEADD", script);
    }

    [SqlServerFact]
    public async Task WatchdogSelectsCompileAgainstIsolatedSqlServerSchema()
    {
        await using var database = new SyntheticPipelineDatabase(sqlServer: true);
        await database.InitializeAsync();
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(),
            "Sati.DemoRefresh", "Shared", "DemoWatchdog.ps1"));
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        foreach (var (functionName, parameterName, parameterValue) in new[]
                 {
                     ("Assert-DemoWatchdogDatabase", "", (object)DBNull.Value),
                     ("Read-DemoWatchdogResetState", "@ScheduledStartUtc", (object)new DateTime(2026, 10, 3, 7, 15, 0)),
                     ("Read-DemoWatchdogOverdueCount", "@Today", (object)new DateTime(2026, 10, 3)),
                     ("Read-DemoWatchdogBillingState", "@NowUtc", (object)new DateTime(2026, 10, 3, 8, 0, 0)),
                     ("Read-DemoWatchdogSignatureState", "@NowUtc", (object)new DateTime(2026, 10, 3, 8, 0, 0))
                 })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = SqlHereString(script, functionName);
            if (parameterName.Length > 0)
                command.Parameters.AddWithValue(parameterName, parameterValue);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), $"{functionName} returned no result row.");
        }
    }

    private static void AssertColumns<TEntity>(Microsoft.EntityFrameworkCore.Metadata.IModel model,
        string expectedTable, params string[] columns)
    {
        var entity = model.FindEntityType(typeof(TEntity));
        Assert.NotNull(entity);
        Assert.Equal(expectedTable, entity.GetTableName());
        foreach (var column in columns)
            Assert.NotNull(entity.FindProperty(column));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Sati repository root was not found.");
    }

    private static string SqlHereString(string script, string functionName)
    {
        var function = script.IndexOf($"function {functionName}", StringComparison.Ordinal);
        Assert.True(function >= 0, $"Missing watchdog function {functionName}.");
        var opening = script.IndexOf("@'", function, StringComparison.Ordinal);
        var closing = script.IndexOf("'@", opening + 2, StringComparison.Ordinal);
        Assert.True(opening >= 0 && closing > opening, $"Missing SQL here-string in {functionName}.");
        return script[(opening + 2)..closing];
    }
}
