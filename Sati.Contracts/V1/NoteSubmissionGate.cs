namespace Sati.Contracts.V1;

public sealed record NoteSubmissionResult(
    bool Passed,
    IReadOnlyList<string> Reasons,
    bool HistoricalWindowBlocked,
    bool ConfigurationInvalid = false)
{
    public string Message => Passed ? string.Empty :
        ConfigurationInvalid
            ? "This note cannot be submitted for approval. " + string.Join(" ", Reasons) +
              " Your draft has not been submitted. Save it as Pending, Held for compliance, or " +
              "Compliance blocked to retain the service documentation."
            : "This service date is inside a compliance gap. " + string.Join(" ", Reasons) +
              " Keep the service documentation by saving it as Pending, Held for compliance, or " +
              "Compliance blocked, or send it to your supervisor with a written justification. " +
              "A justification is clinical context, not an approved exception: billing stays blocked " +
              "until a supervisor records an exception naming these exact obligations, or an " +
              "administrator records a recovery. Later completion does not make service inside a " +
              "past compliance gap billable.";
}

/// <summary>
/// The shared gate for entering supervisory review. This is not an approval or
/// billing override. A note's form label or case-manager justification never
/// proves that a form was completed: the stored completion evidence does.
///
/// Billability follows the note's SERVICE date. A document that became overdue
/// after the service was delivered cannot reach backward and block it, so this
/// gate never evaluates today's compliance state — see the 2026-09-14 decision.
/// </summary>
public static class NoteSubmissionGate
{
    public const string RefusalCode = "note_compliance_blocked";

    public static NoteSubmissionResult Evaluate(
        int? targetStatus,
        DateTime? effectiveDate,
        IEnumerable<ComplianceFormSnapshot> forms,
        DateTime? serviceDate,
        DateTime today,
        BillingComplianceRequirements requirements = BillingComplianceGate.DefaultRequirements)
    {
        if (targetStatus != NoteWorkflow.Logged) return new(true, [], false);
        if (!BillingComplianceGate.IsSupported(requirements))
            return new(false, [BillingComplianceGate.UnsupportedConfigurationReason], false,
                ConfigurationInvalid: true);

        var historical = serviceDate is DateTime date
            ? BillingComplianceGate.EvaluateBillingWindow(forms.ToList(), date, requirements)
            : [];
        var reasons = historical.Distinct(StringComparer.Ordinal).ToList();
        return new(reasons.Count == 0, reasons, reasons.Count > 0);
    }

    /// <summary>
    /// Clinical review may proceed for a blocked service date when the case
    /// manager records a written justification. An unsupported agency
    /// configuration is never waivable: the reasons cannot be trusted at all.
    /// </summary>
    public static bool IsSubmissionAllowed(NoteSubmissionResult result, string? justification) =>
        result.Passed ||
        (!result.ConfigurationInvalid && !string.IsNullOrWhiteSpace(justification));
}
