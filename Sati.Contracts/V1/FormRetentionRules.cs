namespace Sati.Contracts.V1;

/// <summary>
/// Ordinary form-deletion requests must retain every persisted form, including an
/// incomplete, future, or currently optional form. Its stored dates are evidence for
/// compliance and historical billing, not disposable rows to regenerate later.
/// Separately authorized consumer-deletion and audited duplicate-repair workflows
/// keep their own narrower rules; this is not a general database deletion policy.
/// </summary>
public static class FormRetentionRules
{
    public const int MaximumRequestIds = 100;

    public const string RequestLimitMessage =
        "No more than 100 form IDs may be requested at once.";

    public const string ErrorCode = "form_retention_required";

    public const string Message =
        "Persisted forms cannot be deleted because they preserve compliance and billing history. " +
        "Use attestation or revocation to record completion changes.";
}
