using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Sati.Data;

// Runs the real startup analyzer against a real database and prints what it found.
// With --apply, it then applies the pending updates itself, the same way a Demo
// release is applied, after a backup and with the protected data fingerprinted.
//
// Three releases have refused to start on the production workstation. The shipped
// 1.3.13 binary is byte-identical to the one that converts a database at the same
// migration state cleanly on the development machine, and this report, run on the
// workstation against its database, finds nothing already present. The refusal is
// unexplained. Rather than guess at it, --apply re-runs the gate's own check first
// and refuses exactly as startup would if that check disagrees, so a disagreement is
// caught with its full detail instead of as a one-line dialog.
//
// Without --apply this is read-only: no transaction, nothing applied.

var apply = args.Contains("--apply", StringComparer.OrdinalIgnoreCase);
var database = args.FirstOrDefault(a => !a.StartsWith('-')) ?? "SatiProduction";
var server = args.SkipWhile(a => a != "--server").Skip(1).FirstOrDefault() ?? @"(localdb)\MSSQLLocalDB";
var connectionString =
    $"Server={server};Database={database};Integrated Security=true;Encrypt=false;Connect Timeout=30;";

Console.WriteLine(apply ? "Sati update - APPLY" : "Sati update-check report");
Console.WriteLine($"Run at   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
Console.WriteLine($"Server   : {server}");
Console.WriteLine($"Database : {database}");
Console.WriteLine(apply
    ? "This applies the pending updates after a backup."
    : "This report makes no changes.");
Console.WriteLine();

try
{
    await using var context = new SatiContext(
        new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer(connectionString, sql => sql.CommandTimeout(1800))
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

    if (!apply)
    {
        Console.WriteLine("Report complete. Send this whole file to Josh. Nothing was changed.");
        return 0;
    }

    // Stricter than startup: anything but a clean NotApplied stops here. Startup would
    // record an AlreadyPresent migration; a person should look at that first instead.
    var unclear = findings.Where(f => f.State != MigrationEffectState.NotApplied).ToList();
    if (unclear.Count > 0)
    {
        Console.WriteLine("STOPPED: the check above did not come back clean for every update,");
        Console.WriteLine("so nothing was applied. Send this whole file to Josh.");
        return 2;
    }

    var connection = (SqlConnection)context.Database.GetDbConnection();
    await context.Database.OpenConnectionAsync();

    var marker = await ScalarAsync<string?>(connection,
        "SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1;");
    if (!string.Equals(marker, "Production", StringComparison.Ordinal))
    {
        Console.WriteLine($"STOPPED: this database is marked '{marker}', not Production. Nothing was applied.");
        return 2;
    }

    var before = await FingerprintAsync(connection);
    Console.WriteLine("Protected data before:");
    before.Print();
    Console.WriteLine();

    // LocalDB runs as the signed-in user, so the user's own application-data folder is
    // a place it can actually write. Startup backs up to the same folder.
    var backupDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Sati", "schema-backups");
    Directory.CreateDirectory(backupDirectory);
    var backupPath = Path.Combine(backupDirectory,
        $"{database}-{DateTime.Now:yyyyMMdd-HHmmss}-before-annual-compliance-update.bak");

    Console.WriteLine("Backing up...");
    await using (var backup = connection.CreateCommand())
    {
        backup.CommandTimeout = 1800;
        backup.CommandText =
            $"BACKUP DATABASE [{database}] TO DISK = @path WITH COPY_ONLY, INIT, CHECKSUM;";
        backup.Parameters.AddWithValue("@path", backupPath);
        await backup.ExecuteNonQueryAsync();
    }
    await using (var verify = connection.CreateCommand())
    {
        verify.CommandTimeout = 1800;
        verify.CommandText = "RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM;";
        verify.Parameters.AddWithValue("@path", backupPath);
        await verify.ExecuteNonQueryAsync();
    }
    Console.WriteLine($"Backup verified: {backupPath}");
    Console.WriteLine();

    await context.Database.CloseConnectionAsync();

    Console.WriteLine("Applying updates...");
    await context.Database.MigrateAsync();

    var stillPending = (await context.Database.GetPendingMigrationsAsync()).ToList();
    Console.WriteLine($"Pending updates now: {stillPending.Count}");
    Console.WriteLine();

    await context.Database.OpenConnectionAsync();
    var after = await FingerprintAsync(connection);
    Console.WriteLine("Protected data after:");
    after.Print();
    Console.WriteLine();

    if (!before.Equals(after))
    {
        Console.WriteLine("WARNING: protected data differs from before the update.");
        Console.WriteLine("Do not use Sati. Send this file to Josh. The backup above holds the");
        Console.WriteLine("database exactly as it was before this ran.");
        return 3;
    }

    if (stillPending.Count > 0)
    {
        Console.WriteLine("WARNING: some updates are still pending. Send this file to Josh.");
        return 3;
    }

    Console.WriteLine("DONE. The update is applied and the client list, notes since");
    Console.WriteLine("September 1, and the last 30 days of scratchpad are unchanged.");
    Console.WriteLine("Start Sati normally.");
    return 0;
}
catch (Exception failure)
{
    Console.WriteLine();
    Console.WriteLine("STOPPED with an error. If this happened while applying, the update");
    Console.WriteLine("rolls back on its own, and the backup above is still there.");
    Console.WriteLine("Send this whole file to Josh.");
    Console.WriteLine();
    Console.WriteLine(failure.ToString());
    return 1;
}

static async Task<T> ScalarAsync<T>(SqlConnection connection, string sql)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    var value = await command.ExecuteScalarAsync();
    return value is null or DBNull ? default! : (T)value;
}

// The three things that must survive, fingerprinted by content so an edit is caught
// as well as a deletion. Same columns the compliance repair protects.
static async Task<Fingerprint> FingerprintAsync(SqlConnection connection)
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
            (SELECT COUNT_BIG(*) FROM dbo.Notes WHERE EventDate >= '2026-09-01'),
            (SELECT ISNULL(SUM(CONVERT(bigint, CHECKSUM(Id, Narrative, EventDate, Status, Minutes, PersonId, CaseManagerJustification))), 0) FROM dbo.Notes WHERE EventDate >= '2026-09-01'),
            @pad, @padSum;
        """;
    await using var reader = await command.ExecuteReaderAsync();
    await reader.ReadAsync();
    return new Fingerprint(
        reader.GetInt64(0), reader.GetInt64(1),
        reader.GetInt64(2), reader.GetInt64(3),
        reader.GetInt64(4), reader.GetInt64(5));
}

internal sealed record Fingerprint(
    long Clients, long ClientSum,
    long RecentNotes, long RecentNoteSum,
    long RecentScratchpad, long RecentScratchpadSum)
{
    public void Print()
    {
        Console.WriteLine($"  clients                       {Clients}  (fingerprint {ClientSum})");
        Console.WriteLine($"  notes since September 1       {RecentNotes}  (fingerprint {RecentNoteSum})");
        Console.WriteLine($"  scratchpad, last 30 days      {RecentScratchpad}  (fingerprint {RecentScratchpadSum})");
    }
}
