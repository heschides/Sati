using Microsoft.EntityFrameworkCore;
using Sati.Data;

// Runs the real startup analyzer against a real database and prints what it found.
//
// Three releases have now refused to start on the production workstation, and each
// diagnosis so far has been a reimplementation of the analyzer's reasoning in SQL,
// checked against a list of objects chosen by hand. Two of those were right and the
// third was not, which is a bad ratio for a tool whose whole job is to be exact.
//
// This calls MigrationEffectAnalyzer itself - the same code the startup path runs -
// and prints every present, missing and unverifiable effect. It cannot disagree with
// the verdict that stops startup, because it is that verdict.
//
// Read-only. It opens no transaction and applies nothing.

var database = args.FirstOrDefault(a => !a.StartsWith('-')) ?? "SatiProduction";
var server = args.SkipWhile(a => a != "--server").Skip(1).FirstOrDefault() ?? @"(localdb)\MSSQLLocalDB";
var connectionString =
    $"Server={server};Database={database};Integrated Security=true;Encrypt=false;Connect Timeout=30;";

Console.WriteLine("Sati update-check report");
Console.WriteLine($"Run at   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
Console.WriteLine($"Server   : {server}");
Console.WriteLine($"Database : {database}");
Console.WriteLine($"Tool     : {typeof(SatiContext).Assembly.GetName().Version}");
Console.WriteLine("This report makes no changes.");
Console.WriteLine();

try
{
    await using var context = new SatiContext(
        new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer(connectionString, sql => sql.CommandTimeout(300))
            .Options);

    var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
    var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();

    Console.WriteLine($"Applied updates : {applied.Count}");
    Console.WriteLine($"Pending updates : {pending.Count}");
    foreach (var migration in pending) Console.WriteLine($"  pending  {migration}");
    Console.WriteLine();

    if (pending.Count == 0)
    {
        Console.WriteLine("Nothing is pending. This database is already up to date.");
        return 0;
    }

    var findings = await MigrationEffectAnalyzer.AnalyzeAsync(context, pending);

    foreach (var finding in findings)
    {
        Console.WriteLine($"{finding.MigrationId}");
        Console.WriteLine($"  VERDICT: {finding.State}");
        Console.WriteLine($"  present {finding.PresentEffects.Count}, "
            + $"missing {finding.MissingEffects.Count}, "
            + $"unverifiable {finding.UnverifiableSteps.Count}");

        // The present list is the whole question: on a database that has had none of a
        // release, anything here is a misreading, and its exact wording says which rule
        // produced it.
        foreach (var effect in finding.PresentEffects)
            Console.WriteLine($"    PRESENT      {effect}");
        foreach (var effect in finding.MissingEffects)
            Console.WriteLine($"    missing      {effect}");
        foreach (var step in finding.UnverifiableSteps)
            Console.WriteLine($"    unverifiable {step}");
        Console.WriteLine();
    }

    Console.WriteLine("Report complete. Send this whole file to Josh. Nothing was changed.");
    return 0;
}
catch (Exception failure)
{
    Console.WriteLine("The report could not be produced:");
    Console.WriteLine(failure.ToString());
    return 1;
}
