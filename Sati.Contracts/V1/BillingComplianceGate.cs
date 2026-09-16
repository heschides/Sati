namespace Sati.Contracts.V1;

[Flags]
public enum BillingComplianceRequirements
{
    None = 0,
    QuarterlyReviews = 1 << 0,
    Pcp = 1 << 1,
    ComprehensiveAssessment = 1 << 2,
    Reclassification = 1 << 3,
    SafetyPlan = 1 << 4,
    PrivacyPractices = 1 << 5,
    AgencyRelease = 1 << 6,
    DhhsRelease = 1 << 7,
    MedicalRelease = 1 << 8,
    PcpOpening = 1 << 9,
    ComprehensiveAssessmentOpening = 1 << 10,
    All = QuarterlyReviews | Pcp | ComprehensiveAssessment | Reclassification |
          SafetyPlan | PrivacyPractices | AgencyRelease | DhhsRelease | MedicalRelease |
          PcpOpening | ComprehensiveAssessmentOpening
}

public sealed record ComplianceFormSnapshot(
    string Type,
    DateTime DueDate,
    DateTime? CompletedDate,
    DateTime? OpenedDate = null,
    string? ObligationId = null,
    string? EvidenceId = null,
    string? OpenedEvidenceId = null,
    DateTime? TargetEffectiveDate = null,
    string? RecipientDisplayName = null);

public sealed record BillingComplianceBlocker(
    string ObligationId,
    string Type,
    string Name,
    DateTime DueDate);

public sealed record BillingComplianceResult(
    bool Passed,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<BillingComplianceBlocker>? Blockers = null);

/// <summary>
/// Shared billing-compliance decision for the desktop client and API. A document
/// blocks historical service beginning the day after its due date and ending on
/// its completion date.
/// The agency setting controls which document types participate in the gate.
/// </summary>
public static class BillingComplianceGate
{
    /// <summary>
    /// The PCP-opening billing deadline is a fixed program rule. The separately
    /// configurable UI availability/notification lead time must not rewrite
    /// historical billability.
    /// </summary>
    public const int PcpOpeningBillingLeadDays = 90;

    /// <summary>
    /// The Comprehensive Assessment must be started 30 days before it is due,
    /// which is 120 days before the plan it informs. Fixed for the same reason as
    /// <see cref="PcpOpeningBillingLeadDays"/>: the configurable availability lead
    /// time must not rewrite historical billability. Taken from the agency's
    /// annual tracking workbook, not from a cited OADS rule.
    /// </summary>
    public const int ComprehensiveAssessmentOpeningBillingLeadDays = 30;

    public const BillingComplianceRequirements DefaultRequirements =
        BillingComplianceRequirements.QuarterlyReviews |
        BillingComplianceRequirements.Pcp |
        BillingComplianceRequirements.ComprehensiveAssessment;

    /// <summary>
    /// Projects the independently configurable opening obligations beside the
    /// completion obligations they belong to: the PCP must be opened on the first
    /// day it is available, and the Comprehensive Assessment must be started
    /// <see cref="ComprehensiveAssessmentOpeningBillingLeadDays"/> before it is due.
    /// Each is deliberately a separate snapshot so turning on an opening gate never
    /// changes the document's hard completion deadline.
    /// </summary>
    public static IReadOnlyList<ComplianceFormSnapshot> IncludeOpeningObligations(
        IEnumerable<ComplianceFormSnapshot> forms)
    {
        ArgumentNullException.ThrowIfNull(forms);

        var projected = new List<ComplianceFormSnapshot>();
        foreach (var form in forms)
        {
            projected.Add(form);
            var opening = OpeningObligationFor(form.Type);
            if (opening is null)
                continue;

            projected.Add(new ComplianceFormSnapshot(
                opening.Value.Type,
                form.DueDate.Date.AddDays(-opening.Value.LeadDays),
                form.OpenedDate,
                ObligationId: $"{ResolveObligationId(form)}/opening",
                EvidenceId: form.OpenedEvidenceId));
        }

        return projected;
    }

