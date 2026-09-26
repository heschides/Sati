using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Models.Billing;

namespace Sati.Api.Infrastructure;

internal sealed record ClearinghouseUpload(Guid DispatchId, string FileName, string Content,
    string ContentSha256, TradingPartnerKind Partner);

internal sealed record ClearinghouseUploadResult(ClearinghouseAttemptOutcome Outcome,
    string? ExternalFileId, string? VendorCode, int? AcceptedClaims, int? RejectedClaims);

/// <summary>Only transport facts cross this seam. Eligibility, corrections and claim state stay in Sati.</summary>
internal interface IClearinghouseConnector
{
    Task<ClearinghouseUploadResult> UploadAsync(ClearinghouseUpload upload, CancellationToken token);
}

/// <summary>No network calls, credentials, payer response, or claim adjudication.</summary>
internal sealed class SyntheticClearinghouseConnector : IClearinghouseConnector
{
    public Task<ClearinghouseUploadResult> UploadAsync(ClearinghouseUpload upload, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var parsed = ClaimResponseReader.ReadSubmission(upload.Content);
        if (!parsed.Envelope.IsTestInterchange)
            throw new InvalidOperationException("The fake connector accepts only test 837P files.");
        return Task.FromResult(new ClearinghouseUploadResult(ClearinghouseAttemptOutcome.Accepted,
            $"FAKE-{upload.DispatchId:N}", null, parsed.Claims.Count, 0));
    }
}

/// <summary>Claims a queued row before any upload. Sending is never selected again after interruption.</summary>
internal sealed class ClearinghouseDispatchWorker(
    IDbContextFactory<ApiDbContext> contexts, IClearinghouseConnector connector,
    ClearinghouseDispatchGate gate, ILogger<ClearinghouseDispatchWorker> logger) : BackgroundService
{
    internal async Task<bool> ProcessOneAsync(CancellationToken token)
    {
        if (!gate.IsEnabled) return false;
        await using var db = await contexts.CreateDbContextAsync(token);
        var dispatch = await db.ClearinghouseDispatches
            .Where(row => row.State == ClearinghouseDispatchState.Queued)
            .OrderBy(row => row.RequestedAtUtc).ThenBy(row => row.Id)
            .FirstOrDefaultAsync(token);
        if (dispatch is null) return false;

        var account = await db.ClearinghouseAccounts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == dispatch.AccountId && row.AgencyId == dispatch.AgencyId, token);
        var generation = await db.EdiGenerations.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == dispatch.EdiGenerationId && row.AgencyId == dispatch.AgencyId, token);
        if (account is null || generation is null || !account.IsEnabled || !account.IsTest ||
            !generation.IsTest || dispatch.TradingPartnerProfileVersion != account.TradingPartnerProfileVersion ||
            !ClearinghouseAccountSelection.Matches(generation.Content, account, generation.BillingPeriodId))
        {
            dispatch.State = ClearinghouseDispatchState.CancelledBeforeSend;
            dispatch.SafeErrorCode = "source_or_account_changed";
            dispatch.Revision++;
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateConcurrencyException) { return true; }
            return true;
        }

        dispatch.State = ClearinghouseDispatchState.Sending;
        dispatch.Revision++;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { return true; }

        var startedAt = DateTime.UtcNow;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(generation.Content)));
        ClearinghouseUploadResult outcome;
        try
        {
            outcome = await connector.UploadAsync(new ClearinghouseUpload(dispatch.Id,
                generation.FileName, generation.Content, hash, account.ConnectorKind), token);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Once Sending was committed, even a timeout may mean the other side received the file.
            // Never put the exception, request body, or vendor response in a log or safe error code.
            outcome = new ClearinghouseUploadResult(ClearinghouseAttemptOutcome.OutcomeUnknown,
                null, "upload_outcome_unknown", null, null);
        }

        var safeFileId = SafeIdentifier(outcome.ExternalFileId, 128);
        var safeVendorCode = SafeIdentifier(outcome.VendorCode, 40);
        var accepted = outcome.Outcome == ClearinghouseAttemptOutcome.Accepted && safeFileId is not null;
        var rejected = outcome.Outcome == ClearinghouseAttemptOutcome.Rejected;
        dispatch.State = accepted ? ClearinghouseDispatchState.AcceptedByClearinghouse :
            rejected ? ClearinghouseDispatchState.RejectedByClearinghouse : ClearinghouseDispatchState.OutcomeUnknown;
        dispatch.ExternalFileId = accepted ? safeFileId : null;
        dispatch.AcceptedClaimCount = accepted ? outcome.AcceptedClaims : null;
        dispatch.RejectedClaimCount = rejected ? outcome.RejectedClaims : null;
        dispatch.SafeErrorCode = accepted ? null : safeVendorCode ?? "upload_outcome_unknown";
        dispatch.Revision++;
        db.ClearinghouseDispatchAttempts.Add(new ClearinghouseDispatchAttempt
        {
            Id = Guid.NewGuid(), DispatchId = dispatch.Id, AttemptNumber = 1,
            StartedAtUtc = startedAt, CompletedAtUtc = DateTime.UtcNow,
            ContentSha256 = hash, FileName = generation.FileName,
            Outcome = accepted ? ClearinghouseAttemptOutcome.Accepted :
                rejected ? ClearinghouseAttemptOutcome.Rejected : ClearinghouseAttemptOutcome.OutcomeUnknown,
            VendorCode = safeVendorCode, CorrelationId = dispatch.Id.ToString("N")
        });
        db.BillingSubmissionEvents.Add(new ServerBillingSubmissionEvent
        {
            AgencyId = dispatch.AgencyId, BillingPeriodId = generation.BillingPeriodId,
            EdiGenerationId = generation.Id, OccurredAtUtc = DateTime.UtcNow,
            Stage = accepted ? BillingSubmissionStage.Transmitted : BillingSubmissionStage.TransportFailed,
            Reference = accepted ? safeFileId : null, ResponseType = "837P",
            ResponseCode = accepted ? "synthetic-upload" : dispatch.SafeErrorCode,
            Explanation = accepted
                ? "Synthetic test upload accepted by the fake connector; no payer response is implied."
                : rejected
                    ? "The fake connector rejected the test upload. Review the dispatch evidence."
                    : "The test upload outcome is unknown. Do not resend until reconciled.",
            IsSynthetic = true
        });
        // The queued audit event names the human requester. These rows are system-operated
        // transport evidence; no background process impersonates that human in AuditEvents.
        try { await db.SaveChangesAsync(CancellationToken.None); }
        catch (DbUpdateException)
        {
            logger.LogError("Clearinghouse dispatch evidence could not be committed; dispatch requires manual reconciliation.");
            throw;
        }
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!gate.IsEnabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await ProcessOneAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                logger.LogError("Synthetic clearinghouse dispatch worker paused after a safe processing failure.");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static string? SafeIdentifier(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
            ? value : null;
}
