namespace Sati.Contracts.V1;

public static class ReleaseNoteLinkRules
{
    public static string FormTypeName(ReleaseObligationCategory category) => category switch
    {
        ReleaseObligationCategory.Agency => "Release_Agency",
        ReleaseObligationCategory.Medical => "Release_Medical",
        ReleaseObligationCategory.Dhhs => "Release_DHHS",
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };
}
