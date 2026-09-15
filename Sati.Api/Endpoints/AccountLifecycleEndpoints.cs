using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;

namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    private static void MapAccountLifecycle(RouteGroupBuilder api)
    {
        api.MapPut("/users/{userId:int}/enabled", async (int userId, SetUserEnabledRequest request,
            ClaimsPrincipal principal, ApiDbContext db, AuditTrail audit, CancellationToken ct) =>
            await ChangeAccountAccess(userId, request.IsEnabled, principal, db, audit, ct));
        api.MapDelete("/users/{userId:int}/sessions", async (int userId,
            ClaimsPrincipal principal, ApiDbContext db, AuditTrail audit, CancellationToken ct) =>
            await ChangeAccountAccess(userId, null, principal, db, audit, ct));
    }

    private static async Task<IResult> ChangeAccountAccess(int userId, bool? enabled,
        ClaimsPrincipal principal, ApiDbContext db, AuditTrail audit, CancellationToken ct)
    {
        var actor = Actor.From(principal);
        if (!actor.HasAdminPermissions) return Results.Forbid();
        // Revalidate the administrator inside the transaction. Two administrators
        // concurrently disabling each other cannot both act on an already-ended session.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (!await TenantAccess.IsCurrentActorAsync(db, actor, ct)) return Results.Unauthorized();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId &&
            x.AgencyId == actor.AgencyId && x.Role != "PlatformOperator", ct);
        if (user is null) return Results.NotFound();
        var refusal = AccountSessionRules.DescribeManagementRefusal(
            actor.ToAgencyActor(), user.Id, user.AgencyId, user.Role, enabled);
        if (refusal is not null)
            return Results.ValidationProblem(new Dictionary<string, string[]> { [refusal.Field] = [refusal.Message] });
        if (enabled.HasValue && user.IsEnabled == enabled.Value)
            return Results.Ok(ContractMapper.ToProfile(user));
        if (!TryAdvanceSecurityVersion(user)) return AccountStateConflict();
        if (enabled.HasValue) user.IsEnabled = enabled.Value;
        audit.Record(actor, enabled switch
        {
            true => "user.enabled",
            false => "user.disabled",
            _ => "user.sessions-revoked"
        }, "User", userId);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException) { return AccountStateConflict(); }
        return enabled.HasValue ? Results.Ok(ContractMapper.ToProfile(user)) : Results.NoContent();
    }

    private static bool TryAdvanceSecurityVersion(ServerUser user)
    {
        try { user.SecurityVersion = AccountSessionRules.NextSecurityVersion(user.SecurityVersion); return true; }
        catch (InvalidOperationException) { return false; }
    }

    private static IResult AccountStateConflict() => Results.Conflict(new ApiErrorDto(
        "account_state_changed", "The account changed. Refresh and try again.", string.Empty));
}
