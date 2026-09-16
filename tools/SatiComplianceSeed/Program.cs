using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Tools.ComplianceSeed;

// One-off maintenance for Josh's local Production database, which is not the system of
// record (Evergreen, Credible, and his tracking sheet are). It records every compliance
// item that is already past due as completed ON ITS DUE DATE, so the database stops
// showing old work as overdue and starts tracking what is coming due. Anything due today
// or later is left alone. Contact notes are never created.
//
// Every write goes through Sati's own entities and rules: form attestations are System
// attestations, release attestations are the consumer's own case manager's, and each
// carries the SeedReason text so the rows can be recognised later. Each write is audited.
//
//   (no flag)         read-only report of what would change
//   --apply --expect N
//                     backup, rehearse on a restored copy, then apply for real
//
// The report contains counts only: no names, notes, or other personal information.

var apply = args.Contains("--apply", StringComparer.OrdinalIgnoreCase);
var testDatabase = args.Contains("--test-database", StringComparer.OrdinalIgnoreCase);
var database = args.FirstOrDefault(a => !a.StartsWith('-') && !IsOptionValue(args, a)) ?? "SatiProduction";
var server = OptionValue(args, "--server") ?? @"(localdb)\MSSQLLocalDB";
var expected = int.TryParse(OptionValue(args, "--expect"), out var parsedExpected) ? parsedExpected : (int?)null;
var today = DateTime.Today;

Console.WriteLine(apply ? "Sati compliance seed - APPLY" : "Sati compliance seed - CHECK ONLY");
Console.WriteLine($"Run at   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
Console.WriteLine($"Server   : {server}");
Console.WriteLine($"Database : {database}");
Console.WriteLine($"Clears   : items due before {today:yyyy-MM-dd}, recorded as done on their own due date");
Console.WriteLine(apply ? "This changes the database after a backup and a rehearsal." : "This makes no changes.");
Console.WriteLine();

try
{
    if (!testDatabase && !string.Equals(database, "SatiProduction", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("STOPPED: this tool only runs against SatiProduction. Nothing was changed.");
        return 2;
    }

    // Sati never opens a test database, so only a real run needs Sati closed.
    if (apply && !testDatabase && Process.GetProcessesByName("Sati").Length > 0)
    {
        Console.WriteLine("STOPPED: Sati is open. Close it and run this again. Nothing was changed.");
        return 2;
    }

    SeedPlan plan;
    Fingerprint before;
    await using (var context = Seeder.Open(server, database))
    {
        if (await Seeder.CheckReadyAsync(context, database) is { } problem)
        {
            Console.WriteLine($"STOPPED: {problem} Nothing was changed.");
            return 2;
        }

        plan = await Seeder.PlanAsync(context, today);
        plan.Print();
        before = await Fingerprint.ReadAsync(context);
    }

    if (!apply)
    {
        Console.WriteLine();
        Console.WriteLine($"TOTAL CHANGES: {plan.TotalChanges}");
        Console.WriteLine("Check complete. Nothing was changed.");
        return 0;
    }

    if (expected != plan.TotalChanges)
    {
        Console.WriteLine();
        Console.WriteLine($"STOPPED: you entered {expected?.ToString() ?? "nothing"}, but there are "
            + $"{plan.TotalChanges} changes to make. Run Step 1 again and type its TOTAL CHANGES. "
            + "Nothing was changed.");
        return 2;
    }

    if (plan.TotalChanges == 0)
    {
        Console.WriteLine();
        Console.WriteLine("DONE. There was nothing to change.");
        return 0;
    }

    Console.WriteLine();
    var backupPath = await Seeder.BackupAsync(server, database);
    Console.WriteLine($"Backup verified: {backupPath}");

    // Rehearse on a restored copy first. The real run happens only if the copy comes out
    // exactly as planned: every planned item cleared and nothing protected touched.
    var rehearsal = $"{database}_SeedRehearsal";
    Console.WriteLine();
    Console.WriteLine($"Rehearsing on a restored copy ({rehearsal})...");
    await Seeder.RestoreCopyAsync(server, backupPath, rehearsal);
    try
    {
        var rehearsed = await Seeder.ApplyAndVerifyAsync(server, rehearsal, today, plan, before);
        if (rehearsed is not null)
        {
            Console.WriteLine($"STOPPED: the rehearsal did not come out as planned: {rehearsed}");
            Console.WriteLine("Your real database was not changed. Send this whole file to Josh.");
            return 3;
        }
        Console.WriteLine("Rehearsal matched the plan.");
    }
    finally
    {
        await Seeder.DropCopyAsync(server, rehearsal);
    }

    Console.WriteLine();
    Console.WriteLine("Applying to your database...");
    var result = await Seeder.ApplyAndVerifyAsync(server, database, today, plan, before);
    if (result is not null)
    {
        Console.WriteLine($"WARNING: {result}");
        Console.WriteLine("Do not use Sati. Send this file to Josh. The backup above holds the");
        Console.WriteLine("database exactly as it was before this ran.");
        return 3;
    }

    Console.WriteLine();
    Console.WriteLine("DONE. Past-due items are recorded as done on their due dates. Your client list,");
    Console.WriteLine("notes, and recent scratchpad are unchanged. Start Sati normally.");
    return 0;
}
catch (Exception failure)
{
    Console.WriteLine();
    Console.WriteLine("STOPPED with an error. Changes are saved all at once, so a failure while");
    Console.WriteLine("applying leaves the database as it was. Send this whole file to Josh.");
    Console.WriteLine();
    Console.WriteLine(failure.ToString());
    return 1;
}

static string? OptionValue(string[] args, string name)
{
    var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static bool IsOptionValue(string[] args, string value)
{
    var index = Array.IndexOf(args, value);
    return index > 0 && args[index - 1] is "--server" or "--expect";
}
