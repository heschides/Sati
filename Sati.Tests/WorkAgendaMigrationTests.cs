using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

[Collection("Local synthetic SQL schedule")]
public sealed class WorkAgendaMigrationTests
{
    private static string RepositoryRoot => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(WorkAgendaMigrationTests).Assembly.Location)!,
        "..", "..", "..", "..", ".."));

    private const string PreviousMigration =
        "20260922191918_SupportReleaseAttestationReviewFlags";
    private const string RepairMigration =
        "20260923180000_ReconcileDuplicateScheduledAgendaNotes";

    [Fact]
    public void DuplicateRepairCancelsOnlyUnambiguousUnevidencedFanOutAndAuditsEveryCancellation()
    {
        var migration = new Sati.Persistence.Migrations.ReconcileDuplicateScheduledAgendaNotes();
        var operation = Assert.Single(migration.UpOperations.OfType<SqlOperation>());
        var sql = operation.Sql;

        Assert.Contains("legacy.FormId IS NULL", sql);
        Assert.Contains("linked.FormId IS NOT NULL", sql);
        Assert.Contains("MIN(pair.LinkedFormId) = MAX(pair.LinkedFormId)", sql);
        Assert.Contains("MAX(pair.LegacyMatches) = 1", sql);
        Assert.Contains("MIN(pair.LinkedNoteId) AS SurvivingNoteId", sql);
        Assert.Contains("member.NoteId <> candidate.SurvivingNoteId", sql);
        Assert.DoesNotContain("linked.Id > legacy.Id", sql);
        Assert.Contains("legacy.Status = 0", sql);
        Assert.Contains("legacy.NoteType = 2", sql);
        Assert.Contains("legacy.FormType BETWEEN 0 AND 8", sql);
        Assert.Contains("legacy.Minutes = 15", sql);
        Assert.Contains("legacy.StartTime IS NULL", sql);
        Assert.Contains("legacy.GoalProgress IS NULL", sql);
        Assert.Contains("legacy.Revision >= 1", sql);
        Assert.Contains("linked.Revision >= 1", sql);
        Assert.DoesNotContain("linked.Revision = 1", sql);
        Assert.Contains("Latin1_General_100_BIN2", sql);
        Assert.Contains("formRow.PersonId = linked.PersonId", sql);
        Assert.Contains("formRow.Type = CASE linked.FormType", sql);
        Assert.Contains("WHEN 5 THEN N'ComprehensiveAssessment'", sql);
        Assert.Contains("WHEN 6 THEN N'Reclassification'", sql);
        Assert.Contains("personRow.AgencyId = linked.AgencyId", sql);
        Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", sql);

        foreach (var protectedReference in new[]
                 {
                     "dbo.ClaimLines",
                     "dbo.ClaimCorrections",
                     "dbo.BillingComplianceRecoveryNotes",
                     "dbo.BillingCompliancePolicyReviewFlags",
                     "dbo.FormAttestationChangeReviewFlags",
                     "dbo.FormAttestations",
                     "dbo.ReleaseObligationAttestations"
                 })
        {
            Assert.Contains(protectedReference, sql);
        }

        Assert.Contains("dbo.AuditEvents AS auditRow", sql);
        Assert.Contains("auditRow.[Action] NOT IN (N'note.created', N'note.updated')", sql);
        Assert.Contains("SET Status = 4", sql);
        Assert.Contains("Revision = target.Revision + 1", sql);
        Assert.Contains("note.scheduled-duplicate-cancelled", sql);
        Assert.Contains("ActorUserId", sql);
        Assert.Contains("survivingNoteId", sql);
        Assert.Contains("survivingFormId", sql);
        Assert.Contains("extra-exact-linked", sql);
        Assert.DoesNotContain("DELETE FROM dbo.Notes", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SET FormId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(migration.DownOperations);
    }

    [Fact]
    public void DuplicateRepairIsTheNextRegisteredMigrationAndGeneratesSqlServerSql()
    {
        var options = new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer(
                "Server=synthetic.invalid;Database=ModelOnly;Integrated Security=true;Encrypt=true")
            .Options;
        using var context = new SatiContext(options);

        Assert.Equal(RepairMigration, context.Database.GetMigrations().Last());

        var script = context.GetService<IMigrator>()
            .GenerateScript(PreviousMigration, RepairMigration);

        Assert.Contains("20260923180000_ReconcileDuplicateScheduledAgendaNotes", script);
        Assert.Contains("WHEN 6 THEN N'Reclassification'", script);
        Assert.Contains("note.scheduled-duplicate-cancelled", script);
    }

    [Fact]
    public void ControlledRunnerPinsTheExactMigrationBodyAndRejectsPostHistoryDrift()
    {
        var migration = new Sati.Persistence.Migrations.ReconcileDuplicateScheduledAgendaNotes();
        var operation = Assert.Single(migration.UpOperations.OfType<SqlOperation>());
        var generatedPath = Path.Combine(
            RepositoryRoot,
            "scripts",
            "Apply-WorkAgendaDuplicateReconciliationMigration.generated.sql");
        var runnerPath = Path.Combine(
            RepositoryRoot,
            "scripts",
            "Apply-WorkAgendaDuplicateReconciliationMigration.ps1");
        var generated = File.ReadAllText(generatedPath);
        var runner = File.ReadAllText(runnerPath);

        static string NormalizeLineEndings(string value) => value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);

        var normalizedGenerated = NormalizeLineEndings(generated);
        const string bodyStartMarker = "    SET NOCOUNT ON;";
        const string bodyEndMarker = "\nEND;\n\nIF NOT EXISTS (\n    SELECT * FROM [__EFMigrationsHistory]";
        var bodyStart = normalizedGenerated.IndexOf(bodyStartMarker, StringComparison.Ordinal);
        var bodyEnd = normalizedGenerated.IndexOf(
            bodyEndMarker,
            bodyStart,
            StringComparison.Ordinal);
        Assert.True(bodyStart >= 0 && bodyEnd > bodyStart);

        var generatedBodyLines = normalizedGenerated[bodyStart..bodyEnd].Split('\n');
        Assert.All(generatedBodyLines, line =>
            Assert.True(line.Length == 0 || line.StartsWith("    ", StringComparison.Ordinal)));
        var generatedBody = string.Join(
            '\n',
            generatedBodyLines.Select(line => line.Length == 0 ? line : line[4..]));
        Assert.Equal(
            NormalizeLineEndings(operation.Sql).Trim(),
            generatedBody.Trim());

        var generatedEnvelope = normalizedGenerated[..bodyStart] +
            "<MIGRATION_BODY>" + normalizedGenerated[bodyEnd..];
        var expectedEnvelope = $$"""
            BEGIN TRANSACTION;
            IF NOT EXISTS (
                SELECT * FROM [__EFMigrationsHistory]
                WHERE [MigrationId] = N'{{RepairMigration}}'
            )
            BEGIN
            <MIGRATION_BODY>
            END;

            IF NOT EXISTS (
                SELECT * FROM [__EFMigrationsHistory]
                WHERE [MigrationId] = N'{{RepairMigration}}'
            )
            BEGIN
                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES (N'{{RepairMigration}}', N'10.0.5');
            END;

            COMMIT;
            GO
            """;
        Assert.Equal(expectedEnvelope, generatedEnvelope.TrimEnd());

        var normalizedLineEndings = NormalizeLineEndings(generated);
        var expectedHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(normalizedLineEndings)));
        Assert.Contains($"$expectedSqlHash = '{expectedHash}'", runner);

        Assert.Contains("[ValidateSet('SatiDemo', 'SatiProduction')]", runner);
        Assert.Contains("DB_NAME() COLLATE Latin1_General_100_BIN2", runner);
        Assert.Contains("SatiDatabaseIdentity", runner);
        Assert.Contains("@expectedEnvironment COLLATE Latin1_General_100_BIN2", runner);
        Assert.Contains("[System.Data.IsolationLevel]::Serializable", runner);
        Assert.Contains("-WhatIfOnly", runner);
        Assert.Contains("$connection.AccessToken = $AccessToken.Trim()", runner);
        Assert.Contains("$connectionString['Data Source'] = $SqlServer", runner);
        Assert.Contains("$connectionString['Initial Catalog'] = $DatabaseName", runner);
        Assert.DoesNotContain("$connectionString.DataSource =", runner);
        Assert.Contains("[System.BitConverter]::ToString", runner);
        Assert.DoesNotContain("[Convert]::ToHexString", runner);
        Assert.Contains("DELETE FROM dbo.__EFMigrationsHistory WHERE MigrationId = @migrationId", runner);
        Assert.Contains("$createRepairAuditSnapshotSql", runner);
        Assert.Contains("Create it as an unparameterized session batch", runner);
        Assert.Contains("Post-history duplicate drift detected", runner);
        Assert.Contains("EligibleCandidateGroups", runner);
        Assert.Contains("NotesCancelled", runner);
        Assert.Contains("AuditEventsAdded", runner);
        Assert.Contains("HistoryRowsStaged", runner);
        Assert.Contains("HistoryRowsCommitted", runner);
        Assert.Contains("Every staged change was rolled back", runner);
    }

    [LocalSqlFact]
    public async Task SystemDataSqlClientSessionTempTableSurvivesParameterizedCommands()
    {
        const string script = """
            $ErrorActionPreference = 'Stop'
            $connection = New-Object System.Data.SqlClient.SqlConnection 'Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=true;Encrypt=false;Connect Timeout=30;Application Name=Sati temp-table scope test;'
            try {
                $connection.Open()

                $create = $connection.CreateCommand()
                $create.CommandText = 'CREATE TABLE #RunnerScopeProbe (Value int NOT NULL);'
                try { $create.ExecuteNonQuery() | Out-Null } finally { $create.Dispose() }

                $insert = $connection.CreateCommand()
                $insert.CommandText = 'INSERT #RunnerScopeProbe (Value) VALUES (@value);'
                $insert.Parameters.AddWithValue('@value', 42) | Out-Null
                try { $insert.ExecuteNonQuery() | Out-Null } finally { $insert.Dispose() }

                $read = $connection.CreateCommand()
                $read.CommandText = 'SELECT COUNT_BIG(*) FROM #RunnerScopeProbe WHERE Value = @expected;'
                $read.Parameters.AddWithValue('@expected', 42) | Out-Null
                try {
                    if ([long]$read.ExecuteScalar() -ne 1) {
                        throw 'The session temp table was not visible to the later parameterized command.'
                    }
                }
                finally { $read.Dispose() }

                'TEMP_SCOPE_OK'
            }
            finally { $connection.Dispose() }
            """;

        var startInfo = new System.Diagnostics.ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(Convert.ToBase64String(
            System.Text.Encoding.Unicode.GetBytes(script)));

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Windows PowerShell.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;

        Assert.True(process.ExitCode == 0, error);
        Assert.Contains("TEMP_SCOPE_OK", output);
    }

    [LocalSqlFact]
    public async Task DuplicateRepairKeepsLowestExactRowCancelsSafeFanOutAndSkipsUnsafeGroups()
    {
        var catalog = $"SatiWorkAgendaRepair_{Guid.NewGuid():N}";
        ValidateSyntheticCatalog(catalog);
        await CreateDatabaseAsync(catalog);
        try
        {
            int repairedLegacyId;
            int repairedSurvivorId;
            int repairedExtraLinkedId;
            int repairedFormId;
            int earlierExactLegacyId;
            int earlierExactSurvivorId;
            int[] multipleFormIds;
            int[] multipleLegacyIds;
            int[] evidencedIds;
            int[] auditedIds;

            await using (var setup = Open(catalog))
            {
                await setup.GetService<IMigrator>().MigrateAsync(PreviousMigration);

                var agency = new Agency { Name = "Synthetic Work Agenda repair agency" };
                setup.Agencies.Add(agency);
                await setup.SaveChangesAsync();
                var actor = User.Create(
                    0,
                    "synthetic-work-agenda-repair",
                    "Synthetic Repair Actor",
                    "hash",
                    "salt",
                    UserRole.Admin,
                    null,
                    agency.Id);
                setup.Users.Add(actor);
                await setup.SaveChangesAsync();

                var repaired = await AddGroupAsync(
                    setup,
                    agency.Id,
                    actor.Id,
                    "Synthetic repaired Reclassification",
                    insertionOrder: [null, 0, 0],
                    legacyRevision: 4,
                    linkedRevision: 2);
                repairedLegacyId = repaired.Legacy[0].Id;
                repairedSurvivorId = repaired.Linked[0].Id;
                repairedExtraLinkedId = repaired.Linked[1].Id;
                repairedFormId = repaired.Forms[0].Id;

                var earlierExact = await AddGroupAsync(
                    setup,
                    agency.Id,
                    actor.Id,
                    "Synthetic exact-before-legacy Reclassification",
                    insertionOrder: [0, null]);
                earlierExactLegacyId = earlierExact.Legacy[0].Id;
                earlierExactSurvivorId = earlierExact.Linked[0].Id;
                Assert.True(earlierExactSurvivorId < earlierExactLegacyId);

                var multipleForms = await AddGroupAsync(
                    setup,
                    agency.Id,
                    actor.Id,
                    "Synthetic multiple-form Reclassification",
                    insertionOrder: [0, null, 1]);
                Assert.True(multipleForms.Linked[0].Id < multipleForms.Legacy[0].Id);
                multipleFormIds =
                    [.. multipleForms.Legacy.Select(note => note.Id),
                     .. multipleForms.Linked.Select(note => note.Id)];

                var multipleLegacy = await AddGroupAsync(
                    setup,
                    agency.Id,
                    actor.Id,
                    "Synthetic multiple-legacy Reclassification",
                    insertionOrder: [null, 0, null]);
                Assert.True(multipleLegacy.Legacy[1].Id > multipleLegacy.Linked[0].Id);
                multipleLegacyIds =
                    [.. multipleLegacy.Legacy.Select(note => note.Id),
                     .. multipleLegacy.Linked.Select(note => note.Id)];

                var evidenced = await AddGroupAsync(
                    setup,
                    agency.Id,
                    actor.Id,
                    "Synthetic evidenced Reclassification",
                    insertionOrder: [null, 0]);
                evidenced.Forms[0].Attest(FormAttestation.Attested(
                    new DateTime(2026, 9, 1),
                    AttestationActorKind.CaseManager,
                    actor.Id,
                    DateTime.UtcNow,
                    evidenced.Legacy[0].Id));
                await setup.SaveChangesAsync();
                evidencedIds = [evidenced.Legacy[0].Id, evidenced.Linked[0].Id];

                var audited = await AddGroupAsync(
                    setup,
                    agency.Id,
                    actor.Id,
                    "Synthetic audited Reclassification",
                    insertionOrder: [null, 0]);
                setup.AuditEvents.Add(new AuditEvent
                {
                    AgencyId = agency.Id,
                    ActorUserId = actor.Id,
                    Action = "note.form-date-corrected",
                    ResourceType = "Note",
                    ResourceId = audited.Linked[0].Id.ToString(),
                    CorrelationId = "synthetic-unsafe-note-audit"
                });
                await setup.SaveChangesAsync();
                auditedIds = [audited.Legacy[0].Id, audited.Linked[0].Id];

                await setup.GetService<IMigrator>().MigrateAsync(RepairMigration);
            }

            await using var verify = Open(catalog);
            var repairedLegacy = await verify.Notes.SingleAsync(note => note.Id == repairedLegacyId);
            var repairedSurvivor = await verify.Notes.SingleAsync(note => note.Id == repairedSurvivorId);
            Assert.Equal(NoteStatus.Cancelled, repairedLegacy.Status);
            Assert.Equal(5, repairedLegacy.Revision);
            Assert.Null(repairedLegacy.FormId);
            Assert.Equal(NoteStatus.Scheduled, repairedSurvivor.Status);
            Assert.Equal(2, repairedSurvivor.Revision);
            Assert.Equal(repairedFormId, repairedSurvivor.FormId);
            var repairedExtraLinked = await verify.Notes.SingleAsync(
                note => note.Id == repairedExtraLinkedId);
            Assert.Equal(NoteStatus.Cancelled, repairedExtraLinked.Status);
            Assert.Equal(3, repairedExtraLinked.Revision);
            Assert.Equal(repairedFormId, repairedExtraLinked.FormId);

            var earlierExactLegacy = await verify.Notes.SingleAsync(
                note => note.Id == earlierExactLegacyId);
            var earlierExactSurvivor = await verify.Notes.SingleAsync(
                note => note.Id == earlierExactSurvivorId);
            Assert.Equal(NoteStatus.Cancelled, earlierExactLegacy.Status);
            Assert.Equal(NoteStatus.Scheduled, earlierExactSurvivor.Status);

            var untouched = await verify.Notes
                .Where(note => multipleFormIds
                    .Concat(multipleLegacyIds)
                    .Concat(evidencedIds)
                    .Concat(auditedIds)
                    .Contains(note.Id))
                .ToListAsync();
            Assert.Equal(10, untouched.Count);
            Assert.All(untouched, note => Assert.Equal(NoteStatus.Scheduled, note.Status));

            var audits = await verify.AuditEvents
                .Where(item => item.Action == "note.scheduled-duplicate-cancelled")
                .OrderBy(item => item.ResourceId)
                .ToListAsync();
            Assert.Equal(3, audits.Count);
            Assert.All(audits, audit =>
            {
                Assert.Equal(0, audit.ActorUserId);
                Assert.DoesNotContain("Synthetic", audit.MetadataJson);
            });
            Assert.Contains(audits, audit =>
                audit.ResourceId == repairedLegacyId.ToString() &&
                audit.MetadataJson.Contains("\"duplicateKind\":\"legacy-unlinked\"") &&
                audit.MetadataJson.Contains($"\"survivingNoteId\":{repairedSurvivorId}") &&
                audit.MetadataJson.Contains($"\"survivingFormId\":{repairedFormId}"));
            Assert.Contains(audits, audit =>
                audit.ResourceId == repairedExtraLinkedId.ToString() &&
                audit.MetadataJson.Contains("\"duplicateKind\":\"extra-exact-linked\"") &&
                audit.MetadataJson.Contains($"\"survivingNoteId\":{repairedSurvivorId}") &&
                audit.MetadataJson.Contains($"\"survivingFormId\":{repairedFormId}"));
            Assert.Contains(audits, audit =>
                audit.ResourceId == earlierExactLegacyId.ToString() &&
                audit.MetadataJson.Contains($"\"survivingNoteId\":{earlierExactSurvivorId}"));
        }
        finally
        {
            await DropDatabaseAsync(catalog);
        }
    }

    private static async Task<(Form[] Forms, Note[] Legacy, Note[] Linked)> AddGroupAsync(
        SatiContext context,
        int agencyId,
        int actorId,
        string narrative,
        IReadOnlyList<int?> insertionOrder,
        int legacyRevision = 2,
        int linkedRevision = 1)
    {
        var person = Person.CreatePerson(
            actorId,
            "Synthetic",
            Guid.NewGuid().ToString("N"),
            string.Empty,
            new DateTime(1990, 1, 1),
            null,
            WaiverType.Section21,
            new Settings());
        person.AgencyId = agencyId;
        context.People.Add(person);
        await context.SaveChangesAsync();

        var formIndexes = insertionOrder
            .Where(index => index.HasValue)
            .Select(index => index!.Value)
            .ToArray();
        if (formIndexes.Length == 0 || formIndexes.Min() < 0)
            throw new ArgumentException("At least one non-negative Form index is required.", nameof(insertionOrder));
        var formCount = formIndexes.Max() + 1;
        var forms = Enumerable.Range(0, formCount)
            .Select(index => new Form(
                FormType.Reclassification,
                new DateTime(2026, 9, 18).AddYears(index),
                targetEffectiveDate: new DateTime(2026, 12, 16).AddYears(index))
            {
                PersonId = person.Id
            })
            .ToArray();
        context.Forms.AddRange(forms);
        await context.SaveChangesAsync();

        var legacy = new List<Note>();
        var linked = new List<Note>();
        foreach (var formIndex in insertionOrder)
        {
            var note = Note.Create(
                narrative,
                new DateTime(2026, 9, 23),
                NoteStatus.Scheduled,
                15,
                person.Id,
                FormType.Reclassification,
                NoteType.Form,
                formIndex is int index ? forms[index].Id : null);
            note.AgencyId = agencyId;
            note.Revision = formIndex.HasValue ? linkedRevision : legacyRevision;
            context.Notes.Add(note);
            await context.SaveChangesAsync();
            (formIndex.HasValue ? linked : legacy).Add(note);
        }

        return (forms, legacy.ToArray(), linked.ToArray());
    }

    private static SatiContext Open(string catalog) => new(
        new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer(ConnectionTo(catalog), sql => sql.CommandTimeout(300))
            .Options);

    private static string ConnectionTo(string catalog) => new SqlConnectionStringBuilder
    {
        DataSource = @"(localdb)\MSSQLLocalDB",
        InitialCatalog = catalog,
        IntegratedSecurity = true,
        Encrypt = false,
        ConnectTimeout = 30,
        ApplicationName = "Sati Work Agenda migration tests"
    }.ConnectionString;

    private static async Task CreateDatabaseAsync(string catalog)
    {
        ValidateSyntheticCatalog(catalog);
        await using var connection = new SqlConnection(ConnectionTo("master"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "IF DB_ID(@name) IS NOT NULL THROW 50000, 'Refusing an existing test database.', 1; " +
            $"CREATE DATABASE [{catalog}];";
        command.Parameters.AddWithValue("@name", catalog);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string catalog)
    {
        ValidateSyntheticCatalog(catalog);
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(ConnectionTo("master"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF DB_ID('{catalog}') IS NOT NULL BEGIN " +
            $"ALTER DATABASE [{catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"DROP DATABASE [{catalog}]; END;";
        await command.ExecuteNonQueryAsync();
    }

    private static void ValidateSyntheticCatalog(string catalog)
    {
        if (!Regex.IsMatch(
                catalog,
                "\\ASatiWorkAgendaRepair_[0-9a-f]{32}\\z",
                RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException(
                "Refusing a database outside the Work Agenda synthetic test namespace.");
        }
    }
}
