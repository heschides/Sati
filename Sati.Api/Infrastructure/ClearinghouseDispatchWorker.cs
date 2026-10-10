using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Models.Billing;
using Sati.Data;
using Sati.Api.Endpoints;

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
    ILogger<ClearinghouseDispatchWorker> logger, TimeProvider clock,
    ClearinghouseDispatchSchedule schedule) : BackgroundService
{
    private sealed record Preparation(bool Processed, ClearinghouseAccount? Account = null,
        ServerEdiGeneration? Generation = null)
    {
        public static Preparation Idle { get; } = new(false);
        public static Preparation Completed { get; } = new(true);
    }

    internal async Task<bool> ProcessOneAsync(CancellationToken token) =>
        (await ProcessTurnAsync(token)).Kind == DispatchTurnKind.Processed;

    internal async Task<DispatchTurn> ProcessTurnAsync(CancellationToken token)
    {
        if (ExecutionStrategy.Current?.RetriesOnFailure == true)
            throw new InvalidOperationException("Claim release writes cannot run inside a retrying execution scope.");
        token.ThrowIfCancellationRequested();
        if (!gate.IsEnabled) return DispatchTurn.Disabled;
        return await resetCoordination.RunAsync(ProcessUnderResetLeaseAsync, DispatchTurn.Contended, token);
    }

    private IQueryable<ClearinghouseDispatch> EligibleQueued(ApiDbContext db, DateTime now) =>
        db.ClearinghouseDispatches.Where(row => row.State == ClearinghouseDispatchState.Queued)
            .Where(row => !db.ClearinghouseDispatchReadiness.Any(readiness =>
                readiness.AgencyId == row.AgencyId && readiness.AccountId == row.AccountId &&
                readiness.Disposition != ClearinghousePreflightDisposition.Ready &&
                !(gate.IsRealSandboxEnabled && readiness.Disposition == ClearinghousePreflightDisposition.Deferred &&
                    readiness.NextEligibleAtUtc <= now)));

    private async Task<DispatchTurn> ProcessUnderResetLeaseAsync(CancellationToken token)
    {
        await using var db = await contexts.CreateDbContextAsync(token);
        var now = clock.GetUtcNow().UtcDateTime;
        var eligible = EligibleQueued(db, now);
        var dispatch = await eligible
            .OrderBy(row => row.RequestedAtUtc).ThenBy(row => row.Id)
            .FirstOrDefaultAsync(token);
        if (dispatch is null)
        {
            // One scalar snapshot command, including a newly eligible row racing the initial read.
            // Do not materialize all accounts or interpret a missed lease as an empty queue.
            var realSandbox = gate.IsRealSandboxEnabled;
            var deferred = from readiness in db.ClearinghouseDispatchReadiness
                where realSandbox && readiness.Disposition == ClearinghousePreflightDisposition.Deferred &&
                    readiness.NextEligibleAtUtc > now && db.ClearinghouseDispatches.Any(row =>
                        row.AgencyId == readiness.AgencyId && row.AccountId == readiness.AccountId &&
                        row.State == ClearinghouseDispatchState.Queued)
                select readiness.NextEligibleAtUtc;
            var next = await eligible.Select(_ => (DateTime?)now).Concat(deferred)
                .OrderBy(value => value).FirstOrDefaultAsync(token);
            return new DispatchTurn(DispatchTurnKind.Empty, next);
        }

        var processed = await resetCoordination.RunDispatchAsync(dispatch.Id,
            innerToken => new DispatchSingleAttempt(db).ExecuteAsync(
                () => UploadUnderDispatchLeaseAsync(db, dispatch, innerToken)), false, token);
        return processed ? DispatchTurn.Processed : DispatchTurn.Contended;
    }

    private async Task<bool> UploadUnderDispatchLeaseAsync(ApiDbContext db,
        ClearinghouseDispatch dispatch, CancellationToken token)
    {
        var prepared = await resetCoordination.RunAccountPreflightAsync(dispatch.AgencyId, dispatch.AccountId,
            (lease, heldToken) => PrepareUnderAccountLeaseAsync(db, dispatch, lease, heldToken), Preparation.Idle, token);
        if (prepared.Account is null || prepared.Generation is null) return prepared.Processed;
        // Sending is committed. Release account admission before the physical upload;
        // the dispatch/reset leases and existing uncertainty policy remain in force.
        return await UploadAndRetainAsync(db, dispatch, prepared.Account, prepared.Generation, token);
    }

    private async Task<Preparation> PrepareUnderAccountLeaseAsync(ApiDbContext db,
        ClearinghouseDispatch dispatch, IAccountPreflightLease lease, CancellationToken token)
    {
        // Another host may have sent this row while we were acquiring its lease.
        await db.Entry(dispatch).ReloadAsync(token);
        if (!gate.IsEnabled || dispatch.State != ClearinghouseDispatchState.Queued) return Preparation.Idle;
        var preparedReadiness = await db.ClearinghouseDispatchReadiness.AsNoTracking().SingleOrDefaultAsync(
            row => row.AgencyId == dispatch.AgencyId && row.AccountId == dispatch.AccountId, token);
        if (!ClearinghousePreflightRules.IsEligible(preparedReadiness?.Snapshot(), clock.GetUtcNow().UtcDateTime) ||
            preparedReadiness is { FailureCount: > 0 } && !gate.IsRealSandboxEnabled) return Preparation.Idle;

        var account = await db.ClearinghouseAccounts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == dispatch.AccountId && row.AgencyId == dispatch.AgencyId, token);
        var generation = await db.EdiGenerations.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == dispatch.EdiGenerationId && row.AgencyId == dispatch.AgencyId, token);
        var preparedAccount = account;
        if (gate.IsRealSandboxEnabled && account is not null && generation is not null && account.IsEnabled &&
            account.IsTest && generation.IsTest && gate.CanUseAccount(account) &&
            ClearinghouseAccountSelection.Matches(generation.Content, account, generation.BillingPeriodId))
        {
            // Key Vault may perform network I/O. No SQL decision lock spans it.
            token.ThrowIfCancellationRequested();
            try { _ = keys.Resolve(account.SecretReference); }
            catch (ClaimMdAccountKeyUnavailableException)
            {
                token.ThrowIfCancellationRequested();
                return await DeferMissingKeyAsync(db, dispatch, account, preparedReadiness, lease, token);
            }
            _ = await protector.ProtectAsync("claimmd-receipt-preflight",
                new FieldBinding(dispatch.AgencyId, 0, $"ClaimMdPreflight:{dispatch.Id:N}"), token);
        }
        await using (var schedule = await ClaimReleaseWriteScope.BeginAsync(db, dispatch.AgencyId, token))
        {
            await lease.VerifyAsync(token);
            await db.Entry(dispatch).ReloadAsync(token);
            if (!gate.IsEnabled || dispatch.State != ClearinghouseDispatchState.Queued) return Preparation.Idle;
            var readiness = await db.ClearinghouseDispatchReadiness.SingleOrDefaultAsync(row =>
                row.AgencyId == dispatch.AgencyId && row.AccountId == dispatch.AccountId, token);
            if (readiness?.Revision != preparedReadiness?.Revision ||
                !ClearinghousePreflightRules.IsEligible(readiness?.Snapshot(), clock.GetUtcNow().UtcDateTime))
                return Preparation.Idle;
            account = await db.ClearinghouseAccounts.AsNoTracking().SingleOrDefaultAsync(row =>
                row.Id == dispatch.AccountId && row.AgencyId == dispatch.AgencyId, token);
            generation = await db.EdiGenerations.AsNoTracking().SingleOrDefaultAsync(row =>
                row.Id == dispatch.EdiGenerationId && row.AgencyId == dispatch.AgencyId, token);
            if (account is null || generation is null || !account.IsEnabled || !account.IsTest ||
                !generation.IsTest || !gate.CanUseAccount(account) ||
                dispatch.TradingPartnerProfileVersion != account.TradingPartnerProfileVersion ||
                !ClearinghouseAccountSelection.Matches(generation.Content, account, generation.BillingPeriodId) ||
                gate.IsRealSandboxEnabled && (preparedAccount is null || !ClearinghouseAccountBinding.Unchanged(preparedAccount, account)))
            {
                dispatch.State = ClearinghouseDispatchState.CancelledBeforeSend;
                dispatch.SafeErrorCode = "source_or_account_changed"; dispatch.Revision++;
                await db.SaveChangesAsync(token); await schedule.CommitAsync(token);
                return Preparation.Completed;
            }
            var history = await ApiClaimReleaseHistory.LoadAsync(db, dispatch.AgencyId, generation.Id, token);
            var retained = history.Files.SingleOrDefault(row => row.File.Id == generation.Id);
            if (!history.Complete || retained is null || history.Defects.Any(row => row.Code == "candidate_invalid") ||
                !generation.IsCorrection && !ApiClaimReleaseHistory.Evaluate(history, generation.BillingPeriodId,
                    retained.Claims.Select(row => row.NoteId).ToHashSet(), OriginalClaimReleaseOperation.BeginSending,
                    generation.Id, dispatch.Id).Allowed)
            {
                dispatch.State = ClearinghouseDispatchState.CancelledBeforeSend;
                dispatch.SafeErrorCode = "original_claim_release_held";
                dispatch.Revision++;
                try { await db.SaveChangesAsync(token); await schedule.CommitAsync(token); }
                catch (DbUpdateConcurrencyException) { }
                return Preparation.Completed;
            }
            try
            {
                if (!await ApiEndpoints.IsRetainedReleaseAllowedAsync(db, generation, history, retained, token))
                {
                    dispatch.State = ClearinghouseDispatchState.CancelledBeforeSend;
                    dispatch.SafeErrorCode = "billing_release_blocked"; dispatch.Revision++;
                    await db.SaveChangesAsync(token); await schedule.CommitAsync(token);
                    return Preparation.Completed;
                }
            }
            catch (NoteAmendmentWorkflowException)
            {
                dispatch.State = ClearinghouseDispatchState.CancelledBeforeSend;
                dispatch.SafeErrorCode = "note_amendment_financial_hold"; dispatch.Revision++;
                try { await db.SaveChangesAsync(token); await schedule.CommitAsync(token); } catch (DbUpdateConcurrencyException) { }
                return Preparation.Completed;
            }

            if (readiness is { FailureCount: > 0 })
            {
                readiness.Apply(ClearinghousePreflightState.Ready, account.Revision);
                ClearinghousePreflightAudit.RecordSystem(db, readiness, "billing-clearinghouse.preflight-recovered",
                    clock.GetUtcNow().UtcDateTime);
            }
            await lease.VerifyAsync(token);
            dispatch.State = ClearinghouseDispatchState.Sending;
            dispatch.Revision++;
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateConcurrencyException) { return Preparation.Completed; }
            await lease.VerifyAsync(token);
            await schedule.CommitAsync(token);

        }
        return new Preparation(true, account, generation);
    }

    private async Task<bool> UploadAndRetainAsync(ApiDbContext db, ClearinghouseDispatch dispatch,
        ClearinghouseAccount account, ServerEdiGeneration generation, CancellationToken token)
    {
        var startedAt = clock.GetUtcNow().UtcDateTime;
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
        var attempt = new ClearinghouseDispatchAttempt
        {
            Id = Guid.NewGuid(), DispatchId = dispatch.Id, AttemptNumber = 1,
            StartedAtUtc = startedAt, CompletedAtUtc = clock.GetUtcNow().UtcDateTime,
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
        // External upload and wrapping have completed. Re-enter admission only to
        // retain their facts. A receipt arriving after Sending cannot unsend a file.
        await using var resultWrite = await ClaimReleaseWriteScope.BeginAsync(db, dispatch.AgencyId, CancellationToken.None);
        await db.Entry(dispatch).ReloadAsync(CancellationToken.None);
        if (await db.ClearinghouseDispatchAttempts.AnyAsync(row => row.DispatchId == dispatch.Id, CancellationToken.None) ||
            dispatch.State is not (ClearinghouseDispatchState.Sending or ClearinghouseDispatchState.ConfirmedNotReceived or
                ClearinghouseDispatchState.AcceptedByClearinghouse))
            throw new InvalidOperationException("Upload outcome requires reconciliation against retained dispatch evidence.");
        if (dispatch.State != ClearinghouseDispatchState.AcceptedByClearinghouse)
        {
            dispatch.State = accepted ? ClearinghouseDispatchState.AcceptedByClearinghouse :
                rejected ? ClearinghouseDispatchState.RejectedByClearinghouse : ClearinghouseDispatchState.OutcomeUnknown;
            dispatch.ExternalFileId = accepted ? safeFileId : null;
            dispatch.AcceptedClaimCount = accepted ? outcome.AcceptedClaims : null;
            dispatch.RejectedClaimCount = rejected ? outcome.RejectedClaims : null;
            dispatch.SafeErrorCode = accepted ? null : safeVendorCode ?? "upload_outcome_unknown";
        }
        else if (!accepted || dispatch.ExternalFileId != safeFileId)
            dispatch.SafeErrorCode = "upload_reconciliation_conflict";
        dispatch.Revision++;
        db.ClearinghouseDispatchAttempts.Add(attempt);
        db.BillingSubmissionEvents.Add(new ServerBillingSubmissionEvent
        {
            AgencyId = dispatch.AgencyId, BillingPeriodId = generation.BillingPeriodId,
            EdiGenerationId = generation.Id, OccurredAtUtc = clock.GetUtcNow().UtcDateTime,
            Stage = accepted ? BillingSubmissionStage.Transmitted : BillingSubmissionStage.TransportFailed,
            Reference = accepted ? safeFileId : null, ResponseType = "837P",
            ResponseCode = accepted ? gate.IsRealSandboxEnabled ? "claimmd-upload" : "synthetic-upload" : safeVendorCode ?? "upload_outcome_unknown",
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
        try { await db.SaveChangesAsync(CancellationToken.None); await resultWrite.CommitAsync(CancellationToken.None); }
        catch (DbUpdateException)
        {
            logger.LogError("Clearinghouse dispatch evidence could not be committed; dispatch requires manual reconciliation.");
            throw;
        }
        return true;
    }

    private async Task<Preparation> DeferMissingKeyAsync(ApiDbContext db, ClearinghouseDispatch dispatch,
        ClearinghouseAccount preparedAccount, ClearinghouseDispatchReadiness? preparedReadiness,
        IAccountPreflightLease lease, CancellationToken token)
    {
        await using var admission = await ClaimReleaseWriteScope.BeginAsync(db, dispatch.AgencyId, token);
        await lease.VerifyAsync(token);
        await db.Entry(dispatch).ReloadAsync(token);
        if (!gate.IsRealSandboxEnabled || dispatch.State != ClearinghouseDispatchState.Queued) return Preparation.Idle;
        var account = await db.ClearinghouseAccounts.AsNoTracking().SingleOrDefaultAsync(row =>
            row.AgencyId == dispatch.AgencyId && row.Id == dispatch.AccountId, token);
        if (account is null || !ClearinghouseAccountBinding.Unchanged(preparedAccount, account) ||
            !gate.CanUseAccount(account)) return Preparation.Idle;
        var readiness = await db.ClearinghouseDispatchReadiness.SingleOrDefaultAsync(row =>
            row.AgencyId == dispatch.AgencyId && row.AccountId == dispatch.AccountId, token);
        var now = clock.GetUtcNow().UtcDateTime;
        if (readiness?.Revision != preparedReadiness?.Revision ||
            !ClearinghousePreflightRules.IsEligible(readiness?.Snapshot(), now)) return Preparation.Idle;
        var history = await ApiClaimReleaseHistory.LoadAsync(db, dispatch.AgencyId, dispatch.EdiGenerationId, token);
        var retained = history.Files.SingleOrDefault(row => row.File.Id == dispatch.EdiGenerationId);
        var facts = history.Facts.Where(row => row.GenerationId == dispatch.EdiGenerationId).ToList();
        if (!history.Complete || retained is null || history.Defects.Any(row => row.Code == "candidate_invalid") ||
            facts.Count != retained.Claims.Count || facts.Any(row =>
                row.DispatchId != dispatch.Id || row.Evidence != OriginalClaimDeliveryEvidence.Queued)) return Preparation.Idle;
        if (readiness is null)
        {
            readiness = new() { AgencyId = dispatch.AgencyId, AccountId = dispatch.AccountId };
            db.ClearinghouseDispatchReadiness.Add(readiness);
        }
        readiness.Apply(ClearinghousePreflightRules.MissingKey(account.Id, preparedReadiness?.Snapshot(), Guid.NewGuid(), now), account.Revision);
        ClearinghousePreflightAudit.RecordSystem(db, readiness,
            readiness.Disposition == ClearinghousePreflightDisposition.Held
                ? "billing-clearinghouse.preflight-held" : "billing-clearinghouse.preflight-deferred", now);
        await lease.VerifyAsync(token);
        await db.SaveChangesAsync(token);
        await lease.VerifyAsync(token);
        await admission.CommitAsync(token);
        return Preparation.Completed;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        schedule.RunAsync(ProcessTurnAsync,
            () => logger.LogError("Clearinghouse dispatch worker paused after a safe processing failure."), stoppingToken);

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