    /// <summary>
    /// The date a document must be opened by, or null when the document type has
    /// no opening obligation. Readers that warn about a late opening use this so
    /// the warning and the gate cannot name different days.
    /// </summary>
    public static DateTime? OpeningDeadline(string formType, DateTime dueDate) =>
        OpeningObligationFor(formType) is { } opening
            ? dueDate.Date.AddDays(-opening.LeadDays)
            : null;

    /// <summary>The opening obligation type projected for a document type, if any.</summary>
    public static string? OpeningObligationType(string formType) =>
        OpeningObligationFor(formType)?.Type;

    private static (string Type, int LeadDays)? OpeningObligationFor(string formType) =>
        formType switch
        {
            "PCP" => (BillingComplianceObligationTypes.PcpOpening, PcpOpeningBillingLeadDays),
            "ComprehensiveAssessment" => (
                BillingComplianceObligationTypes.ComprehensiveAssessmentOpening,
                ComprehensiveAssessmentOpeningBillingLeadDays),
            _ => null
        };

    public static BillingComplianceResult Evaluate(
        DateTime? effectiveDate,
        IEnumerable<ComplianceFormSnapshot> forms,
        DateTime today,
        string? beingCompleted = null,
        BillingComplianceRequirements requirements = DefaultRequirements)
    {
        // Kept in the shared signature because cycle generation and callers still
        // own an effective date, but its absence is a profile/data-quality issue;
        // it is not an incomplete overdue document and therefore cannot fail this gate.
        _ = effectiveDate;

        // TODO(human): decide whether an unreadable requirement value fails this gate.
        // IsSupported(requirements) is false when the stored setting carries bits this
        // build does not know. Today that makes every form type non-required, so nothing
        // is overdue and the gate reports Passed. UnsupportedConfigurationReason holds
        // the shared wording if you choose to fail closed here.

        var overdue = forms
            .Where(form => IsRequired(form.Type, requirements))
            .Where(form => IsIncompleteAndOverdue(form.DueDate, form.CompletedDate, today))
            .OrderBy(form => form.DueDate)
            .ThenBy(form => form.Type, StringComparer.Ordinal)
            .ToList();

        var exemptionIndex = -1;
        for (var index = overdue.Count - 1; index >= 0; index--)
        {
            if (!string.Equals(overdue[index].Type, beingCompleted, StringComparison.Ordinal))
                continue;
            exemptionIndex = index;
            break;
        }

        var reasons = overdue
            .Where((_, index) => index != exemptionIndex)
            .Select(form => $"{DisplayName(form.Type)} was due {form.DueDate:MMM d, yyyy} and is incomplete.")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new BillingComplianceResult(reasons.Count == 0, reasons);
    }

    public static IReadOnlyList<string> EvaluateBillingWindow(
        IEnumerable<ComplianceFormSnapshot> forms,
        DateTime serviceDate,
        BillingComplianceRequirements requirements = DefaultRequirements)
        => EvaluateBillingWindowDetailed(forms, serviceDate, requirements).Reasons;

    public static BillingComplianceResult EvaluateBillingWindowDetailed(
        IEnumerable<ComplianceFormSnapshot> forms,
        DateTime serviceDate,
        BillingComplianceRequirements requirements = DefaultRequirements)
    {
        ArgumentNullException.ThrowIfNull(forms);

        var blockers = forms
            .Where(form => IsBillingWindowBlocked(
                form.Type, form.DueDate, form.CompletedDate, serviceDate, requirements))
            .OrderBy(form => form.DueDate)
            .ThenBy(form => form.Type, StringComparer.Ordinal)
            .ThenBy(ResolveObligationId, StringComparer.Ordinal)
            .Select(form => new BillingComplianceBlocker(
                ResolveObligationId(form),
                form.Type,
                DisplayName(form),
                form.DueDate.Date))
            .ToList();

        var reasons = blockers
            .Select(blocker => $"{blocker.Name} was due {blocker.DueDate:MMM d, yyyy} " +
                               "and was not completed as of this service date.")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new BillingComplianceResult(blockers.Count == 0, reasons, blockers);
    }

    public static string ResolveObligationId(ComplianceFormSnapshot form) =>
        string.IsNullOrWhiteSpace(form.ObligationId)
            ? $"form:{form.Type}:{form.DueDate:yyyy-MM-dd}"
            : form.ObligationId.Trim();

