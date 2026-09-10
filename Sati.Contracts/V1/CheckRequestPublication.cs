namespace Sati.Contracts.V1;

/// <summary>
/// The single validation owner for check-request drafts and publication.
/// The desktop and API both call these rules so the document cannot be accepted
/// under different standards depending on which client saved it.
/// </summary>
public static class CheckRequestPublication
{
    public const int PayableToMaxLength = 200;
    public const int MailingAddressMaxLength = 500;
    public const int ReasonMaxLength = 1_000;
    public const int SnapshotNameMaxLength = 200;
    public const decimal MaximumAmount = 9_999_999_999_999_999.99m;

    public static IReadOnlyList<string> FindDraftErrors(
        DateTime? requestDate,
        string? payableTo,
        string? mailingAddress,
        decimal amount,
        DateTime? neededByDate,
        string? reason)
    {
        var errors = new List<string>();
        if (requestDate is null)
            errors.Add("Enter the request date.");
        if (payableTo?.Trim().Length > PayableToMaxLength)
            errors.Add($"Check payable to must be {PayableToMaxLength} characters or fewer.");
        if (mailingAddress?.Trim().Length > MailingAddressMaxLength)
            errors.Add($"The address must be {MailingAddressMaxLength} characters or fewer.");
        if (amount < 0)
            errors.Add("The amount cannot be negative.");
        if (amount > MaximumAmount)
            errors.Add("The amount is too large to store.");
        if (reason?.Trim().Length > ReasonMaxLength)
            errors.Add($"The reason must be {ReasonMaxLength} characters or fewer.");
        return errors;
    }

    public static IReadOnlyList<string> FindPublicationBlockers(
        DateTime? requestDate,
        string? payableTo,
        string? mailingAddress,
        decimal amount,
        DateTime? neededByDate,
        string? reason,
        bool alreadyPublished)
    {
        var blockers = FindDraftErrors(requestDate, payableTo, mailingAddress, amount, neededByDate, reason).ToList();
        if (alreadyPublished)
            blockers.Add("A PDF has already been prepared from this check request.");
        if (string.IsNullOrWhiteSpace(payableTo))
            blockers.Add("Enter who the check is payable to.");
        if (string.IsNullOrWhiteSpace(mailingAddress))
            blockers.Add("Enter the mailing address.");
        if (amount <= 0)
            blockers.Add("Enter an amount greater than zero.");
        if (neededByDate is null)
            blockers.Add("Enter the date the check is needed.");
        if (string.IsNullOrWhiteSpace(reason))
            blockers.Add("Enter the reason for the request.");
        return blockers.Distinct(StringComparer.Ordinal).ToList();
    }
}
