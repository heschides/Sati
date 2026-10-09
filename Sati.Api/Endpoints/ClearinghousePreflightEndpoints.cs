using System.Security.Claims;
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
    private static void MapClearinghousePreflight(RouteGroupBuilder api)
    {
        api.MapPost("/admin/clearinghouse/accounts/{accountId:guid}/preflight/reopen",
            async Task<IResult> (Guid accountId, ReopenClearinghousePreflightRequest request,
                ClaimsPrincipal principal, ApiDbContext db, ClearinghouseDispatchGate gate,
                IDemoWorkerResetCoordination coordination, IClaimMdSandboxKeySource keys,
                EnvelopeProtector protector, ApiClock clock, HttpContext http, CancellationToken cancellationToken) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                var actor = Actor.From(principal);
                if (!actor.HasAdminPermissions) return Results.Forbid();
                if (!gate.IsEnabled) return Results.NotFound();
                // Scope before key resolution or lease acquisition, including foreign IDs.
                var staged = await db.ClearinghouseAccounts.AsNoTracking().SingleOrDefaultAsync(row =>
                    row.AgencyId == actor.AgencyId && row.Id == accountId, cancellationToken);
                if (staged is null || !gate.CanUseAccount(staged)) return Results.NotFound();
                if (staged.ConnectorKind != TradingPartnerKind.ClaimMd ||
                    !EnvironmentClaimMdSandboxKeySource.IsValidReference(staged.SecretReference))
                    return ReconciliationConflict("account_invalid", "The test account needs administrator review.");
                var snapshot = await db.ClearinghouseDispatchReadiness.AsNoTracking().SingleOrDefaultAsync(row =>
                    row.AgencyId == actor.AgencyId && row.AccountId == accountId, cancellationToken);
                if (!CanReopenPreflight(staged, snapshot, request)) return PreflightRevisionConflict();

                return await coordination.RunAccountPreflightAsync<IResult>(actor.AgencyId, accountId,
                    async (lease, token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        // Receipt protection is shared infrastructure, not an account failure.
                        try { _ = keys.Resolve(staged.SecretReference); }
                        catch (ClaimMdAccountKeyUnavailableException)
                        {
                            token.ThrowIfCancellationRequested();
                            return Results.Json(new ApiErrorDto("account_key_unavailable",
                                "Restore the account key before reopening queued work.", string.Empty), statusCode: 503);
                        }
                        _ = await protector.ProtectAsync("claimmd-readiness-preflight",
                            new FieldBinding(actor.AgencyId, 0, $"ClaimMdReadiness:{accountId:N}:Preflight"), token);
                        await using var write = await ClaimReleaseWriteScope.BeginAsync(db, actor.AgencyId, token);
                        await lease.VerifyAsync(token);
                        if (!await TenantAccess.IsCurrentActorAsync(db, actor, token)) return Results.Unauthorized();
                        var account = await db.ClearinghouseAccounts.AsNoTracking().SingleOrDefaultAsync(row =>
                            row.AgencyId == actor.AgencyId && row.Id == accountId, token);
                        var readiness = await db.ClearinghouseDispatchReadiness.SingleOrDefaultAsync(row =>
                            row.AgencyId == actor.AgencyId && row.AccountId == accountId, token);
                        if (!gate.IsEnabled || account is null || !gate.CanUseAccount(account) ||
                            !ClearinghouseAccountBinding.Unchanged(staged, account) ||
                            !CanReopenPreflight(account, readiness, request)) return PreflightRevisionConflict();
                        readiness!.Apply(ClearinghousePreflightState.Ready, account.Revision);
                        db.AuditEvents.Add(new ServerAuditEvent
                        {
                            AgencyId = actor.AgencyId, ActorUserId = actor.UserId,
                            Action = "billing-clearinghouse.preflight-reopened", ResourceType = "ClearinghouseAccount",
                            ResourceId = accountId.ToString("N"), OccurredAtUtc = clock.UtcNow.UtcDateTime,
                            CorrelationId = http.TraceIdentifier,
                            MetadataJson = JsonSerializer.Serialize(new { accountId, readiness.Revision,
                                expectedReadinessRevision = request.ExpectedReadinessRevision, accountRevision = account.Revision })
                        });
                        await lease.VerifyAsync(token);
                        await db.SaveChangesAsync(token);
                        await lease.VerifyAsync(token);
                        await write.CommitAsync(token);
                        return Results.Ok(ToReadinessDto(account, readiness));
                    }, ReconciliationConflict("account_busy", "Account preparation is in progress. Refresh before retrying."),
                    cancellationToken);
            });
    }

    private static bool CanReopenPreflight(ClearinghouseAccount account, ClearinghouseDispatchReadiness? readiness,
        ReopenClearinghousePreflightRequest request)
    {
        if (readiness is null) return false;
        return account.Revision == request.ExpectedAccountRevision && readiness.Revision == request.ExpectedReadinessRevision &&
            ClearinghousePreflightRules.CanReopen(readiness.Snapshot());
    }

    private static IResult PreflightRevisionConflict() => ReconciliationConflict("revision_conflict",
        "Account readiness changed. Refresh and review before reopening.");

    private static ClearinghouseAccountReadinessDto ToReadinessDto(ClearinghouseAccount account,
        ClearinghouseDispatchReadiness? readiness) => new(account.Id, account.Revision, readiness?.Revision ?? 0,
            (readiness?.Disposition ?? ClearinghousePreflightDisposition.Ready).ToString(), readiness?.FailureCount ?? 0,
            readiness?.NextEligibleAtUtc is { } due ? DateTime.SpecifyKind(due, DateTimeKind.Utc) : null,
            readiness?.SafeFailureCode);
}
