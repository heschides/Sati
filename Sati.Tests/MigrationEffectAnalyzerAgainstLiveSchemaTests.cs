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
/// same question startup asks. Structural migrations must read as NotApplied. A raw
/// data-only migration has no schema effect to inspect and may read as Indeterminate;
/// it must never make an untouched database look AlreadyPresent or PartiallyPresent.
/// </summary>
[Collection("Local synthetic SQL schedule")]
public sealed class MigrationEffectAnalyzerAgainstLiveSchemaTests(ITestOutputHelper output)
{
    private const string LastAppliedOnTheWorkstation = "20260914030703_AddGoalProgressToCaseNotes";
    private const string DataOnlyAgendaRepair =
        "20260923180000_ReconcileDuplicateScheduledAgendaNotes";

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

            var wrong = findings
                .Where(f => f.MigrationId != DataOnlyAgendaRepair)
                .Where(f => f.State != MigrationEffectState.NotApplied)
                .ToList();
            Assert.True(
                wrong.Count == 0,
                "Structural migrations that have never run must read as NotApplied. Got: "
                + string.Join("; ", wrong.Select(f =>
                    $"{f.MigrationId} is {f.State}; effects that read as present: "
                    + string.Join(", ", f.PresentEffects))));

            var dataOnly = Assert.Single(
                findings,
                finding => finding.MigrationId == DataOnlyAgendaRepair);
            Assert.Equal(MigrationEffectState.Indeterminate, dataOnly.State);
            Assert.Contains("a raw SQL step", dataOnly.UnverifiableSteps);
        }
        finally
        {
            await DropDatabaseAsync(catalog);
        }
    }

    [LocalSqlFact]
    public async Task TheStartupUpdaterDoesNotRefuseAReleaseThatHasNeverRun()
    {
        // The analyzer test above passes and the production workstation still refuses.
        // This runs what startup runs - LocalDatabaseUpdater over SqlLocalDatabaseMaintenance,
        // the pairing App.xaml.cs builds - rather than the analyzer alone, so any
        // difference between the two paths shows up here instead of in the field.
        var catalog = $"SatiStartupUpdater_{Guid.NewGuid():N}";
        await CreateDatabaseAsync(catalog);
        try
        {
            await using (var setup = Open(catalog))
                await setup.GetService<IMigrator>().MigrateAsync(LastAppliedOnTheWorkstation);

            await using var context = Open(catalog);
            var result = await new LocalDatabaseUpdater(new SqlLocalDatabaseMaintenance(context))
                .UpdateAsync();

            output.WriteLine($"Outcome: {result.Outcome}");
            foreach (var finding in result.Findings ?? [])
            {
                output.WriteLine($"  {finding.MigrationId}: {finding.State}");
                foreach (var effect in finding.PresentEffects)
                    output.WriteLine($"    PRESENT {effect}");
            }
            if (result.Failure is not null)
                output.WriteLine($"Failure: {result.Failure}");

            Assert.NotEqual(LocalDatabaseUpdateOutcome.NeedsRepair, result.Outcome);
            Assert.NotEqual(LocalDatabaseUpdateOutcome.Failed, result.Outcome);
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
