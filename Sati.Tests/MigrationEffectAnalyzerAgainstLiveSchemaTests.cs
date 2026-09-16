using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sati.Data;
using Xunit;
using Xunit.Abstractions;

namespace Sati.Tests;

/// <summary>
/// The hand-built schemas in <see cref="MigrationEffectAnalyzerTests"/> exercise the
/// classification rules. They cannot catch a rule that is right in the abstract and
/// wrong against the real migration chain, which is what stopped the production
/// workstation twice: 1.3.11 refused on a database where none of the release had run,
/// and 1.3.12 refused again on the same database after the first cause was fixed.
///
/// This builds a database at the exact state that workstation reports - every migration
/// through AddGoalProgressToCaseNotes and nothing after it - and asks the analyzer the
/// same question startup asks. A database that has never seen the release must read as
/// NotApplied. Anything else stops a caseload from opening over a problem that is not
/// there.
/// </summary>
[Collection("Local synthetic SQL schedule")]
public sealed class MigrationEffectAnalyzerAgainstLiveSchemaTests(ITestOutputHelper output)
{
    private const string LastAppliedOnTheWorkstation = "20260914030703_AddGoalProgressToCaseNotes";

    [LocalSqlFact]
    public async Task AReleaseThatHasNeverRunReadsAsNotAppliedAgainstTheRealChain()
    {
        var catalog = $"SatiAnalyzerLiveSchema_{Guid.NewGuid():N}";
        await CreateDatabaseAsync(catalog);
        try
        {
            await using var context = Open(catalog);
            await context.GetService<IMigrator>().MigrateAsync(LastAppliedOnTheWorkstation);

            var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
            Assert.NotEmpty(pending);

            var findings = await MigrationEffectAnalyzer.AnalyzeAsync(context, pending);

            foreach (var finding in findings)
            {
                output.WriteLine($"{finding.MigrationId}: {finding.State}");
                foreach (var effect in finding.PresentEffects)
                    output.WriteLine($"    PRESENT     {effect}");
                foreach (var effect in finding.MissingEffects)
                    output.WriteLine($"    missing     {effect}");
                foreach (var step in finding.UnverifiableSteps)
                    output.WriteLine($"    unverifiable {step}");
            }

            var wrong = findings.Where(f => f.State != MigrationEffectState.NotApplied).ToList();
            Assert.True(
                wrong.Count == 0,
                "A database that has never seen these migrations must read as NotApplied. Got: "
                + string.Join("; ", wrong.Select(f =>
                    $"{f.MigrationId} is {f.State} because these read as present: "
                    + string.Join(", ", f.PresentEffects))));
        }
        finally
        {
            await DropDatabaseAsync(catalog);
        }
    }

    private static string ConnectionTo(string catalog) => new SqlConnectionStringBuilder
    {
        DataSource = @"(localdb)\MSSQLLocalDB",
        InitialCatalog = catalog,
        IntegratedSecurity = true,
        Encrypt = false,
        ConnectTimeout = 30,
        ApplicationName = "Sati migration analyzer live-schema tests"
    }.ConnectionString;

    private static SatiContext Open(string catalog) => new(
        new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer(ConnectionTo(catalog), sql => sql.CommandTimeout(300))
            .Options);

    private static async Task CreateDatabaseAsync(string catalog)
    {
        await using var connection = new SqlConnection(ConnectionTo("master"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "IF DB_ID(@name) IS NOT NULL THROW 50000, 'Refusing an existing test database.', 1; "
            + $"CREATE DATABASE [{catalog}];";
        command.Parameters.AddWithValue("@name", catalog);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string catalog)
    {
        try
        {
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(ConnectionTo("master"));
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"IF DB_ID('{catalog}') IS NOT NULL BEGIN "
                + $"ALTER DATABASE [{catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; "
                + $"DROP DATABASE [{catalog}]; END;";
            await command.ExecuteNonQueryAsync();
        }
        catch
        {
            // A leftover disposable database is noise, not a test failure.
        }
    }
}

public sealed class LocalSqlFactAttribute : FactAttribute
{
    public LocalSqlFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("SATI_RUN_SQLSERVER_TESTS") != "1")
            Skip = "Set SATI_RUN_SQLSERVER_TESTS=1 on Windows to run against disposable synthetic LocalDB databases.";
    }
}
