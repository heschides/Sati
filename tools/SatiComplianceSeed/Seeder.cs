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
internal sealed record HeldBack(Person Person, Form Form);
internal sealed record GateBlocker(int PersonId, int CaseManagerId, string Type, DateTime DueDate, string ObligationId);

internal sealed class SeedPlan
{
    public List<FormCompletion> Completions { get; } = [];
    public List<FormOpening> Openings { get; } = [];
    public List<ReleaseCompletion> Releases { get; } = [];
    public List<NewRelease> NewReleases { get; } = [];
    public List<string> Skips { get; } = [];
    public int MissingPastDueRows { get; set; }

    /// <summary>
    /// Demo only: past-due forms deliberately left overdue so a presenter can show the
    /// billing gate. Not a change and not a skip; completions never include them.
    /// </summary>
    public List<HeldBack> HeldBack { get; } = [];

    /// <summary>Demo only: the teaching exceptions whose restored completion must be revoked.</summary>
    public List<HeldBack> Reopened { get; } = [];

    /// <summary>
    /// Demo only: the showcase seed's profile teaching cases. One deliberately has no
    /// effective date, so Sati cannot place its forms in a plan year; whatever that
    /// holds back is part of the lesson, not a failed reset.
    /// </summary>
    public HashSet<int> ProfileTeachingCaseIds { get; } = [];

    /// <summary>
    /// Demo only: past forms recorded as done after their due date, re-recorded on it. A
    /// late completion holds every note between the two, so synthetic history must not
    /// carry one.
    /// </summary>
    public List<FormCompletion> Redates { get; } = [];

    /// <summary>Annual rows Sati itself would create on its next load; saved with the rest.</summary>
    public int RowsCreated { get; set; }

