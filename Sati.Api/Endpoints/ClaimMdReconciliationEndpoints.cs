using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models.Billing;

namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    private static void MapClaimMdReconciliation(RouteGroupBuilder api)
    {
        api.MapGet("/admin/clearinghouse/dispatches/{dispatchId:guid}/reconciliation",
            async Task<IResult> (Guid dispatchId, ClaimsPrincipal principal, ApiDbContext db,
                ClearinghouseDispatchGate gate, HttpResponse response, CancellationToken token) =>
            {
                response.Headers.CacheControl = "no-store";
                if (!gate.IsSyntheticEnvironment) return Results.NotFound();
                var actor = Actor.From(principal);
                if (!actor.HasAdminPermissions) return Results.Forbid();
                if (!await TenantAccess.IsCurrentActorAsync(db, actor, token)) return Results.Unauthorized();
                var source = await LoadClaimMdReconciliationSourceAsync(db, actor.AgencyId, dispatchId, token);
                if (source is null) return Results.NotFound();
                if (!ClaimMdReconciliationRules.CanResolveFrom((int)source.Value.Dispatch.State))
                    return ReconciliationConflict("dispatch_not_uncertain", "Only an uncertain test upload can be reconciled.");
                var manifest = ReadClaimMdReconciliationManifest(source.Value);
                if (manifest is null)
                    return ReconciliationConflict("source_invalid", "The retained test file needs source review before reconciliation.");
                var attempts = await db.ClearinghouseDispatchAttempts.AsNoTracking()
                    .Where(row => row.DispatchId == dispatchId).Take(2).ToListAsync(token);
                if (!ConsistentUncertainAttempts(attempts, manifest))
                    return ReconciliationConflict("attempt_conflict", "Retained upload attempts require separate review.");
                return Results.Ok(WithEvidenceFloor(manifest, attempts));
            });

        api.MapPost("/admin/clearinghouse/dispatches/{dispatchId:guid}/reconciliation",
            async Task<IResult> (Guid dispatchId, ReconcileClaimMdDispatchRequest request,
                ClaimsPrincipal principal, ApiDbContext db, ClearinghouseDispatchGate gate,
                AuditTrail audit, IDemoWorkerResetCoordination coordination,
                ApiClock clock,
                HttpResponse response, CancellationToken cancellationToken) =>
            {
                response.Headers.CacheControl = "no-store";
                if (!gate.IsSyntheticEnvironment) return Results.NotFound();
                var actor = Actor.From(principal);
                if (!actor.HasAdminPermissions) return Results.Forbid();
                // Scope the identifier before taking a dispatch-wide lease. A foreign ID must
                // look like an unknown ID even when another agency is actively uploading it.
                var periodKey = await (from dispatch in db.ClearinghouseDispatches.AsNoTracking()
                                       join generation in db.EdiGenerations.AsNoTracking()
                                           on dispatch.EdiGenerationId equals generation.Id
                                       join period in db.BillingPeriods.AsNoTracking()
                                           on generation.BillingPeriodId equals period.Id
                                       join owner in db.Users.AsNoTracking()
                                           on period.UserId equals owner.Id
                                       where dispatch.Id == dispatchId && dispatch.AgencyId == actor.AgencyId &&
                                             generation.AgencyId == actor.AgencyId && owner.AgencyId == actor.AgencyId
                                       select new { period.UserId, period.Year, period.Month })
                    .SingleOrDefaultAsync(cancellationToken);
                if (periodKey is null) return Results.NotFound();
                return await coordination.RunDispatchAsync<IResult>(dispatchId, async heldToken =>
                {
                var token = heldToken;
                try
                {
                    // The Demo reset middleware owns the outer shared lease. This period lock
                    // serializes reconciliation with queue and 837 generation across API hosts.
                    await using var periodWrite = await BillingPeriodWriteScope.BeginAsync(db,
                        actor.AgencyId, periodKey.UserId, periodKey.Year, periodKey.Month, token);
                    if (!await TenantAccess.IsCurrentActorAsync(db, actor, token)) return Results.Unauthorized();
                    var source = await LoadClaimMdReconciliationSourceAsync(db, actor.AgencyId, dispatchId, token);
                    if (source is null) return Results.NotFound();
                    var (dispatch, generation, _) = source.Value;
                    if (!ClaimMdReconciliationRules.CanResolveFrom((int)dispatch.State))
                        return ReconciliationConflict("dispatch_not_uncertain", "Only an uncertain test upload can be reconciled.");
                    if (request.ExpectedRevision != dispatch.Revision)
                        return ReconciliationConflict("revision_conflict", "The dispatch changed. Refresh its evidence manifest.");
                    var manifest = ReadClaimMdReconciliationManifest(source.Value);
                    if (manifest is null)
                        return ReconciliationConflict("source_invalid", "The retained test file needs source review before reconciliation.");
                    var attempts = await db.ClearinghouseDispatchAttempts.AsNoTracking()
                        .Where(row => row.DispatchId == dispatch.Id).Take(2).ToListAsync(token);
                    if (!ConsistentUncertainAttempts(attempts, manifest))
                        return ReconciliationConflict("attempt_conflict", "Retained upload attempts require separate review.");
                    manifest = WithEvidenceFloor(manifest, attempts);
                    var resolvedAtUtc = clock.UtcNow.UtcDateTime;
                    var failure = ClaimMdReconciliationRules.Validate(manifest, request, resolvedAtUtc);
                    if (failure == "source_changed")
                        return ReconciliationConflict(failure, "The selected account or retained file changed. Refresh its evidence manifest.");
                    if (failure is not null)
                        return Results.BadRequest(new ApiErrorDto(failure,
                            "The manual vendor evidence does not establish the selected finding for every retained claim.", string.Empty));
                    var decision = Enum.Parse<ClaimMdReconciliationDecision>(request.Decision);
                    var previousState = dispatch.State;
                    var received = decision == ClaimMdReconciliationDecision.ConfirmedReceived;
                    if (!received && await db.ClearinghouseResponseMatches.AsNoTracking()
                            .AnyAsync(row => row.EdiGenerationId == generation.Id, token))
                        return ReconciliationConflict("response_exists", "A response already matches this file; non-receipt cannot be recorded.");
                    if (!received && await db.BillingSubmissionEvents.AsNoTracking()
                            .AnyAsync(row => row.EdiGenerationId == generation.Id &&
                                             row.Stage == BillingSubmissionStage.Transmitted, token))
                        return ReconciliationConflict("transmission_exists", "Transmission history conflicts with a non-receipt finding.");

                    dispatch.State = received ? ClearinghouseDispatchState.AcceptedByClearinghouse :
                        ClearinghouseDispatchState.ConfirmedNotReceived;
                    dispatch.ExternalFileId = received ? request.ExternalFileId : null;
                    dispatch.AcceptedClaimCount = received ? request.AcceptedClaimCount : null;
                    dispatch.RejectedClaimCount = received ? request.RejectedClaimCount : null;
                    dispatch.SafeErrorCode = received ? null : "manual-confirmed-not-received";
                    dispatch.Revision++;
                    var claimIdentityDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                        string.Join('\n', manifest.Claims.OrderBy(row => row.ClaimReference, StringComparer.Ordinal)
                            .Select(row => $"{row.ClaimReference}|{row.RemoteClaimId}")))));
                    var metadata = JsonSerializer.Serialize(new
                    {
                        dispatchId = dispatch.Id, accountId = dispatch.AccountId,
                        generationId = generation.Id, sourceFileName = generation.FileName,
                        sourceSha256 = manifest.ContentSha256, claimCount = manifest.Claims.Count,
                        claimIdentitySha256 = claimIdentityDigest, previousState = previousState.ToString(),
                        newState = dispatch.State.ToString(), previousRevision = request.ExpectedRevision,
                        evidenceKind = request.EvidenceKind, evidenceReference = request.EvidenceReference,
                        evidenceSha256 = request.EvidenceSha256,
                        evidenceObservedAtUtc = request.EvidenceObservedAtUtc,
                        vendorFileId = received ? request.ExternalFileId : null,
                        acceptedClaimCount = received ? request.AcceptedClaimCount : null,
                        rejectedClaimCount = received ? request.RejectedClaimCount : null,
                        manualAttestation = true, byteIdentityUnverifiedByApi = true
                    });
                    audit.Record(actor, "billing-clearinghouse.dispatch-reconciled", "ClearinghouseDispatch",
                        metadataJson: metadata);
                    db.BillingSubmissionEvents.Add(new ServerBillingSubmissionEvent
                    {
                        AgencyId = actor.AgencyId, BillingPeriodId = generation.BillingPeriodId,
                        EdiGenerationId = generation.Id, OccurredAtUtc = resolvedAtUtc,
                        Stage = received ? BillingSubmissionStage.Transmitted : BillingSubmissionStage.TransportFailed,
                        Reference = received ? request.ExternalFileId : null,
                        ResponseType = "837P", ResponseCode = received
                            ? "manual-receipt-finding" : "manual-nonreceipt-finding",
                        Explanation = received
                            ? "An administrator attested that Claim.MD received this exact test file and claim identities. Original bytes and payer acceptance were not verified by the API."
                            : "An administrator attested, with Claim.MD support evidence, that this test file was not received. A separate new generation is required to send again.",
                        IsSynthetic = true
                    });
                    await db.SaveChangesAsync(token);
                    await periodWrite.CommitAsync(token);
                    return Results.Ok(new ClaimMdReconciliationResultDto(dispatch.Id, dispatch.Revision,
                        dispatch.State.ToString(), dispatch.ExternalFileId, request.EvidenceReference,
                        received
                            ? "Manual Claim.MD receipt finding retained; payer acceptance and original-byte identity remain separate."
                            : "Manual Claim.MD non-receipt finding retained; create a separate generation to send again."));
                }
                catch (BillingPeriodWriteConflictException)
                { return ReconciliationConflict("period_busy", "Another billing change is in progress. Refresh and try again."); }
                catch (DbUpdateConcurrencyException)
                { return ReconciliationConflict("revision_conflict", "The dispatch changed. Refresh its evidence manifest."); }
                }, ReconciliationConflict("dispatch_processing",
                    "The upload is being processed. Refresh the dispatch after it finishes."), cancellationToken);
            });
    }

    private static async Task<(ClearinghouseDispatch Dispatch, ServerEdiGeneration Generation,
        ClearinghouseAccount Account)?> LoadClaimMdReconciliationSourceAsync(
        ApiDbContext db, int agencyId, Guid dispatchId, CancellationToken token)
    {
        var dispatch = await db.ClearinghouseDispatches.SingleOrDefaultAsync(row =>
            row.Id == dispatchId && row.AgencyId == agencyId, token);
        if (dispatch is null) return null;
        var account = await db.ClearinghouseAccounts.AsNoTracking().SingleOrDefaultAsync(row =>
            row.Id == dispatch.AccountId && row.AgencyId == agencyId && row.IsTest &&
            row.ConnectorKind == TradingPartnerKind.ClaimMd, token);
        var generation = await db.EdiGenerations.AsNoTracking().SingleOrDefaultAsync(row =>
            row.Id == dispatch.EdiGenerationId && row.AgencyId == agencyId && row.IsTest, token);
        if (account is null || generation is null ||
            dispatch.TradingPartnerProfileVersion != account.TradingPartnerProfileVersion)
            return null;
        if (!await (from period in db.BillingPeriods.AsNoTracking()
                    join owner in db.Users.AsNoTracking() on period.UserId equals owner.Id
                    where period.Id == generation.BillingPeriodId && owner.AgencyId == agencyId
                    select period.Id).AnyAsync(token))
            return null;
        return (dispatch, generation, account);
    }

    private static ClaimMdReconciliationManifestDto? ReadClaimMdReconciliationManifest(
        (ClearinghouseDispatch Dispatch, ServerEdiGeneration Generation, ClearinghouseAccount Account) source)
    {
        if (!ClearinghouseAccountSelection.Matches(source.Generation.Content, source.Account,
                source.Generation.BillingPeriodId)) return null;
        try
        {
            var content = source.Generation.Content;
            if (content.Any(character => !char.IsAscii(character))) return null;
            var claims = ClaimResponseReader.ReadSubmission(content).Claims;
            if (claims.Count is < 1 or > 2000 || claims.Any(claim => claim.RemoteClaimId is null) ||
                claims.Select(claim => claim.RemoteClaimId).Distinct(StringComparer.Ordinal).Count() != claims.Count)
                return null;
            return new ClaimMdReconciliationManifestDto(source.Dispatch.Id, source.Dispatch.Revision,
                source.Account.Id, source.Account.ExternalAccountNumber,
                source.Generation.Id, source.Dispatch.State.ToString(),
                source.Dispatch.RequestedAtUtc,
                source.Dispatch.RequestedAtUtc,
                source.Generation.FileName,
                Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(content))),
                claims.Select(claim => new ClaimMdClaimIdentity(claim.ClaimReference, claim.RemoteClaimId!)).ToArray(),
                ClaimMdReconciliationRules.ReceivedAttestation,
                ClaimMdReconciliationRules.NotReceivedAttestation);
        }
        catch (FormatException) { return null; }
    }

    private static bool ConsistentUncertainAttempts(
        IReadOnlyList<ClearinghouseDispatchAttempt> attempts, ClaimMdReconciliationManifestDto manifest) =>
        attempts.Count <= 1 && attempts.All(row =>
            row.AttemptNumber == 1 && row.ContentSha256 == manifest.ContentSha256 &&
            row.FileName == manifest.FileName && row.Outcome == ClearinghouseAttemptOutcome.OutcomeUnknown);

    private static ClaimMdReconciliationManifestDto WithEvidenceFloor(
        ClaimMdReconciliationManifestDto manifest, IReadOnlyList<ClearinghouseDispatchAttempt> attempts) =>
        manifest with { EvidenceNotBeforeUtc = attempts.Count == 1 &&
            attempts[0].CompletedAtUtc > manifest.RequestedAtUtc
                ? attempts[0].CompletedAtUtc : manifest.RequestedAtUtc };

    private static IResult ReconciliationConflict(string code, string message) =>
        Results.Conflict(new ApiErrorDto(code, message, string.Empty));
}
