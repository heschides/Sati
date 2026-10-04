using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sati.Api.Data;

namespace Sati.Api.Infrastructure;

/// <summary>One SQL Server application lock protects the agency sweep across API hosts.</summary>
internal interface INoteAbandonmentCoordination
{
    Task<bool> RunOnceAsync(Func<CancellationToken, Task> sweep, CancellationToken token);
}

internal sealed class SqlNoteAbandonmentCoordination(
    IDbContextFactory<ApiDbContext> contexts,
    IOptions<SatiApiOptions> options) : INoteAbandonmentCoordination
{
    private const string Resource = "Sati.NoteAbandonmentSweep";

    public async Task<bool> RunOnceAsync(Func<CancellationToken, Task> sweep, CancellationToken token)
    {
        await using var db = await contexts.CreateDbContextAsync(token);
        if (!db.Database.IsSqlServer())
            throw new InvalidOperationException("Note abandonment coordination requires SQL Server.");
        await db.Database.OpenConnectionAsync(token);
        var isConfiguredDemoTarget =
            string.Equals(options.Value.ExpectedEnvironment, "Demo", StringComparison.Ordinal) &&
            string.Equals(options.Value.ExpectedDatabaseName, "SatiDemo", StringComparison.Ordinal);
        var resetLeaseAcquired = false;
        var sweepLeaseAcquired = false;
        try
        {
            // HTTP mutations already hold this shared lease. The hosted worker has
            // no middleware, so it must join the same reset exclusion before SQL reads.
            if (isConfiguredDemoTarget)
            {
                if (!await TryAcquireAsync(db, DemoResetLease.Resource, "Shared", token))
                    return false;
                resetLeaseAcquired = true;
            }
            if (!await TryAcquireAsync(db, Resource, "Exclusive", token))
                return false; // Another API host owns this sweep.
            sweepLeaseAcquired = true;
            await sweep(token);
            return true;
        }
        finally
        {
            try
            {
                if (sweepLeaseAcquired)
                    await ReleaseAsync(db, Resource);
            }
            finally
            {
                if (resetLeaseAcquired)
                    await ReleaseAsync(db, DemoResetLease.Resource);
            }
        }
    }

    private static async Task<bool> TryAcquireAsync(ApiDbContext db, string resource,
        string mode, CancellationToken token)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock " +
            "@Resource = @resource, @LockMode = @mode, @LockOwner = 'Session', " +
            "@LockTimeout = 0; SELECT @result;";
        command.CommandTimeout = 10;
        var resourceParameter = command.CreateParameter();
        resourceParameter.ParameterName = "@resource";
        resourceParameter.Value = resource;
        command.Parameters.Add(resourceParameter);
        var modeParameter = command.CreateParameter();
        modeParameter.ParameterName = "@mode";
        modeParameter.Value = mode;
        command.Parameters.Add(modeParameter);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(token));
        if (result == -1) return false;
        if (result < 0)
            throw new InvalidOperationException("Note abandonment coordination lock was not acquired.");
        return true;
    }

    private static async Task ReleaseAsync(ApiDbContext db, string resource)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            "EXEC sys.sp_releaseapplock @Resource = @resource, @LockOwner = 'Session';";
        command.CommandTimeout = 5;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@resource";
        parameter.Value = resource;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }
}

/// <summary>
/// Runs immediately after API wake and again on a later local day. The hourly
/// in-memory date check never opens SQL while the current day's work is complete.
/// </summary>
internal sealed class NoteAbandonmentWorker(
    IDbContextFactory<ApiDbContext> contexts,
    NoteAbandonmentSweep sweep,
    INoteAbandonmentCoordination coordination,
    IOptionsMonitor<SatiApiOptions> options,
    ApiClock clock,
    TimeProvider timeProvider,
    ILogger<NoteAbandonmentWorker> logger) : BackgroundService
{
    private readonly SemaphoreSlim runGate = new(1, 1);
    private readonly Dictionary<int, DateTime> completedByAgency = [];
    private DateTime? allCompletedOn;

    internal async Task<int> RunDueAsync(CancellationToken token)
    {
        if (!options.CurrentValue.EnableNoteAbandonmentWorker) return 0;
        await runGate.WaitAsync(token);
        try
        {
            var today = clock.Today;
            if (allCompletedOn == today) return 0;
            var changedCount = 0;
            var allCompleted = false;
            var acquired = await coordination.RunOnceAsync(async innerToken =>
            {
                await using var db = await contexts.CreateDbContextAsync(innerToken);
                var agencyIds = await db.Agencies.AsNoTracking()
                    .OrderBy(agency => agency.Id).Select(agency => agency.Id)
                    .ToListAsync(innerToken);
                allCompleted = true;
                foreach (var agencyId in agencyIds)
                {
                    if (!options.CurrentValue.EnableNoteAbandonmentWorker)
                    {
                        allCompleted = false;
                        break;
                    }
                    if (completedByAgency.GetValueOrDefault(agencyId) == today) continue;
                    var result = await sweep.RunAsync(agencyId, userId: null,
                        NoteAbandonmentSweep.WorkerBatchSize, "worker", innerToken);
                    changedCount += result.ChangedCount;
                    if (result.AtLimit) allCompleted = false;
                    else completedByAgency[agencyId] = today;
                }
            }, token);
            if (acquired && allCompleted) allCompletedOn = today;
            if (changedCount > 0)
                logger.LogInformation("Note abandonment worker changed {NoteCount} notes.", changedCount);
            return changedCount;
        }
        finally { runGate.Release(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunDueAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                logger.LogError("Note abandonment worker failed ({FailureType}).",
                    error.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromHours(1), timeProvider, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
