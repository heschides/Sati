using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;

namespace Sati.Tools.ComplianceSeed;

internal sealed record FormCompletion(Person Person, Form Form, DateTime CompletedOn);
internal sealed record FormOpening(Person Person, Form Form, DateTime OpenedOn);
internal sealed record ReleaseCompletion(Person Person, ReleaseObligation Row, DateTime CompletedOn);
internal sealed record NewRelease(Person Person, MissingReleasePlan Missing, DateTime CompletedOn);

internal sealed class SeedPlan
{
    public List<FormCompletion> Completions { get; } = [];
    public List<FormOpening> Openings { get; } = [];
    public List<ReleaseCompletion> Releases { get; } = [];
    public List<NewRelease> NewReleases { get; } = [];
    public List<string> Skips { get; } = [];
    public int MissingPastDueRows { get; set; }

    /// <summary>Annual rows Sati itself would create on its next load; saved with the rest.</summary>
    public int RowsCreated { get; set; }

    public int TotalChanges => Completions.Count + Openings.Count + Releases.Count + NewReleases.Count + RowsCreated;

    /// <summary>Counts by kind and type; two plans with the same summary make the same changes.</summary>
    public string Summary => string.Join("; ",
        Completions.GroupBy(item => item.Form.Type.ToString()).OrderBy(g => g.Key)
            .Select(g => $"done {g.Key}={g.Count()}")
            .Concat(Openings.GroupBy(item => item.Form.Type.ToString()).OrderBy(g => g.Key)
                .Select(g => $"opened {g.Key}={g.Count()}"))
            .Concat(Releases.GroupBy(item => item.Row.Category.ToString()).OrderBy(g => g.Key)
                .Select(g => $"release {g.Key}={g.Count()}"))
            .Concat(NewReleases.GroupBy(item => item.Missing.Plan.Category.ToString()).OrderBy(g => g.Key)
                .Select(g => $"new release {g.Key}={g.Count()}"))
            .Append($"rows={RowsCreated}").Append($"skipped={Skips.Count}"));

    public void Print()
    {
        Console.WriteLine($"Clients checked                          {PeopleChecked}");
        Console.WriteLine();
        Console.WriteLine($"Form rows Sati has not created yet       {RowsCreated}");
        Console.WriteLine("  (the same rows Sati adds when it opens; created here first)");
        Console.WriteLine($"Forms to record as done on their due date {Completions.Count}");
        foreach (var group in Completions.GroupBy(item => Person.FormDisplayName(item.Form.Type)).OrderBy(g => g.Key))
            Console.WriteLine($"  {group.Key,-26} {group.Count(),5}");
        Console.WriteLine($"PCPs/assessments to record as opened     {Openings.Count}");
        foreach (var group in Openings.GroupBy(item => Person.FormDisplayName(item.Form.Type)).OrderBy(g => g.Key))
            Console.WriteLine($"  {group.Key,-26} {group.Count(),5}");
        Console.WriteLine($"Releases to record as signed             {Releases.Count}");
        foreach (var group in Releases.GroupBy(item => item.Row.Category.ToString()).OrderBy(g => g.Key))
            Console.WriteLine($"  {group.Key,-26} {group.Count(),5}");
        Console.WriteLine($"Older-year releases to add, as signed    {NewReleases.Count}");
        foreach (var group in NewReleases.GroupBy(item => item.Missing.Plan.Category.ToString()).OrderBy(g => g.Key))
            Console.WriteLine($"  {group.Key,-26} {group.Count(),5}");
        Console.WriteLine();
        Console.WriteLine($"Left alone, needs a look                 {Skips.Count}");
        foreach (var group in Skips.GroupBy(reason => reason).OrderBy(g => g.Key))
            Console.WriteLine($"  {group.Count(),5}  {group.Key}");
        if (MissingPastDueRows > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Past-due forms with no row yet           {MissingPastDueRows}");
            Console.WriteLine("  Sati creates these when it opens. Open Sati once, close it, and run");
            Console.WriteLine("  Step 1 again so they are included.");
        }
    }

    public int PeopleChecked { get; set; }
}

internal static class Seeder
{
    public static string SeedReason(DateTime today) =>
        $"Seeded {today:yyyy-MM-dd} to match the external tracking sheet; not a record of completion.";

    public static SatiContext Open(string server, string database) => new(
        new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer(ConnectionString(server, database), sql => sql.CommandTimeout(1800))
            .Options);

    public static string ConnectionString(string server, string database) =>
        $"Server={server};Database={database};Integrated Security=true;Encrypt=false;Connect Timeout=30;";

