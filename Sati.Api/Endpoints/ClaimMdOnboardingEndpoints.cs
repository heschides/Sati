using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Models.Billing;

namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    private const string ClaimMdOnboardingAction = "billing-clearinghouse.claimmd-test-account-onboarded";

    private static void MapClaimMdOnboarding(RouteGroupBuilder api)
    {
        api.MapPost("/admin/clearinghouse/claimmd-test-accounts", async Task<IResult> (
            ClaimMdTestAccountOnboardingRequest request, ClaimsPrincipal principal, ApiDbContext db,
            IOptions<SatiApiOptions> options, IHostEnvironment hostEnvironment,
            ApiClock clock, AuditTrail audit, CancellationToken token) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasAdminPermissions) return Results.Forbid();
            // Onboarding precedes transport enablement, so the transport flag is deliberately
            // irrelevant. The exact Demo identity still governs any account metadata write.
            if (options.Value.ExpectedEnvironment != "Demo" ||
                options.Value.ExpectedDatabaseName != "SatiDemo") return Results.NotFound();

            var errors = ClaimMdOnboardingRules.Validate(request);
            if (!EnvironmentClaimMdSandboxKeySource.IsValidReference(request.SecretReference))
                errors["secretReference"] = ["Provide a valid API-host Claim.MD sandbox secret reference, never an AccountKey."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var fingerprint = FingerprintClaimMdOnboarding(request);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            if (db.Database.IsSqlServer())
            {
                if (!await ClaimMdOnboardingCoordination.TryAcquireAsync(db, token))
                    return ClaimMdOnboardingBusy();
            }
            else if (!hostEnvironment.IsEnvironment("Testing"))
                throw new InvalidOperationException("Claim.MD onboarding requires SQL Server.");
            if (!await TenantAccess.IsCurrentActorAsync(db, actor, token)) return Results.Unauthorized();

            var existing = await db.ClearinghouseAccounts.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == request.AccountId, token);
            if (existing is not null)
            {
                if (existing.AgencyId != actor.AgencyId || existing.ConnectorKind != TradingPartnerKind.ClaimMd ||
                    !existing.IsTest || !existing.IsEnabled || existing.Revision != request.ExpectedRevision ||
                    existing.ExternalAccountNumber != request.ExternalAccountNumber ||
                    existing.ClaimNamespace != request.ClaimNamespace ||
                    existing.SecretReference != request.SecretReference ||
                    existing.TradingPartnerProfileVersion != TradingPartnerProfile.CurrentVersion)
                    return ClaimMdOnboardingConflict();
                var checkpoints = await db.ClearinghouseFeedCheckpoints.AsNoTracking()
                    .Where(row => row.AccountId == existing.Id && row.AgencyId == actor.AgencyId)
                    .ToListAsync(token);
                if (checkpoints.Count != 2 || checkpoints.Any(row => row.Revision != 0 || row.LastReceiptId is not null) ||
                    checkpoints.SingleOrDefault(row => row.FeedKind == ClearinghouseFeedKind.Status)?.Cursor !=
                    request.StatusFeed.Cursor ||
                    checkpoints.SingleOrDefault(row => row.FeedKind == ClearinghouseFeedKind.Era)?.Cursor !=
                    request.EraFeed.Cursor)
                    return ClaimMdOnboardingConflict();
                var auditRows = await db.AuditEvents.AsNoTracking().Where(row =>
                        row.AgencyId == actor.AgencyId && row.Action == ClaimMdOnboardingAction)
                    .Select(row => row.MetadataJson).ToListAsync(token);
                if (!auditRows.Any(row => IsExactClaimMdOnboardingAudit(row, request.AccountId, fingerprint)))
                    return ClaimMdOnboardingConflict();
                return Results.Ok(ToClaimMdOnboardingDto(existing, request, alreadyProvisioned: true));
            }

            // This is a global identifier collision check, not a cross-tenant account lookup.
            // No other agency's metadata is returned, even when its number or secret alias
            // collides. Serializable isolation closes the concurrent check/insert window.
            var reused = await db.ClearinghouseAccounts.AsNoTracking().AnyAsync(row =>
                (row.SecretReference != null && row.SecretReference.ToUpper() == request.SecretReference) ||
                row.ConnectorKind == TradingPartnerKind.ClaimMd &&
                (row.ClaimNamespace == request.ClaimNamespace ||
                 row.ExternalAccountNumber.ToUpper() == request.ExternalAccountNumber), token);
            if (reused) return ClaimMdOnboardingConflict();

            var now = clock.UtcNow.UtcDateTime;
            var account = new ClearinghouseAccount
            {
                Id = request.AccountId, AgencyId = actor.AgencyId, ConnectorKind = TradingPartnerKind.ClaimMd,
                IsTest = true, IsEnabled = true, ExternalAccountNumber = request.ExternalAccountNumber,
                ClaimNamespace = request.ClaimNamespace, SecretReference = request.SecretReference,
                TradingPartnerProfileVersion = TradingPartnerProfile.CurrentVersion,
                Revision = 0, CreatedAtUtc = now, UpdatedAtUtc = now
            };
            db.ClearinghouseAccounts.Add(account);
            db.ClearinghouseFeedCheckpoints.AddRange(
                new ClearinghouseFeedCheckpoint
                {
                    Id = Guid.NewGuid(), AgencyId = actor.AgencyId, AccountId = account.Id,
                    FeedKind = ClearinghouseFeedKind.Status, Cursor = request.StatusFeed.Cursor,
                    Revision = 0, UpdatedAtUtc = now
                },
                new ClearinghouseFeedCheckpoint
                {
                    Id = Guid.NewGuid(), AgencyId = actor.AgencyId, AccountId = account.Id,
                    FeedKind = ClearinghouseFeedKind.Era, Cursor = request.EraFeed.Cursor,
                    Revision = 0, UpdatedAtUtc = now
                });
            audit.Record(actor, ClaimMdOnboardingAction, "ClearinghouseAccount", metadataJson:
                JsonSerializer.Serialize(new ClaimMdOnboardingAudit(
                    request.AccountId, fingerprint, request.TestAccountEvidenceReference,
                    request.DuplicateFieldEvidenceReference, request.StatusFeed.ReviewKind,
                    request.StatusFeed.EvidenceReference, request.EraFeed.ReviewKind,
                    request.EraFeed.EvidenceReference)));
            try
            {
                await db.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
            }
            catch (DbUpdateException) { return ClaimMdOnboardingConflict(); }
            return Results.Json(ToClaimMdOnboardingDto(account, request, alreadyProvisioned: false),
                statusCode: StatusCodes.Status201Created);
        });
    }

    private static ClaimMdTestAccountOnboardingDto ToClaimMdOnboardingDto(ClearinghouseAccount account,
        ClaimMdTestAccountOnboardingRequest request, bool alreadyProvisioned) =>
        new(account.Id, account.ExternalAccountNumber, account.ClaimNamespace!,
            request.StatusFeed.Cursor, request.EraFeed.Cursor, account.Revision, alreadyProvisioned);

    private static IResult ClaimMdOnboardingConflict() => Results.Conflict(new ApiErrorDto(
        "claimmd_onboarding_conflict",
        "The test account ID or routing metadata is already reserved or has changed. Review onboarding evidence before retrying.",
        string.Empty));

    private static IResult ClaimMdOnboardingBusy() => Results.Conflict(new ApiErrorDto(
        "claimmd_onboarding_busy",
        "Another test-account setup is in progress. Retry after it finishes.", string.Empty));

    private static string FingerprintClaimMdOnboarding(ClaimMdTestAccountOnboardingRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));

    private static bool IsExactClaimMdOnboardingAudit(string metadata, Guid accountId, string fingerprint)
    {
        try
        {
            var evidence = JsonSerializer.Deserialize<ClaimMdOnboardingAudit>(metadata);
            return evidence?.AccountId == accountId && evidence.RequestFingerprint == fingerprint;
        }
        catch (JsonException) { return false; }
    }

    private sealed record ClaimMdOnboardingAudit(
        Guid AccountId, string RequestFingerprint, string TestAccountEvidenceReference,
        string DuplicateFieldEvidenceReference, ClaimMdFeedReviewKind StatusReviewKind,
        string StatusReviewReference, ClaimMdFeedReviewKind EraReviewKind, string EraReviewReference);
}
