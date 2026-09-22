using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Security;
using Sati.Contracts.V1;

namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    private static void MapFormAttestationChangeReviewFlags(RouteGroupBuilder api)
    {
        api.MapGet("/form-attestation-change-review-flags", async Task<IResult> (
            string audience,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var flags = db.FormAttestationChangeReviewFlags.AsNoTracking()
                .Where(flag => flag.AgencyId == actor.AgencyId);
            switch (audience?.Trim().ToLowerInvariant())
            {
                case "supervisor":
                    if (!actor.HasSupervisorPermissions)
                        return Results.Forbid();
                    var canReviewAgency = actor.HasAgencyWideSupervisionPermissions;
                    flags = flags.Where(flag =>
                        flag.RequiresSupervisorAttention &&
                        db.People.Any(person =>
                            person.Id == flag.PersonId &&
                            person.AgencyId == actor.AgencyId &&
                            db.Users.Any(owner =>
                                owner.Id == person.UserId &&
                                owner.AgencyId == actor.AgencyId &&
                                (owner.Permissions & UserPermissions.CaseManagement) != 0 &&
                                (canReviewAgency || owner.SupervisorId == actor.UserId))));
                    break;

                case "billing":
                    if (!actor.HasBillingPermissions)
                        return Results.Forbid();
                    flags = flags.Where(flag => flag.RequiresBillingAttention);
                    break;

                default:
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["audience"] = ["Choose supervisor or billing."]
                    });
            }

            var rows = await flags.OrderByDescending(flag => flag.CreatedAtUtc)
                .ThenByDescending(flag => flag.Id)
                .ToListAsync(cancellationToken);
            return Results.Ok(rows.Select(flag => flag.ToContract()).ToArray());
        });
    }
}
