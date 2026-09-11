using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;

namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    private static void MapClaimResponseIntake(RouteGroupBuilder api)
    {
        api.MapPost("/billing/responses", async Task<IResult> (ClaimResponseIngestRequest request,
            ClaimsPrincipal principal, ClaimResponseIngestion ingestion, HttpResponse response, CancellationToken token) =>
        {
            response.Headers.CacheControl = "no-store";
            var actor = Actor.From(principal);
            if (!actor.HasBillingPermissions) return Results.Forbid();
            try { return Results.Ok(await ingestion.ImportAsync(request.Document, actor, null, token)); }
            catch (ClaimResponseRejected rejected) { return ClaimResponseFailure(rejected); }
            catch (InvalidOperationException)
            { return ResponseStorageUnavailable(); }
        });

        // Compatibility only: the requested period is an assertion, never correlation authority.
        api.MapPost("/billing/periods/{periodId:int}/responses", async Task<IResult> (int periodId,
            ClaimResponseIngestRequest request, ClaimsPrincipal principal, ApiDbContext db,
            ClaimResponseIngestion ingestion, HttpResponse response, CancellationToken token) =>
        {
            response.Headers.CacheControl = "no-store";
            var actor = Actor.From(principal);
            if (!actor.HasBillingPermissions) return Results.Forbid();
            if (!await (from period in db.BillingPeriods.AsNoTracking()
                        join owner in db.Users.AsNoTracking() on period.UserId equals owner.Id
                        where period.Id == periodId && owner.AgencyId == actor.AgencyId select period.Id).AnyAsync(token))
                return Results.NotFound();
            try { return Results.Ok(await ingestion.ImportAsync(request.Document, actor, periodId, token)); }
            catch (ClaimResponseRejected rejected) { return ClaimResponseFailure(rejected); }
            catch (InvalidOperationException)
            { return ResponseStorageUnavailable(); }
        });
    }

    private static IResult ClaimResponseFailure(ClaimResponseRejected rejected) =>
        Results.Json(new ApiErrorDto(rejected.Code, rejected.Message, string.Empty), statusCode: rejected.Code switch
        {
            "forbidden" => 403,
            "response_intake_unavailable" => 404,
            "response_identity_conflict" or "response_write_conflict" => 409,
            _ => 400
        });

    private static IResult ResponseStorageUnavailable() => Results.Json(new ApiErrorDto("response_storage_unavailable",
        "The import could not be confirmed. Retry the exact same file; a completed import will return its existing receipt without adding records.", string.Empty), statusCode: 503);
}
