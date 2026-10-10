using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sati.Api.Data;
using Sati.Models;
using Sati.Signatures;

namespace Sati.Api.Infrastructure;

/// <summary>Server-only bounded recovery; scheduling offers never establish clinical effects or delivery.</summary>
internal sealed class SignatureProcessingService(IDbContextFactory<ApiDbContext> factory, SignatureWorkerGate gate,
    SignatureWorkSelector selector, SignatureComplianceProjectionService compliance,
    SignatureCompletionWorker packages, SignatureMailWorker mail, ILogger<SignatureProcessingService> logger,
    IDemoWorkerResetCoordination resetCoordination, TimeProvider timeProvider) : BackgroundService
{
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
        if (!gate.CanRun(token)) return;
        foreach (var kind in Enum.GetValues<SignatureWorkKind>())
        {
            var visited = new HashSet<long>();
            for (var attempt = 0; attempt < SignatureWorkSelector.MaximumOffersPerPhase; attempt++)
            {
                if (!gate.CanRun(token)) return;
                var result = await resetCoordination.RunAsync(async heldToken =>
                {
                    if (!gate.CanRun(heldToken)) return new SignatureSelection(SignatureSelectionKind.Disabled);
                    var offer = await selector.SelectAsync(kind, visited, heldToken);
                    if (offer.Kind == SignatureSelectionKind.Selected)
                    {
                        if (!visited.Add(offer.ItemId)) throw new InvalidOperationException("Signature work repeated an item offer in one phase.");
                        if (gate.CanRun(heldToken)) await ProcessOfferAsync(kind, offer, heldToken);
                    }
                    return offer;
                }, new SignatureSelection(SignatureSelectionKind.Contended), token);
                token.ThrowIfCancellationRequested();
                if (result.Kind is SignatureSelectionKind.Contended or SignatureSelectionKind.Disabled) return;
                if (result.Kind == SignatureSelectionKind.Empty) break;
            }
        }
    }

    private async Task ProcessOfferAsync(SignatureWorkKind kind, SignatureSelection offer, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        // Shared read failures stop the pass. Business processors revalidate the stored scope again.
        var current = kind switch
        {
            SignatureWorkKind.Projection => await SignatureComplianceProjectionService.EligibleCandidates(db)
                .AnyAsync(x => x.AgencyId == offer.AgencyId && x.Id == offer.ItemId, token),
            SignatureWorkKind.Package => await SignatureCompletionWorker.EligibleCandidates(db)
                .AnyAsync(x => x.AgencyId == offer.AgencyId && x.Id == offer.ItemId, token),
            SignatureWorkKind.Mail => await SignatureMailWorker.EligibleCandidates(db, timeProvider.GetUtcNow().UtcDateTime)
                .AnyAsync(x => x.AgencyId == offer.AgencyId && x.Id == offer.ItemId, token),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        if (!current || !gate.CanRun(token)) return;
        try
        {
            await new SignatureStaffSingleAttempt(db).ExecuteAsync(async () =>
            {
                if (kind == SignatureWorkKind.Projection) await compliance.ProjectCompletionAsync(db, checked((int)offer.ItemId), token);
                else if (kind == SignatureWorkKind.Package) await packages.BuildAsync(db, checked((int)offer.ItemId), token);
                else await mail.ProcessCandidateAsync(db, offer.AgencyId, offer.ItemId, token);
            });
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning("Signature {WorkKind} work deferred for agency {AgencyId}, item {ItemId} ({FailureType}). Review signing service health.",
                kind, offer.AgencyId, offer.ItemId, error.GetType().Name);
        }
    }
}
