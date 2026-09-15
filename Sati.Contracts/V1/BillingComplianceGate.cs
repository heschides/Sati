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
    All = QuarterlyReviews | Pcp | ComprehensiveAssessment | Reclassification |
          SafetyPlan | PrivacyPractices | AgencyRelease | DhhsRelease | MedicalRelease |
          PcpOpening
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

    public const BillingComplianceRequirements DefaultRequirements =
        BillingComplianceRequirements.QuarterlyReviews |
        BillingComplianceRequirements.Pcp |
        BillingComplianceRequirements.ComprehensiveAssessment;

    /// <summary>
    /// Projects the independently configurable PCP-opening obligation beside the
    /// PCP-completion obligation. The opening deadline is the first day the plan
    /// is available to open. It is deliberately a separate snapshot so turning
    /// on the opening gate never changes the PCP's hard completion deadline.
    /// </summary>
    public static IReadOnlyList<ComplianceFormSnapshot> IncludePcpOpeningObligations(
        IEnumerable<ComplianceFormSnapshot> forms)
    {
        ArgumentNullException.ThrowIfNull(forms);

        var projected = new List<ComplianceFormSnapshot>();
        foreach (var form in forms)
        {
            projected.Add(form);
            if (!string.Equals(form.Type, "PCP", StringComparison.Ordinal))
                continue;

            projected.Add(new ComplianceFormSnapshot(
                BillingComplianceObligationTypes.PcpOpening,
                form.DueDate.Date.AddDays(-PcpOpeningBillingLeadDays),
                form.OpenedDate,
                ObligationId: $"{ResolveObligationId(form)}/opening",
                EvidenceId: form.OpenedEvidenceId));
        }

        return projected;
    }

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
}
