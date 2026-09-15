namespace Sati.Contracts.V1;

/// <summary>Shared account-lifecycle policy; callers must also validate the actor
/// against current persisted identity and version inside the protected operation.</summary>
public static class AccountSessionRules
{
    public const string RequiresAdministration =
        "Account access and session revocation require administration permission.";
    public const string CannotDisableSelf =
        "You cannot disable your own account. Ask another administrator.";
    public const string SessionExpired =
        "Your sign-in is no longer valid. Sign in again to continue.";

    public static bool IsCurrentSession(bool isEnabled, long currentVersion, long sessionVersion) =>
        isEnabled && currentVersion > 0 && sessionVersion > 0 && currentVersion == sessionVersion;

    public static long NextSecurityVersion(long currentVersion)
    {
        if (currentVersion <= 0 || currentVersion == long.MaxValue)
            throw new InvalidOperationException("The account security version cannot be advanced safely.");
        return checked(currentVersion + 1);
    }

    /// <param name="requestedEnabled">Null means revoke sessions without changing enabled state.</param>
    public static UserManagementRules.Refusal? DescribeManagementRefusal(
        AgencyActor actor, int targetUserId, int targetAgencyId, string targetRole,
        bool? requestedEnabled)
    {
        if (!UserPermissionRules.HasAdminPermissions(actor.Permissions))
            return new("permissions", RequiresAdministration);
        if (targetAgencyId != actor.AgencyId)
            return new("agencyId", UserManagementRules.ForeignAgency);
        if (string.Equals(targetRole, "PlatformOperator", StringComparison.Ordinal))
            return new("user", UserManagementRules.PlatformOperatorNotManageable);
        if (requestedEnabled == false && targetUserId == actor.UserId)
            return new("isEnabled", CannotDisableSelf);
        return null;
    }
}
