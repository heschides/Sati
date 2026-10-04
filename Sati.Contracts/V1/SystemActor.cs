namespace Sati.Contracts.V1;

/// <summary>Audit identity for scheduled operations, never an authenticated user account.</summary>
public static class SystemActor
{
    public const int UserId = 0;
    public const string DisplayName = "Sati automation";
}