    /// <summary>Null when the database is a Production Sati database this build can read exactly.</summary>
    public static async Task<string?> CheckReadyAsync(SatiContext context, string database)
    {
        var applied = (await context.Database.GetAppliedMigrationsAsync()).Count();
        var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
        if (applied == 0)
            return $"{database} does not look like a Sati database.";
        if (pending.Count > 0)
            return $"{database} is missing {pending.Count} Sati update(s). Start Sati once so it updates, then run this again.";

        var connection = (SqlConnection)context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1;";
            var marker = await command.ExecuteScalarAsync() as string;
            return string.Equals(marker, "Production", StringComparison.Ordinal)
                ? null
                : $"{database} is marked '{marker}', not Production.";
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    public static async Task<SeedPlan> PlanAsync(SatiContext context, DateTime today)
    {
        var people = await context.People
            .Include(person => person.Forms).ThenInclude(form => form.Attestations)
            .Include(person => person.ReleaseObligations).ThenInclude(row => row.Attestations)
            .Include(person => person.ReleaseObligations).ThenInclude(row => row.AuthorizationEvents)
            .AsSplitQuery()
            .ToListAsync();
        var settingsByAgency = (await context.Settings.AsNoTracking().ToListAsync())
            .GroupBy(settings => settings.AgencyId)
            .ToDictionary(group => group.Key, group => group.First());

        var links = (await (from link in context.PersonProviders.AsNoTracking()
                            join provider in context.Providers.AsNoTracking()
                                on link.ProviderId equals provider.Id
                            select new
                            {
                                link.PersonId,
                                provider.AgencyId,
                                Fact = new ReleaseProviderLinkFact(
                                    link.Id, link.ProviderId, provider.Type.ToString(), link.Role,
                                    link.StartDate, link.EndDate, link.AssignmentKnownOn, provider.Name)
                            }).ToListAsync())
            .ToLookup(row => (row.PersonId, row.AgencyId), row => row.Fact);

        var plan = new SeedPlan { PeopleChecked = people.Count };
        foreach (var person in people.OrderBy(person => person.Id))
        {
            var settings = person.AgencyId is int agencyId && settingsByAgency.TryGetValue(agencyId, out var found)
                ? found
                : new Settings();
            // Sati adds any missing annual rows every time it loads the caseload. Doing the
            // same here, with the same method, means nothing past due is missed because
            // Sati has not been opened since a plan year began.
            var before = person.Forms.Count;
            if (person.EffectiveDate is not null && person.EnsureCurrentCycleForms(today.Date, settings))
                plan.RowsCreated += person.Forms.Count - before;
            PlanPerson(plan, person, FormDueDateCalculator.ToSchedule(settings), today.Date, links[(person.Id, person.AgencyId ?? 0)].ToArray());
        }
        return plan;
    }

    private static void PlanPerson(SeedPlan plan, Person person, ComplianceScheduleSettings schedule, DateTime today, IReadOnlyList<ReleaseProviderLinkFact> links)
    {
        if (person.EffectiveDate is not DateTime effectiveDate)
        {
            var pastDue = person.Forms.Count(form => form.CompletedDate is null && form.DueDate.Date < today) +
                          person.ReleaseObligations.Count(row => row.CompletedOn is null && row.DueOn.Date < today);
            for (var i = 0; i < pastDue; i++)
                plan.Skips.Add("client has no effective date");
            return;
        }

        // Every past-due, uncompleted form is planned as done on its own due date. The
        // plan is visible to the rules below, so a Reclass sees its assessment as done.
        var planned = person.Forms
            .Where(form => form.CompletedDate is null && form.DueDate.Date < today)
            .ToDictionary(form => form, form => form.DueDate.Date);
        var facts = person.Forms
            .Select(form => new FormFact(
                form.Id,
                person.Id,
                form.Type.ToString(),
                form.DueDate,
                form.CompletedDate ?? (planned.TryGetValue(form, out var plannedOn) ? plannedOn : null),
                TargetOf(form)))
            .ToArray();

        var accepted = new Dictionary<Form, DateTime>();
        foreach (var (form, completedOn) in planned.OrderBy(pair => pair.Value))
        {
            var typeName = form.Type.ToString();
            var cycle = FormAttestationRules.ResolveCycleForForm(
                effectiveDate, typeName, form.DueDate, TargetOf(form));
            if (cycle is null)
            {
                plan.Skips.Add($"{Person.FormDisplayName(form.Type)}: not in a compliance year Sati can place");
                continue;
            }

            var decision = FormAttestationRules.Evaluate(
                typeName,
                completedOn,
                cycle.Value.CycleStart,
                today,
                AttestationActorKind.System,
                [],
                facts,
                targetEffectiveDate: TargetOf(form),
                availableOn: AvailableOn(typeName, form.DueDate, schedule));
            if (!decision.Accepted)
            {
                var why = decision.DateError ?? string.Join(" ", decision.UnmetPrerequisites.Select(item => item.Message));
                plan.Skips.Add($"{Person.FormDisplayName(form.Type)}: {why}");
                continue;
            }

            accepted.Add(form, completedOn);
            plan.Completions.Add(new FormCompletion(person, form, completedOn));
        }

        foreach (var form in person.Forms.Where(form => form.OpenedDate is null))
        {
            var typeName = form.Type.ToString();
            if (BillingComplianceGate.OpeningDeadline(typeName, form.DueDate) is not DateTime deadline ||
                deadline.Date >= today)
                continue;

            var availableOn = AvailableOn(typeName, form.DueDate, schedule);
            var openedOn = deadline.Date > availableOn ? deadline.Date : availableOn;
            var completedOn = form.CompletedDate ?? (accepted.TryGetValue(form, out var seeded) ? seeded : null);
            if (completedOn is DateTime done && done.Date < openedOn)
                openedOn = done.Date;

            if (FormOpeningRules.Validate(openedOn, availableOn, today) is string error)
            {
                plan.Skips.Add($"{Person.FormDisplayName(form.Type)} opening: {error}");
                continue;
            }
            plan.Openings.Add(new FormOpening(person, form, openedOn));
        }

        foreach (var row in person.ReleaseObligations.Where(row =>
                     row.CompletedOn is null && row.DueOn.Date < today))
        {
            if (row.RetiredOn is DateTime retired && retired.Date <= row.DueOn.Date)
                continue; // never became actionable
            if (row.WithdrawnOn is not null)
            {
                plan.Skips.Add("release: withdrawn without a recorded signature");
                continue;
            }
            if (row.DueOn.Date < row.AvailableOn.Date || person.UserId <= 0)
            {
                plan.Skips.Add("release: due before it was available, or no case manager");
                continue;
            }
            plan.Releases.Add(new ReleaseCompletion(person, row, row.DueOn.Date));
        }

        // Sati keeps release rows for the current and next plan only; billing still expects
        // every earlier year's. Those missing rows are added from the same rule billing uses.
        foreach (var missing in ExpectedBillingComplianceObligations.MissingReleasePlans(
                     effectiveDate,
                     person.ReleaseObligations.Select(row => row.StableKey),
                     today,
                     links))
        {
            var release = missing.Plan;
            if (release.DueOn.Date >= today ||
                release.RetiredOn is DateTime retired && retired.Date <= release.DueOn.Date)
                continue;
            if (person.AgencyId is not > 0 || person.UserId <= 0 ||
                release.DueOn.Date < release.AvailableOn.Date ||
                release.Category != ReleaseObligationCategory.Dhhs && missing.Recipient is null)
            {
                plan.Skips.Add("older-year release: cannot be created safely");
                continue;
            }
            plan.NewReleases.Add(new NewRelease(person, missing, release.DueOn.Date));
        }

        var stored = person.Forms.Select(form => new ComplianceFormSnapshot(
            form.Type.ToString(), form.DueDate, form.CompletedDate,
            TargetEffectiveDate: TargetOf(form)));
        plan.MissingPastDueRows += ExpectedBillingComplianceObligations
            .IncludeMissingForms(effectiveDate, stored, today, schedule)
            .Count(item => item.ObligationId?.StartsWith("missing-form:", StringComparison.Ordinal) == true &&
                           !item.Type.StartsWith("Release_", StringComparison.Ordinal) &&
                           item.DueDate.Date < today);
    }

    public static void Apply(SatiContext context, SeedPlan plan, DateTime today)
    {
        var reason = SeedReason(today);
        var recordedAtUtc = DateTime.UtcNow;
        var correlation = $"compliance-seed-{Guid.NewGuid():N}";

        foreach (var item in plan.Completions)
        {
            item.Form.Attest(FormAttestation.Attested(
                item.CompletedOn,
                AttestationActorKind.System,
                actorUserId: null,
                recordedAtUtc,
                prerequisiteStateJson: FormAttestationRules.NoPrerequisitesStateJson,
                reason: reason));
            Audit(context, item.Person, -1, "form.attested", "Form", item.Form.Id.ToString(), correlation, new
            {
                formType = item.Form.Type.ToString(),
                completedOn = item.CompletedOn.ToString("yyyy-MM-dd"),
                actorKind = "System",
                seeded = true,
                reason
            });
        }

        foreach (var item in plan.Openings)
        {
            item.Form.OpenedDate = item.OpenedOn;
            Audit(context, item.Person, -1, "form.opened", "Form", item.Form.Id.ToString(), correlation, new
            {
                formType = item.Form.Type.ToString(),
                openedOn = item.OpenedOn.ToString("yyyy-MM-dd"),
                seeded = true,
                reason
            });
        }

        foreach (var item in plan.Releases)
        {
            // A release attestation must name a person; it is the consumer's own case
            // manager, who is the one asserting these are done.
            item.Row.AttestManually(
                item.CompletedOn,
                today,
                AttestationActorKind.CaseManager,
                item.Person.UserId,
                recordedAtUtc,
                reason);
            Audit(context, item.Person, item.Person.UserId, "release-obligation.attested", "Person",
                item.Person.Id.ToString(), correlation, new
                {
                    obligationId = item.Row.ObligationId,
                    category = item.Row.Category.ToString(),
                    completedOn = item.CompletedOn.ToString("yyyy-MM-dd"),
                    seeded = true,
                    reason
                });
        }

        ApplyNewReleases(context, plan, today, reason, recordedAtUtc, correlation);
    }

    private static void ApplyNewReleases(SatiContext context, SeedPlan plan, DateTime today, string reason,
        DateTime recordedAtUtc, string correlation)
    {
        foreach (var item in plan.NewReleases)
        {
            var release = item.Missing.Plan;
            var row = ReleaseObligation.Create(
                item.Person.AgencyId!.Value,
                item.Person.Id,
                release,
                recordedAtUtc,
                item.Missing.Recipient?.ProviderId,
                item.Missing.Recipient?.RecipientDisplayName);
            row.AttestManually(item.CompletedOn, today, AttestationActorKind.CaseManager,
                item.Person.UserId, recordedAtUtc, reason);
            item.Person.ReleaseObligations.Add(row);
            Audit(context, item.Person, item.Person.UserId, "release-obligations.reconciled", "Person",
                item.Person.Id.ToString(), correlation, new
                {
                    targetEffectiveDate = release.TargetEffectiveDate.ToString("yyyy-MM-dd"),
                    created = new[] { release.StableKey },
                    source = "compliance-seed"
                });
            Audit(context, item.Person, item.Person.UserId, "release-obligation.attested", "Person",
                item.Person.Id.ToString(), correlation, new
                {
                    stableKey = release.StableKey,
                    category = release.Category.ToString(),
                    completedOn = item.CompletedOn.ToString("yyyy-MM-dd"),
                    seeded = true,
                    reason
                });
        }
    }

    /// <summary>
    /// Re-plans against <paramref name="database"/>, applies only if that plan matches the
    /// reviewed one, then confirms nothing is left to do and protected data is unchanged.
    /// Returns null on success, otherwise what went wrong.
    /// </summary>
    public static async Task<string?> ApplyAndVerifyAsync(
        string server, string database, DateTime today, SeedPlan reviewed, Fingerprint before)
    {
        await using (var context = Open(server, database))
        {
            if (await CheckReadyAsync(context, database) is { } problem)
                return problem;
            var plan = await PlanAsync(context, today);
            if (plan.Summary != reviewed.Summary)
                return $"the database changed since the check ({plan.Summary} vs {reviewed.Summary}).";
            // Generated rows are saved first so their ids exist for the audit entries; both
            // saves share one transaction, so a failure leaves nothing behind.
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.SaveChangesAsync();
            Apply(context, plan, today);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using (var context = Open(server, database))
        {
            var remaining = await PlanAsync(context, today);
            if (remaining.TotalChanges != 0)
                return $"{remaining.TotalChanges} item(s) still needed changes afterwards.";
            if (remaining.Skips.Count != reviewed.Skips.Count)
                return $"expected {reviewed.Skips.Count} items left alone, found {remaining.Skips.Count}.";
            var after = await Fingerprint.ReadAsync(context);
            if (!after.Equals(before))
                return "the client list, notes, or recent scratchpad differ from before.";
        }
        return null;
    }

    public static async Task<string> BackupAsync(string server, string database)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sati", "schema-backups");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{database}-{DateTime.Now:yyyyMMdd-HHmmss}-before-compliance-seed.bak");

        await using var connection = new SqlConnection(ConnectionString(server, "master"));
        await connection.OpenAsync();
        await ExecuteAsync(connection,
            $"BACKUP DATABASE [{database}] TO DISK = @path WITH COPY_ONLY, INIT, CHECKSUM;", path);
        await ExecuteAsync(connection, "RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM;", path);
        return path;
    }

