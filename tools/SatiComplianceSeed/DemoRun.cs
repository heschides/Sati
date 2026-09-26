using Sati.Data;

namespace Sati.Tools.ComplianceSeed;

/// <summary>
/// The Demo counterpart of the Production seed. The Demo caseload is synthetic, so its
/// history should read as work done on time: every item due before today is recorded as
/// done on its own due date, through the same Sati rules the Production seed uses, except
/// one recent quarterly review per caseload, left overdue so a presenter can show the
/// billing gate on a real client.
///
/// It runs inside the nightly Demo reset, after the canonical baseline is restored and
/// its dates are rolled to today. That restore is the recovery path, so there is no
/// backup or rehearsal here. The whole change is one transaction; afterwards the run
/// re-plans (nothing may remain) and evaluates every client as billing does. Anything
/// blocking billing other than a teaching exception fails the run loudly instead of leaving
/// a Demo that quietly says everything is overdue.
/// </summary>
internal static class DemoRun
{
    public const string AccessTokenVariable = "SATI_SQL_ACCESS_TOKEN";

    public static async Task<int> RunAsync(
        string server, string database, DateTime today, bool apply, bool testDatabase)
    {
        Console.WriteLine(apply ? "Sati Demo compliance seed - APPLY" : "Sati Demo compliance seed - CHECK ONLY");
        Console.WriteLine($"Server   : {server}");
        Console.WriteLine($"Database : {database}");
        Console.WriteLine($"As of    : {today:yyyy-MM-dd}");
        Console.WriteLine();

        if (!testDatabase && !string.Equals(database, "SatiDemo", StringComparison.Ordinal))
        {
            Console.WriteLine("STOPPED: Demo mode only runs against SatiDemo. Nothing was changed.");
            return 2;
        }

        var token = Environment.GetEnvironmentVariable(AccessTokenVariable);
        SatiContext Open() => Seeder.Open(server, database, token);

        // Timed because the manual reset runs inside an HTTP request with a fixed front-end limit.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            SeedPlan plan;
            await using (var context = Open())
            {
                if (await Seeder.CheckReadyAsync(context, database, Seeder.DemoMarker) is { } problem)
                {
                    Console.WriteLine($"STOPPED: {problem} Nothing was changed.");
                    return 2;
                }

                plan = await Seeder.PlanAsync(context, today, demo: true);
                plan.Print();
                Console.WriteLine();
                Console.WriteLine($"TOTAL CHANGES: {plan.TotalChanges}");
                Console.WriteLine($"Planned in {clock.Elapsed.TotalSeconds:n0}s");

                // Unlike the Production seed, the plan is applied in the context that made it:
                // nothing else writes to the Demo while the reset holds its lock, and the gate
                // evaluation below is the check that matters.
                if (apply && plan.TotalChanges > 0)
                {
                    await using var transaction = await context.Database.BeginTransactionAsync();
                    await context.SaveChangesAsync();
                    Seeder.Apply(context, plan, today, demo: true);
                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    Console.WriteLine($"Applied in {clock.Elapsed.TotalSeconds:n0}s");
                }
            }

            await using (var context = Open())
            {
                var gate = await Seeder.GateAsync(context, today, HistoryDays);
                if (apply)
                {
                    // Anything the plan still finds would be a change Sati's rules accepted but
                    // the write did not make; the gate alone would miss one that blocks nothing.
                    var remaining = await Seeder.PlanAsync(context, today, demo: true);
                    Console.WriteLine($"Verified in {clock.Elapsed.TotalSeconds:n0}s");
                    if (remaining.TotalChanges != 0)
                    {
                        Console.WriteLine($"STOPPED: {remaining.TotalChanges} item(s) still needed changes afterwards.");
                        return 3;
                    }
                }
                return Report(plan, gate, checkOnly: !apply);
            }
        }
        catch (Exception failure)
        {
            Console.WriteLine();
            Console.WriteLine("STOPPED with an error. Changes are saved all at once, so a failure while");
            Console.WriteLine("applying leaves the database as the restore and roll left it.");
            Console.WriteLine();
            Console.WriteLine(failure.ToString());
            return 1;
        }
    }

    /// <summary>How far back recorded notes are re-evaluated on their own service dates.</summary>
    private const int HistoryDays = 180;

    private static int Report(
        SeedPlan plan,
        (IReadOnlyList<GateBlocker> Today, IReadOnlyList<(int NoteId, DateTime ServiceDate, GateBlocker Blocker)> Notes) gate,
        bool checkOnly)
    {
        var blockers = gate.Today;
        var expected = plan.HeldBack
            .Select(item => (item.Person.Id, item.Form.Type.ToString(), item.Form.DueDate.Date))
            .ToHashSet();
        bool IsExpected(GateBlocker blocker) =>
            expected.Contains((blocker.PersonId, blocker.Type, blocker.DueDate.Date)) ||
            plan.ProfileTeachingCaseIds.Contains(blocker.PersonId);
        var unexpected = blockers.Where(blocker => !IsExpected(blocker)).ToList();
        var blockedClients = blockers.Select(blocker => blocker.PersonId).Distinct().Count();
        var heldNotes = gate.Notes.Select(item => item.NoteId).Distinct().Count();
        var unexpectedNotes = gate.Notes.Where(item => !IsExpected(item.Blocker)).ToList();

        Console.WriteLine();
        Console.WriteLine($"Clients the billing gate holds today     {blockedClients}");
        Console.WriteLine($"  by a teaching exception or case        {blockers.Count - unexpected.Count,5}");
        Console.WriteLine($"  by anything else                       {unexpected.Count,5}");
        foreach (var blocker in unexpected.Take(50))
            Console.WriteLine($"    person {blocker.PersonId}: {blocker.Type} due {blocker.DueDate:yyyy-MM-dd} ({blocker.ObligationId})");
        Console.WriteLine($"Notes in the last {HistoryDays} days billing holds {heldNotes,5}");
        Console.WriteLine($"  by anything but a teaching exception   {unexpectedNotes.Select(item => item.NoteId).Distinct().Count(),5}");
        foreach (var group in unexpectedNotes
                     .GroupBy(item => (item.Blocker.PersonId, item.Blocker.Type, item.Blocker.DueDate))
                     .Take(20))
            Console.WriteLine($"    person {group.Key.PersonId}: {group.Key.Type} due {group.Key.DueDate:yyyy-MM-dd} holds {group.Count()} note(s)");
        foreach (var group in plan.Skips.GroupBy(reason => reason).OrderBy(g => g.Key))
            Console.WriteLine($"  left alone: {group.Count(),4}  {group.Key}");

        if (checkOnly)
        {
            Console.WriteLine("Check complete. Nothing was changed.");
            return 0;
        }
        if (unexpected.Count > 0)
        {
            Console.WriteLine("STOPPED: the Demo still blocks billing for something other than a teaching exception.");
            return 3;
        }
        Console.WriteLine("DEMO_COMPLIANCE_HISTORY_COMPLETE");
        return 0;
    }
}
