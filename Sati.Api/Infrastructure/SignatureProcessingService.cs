using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Signatures;

namespace Sati.Api.Infrastructure;

/// <summary>Server-only recovery loop. No public-portal credentials can resolve these workers.</summary>
internal sealed class SignatureProcessingService(IDbContextFactory<ApiDbContext> factory, SignatureFeature feature,
    SignatureOptions options, SignatureComplianceProjectionService compliance,
    SignatureCompletionWorker packages, SignatureMailWorker mail,
    ILogger<SignatureProcessingService> logger, IDemoWorkerResetCoordination resetCoordination,
    TimeProvider timeProvider, IOptions<SatiApiOptions> apiOptions) : BackgroundService
{
    private int lastCompletion;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10), timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) when (error is not OperationCanceledException)
            { logger.LogWarning("Signature processing pass failed ({FailureType}). Review signing service health.", error.GetType().Name); }
        }
    }

    internal async Task RunOnceAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (ExecutionStrategy.Current?.RetriesOnFailure == true)
            throw new InvalidOperationException("Signature processing cannot run inside a retrying execution scope.");
        if (!CanRun(token)) return;
        await resetCoordination.RunAsync(async innerToken =>
        {
            if (!CanRun(innerToken)) return false;
            await ProcessPassAsync(innerToken);
            return true;
        }, false, token);
        token.ThrowIfCancellationRequested();
    }

    private bool CanRun(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!feature.Enabled || !options.WorkersEnabled) return false;
        if (apiOptions.Value.ExpectedEnvironment != options.ExpectedEnvironment ||
            apiOptions.Value.ExpectedDatabaseName != options.ExpectedDatabaseName)
            throw new InvalidOperationException("Signature worker target differs from its API target.");
        return true;
    }

    private async Task ProcessPassAsync(CancellationToken token)
    {
        if (!CanRun(token)) return;
        try { await ProjectCompliance(token); }
        catch (Exception error) when (error is not OperationCanceledException)
        { logger.LogWarning("Signature compliance projection failed ({FailureType}). Review signing service health.", error.GetType().Name); }
        if (!CanRun(token)) return;
        try { await PreparePackages(token); }
        catch (Exception error) when (error is not OperationCanceledException)
        { logger.LogWarning("Signature package scan failed ({FailureType}). Review signing service health.", error.GetType().Name); }
        for (var i = 0; i < 10; i++)
        {
            if (!CanRun(token)) return;
            try
            {
                await using var db = await factory.CreateDbContextAsync(token);
                if (!await new SignatureStaffSingleAttempt(db).ExecuteAsync(() => mail.ProcessNextAsync(db, token))) break;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            { logger.LogWarning("Signature notification processing failed ({FailureType}). Review signing service health.", error.GetType().Name); break; }
        }
    }
    private async Task ProjectCompliance(CancellationToken ct)
    {
        for (var i = 0; i < 10; i++)
        {
            if (!CanRun(ct)) return;
            await using var db = await factory.CreateDbContextAsync(ct);
            if (!await new SignatureStaffSingleAttempt(db).ExecuteAsync(
                    () => compliance.ProcessNextAsync(db, ct)))
                break;
        }
    }

    private async Task PreparePackages(CancellationToken ct)
    {
        if (!CanRun(ct)) return;
        int[] candidates;
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            candidates = await db.SignatureCompletions.AsNoTracking().Where(x => x.Id > lastCompletion && !db.SignaturePackages.Any(p => p.CompletionId == x.Id))
                .OrderBy(x => x.Id).Select(x => x.Id).Take(10).ToArrayAsync(ct);
        }
        if (candidates.Length == 0) { lastCompletion = 0; return; }
        foreach (var id in candidates)
        {
            if (!CanRun(ct)) return;
            lastCompletion = id; // A damaged earlier record cannot starve every later signer of a copy.
            try
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                await new SignatureStaffSingleAttempt(db).ExecuteAsync(() => packages.BuildAsync(db, id, ct));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            { logger.LogWarning("Signature copy preparation failed ({FailureType}). Review signing service health.", error.GetType().Name); }
        }
    }
}