    public static string DisplayName(ComplianceFormSnapshot form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var name = DisplayName(form.Type);
        return string.IsNullOrWhiteSpace(form.RecipientDisplayName)
            ? name
            : $"{name} — {form.RecipientDisplayName.Trim()}";
    }

    public static bool IsBillingWindowBlocked(
        string formType,
        DateTime dueDate,
        DateTime? completedDate,
        DateTime serviceDate,
        BillingComplianceRequirements requirements = DefaultRequirements)
        => IsRequired(formType, requirements) &&
           IsWithinBlockedInterval(dueDate, completedDate, serviceDate);

    public static bool IsWithinBlockedInterval(
        DateTime dueDate,
        DateTime? completedDate,
        DateTime serviceDate)
        => serviceDate.Date > dueDate.Date &&
           (completedDate is null || serviceDate.Date < completedDate.Value.Date);

    public static bool IsRequired(
        string formType,
        BillingComplianceRequirements requirements)
    {
        var requirement = RequirementFor(formType);
        return requirement != BillingComplianceRequirements.None &&
               (requirements & requirement) == requirement;
    }

    /// <summary>
    /// One wording for a requirement value this build cannot read. The export gate
    /// and the submission gate both surface it, so the sentence has a single owner
    /// rather than a hand-written copy per gate.
    /// </summary>
    public const string UnsupportedConfigurationReason =
        "The agency billing compliance configuration is invalid.";

    public static bool IsSupported(BillingComplianceRequirements requirements) =>
        (requirements & ~BillingComplianceRequirements.All) == 0;

    public static bool IsIncompleteAndOverdue(
        DateTime dueDate,
        DateTime? completedDate,
        DateTime asOfDate)
        => dueDate.Date < asOfDate.Date &&
           (completedDate is null || completedDate.Value.Date > asOfDate.Date);

    public static BillingComplianceRequirements RequirementFor(string type) => type switch
    {
        "Q1R" or "Q2R" or "Q3R" or "Q4R" => BillingComplianceRequirements.QuarterlyReviews,
        "PCP" => BillingComplianceRequirements.Pcp,
        BillingComplianceObligationTypes.PcpOpening => BillingComplianceRequirements.PcpOpening,
        "ComprehensiveAssessment" => BillingComplianceRequirements.ComprehensiveAssessment,
        BillingComplianceObligationTypes.ComprehensiveAssessmentOpening =>
            BillingComplianceRequirements.ComprehensiveAssessmentOpening,
        "Reclassification" => BillingComplianceRequirements.Reclassification,
        "SafetyPlan" => BillingComplianceRequirements.SafetyPlan,
        "PrivacyPractices" => BillingComplianceRequirements.PrivacyPractices,
        "Release_Agency" => BillingComplianceRequirements.AgencyRelease,
        "Release_DHHS" => BillingComplianceRequirements.DhhsRelease,
        "Release_Medical" => BillingComplianceRequirements.MedicalRelease,
        _ => BillingComplianceRequirements.None
    };

    public static string DisplayName(string type) => type switch
    {
        "PCP" => "PCP",
        BillingComplianceObligationTypes.PcpOpening => "PCP opening",
        "ComprehensiveAssessment" => "Comprehensive Assessment",
        BillingComplianceObligationTypes.ComprehensiveAssessmentOpening =>
            "Comprehensive Assessment start",
        "Reclassification" => "Reclassification",
        "SafetyPlan" => "Safety Plan",
        "PrivacyPractices" => "Privacy Practices",
        "Release_Agency" => "Agency Release",
        "Release_DHHS" => "DHHS Release",
        "Release_Medical" => "Medical Release",
        "Q1R" => "Q1 Review",
        "Q2R" => "Q2 Review",
        "Q3R" => "Q3 Review",
        "Q4R" => "Q4 Review",
        _ => type
    };
}

public static class BillingComplianceObligationTypes
{
    public const string PcpOpening = "PCP_Opening";
    public const string ComprehensiveAssessmentOpening = "ComprehensiveAssessment_Opening";
}
