namespace Sati.Contracts.V1;

/// <summary>
/// User-entered answers for Maine DHHS OADS's Housing Support Funds application.
/// Consumer identity, waiver, Shared Living status, assigned case manager, and
/// provider identity are deliberately absent: the service derives those values
/// from the selected consumer and signed-in agency.
/// </summary>
public sealed record HousingSupportFundsRequest(
    string? ConsumerTelephone = null,
    string? ConsumerEmail = null,
    string? ConsumerAddress = null,
    string? HousingType = null,
    string? LandlordName = null,
    string? LandlordAddress = null,
    string? LandlordTelephone = null,
    string? LandlordEmail = null,
    decimal? MonthlyHousingAmount = null,
    decimal? AmountRequested = null,
    bool? ReceivesSubsidy = null,
    string? SubsidyType = null,
    string? GuardianAddress = null,
    string? GuardianTelephone = null,
    string? GuardianEmail = null,
    string? RepresentativePayeeName = null,
    string? RepresentativePayeeAddress = null,
    string? AdditionalDetails = null,
    bool SupportingDocumentReady = false);

public sealed record HousingSupportFundsSubject(
    int PersonId,
    string ConsumerName,
    string Waiver,
    bool IsSharedLiving,
    bool HasGuardian,
    string? GuardianName,
    string CaseManagerName,
    string ProviderName,
    string? ProviderAddress,
    string? ProviderTelephone,
    string? ProviderEmail);

public sealed record HousingSupportFundsResult(
    byte[] Pdf,
    string FileName,
    IReadOnlyList<string> ReviewItems,
    string SourceRevision);

public static class HousingSupportFundsRules
{
    public const string SourceRevision = "Maine DHHS OADS - 06/30/2025";
    public const decimal MaximumRequestAmount = 3_000m;
    public const int ShortTextMaxLength = 500;
    // The state's fixed narrative box fits about 2,000 characters at its own
    // configured font size. Reject overflow instead of silently clipping it.
    public const int NarrativeMaxLength = 2_000;

    public static IReadOnlyList<string> HousingTypes { get; } =
        ["Rental", "Consumer-owned home"];

    public static IReadOnlyDictionary<string, string[]> Validate(HousingSupportFundsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var values))
                errors[field] = values = [];
            values.Add(message);
        }

        foreach (var (field, value) in TextFields(request))
        {
            var maximum = field == nameof(request.AdditionalDetails)
                ? NarrativeMaxLength
                : ShortTextMaxLength;
            if (value?.Trim().Length > maximum)
                Add(field, $"{Label(field)} cannot exceed {maximum:N0} characters.");
        }

        if (!string.IsNullOrWhiteSpace(request.HousingType) &&
            !HousingTypes.Contains(request.HousingType, StringComparer.Ordinal))
            Add(nameof(request.HousingType), "Housing type contains an unknown choice.");
        if (request.MonthlyHousingAmount is < 0)
            Add(nameof(request.MonthlyHousingAmount), "Monthly rent or mortgage cannot be negative.");
        if (request.AmountRequested is <= 0)
            Add(nameof(request.AmountRequested), "Amount requested must be greater than zero.");
        if (request.AmountRequested > MaximumRequestAmount)
            Add(nameof(request.AmountRequested), "The application states that requests over $3,000 will not be granted.");
        if (request.ReceivesSubsidy == true && string.IsNullOrWhiteSpace(request.SubsidyType))
            Add(nameof(request.SubsidyType), "Enter the subsidy type when the consumer receives a subsidy.");

        return errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> FindReviewItems(
        HousingSupportFundsSubject subject,
        HousingSupportFundsRequest request)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(request);
        var items = new List<string>();

        void Need(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) items.Add(label);
        }
        void NeedAnswer(string label, bool? value)
        {
            if (value is null) items.Add(label);
        }

        Need("Section 21 or Section 29 waiver", NormalizeWaiver(subject.Waiver));
        Need("Consumer telephone", request.ConsumerTelephone);
        Need("Consumer email", request.ConsumerEmail);
        Need("Consumer address", request.ConsumerAddress);
        Need("Rental or consumer-owned home", request.HousingType);
        if (request.HousingType == "Rental")
        {
            Need("Landlord name", request.LandlordName);
            Need("Landlord address", request.LandlordAddress);
            Need("Landlord telephone", request.LandlordTelephone);
            Need("Landlord email", request.LandlordEmail);
        }
        if (request.MonthlyHousingAmount is null) items.Add("Monthly rent or mortgage amount");
        if (request.AmountRequested is null) items.Add("Amount requested");
        NeedAnswer("Housing subsidy answer", request.ReceivesSubsidy);
        if (subject.HasGuardian)
        {
            Need("Guardian name", subject.GuardianName);
            Need("Guardian address", request.GuardianAddress);
            Need("Guardian telephone", request.GuardianTelephone);
            Need("Guardian email", request.GuardianEmail);
        }
        Need("Case manager or community resource coordinator", subject.CaseManagerName);
        Need("Provider name", subject.ProviderName);
        Need("Provider address", subject.ProviderAddress);
        Need("Provider telephone", subject.ProviderTelephone);
        Need("Provider email", subject.ProviderEmail);
        Need("Additional request details", request.AdditionalDetails);
        if (!request.SupportingDocumentReady)
            items.Add("Proof of room and board agreement, lease, or mortgage");
        if (subject.IsSharedLiving)
            items.Add("Eligibility conflict: profile records a Shared Living situation");
        if (request.ReceivesSubsidy == true)
            items.Add("Eligibility conflict: consumer currently receives a housing subsidy");
        items.Add("Consumer or guardian signature and date");

        return items.Distinct(StringComparer.Ordinal).ToList();
    }

    public static string? NormalizeWaiver(string? waiver) => waiver switch
    {
        "Section21" or "Section 21" => "Section 21",
        "Section29" or "Section 29" => "Section 29",
        _ => null
    };

    private static IEnumerable<KeyValuePair<string, string?>> TextFields(HousingSupportFundsRequest request)
    {
        yield return new(nameof(request.ConsumerTelephone), request.ConsumerTelephone);
        yield return new(nameof(request.ConsumerEmail), request.ConsumerEmail);
        yield return new(nameof(request.ConsumerAddress), request.ConsumerAddress);
        yield return new(nameof(request.HousingType), request.HousingType);
        yield return new(nameof(request.LandlordName), request.LandlordName);
        yield return new(nameof(request.LandlordAddress), request.LandlordAddress);
        yield return new(nameof(request.LandlordTelephone), request.LandlordTelephone);
        yield return new(nameof(request.LandlordEmail), request.LandlordEmail);
        yield return new(nameof(request.SubsidyType), request.SubsidyType);
        yield return new(nameof(request.GuardianAddress), request.GuardianAddress);
        yield return new(nameof(request.GuardianTelephone), request.GuardianTelephone);
        yield return new(nameof(request.GuardianEmail), request.GuardianEmail);
        yield return new(nameof(request.RepresentativePayeeName), request.RepresentativePayeeName);
        yield return new(nameof(request.RepresentativePayeeAddress), request.RepresentativePayeeAddress);
        yield return new(nameof(request.AdditionalDetails), request.AdditionalDetails);
    }

    private static string Label(string field) => string.Concat(field.Select((character, index) =>
        index > 0 && char.IsUpper(character) ? $" {char.ToLowerInvariant(character)}" : character.ToString()));
}
