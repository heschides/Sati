using System.Security.Claims;
using Sati.Contracts.V1;

namespace Sati.Api.Security;

internal readonly record struct Actor(
    int UserId,
    int AgencyId,
    string Role,
    string DisplayName,
    UserPermissions Permissions,
    long SecurityVersion = 1)
{
    internal const string ValidatedPermissionsClaim = "sati_validated_permissions";

    public static Actor From(ClaimsPrincipal principal)
    {
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !int.TryParse(principal.FindFirstValue("agency_id"), out var agencyId) ||
            !int.TryParse(principal.FindFirstValue(ValidatedPermissionsClaim), out var permissionsValue) ||
            !TrySecurityVersion(principal, out var securityVersion))
            throw new UnauthorizedAccessException("The authenticated session has no valid Sati identity.");

        return new Actor(
            userId,
            agencyId,
            principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
            principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            (UserPermissions)permissionsValue,
            securityVersion);
    }

    public static Actor FromUnvalidatedClaims(ClaimsPrincipal principal)
    {
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !int.TryParse(principal.FindFirstValue("agency_id"), out var agencyId) ||
            !TrySecurityVersion(principal, out var securityVersion))
            throw new UnauthorizedAccessException("The authenticated session has no valid Sati identity.");

        return new Actor(
            userId,
            agencyId,
            principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
            principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            UserPermissions.None,
            securityVersion);
    }

    public bool HasCaseManagerPermissions =>
        UserPermissionRules.HasCaseManagerPermissions(Permissions);
    public bool HasSupervisorPermissions =>
        UserPermissionRules.HasSupervisorPermissions(Permissions);
    public bool HasAdminPermissions =>
        UserPermissionRules.HasAdminPermissions(Permissions);
    public bool HasBillingPermissions =>
        UserPermissionRules.HasBillingPermissions(Permissions);
    public bool HasAgencyWideSupervisionPermissions =>
        UserPermissionRules.HasAgencyWideSupervisionPermissions(Permissions);

    internal static bool TrySecurityVersion(ClaimsPrincipal principal, out long version)
    {
        version = 0;
        var claims = principal.FindAll(TokenIssuer.SecurityVersionClaim).Take(2).ToArray();
        return claims.Length == 1 && long.TryParse(claims[0].Value,
            System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out version) && version > 0;
    }

    public AgencyActor ToAgencyActor() => new(UserId, AgencyId, Permissions, SecurityVersion);
}
