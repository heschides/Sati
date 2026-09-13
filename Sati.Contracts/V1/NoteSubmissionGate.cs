namespace Sati.Contracts.V1;

public sealed record NoteSubmissionResult(
    bool Passed,
    IReadOnlyList<string> Reasons,
    bool HistoricalWindowBlocked)
{
    public string Message => Passed ? string.Empty :
        "This note cannot be submitted for approval. " + string.Join(" ", Reasons) +
        " Your draft has not been submitted. Save it as Pending, Held for compliance, or Compliance blocked " +
        "to retain the service documentation. Resolve current requirements before submitting; " +
        "later completion does not make service inside a past compliance gap billable.";
}

/// <summary>
/// The shared gate for entering supervisory review. This is not an approval or
/// billing override. A note's form label or case-manager justification never
/// proves that a form was completed: the stored completion evidence does.
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
            return new(false, [BillingComplianceGate.UnsupportedConfigurationReason], false);

        var snapshots = forms.ToList();
        var current = BillingComplianceGate.Evaluate(
            effectiveDate, snapshots, today, requirements: requirements);
        var historical = serviceDate is DateTime date
            ? BillingComplianceGate.EvaluateBillingWindow(snapshots, date, requirements)
            : [];
        var reasons = current.Reasons.Concat(historical).Distinct(StringComparer.Ordinal).ToList();
        return new(reasons.Count == 0, reasons, serviceDate.HasValue && historical.Count > 0);
    }
}