    public int TotalChanges => Completions.Count + Openings.Count + Releases.Count + NewReleases.Count +
                               RowsCreated + Reopened.Count + Redates.Count;

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
            .Append($"rows={RowsCreated}").Append($"skipped={Skips.Count}")
            .Append($"held={HeldBack.Count}").Append($"reopened={Reopened.Count}")
            .Append($"redated={Redates.Count}"));

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
        if (Redates.Count > 0)
        {
            Console.WriteLine($"Late completions moved to the due date   {Redates.Count}");
            foreach (var group in Redates.GroupBy(item => Person.FormDisplayName(item.Form.Type)).OrderBy(g => g.Key))
                Console.WriteLine($"  {group.Key,-26} {group.Count(),5}");
        }
        if (HeldBack.Count > 0)
        {
            Console.WriteLine($"Teaching exceptions left overdue         {HeldBack.Count}");
            foreach (var group in HeldBack.GroupBy(item => Person.FormDisplayName(item.Form.Type)).OrderBy(g => g.Key))
                Console.WriteLine($"  {group.Key,-26} {group.Count(),5}");
            Console.WriteLine($"  reopened from a restored completion    {Reopened.Count,5}");
        }
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
    public const string ProductionMarker = "Production";
    public const string DemoMarker = "Demo";

    /// <summary>
    /// The Bio prefix the Demo showcase seed gives its six profile teaching cases. They
    /// demonstrate missing demographics and are never also a billing exception.
    /// </summary>
    public const string ProfileTeachingCasePrefix = "[DEMO TEACHING CASE";

    /// <summary>
    /// How recently a quarterly review must have fallen due to be chosen as a Demo billing
    /// exception: recent enough that the notes the showcase seed dates in the last few
    /// weeks fall inside its blocked window, so the gate is visible on real notes.
    /// </summary>
    public const int TeachingExceptionWindowDays = 75;

    public const string DemoTeachingExceptionReason =
        "Demo teaching exception: left overdue so the billing gate can be demonstrated.";

    public static string SeedReason(DateTime today) =>
        $"Seeded {today:yyyy-MM-dd} to match the external tracking sheet; not a record of completion.";

    public static string DemoSeedReason(DateTime today) =>
        $"Demo reset {today:yyyy-MM-dd}: synthetic history recorded as done on its due date; not a record of completion.";

    public static SatiContext Open(string server, string database, string? accessToken = null)
    {
        var builder = new DbContextOptionsBuilder<SatiContext>();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            builder.UseSqlServer(ConnectionString(server, database), sql => sql.CommandTimeout(1800));
        }
        else
        {
            // Azure SQL through a managed-identity or workstation token; no password exists.
            var connection = new SqlConnection(
                $"Server={server};Database={database};Encrypt=true;TrustServerCertificate=false;Connect Timeout=30;")
            {
                AccessToken = accessToken
            };
            // EF's default batch size is deliberate: on Azure SQL, 1000-row batches made the
            // Demo apply four times slower (large MERGE statements compile slowly).
            builder.UseSqlServer(connection, contextOwnsConnection: true, sql => sql.CommandTimeout(1800));
        }
        return new SatiContext(builder.Options);
    }

    public static string ConnectionString(string server, string database) =>
        $"Server={server};Database={database};Integrated Security=true;Encrypt=false;Connect Timeout=30;";

    /// <summary>Null when the database is a Production Sati database this build can read exactly.</summary>
    public static Task<string?> CheckReadyAsync(SatiContext context, string database) =>
        CheckReadyAsync(context, database, ProductionMarker);

    /// <summary>Null when the database is marked <paramref name="marker"/> and this build can read it exactly.</summary>
    public static async Task<string?> CheckReadyAsync(SatiContext context, string database, string marker)
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
            var actual = await command.ExecuteScalarAsync() as string;
            return string.Equals(actual, marker, StringComparison.Ordinal)
                ? null
                : $"{database} is marked '{actual}', not {marker}.";
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    public static async Task<SeedPlan> PlanAsync(SatiContext context, DateTime today, bool demo = false)
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
        var ordered = people.OrderBy(person => person.Id).ToList();
        foreach (var person in ordered)
        {
            // Sati adds any missing annual rows every time it loads the caseload. Doing the
            // same here, with the same method, means nothing past due is missed because
            // Sati has not been opened since a plan year began.
            var before = person.Forms.Count;
            if (person.EffectiveDate is not null &&
                person.EnsureCurrentCycleForms(today.Date, SettingsFor(person, settingsByAgency)))
                plan.RowsCreated += person.Forms.Count - before;
        }

        // Chosen after the rows exist, so a review Sati had not created yet can still be one.
        var heldBack = demo ? SelectTeachingExceptions(ordered, today.Date) : [];
        foreach (var item in heldBack)
        {
            plan.HeldBack.Add(item);
            if (item.Form.CompletedDate is not null)
                plan.Reopened.Add(item);
        }

        if (demo)
        {
            plan.ProfileTeachingCaseIds.UnionWith(ordered
                .Where(person => (person.Bio ?? string.Empty).StartsWith(ProfileTeachingCasePrefix, StringComparison.Ordinal))
                .Select(person => person.Id));
        }

        var held = heldBack.Select(item => (object)item.Form).ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var person in ordered)
        {
            PlanPerson(plan, person, FormDueDateCalculator.ToSchedule(SettingsFor(person, settingsByAgency)),
                today.Date, links[(person.Id, person.AgencyId ?? 0)].ToArray(), held, redateLate: demo);
        }
        return plan;
    }

    private static Settings SettingsFor(Person person, IReadOnlyDictionary<int, Settings> settingsByAgency) =>
        person.AgencyId is int agencyId && settingsByAgency.TryGetValue(agencyId, out var found)
            ? found
            : new Settings();

    /// <summary>
    /// One overdue quarterly review per caseload, so whichever case manager a presenter
    /// signs in as has a client whose recent notes the billing gate holds back. The first
    /// eligible client by id is chosen, so the same fictional people carry the exception
    /// after every reset.
    /// </summary>
    public static IReadOnlyList<HeldBack> SelectTeachingExceptions(IEnumerable<Person> people, DateTime today)
    {
        var earliestDue = today.Date.AddDays(-TeachingExceptionWindowDays);
        var chosen = new List<HeldBack>();
        foreach (var caseload in people
                     .Where(person => person.EffectiveDate is not null &&
                                      person.UserId > 0 &&
                                      !(person.Bio ?? string.Empty).StartsWith(
                                          ProfileTeachingCasePrefix, StringComparison.Ordinal))
                     .GroupBy(person => person.UserId)
                     .OrderBy(group => group.Key))
        {
            foreach (var person in caseload.OrderBy(person => person.Id))
            {
                var review = person.Forms
                    .Where(form => form.Type is FormType.Q1R or FormType.Q2R or FormType.Q3R or FormType.Q4R &&
                                   form.DueDate.Date >= earliestDue &&
                                   form.DueDate.Date < today.Date)
                    .OrderByDescending(form => form.DueDate)
                    .FirstOrDefault();
                if (review is null)
                    continue;
                chosen.Add(new HeldBack(person, review));
                break;
            }
        }
        return chosen;
    }

    private static void PlanPerson(SeedPlan plan, Person person, ComplianceScheduleSettings schedule, DateTime today,
        IReadOnlyList<ReleaseProviderLinkFact> links, IReadOnlySet<object> held, bool redateLate = false)
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
        // In the Demo, a past form finished after its due date is planned onto the due date
        // too; the plan overrides its recorded date in the facts the rules see.
        var planned = person.Forms
            .Where(form => form.DueDate.Date < today && !held.Contains(form) &&
                           (form.CompletedDate is null ||
                            redateLate && form.CompletedDate.Value.Date > form.DueDate.Date))
            .ToDictionary(form => form, form => form.DueDate.Date);
        var facts = person.Forms
            .Select(form => new FormFact(
                form.Id,
                person.Id,
                form.Type.ToString(),
                form.DueDate,
                held.Contains(form)
                    ? null
                    : planned.TryGetValue(form, out var plannedOn) ? plannedOn : form.CompletedDate,
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
            var redate = form.CompletedDate is not null;
            if (!decision.Accepted)
            {
                var why = decision.DateError ?? string.Join(" ", decision.UnmetPrerequisites.Select(item => item.Message));
                plan.Skips.Add(redate
                    ? $"{Person.FormDisplayName(form.Type)} (late completion kept): {why}"
                    : $"{Person.FormDisplayName(form.Type)}: {why}");
                continue;
            }

            accepted.Add(form, completedOn);
            (redate ? plan.Redates : plan.Completions).Add(new FormCompletion(person, form, completedOn));
        }

        foreach (var form in person.Forms.Where(form => form.OpenedDate is null))
        {
            var typeName = form.Type.ToString();
            if (BillingComplianceGate.OpeningDeadline(typeName, form.DueDate) is not DateTime deadline ||
                deadline.Date >= today)
                continue;

            var availableOn = AvailableOn(typeName, form.DueDate, schedule);
            var openedOn = deadline.Date > availableOn ? deadline.Date : availableOn;
            var completedOn = accepted.TryGetValue(form, out var seeded) ? seeded : form.CompletedDate;
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

    public static void Apply(SatiContext context, SeedPlan plan, DateTime today, bool demo = false)
    {
        var reason = demo ? DemoSeedReason(today) : SeedReason(today);
        var recordedAtUtc = DateTime.UtcNow;
        var correlation = $"compliance-seed-{Guid.NewGuid():N}";
        // The Demo run is part of the nightly baseline rebuild, like the showcase seed's own
        // attestations, and every row it writes carries the reason. Auditing each one would
        // bury the Admin activity feed the demo walkthrough shows under thousands of entries.
        Action<Person, int, string, string, string, object> audit = demo
            ? (_, _, _, _, _, _) => { }
            : (person, actorUserId, action, resourceType, resourceId, metadata) =>
                Audit(context, person, actorUserId, action, resourceType, resourceId, correlation, metadata);

        foreach (var item in plan.Reopened)
        {
            item.Form.RevokeAttestation(FormAttestation.Revoked(
                AttestationActorKind.System,
                actorUserId: null,
                recordedAtUtc,
                DemoTeachingExceptionReason));
        }

        // Append-only: the late attestation is revoked, then the due-date one recorded a
        // millisecond later so it is unambiguously the live row.
        foreach (var item in plan.Redates)
        {
            item.Form.RevokeAttestation(FormAttestation.Revoked(
                AttestationActorKind.System,
                actorUserId: null,
                recordedAtUtc,
                reason));
            item.Form.Attest(FormAttestation.Attested(
                item.CompletedOn,
                AttestationActorKind.System,
                actorUserId: null,
                recordedAtUtc.AddMilliseconds(1),
                prerequisiteStateJson: FormAttestationRules.NoPrerequisitesStateJson,
                reason: reason));
        }

        foreach (var item in plan.Completions)
        {
            item.Form.Attest(FormAttestation.Attested(
                item.CompletedOn,
                AttestationActorKind.System,
                actorUserId: null,
                recordedAtUtc,
                prerequisiteStateJson: FormAttestationRules.NoPrerequisitesStateJson,
                reason: reason));
            audit(item.Person, -1, "form.attested", "Form", item.Form.Id.ToString(), new
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
            audit(item.Person, -1, "form.opened", "Form", item.Form.Id.ToString(), new
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
            audit(item.Person, item.Person.UserId, "release-obligation.attested", "Person",
                item.Person.Id.ToString(), new
                {
                    obligationId = item.Row.ObligationId,
                    category = item.Row.Category.ToString(),
                    completedOn = item.CompletedOn.ToString("yyyy-MM-dd"),
                    seeded = true,
                    reason
                });
        }

        ApplyNewReleases(plan, today, reason, recordedAtUtc, audit);
    }

    private static void ApplyNewReleases(SeedPlan plan, DateTime today, string reason,
        DateTime recordedAtUtc, Action<Person, int, string, string, string, object> audit)
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
            audit(item.Person, item.Person.UserId, "release-obligations.reconciled", "Person",
                item.Person.Id.ToString(), new
                {
                    targetEffectiveDate = release.TargetEffectiveDate.ToString("yyyy-MM-dd"),
                    created = new[] { release.StableKey },
                    source = "compliance-seed"
                });
            audit(item.Person, item.Person.UserId, "release-obligation.attested", "Person",
                item.Person.Id.ToString(), new
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
    public static Task<string?> ApplyAndVerifyAsync(
        string server, string database, DateTime today, SeedPlan reviewed, Fingerprint before) =>
        ApplyAndVerifyAsync(() => Open(server, database), database, ProductionMarker, today, reviewed, before,
            demo: false);

    public static async Task<string?> ApplyAndVerifyAsync(
        Func<SatiContext> open, string database, string marker, DateTime today, SeedPlan reviewed,
        Fingerprint before, bool demo)
    {
        await using (var context = open())
        {
            if (await CheckReadyAsync(context, database, marker) is { } problem)
                return problem;
            var plan = await PlanAsync(context, today, demo);
            if (plan.Summary != reviewed.Summary)
                return $"the database changed since the check ({plan.Summary} vs {reviewed.Summary}).";
            // Generated rows are saved first so their ids exist for the audit entries; both
            // saves share one transaction, so a failure leaves nothing behind.
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.SaveChangesAsync();
            Apply(context, plan, today, demo);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using (var context = open())
        {
            var remaining = await PlanAsync(context, today, demo);
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

    /// <summary>
    /// Evaluates every client as billing does on <paramref name="today"/>: stored rows, the
    /// expected rows billing projects when one is missing, openings, releases, and monthly
    /// contact, each against its own agency's requirements. Ids only, never names.
    /// </summary>
    public static async Task<IReadOnlyList<GateBlocker>> BlockersAsync(SatiContext context, DateTime today) =>
        (await GateAsync(context, today, historyDays: 0)).Today;

    /// <summary>
    /// <see cref="BlockersAsync"/>, plus every recorded note from the last
    /// <paramref name="historyDays"/> days evaluated on its own service date, the way
    /// billing evaluates it. History completed late holds those notes even when today
    /// reads clean, so both are reported.
    /// </summary>
    public static async Task<(IReadOnlyList<GateBlocker> Today, IReadOnlyList<(int NoteId, DateTime ServiceDate, GateBlocker Blocker)> Notes)>
        GateAsync(SatiContext context, DateTime today, int historyDays)
    {
        var people = await context.People
            .Include(person => person.Forms).ThenInclude(form => form.Attestations)
            .Include(person => person.ReleaseObligations).ThenInclude(row => row.Attestations)
            .Include(person => person.ReleaseObligations).ThenInclude(row => row.AuthorizationEvents)
            .AsSplitQuery()
            .AsNoTracking()
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
        // Read as the desktop's BillingComplianceProjectionLoader reads it: dates and types, never narratives.
        var contacts = (await context.Notes.AsNoTracking()
                .Where(note => note.EventDate != null)
                .Select(note => new { note.Id, note.PersonId, note.AgencyId, note.EventDate, note.NoteType, note.Activities, note.Status })
                .ToListAsync())
            .Select(note => new
            {
                note.PersonId,
                note.AgencyId,
                Fact = MonthlyContactRules.ToFact(
                    note.NoteType?.ToString(), note.Status?.ToString(), note.EventDate, note.Id, (int?)note.Activities)
            })
            .Where(item => item.Fact is not null)
            .ToLookup(item => (item.PersonId, item.AgencyId), item => item.Fact!);

        var historyFrom = today.Date.AddDays(-historyDays);
        var serviceNotes = historyDays <= 0
            ? Array.Empty<(int PersonId, int NoteId, DateTime Date)>().ToLookup(note => note.PersonId)
            : (await context.Notes.AsNoTracking()
                    .Where(note => note.EventDate >= historyFrom && note.EventDate < today.Date)
                    .Select(note => new { note.Id, note.PersonId, note.EventDate, note.Status })
                    .ToListAsync())
                .Where(note => MonthlyContactRules.HasOccurred(note.Status?.ToString()))
                .Select(note => (note.PersonId, NoteId: note.Id, Date: note.EventDate!.Value.Date))
                .ToLookup(note => note.PersonId);

        var blockers = new List<GateBlocker>();
        var heldNotes = new List<(int, DateTime, GateBlocker)>();
        foreach (var person in people.OrderBy(person => person.Id))
        {
            var settings = SettingsFor(person, settingsByAgency);
            var schedule = FormDueDateCalculator.ToSchedule(settings);
            person.ReleaseProviderLinksForCompliance = links[(person.Id, person.AgencyId ?? 0)].ToList();
            person.ContactFactsForCompliance = contacts[(person.Id, person.AgencyId)].ToList();
            GateBlocker[] Evaluate(DateTime serviceDate) =>
                (person.EvaluateBillingWindowDetailed(serviceDate, settings.BillingComplianceRequirements, schedule)
                        .Blockers ?? [])
                    .Select(blocker => new GateBlocker(
                        person.Id, person.UserId, blocker.Type, blocker.DueDate, blocker.ObligationId))
                    .ToArray();

            blockers.AddRange(Evaluate(today.Date));
            foreach (var note in serviceNotes[person.Id])
                heldNotes.AddRange(Evaluate(note.Date).Select(blocker => (note.NoteId, note.Date, blocker)));
        }
        return (blockers, heldNotes);
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
