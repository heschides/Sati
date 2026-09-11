using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

/// <summary>
/// Desktop-local mirror of <c>Sati.Api.Security.TenantAccess</c>. The transitional
/// local services must not assume the API is their only caller, so a caller-supplied
/// user or client id is re-scoped here with the same rules the server applies:
/// your own caseload with current case-management permission, assigned case managers
/// if you supervise, and agency-wide caseloads when that capability is granted.
/// </summary>
/// <remarks>
/// Database identity and permission freshness are checked locally on every access.
/// CaseloadTransferRules owns the shared supervisory decision used by both paths.
/// </remarks>
internal static class LocalTenantAccess
{
    public static bool IsReviewer(UserPermissions permissions) =>
        UserPermissionRules.HasSupervisorPermissions(permissions);

    public static Task<bool> IsCurrentActorAsync(
        SatiContext context, User actor, CancellationToken cancellationToken = default) =>
        context.Users.AsNoTracking().AnyAsync(user =>
            user.Id == actor.Id && user.AgencyId == actor.AgencyId &&
            user.Role == actor.Role && user.Permissions == actor.Permissions, cancellationToken);

    public static async Task EnsureCurrentActorAsync(
        SatiContext context, User actor, CancellationToken cancellationToken = default)
    {
        if (!await IsCurrentActorAsync(context, actor, cancellationToken))
            throw new UnauthorizedAccessException("Your account access has changed. Sign in again before continuing.");
    }

    public static Task<bool> OwnsPersonAsync(
        SatiContext context, User actor, int personId, CancellationToken cancellationToken = default) =>
        (from person in context.People.AsNoTracking()
         join owner in context.Users.AsNoTracking() on person.UserId equals owner.Id
         where actor.HasCaseManagerPermissions && person.Id == personId && person.AgencyId == actor.AgencyId &&
               owner.Id == actor.Id && owner.AgencyId == actor.AgencyId &&
               owner.Role == actor.Role && owner.Permissions == actor.Permissions &&
               (owner.Permissions & UserPermissions.CaseManagement) != 0
         select person.Id).AnyAsync(cancellationToken);

    public static async Task<bool> CanAccessUserAsync(
        SatiContext context, User actor, int targetUserId, CancellationToken cancellationToken = default)
    {
        if (!await IsCurrentActorAsync(context, actor, cancellationToken))
            return false;
        if (targetUserId == actor.Id)
            return actor.HasCaseManagerPermissions;
        if (!IsReviewer(actor.Permissions))
            return false;

        var participants = await context.Users.AsNoTracking()
            .Where(user => user.Id == targetUserId)
            .Select(user => new CaseloadParticipant(user.Id, user.AgencyId, user.Permissions, user.SupervisorId))
            .ToListAsync(cancellationToken);
        return participants.Count == 1 &&
            CaseloadTransferRules.CanReachCaseloadOf(actor.ToAgencyActor(), participants[0]);
    }

    public static async Task<bool> CanAccessPersonAsync(
        SatiContext context, User actor, int personId, CancellationToken cancellationToken = default)
    {
        var ownerId = await context.People.AsNoTracking()
            .Where(person => person.Id == personId && person.AgencyId == actor.AgencyId)
            .Select(person => (int?)person.UserId)
            .SingleOrDefaultAsync(cancellationToken);
        return ownerId is int owner && await CanAccessUserAsync(context, actor, owner, cancellationToken);
    }
}