    public static async Task RestoreCopyAsync(string server, string backupPath, string copyName)
    {
        await DropCopyAsync(server, copyName);
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sati", "seed-rehearsal");
        Directory.CreateDirectory(directory);

        await using var connection = new SqlConnection(ConnectionString(server, "master"));
        await connection.OpenAsync();
        var files = new List<(string Logical, string Type)>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText = "RESTORE FILELISTONLY FROM DISK = @path;";
            list.Parameters.AddWithValue("@path", backupPath);
            await using var reader = await list.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                files.Add((reader.GetString(reader.GetOrdinal("LogicalName")),
                    reader.GetString(reader.GetOrdinal("Type"))));
        }

        var moves = files.Select((file, index) =>
        {
            var extension = file.Type == "L" ? "_log.ldf" : index == 0 ? ".mdf" : $"_{index}.ndf";
            var target = Path.Combine(directory, copyName + extension).Replace("'", "''");
            return $"MOVE N'{file.Logical.Replace("'", "''")}' TO N'{target}'";
        });
        await ExecuteAsync(connection,
            $"RESTORE DATABASE [{copyName}] FROM DISK = @path WITH {string.Join(", ", moves)}, RECOVERY;",
            backupPath);
    }

    public static async Task DropCopyAsync(string server, string copyName)
    {
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(ConnectionString(server, "master"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 600;
        command.CommandText = $"""
            IF DB_ID(N'{copyName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{copyName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{copyName}];
            END
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, string path)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 1800;
        command.CommandText = sql;
        command.Parameters.AddWithValue("@path", path);
        await command.ExecuteNonQueryAsync();
    }

    private static void Audit(
        SatiContext context, Person person, int actorUserId, string action,
        string resourceType, string resourceId, string correlation, object metadata) =>
        context.AuditEvents.Add(new AuditEvent
        {
            AgencyId = person.AgencyId ?? 0,
            ActorUserId = actorUserId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            CorrelationId = correlation,
            MetadataJson = JsonSerializer.Serialize(metadata)
        });

    private static DateTime? TargetOf(Form form) =>
        form.TargetEffectiveDate == default ? null : form.TargetEffectiveDate.Date;

    private static DateTime AvailableOn(string type, DateTime dueDate, ComplianceScheduleSettings schedule) =>
        ComplianceScheduleRules.AvailableOn(type, dueDate, schedule);
}

/// <summary>
/// What must not change: every client, every note, and the last 30 days of scratchpad,
/// fingerprinted by content so an edit is caught as well as a deletion.
/// </summary>
internal sealed record Fingerprint(
    long Clients, long ClientSum, long Notes, long NoteSum, long Scratchpad, long ScratchpadSum)
{
    public static async Task<Fingerprint> ReadAsync(SatiContext context)
    {
        var connection = (SqlConnection)context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 600;
            command.CommandText = """
                DECLARE @padFrom datetime2 = DATEADD(day, -30, SYSUTCDATETIME());
                DECLARE @pad bigint = -1, @padSum bigint = 0;
                IF OBJECT_ID(N'dbo.Scratchpad', N'U') IS NOT NULL
                    EXEC sp_executesql
                        N'SELECT @n = COUNT_BIG(*), @s = ISNULL(SUM(CONVERT(bigint, CHECKSUM(Id, UserId, [Date], Content))), 0) FROM dbo.Scratchpad WHERE [Date] >= @from',
                        N'@n bigint OUTPUT, @s bigint OUTPUT, @from datetime2', @n = @pad OUTPUT, @s = @padSum OUTPUT, @from = @padFrom;
                SELECT
                    (SELECT COUNT_BIG(*) FROM dbo.People),
                    (SELECT ISNULL(SUM(CONVERT(bigint, CHECKSUM(Id, FirstName, LastName, BirthDate, EffectiveDate, AgencyId, UserId))), 0) FROM dbo.People),
                    (SELECT COUNT_BIG(*) FROM dbo.Notes),
                    (SELECT ISNULL(SUM(CONVERT(bigint, CHECKSUM(Id, Narrative, EventDate, Status, Minutes, PersonId, NoteType))), 0) FROM dbo.Notes),
                    @pad, @padSum;
                """;
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            return new Fingerprint(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2),
                reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5));
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}
