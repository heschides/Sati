namespace Sati.Contracts.V1;

/// <summary>
/// Editable answers only. Revision zero is the API's empty-slot response.
/// A wizard draft is not a submitted form or attestation.
/// </summary>
public sealed record FormWizardProgressDto(
    string FormKey, int Revision, int StepIndex, string AnswersJson, DateTime UpdatedAtUtc);

public sealed record SaveFormWizardProgressRequest(
    int ExpectedRevision, int StepIndex, string AnswersJson);

public static class FormWizardProgressRules
{
    public const int MaximumAnswersBytes = 131_072;
    public const int MaximumStepIndex = 50;
    public static IReadOnlySet<string> FormKeys { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "benefits-application", "cwic-packet", "housing-support-funds",
        "safety-device", "dhhs-authorized-representative", "dhhs-authorization-to-release",
        "agency-release", "medical-release",
        "agency-release-one-off", "medical-release-one-off"
    };

    public static bool ValidKey(string formKey)
    {
        if (FormKeys.Contains(formKey)) return true;
        var separator = formKey.IndexOf('.');
        if (separator < 0) return false;
        var prefix = formKey[..separator];
        if (prefix == "dhhs-authorization-to-release")
            return DateTime.TryParseExact(formKey[(separator + 1)..], "yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _);
        return prefix is "agency-release" or "medical-release" &&
               Guid.TryParseExact(formKey[(separator + 1)..], "D", out _);
    }

    public static bool Valid(string formKey, SaveFormWizardProgressRequest request) =>
        ValidKey(formKey) && request.ExpectedRevision >= 0 &&
        request.StepIndex is >= 0 and <= MaximumStepIndex &&
        !string.IsNullOrWhiteSpace(request.AnswersJson) &&
        System.Text.Encoding.UTF8.GetByteCount(request.AnswersJson) <= MaximumAnswersBytes &&
        IsJsonObject(request.AnswersJson);

    private static bool IsJsonObject(string json)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object;
        }
        catch (System.Text.Json.JsonException) { return false; }
    }
}
