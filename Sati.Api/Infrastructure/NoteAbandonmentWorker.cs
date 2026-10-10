using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
        var result = await command.ExecuteScalarAsync(token);
        try { return SqlSessionAdmission.OwnsLease(result, token); }
        catch
        {
            // RunOnceAsync has not yet received success/recorded this lock for its finally.
            if (SqlSessionAdmission.ConfirmedAcquired(result)) await ReleaseAsync(db, resource);
            throw;
        }
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
    internal const int AgencyDiscoveryPageSize = 100;
    private Dictionary<int, DateTime> completedByAgency = [];
    private DateTime? completionCacheDay;
    private DateTime? allCompletedOn;

    internal async Task<int> RunDueAsync(CancellationToken token)
    {
        if (!options.CurrentValue.EnableNoteAbandonmentWorker) return 0;
        await runGate.WaitAsync(token);
        try
        {
            var today = clock.Today;
            if (completionCacheDay != today)
            {
                // Entries are written only for this pass's captured local day under runGate.
                // Replace storage too, so removed agencies do not retain past-day capacity.
                completedByAgency = [];
                completionCacheDay = today;
            }
            if (allCompletedOn == today) return 0;
            var changedCount = 0;
            var allCompleted = false;
            var acquired = await coordination.RunOnceAsync(async innerToken =>
            {
                await using var db = await contexts.CreateDbContextAsync(innerToken);
                var agencies = db.Agencies.AsNoTracking();
                // Bound the key range, not membership: agencies may change between pages.
                var upperBound = await agencies.MaxAsync(agency => (int?)agency.Id, innerToken);
                int? lastAgencyId = null;
                allCompleted = true;
                while (upperBound is { } maximumId)
                {
                    innerToken.ThrowIfCancellationRequested();
                    if (!options.CurrentValue.EnableNoteAbandonmentWorker)
                    {
                        allCompleted = false;
                        return;
                    }
                    var pageQuery = agencies.Where(agency => agency.Id <= maximumId);
                    // A nullable first cursor preserves rejection of damaged nonpositive IDs.
                    if (lastAgencyId is { } cursor)
                        pageQuery = pageQuery.Where(agency => agency.Id > cursor);
                    var agencyIds = await pageQuery.OrderBy(agency => agency.Id)
                        .Select(agency => agency.Id).Take(AgencyDiscoveryPageSize)
                        .ToListAsync(innerToken);
                    if (agencyIds.Count == 0) break;
                    lastAgencyId = agencyIds[^1];
                    foreach (var agencyId in agencyIds)
                    {
                        innerToken.ThrowIfCancellationRequested();
                        if (!options.CurrentValue.EnableNoteAbandonmentWorker)
                        {
                            allCompleted = false;
                            return;
                        }
                        if (completedByAgency.GetValueOrDefault(agencyId) == today) continue;
                        NoteAbandonmentResult result;
                        try
                        {
                            result = await sweep.RunAsync(agencyId, userId: null,
                                NoteAbandonmentSweep.WorkerBatchSize, "worker", innerToken);
                        }
                        catch (Exception error) when (!innerToken.IsCancellationRequested &&
                            IsRecoverableAgencyFailure(error))
                        {
                            // The sweep has rolled back and disposed its transaction. Leave
                            // this agency due for the next hourly pass, without another retry.
                            allCompleted = false;
                            logger.LogWarning("Note abandonment worker deferred agency {AgencyId} ({FailureType}).",
                                agencyId, error.GetType().Name);
                            continue;
                        }
                        changedCount += result.ChangedCount;
                        if (result.AtLimit) allCompleted = false;
                        else completedByAgency[agencyId] = today;
                    }
                    if (agencyIds.Count < AgencyDiscoveryPageSize || lastAgencyId == maximumId)
                        break;
                }
                innerToken.ThrowIfCancellationRequested();
                if (!options.CurrentValue.EnableNoteAbandonmentWorker)
                {
                    allCompleted = false;
                    return;
                }
                // Observed higher-ID growth keeps the existing hourly pass due. This cannot
                // detect within-range inserts after discovery or inserts after this check.
                if (allCompleted && await (upperBound is { } bound
                        ? agencies.AnyAsync(agency => agency.Id > bound, innerToken)
                        : agencies.AnyAsync(innerToken)))
                    allCompleted = false;
                innerToken.ThrowIfCancellationRequested();
                if (!options.CurrentValue.EnableNoteAbandonmentWorker) allCompleted = false;
            }, token);
            token.ThrowIfCancellationRequested();
            if (acquired && allCompleted) allCompletedOn = today;
            if (changedCount > 0)
                logger.LogInformation("Note abandonment worker changed {NoteCount} notes.", changedCount);
            return changedCount;
        }
        finally { runGate.Release(); }
    }

    private static bool IsRecoverableAgencyFailure(Exception error)
    {
        // EF can wrap an exhausted deadlock retry or an audit-save failure.
        // Unknown wrappers, connection errors and ambiguous commits must escape.
        while (error is not DbUpdateConcurrencyException &&
            (error is DbUpdateException or RetryLimitExceededException) &&
            error.InnerException is { } inner)
            error = inner;

        if (error is DbUpdateConcurrencyException) return true;
        return error is SqlException sql && sql.Errors.Count > 0 &&
            sql.Errors.Cast<SqlError>().All(item => item.Number == 1205 && item.Class < 20);
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
