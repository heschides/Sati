using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Models.Billing;
using Sati.Data;

namespace Sati.Api.Infrastructure;

internal sealed record ClearinghouseUpload(Guid DispatchId, string FileName, string Content,
    string ContentSha256, TradingPartnerKind Partner, string? SecretReference = null);

internal sealed record ClearinghouseUploadResult(ClearinghouseAttemptOutcome Outcome,
    string? ExternalFileId, string? VendorCode, int? AcceptedClaims, int? RejectedClaims,
    string? RawResponse = null);

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
    ClearinghouseDispatchGate gate, EnvelopeProtector protector, IClaimMdSandboxKeySource keys,
    IDemoWorkerResetCoordination resetCoordination,
    ILogger<ClearinghouseDispatchWorker> logger) : BackgroundService
{
    internal async Task<bool> ProcessOneAsync(CancellationToken token)
    {
        if (!gate.IsEnabled) return false;
        return await resetCoordination.RunAsync(ProcessUnderResetLeaseAsync, false, token);
    }

    private async Task<bool> ProcessUnderResetLeaseAsync(CancellationToken token)
    {
        await using var db = await contexts.CreateDbContextAsync(token);
        var dispatch = await db.ClearinghouseDispatches
            .Where(row => row.State == ClearinghouseDispatchState.Queued)
            .OrderBy(row => row.RequestedAtUtc).ThenBy(row => row.Id)
            .FirstOrDefaultAsync(token);
        if (dispatch is null) return false;

        return await resetCoordination.RunDispatchAsync(dispatch.Id,
            innerToken => new DispatchSingleAttempt(db).ExecuteAsync(
                () => UploadUnderDispatchLeaseAsync(db, dispatch, innerToken)), false, token);
    }

    private async Task<bool> UploadUnderDispatchLeaseAsync(ApiDbContext db,
        ClearinghouseDispatch dispatch, CancellationToken token)
    {
        // Another host may have sent this row while we were acquiring its lease.
        await db.Entry(dispatch).ReloadAsync(token);
        if (dispatch.State != ClearinghouseDispatchState.Queued) return false;

        var account = await db.ClearinghouseAccounts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == dispatch.AccountId && row.AgencyId == dispatch.AgencyId, token);
        var generation = await db.EdiGenerations.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == dispatch.EdiGenerationId && row.AgencyId == dispatch.AgencyId, token);
        if (account is null || generation is null || !account.IsEnabled || !account.IsTest ||
            !generation.IsTest || !gate.CanUseAccount(account) ||
            dispatch.TradingPartnerProfileVersion != account.TradingPartnerProfileVersion ||
            !ClearinghouseAccountSelection.Matches(generation.Content, account, generation.BillingPeriodId))
        {
            dispatch.State = ClearinghouseDispatchState.CancelledBeforeSend;
            dispatch.SafeErrorCode = "source_or_account_changed";
            dispatch.Revision++;
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateConcurrencyException) { return true; }
            return true;
        }

        var ownerId = await db.BillingPeriods.AsNoTracking().Where(p => p.Id == generation.BillingPeriodId).Select(p => p.UserId).SingleAsync(token);
        await using (var schedule = await ServiceTimeWriteScope.BeginAsync(db, generation.AgencyId, ownerId, token))
        {
            try { await NoteAmendmentDispatchGuard.ValidateAsync(db, generation, token); }
            catch (NoteAmendmentWorkflowException)
            {
                dispatch.State = ClearinghouseDispatchState.CancelledBeforeSend;
                dispatch.SafeErrorCode = "note_amendment_financial_hold"; dispatch.Revision++;
                try { await db.SaveChangesAsync(token); await schedule.CommitAsync(token); } catch (DbUpdateConcurrencyException) { }
                return true;
            }

            if (gate.IsRealSandboxEnabled)
            {
                // Neither a missing AccountKey nor an unavailable receipt-wrapping key should
                // convert an unsent file into an uncertain upload. Preflight before Sending.
                _ = keys.Resolve(account.SecretReference);
                _ = await protector.ProtectAsync("claimmd-receipt-preflight",
                    new FieldBinding(dispatch.AgencyId, 0, $"ClaimMdPreflight:{dispatch.Id:N}"), token);
            }

            dispatch.State = ClearinghouseDispatchState.Sending;
            dispatch.Revision++;
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateConcurrencyException) { return true; }
            await schedule.CommitAsync(token);

        }

        var startedAt = DateTime.UtcNow;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(generation.Content)));
        ClearinghouseUploadResult outcome;
        try
        {
            outcome = await connector.UploadAsync(new ClearinghouseUpload(dispatch.Id,
                generation.FileName, generation.Content, hash, account.ConnectorKind, account.SecretReference), token);
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
        var attempt = new ClearinghouseDispatchAttempt
        {
            Id = Guid.NewGuid(), DispatchId = dispatch.Id, AttemptNumber = 1,
            StartedAtUtc = startedAt, CompletedAtUtc = DateTime.UtcNow,
            ContentSha256 = hash, FileName = generation.FileName,
            Outcome = accepted ? ClearinghouseAttemptOutcome.Accepted :
                rejected ? ClearinghouseAttemptOutcome.Rejected : ClearinghouseAttemptOutcome.OutcomeUnknown,
            VendorCode = safeVendorCode, CorrelationId = dispatch.Id.ToString("N")
        };
        if (outcome.RawResponse is { } raw)
        {
            attempt.ResponseSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
            var protectedResponse = await protector.ProtectAsync(raw,
                ResponseBinding(dispatch.AgencyId, attempt), CancellationToken.None);
            attempt.ResponseCiphertext = protectedResponse.Ciphertext;
            attempt.ResponseNonce = protectedResponse.Nonce;
            attempt.ResponseTag = protectedResponse.Tag;
            attempt.ResponseWrappedDataKey = protectedResponse.WrappedDataKey;
            attempt.ResponseKeyId = protectedResponse.KeyId;
        }
        db.ClearinghouseDispatchAttempts.Add(attempt);
        db.BillingSubmissionEvents.Add(new ServerBillingSubmissionEvent
        {
            AgencyId = dispatch.AgencyId, BillingPeriodId = generation.BillingPeriodId,
            EdiGenerationId = generation.Id, OccurredAtUtc = DateTime.UtcNow,
            Stage = accepted ? BillingSubmissionStage.Transmitted : BillingSubmissionStage.TransportFailed,
            Reference = accepted ? safeFileId : null, ResponseType = "837P",
            ResponseCode = accepted ? gate.IsRealSandboxEnabled ? "claimmd-upload" : "synthetic-upload" : dispatch.SafeErrorCode,
            Explanation = accepted
                ? gate.IsRealSandboxEnabled
                    ? "Claim.MD received the test file; no payer acceptance or payment is implied."
                    : "Synthetic test upload accepted by the fake connector; no payer response is implied."
                : rejected
                    ? "The test upload was rejected. Review the dispatch evidence."
                    : "The test upload outcome is unknown. Do not resend until reconciled.",
            IsSynthetic = true // This gate only permits test files, even when the vendor receives them.
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
                logger.LogError("Clearinghouse dispatch worker paused after a safe processing failure.");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static string? SafeIdentifier(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
            ? value : null;

    internal static FieldBinding ResponseBinding(int agencyId, ClearinghouseDispatchAttempt attempt) =>
        new(agencyId, 0, $"ClearinghouseAttempt:{attempt.Id:N}:Response:{attempt.ResponseSha256}");

    // SQL providers with retry enabled require an execution scope for our owned
    // transaction. Never replay a scope that may already have uploaded a file.
    private sealed class DispatchSingleAttempt(ApiDbContext context) : ExecutionStrategy(context, 0, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
    }
}
