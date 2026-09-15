namespace Sati.Contracts.V1;

/// <summary>
/// Shared validation for the actual date on which a worker opened a compliance
/// form. The occurrence date is user-selected; the server recording time is a
/// separate audit fact.
/// </summary>
public static class FormOpeningRules
{
    public static string? Validate(
        DateTime openedOn,
        DateTime availableOn,
        DateTime agencyToday)
    {
        if (openedOn.Date > agencyToday.Date)
            return "The opening date cannot be in the future.";
        if (openedOn.Date < availableOn.Date)
            return $"The form was not available to open before {availableOn:MMM d, yyyy}.";
        return null;
    }
}
