namespace Sati.Contracts.V1;

public sealed record ComplianceScheduleSettings(
    int ReviewOpenDaysBefore = 10,
    int PcpOpenDaysBefore = 90,
    int ComprehensiveAssessmentOpenDaysBefore = 30,
    int ReclassificationOpenDaysBefore = 60,
    int SafetyPlanOpenDaysBefore = 90,
    int PrivacyPracticesOpenDaysBefore = 90,
    int AgencyReleaseOpenDaysBefore = 90,
    int DhhsReleaseOpenDaysBefore = 90,
    int MedicalReleaseOpenDaysBefore = 90,
    int PcpDueDaysBeforeEffective = 0,
    int ComprehensiveAssessmentDueDaysBeforeEffective = 90,
    int ReclassificationDueDaysBeforeEffective = 30,
    int SafetyPlanDueDaysBeforeEffective = 0,
    int PrivacyPracticesDueDaysBeforeEffective = 0,
    int AgencyReleaseDueDaysBeforeEffective = 0,
    int DhhsReleaseDueDaysBeforeEffective = 0,
    int MedicalReleaseDueDaysBeforeEffective = 0);

/// <summary>
/// Authoritative calendar-day schedule shared by every client and the API.
/// The annual effective date is the obligation identity; deadlines and opening
/// dates are derived facts and never identify a cycle.
/// </summary>
public static class ComplianceScheduleRules
{
    /// <summary>
    /// Returns the annual effective date whose plan is in force on the supplied
    /// date. Before admission, the first effective date is returned so the
    /// pre-service documents can be prepared without inventing an earlier cycle.
    /// </summary>
    public static DateTime CurrentTargetEffectiveDate(
        DateTime initialEffectiveDate,
        DateTime asOf)
    {
        if (initialEffectiveDate == default)
            throw new ArgumentException("An initial effective date is required.", nameof(initialEffectiveDate));

        var effective = initialEffectiveDate.Date;
        var reference = asOf.Date;
        if (reference < effective)
            return effective;

        var yearsElapsed = reference.Year - effective.Year;
        if (reference < effective.AddYears(yearsElapsed))
            yearsElapsed--;

        return effective.AddYears(yearsElapsed);
    }

    /// <summary>
    /// Returns the annual effective date after the one in force on
    /// <paramref name="asOf"/>. Counted from the initial date rather than by adding a
    /// year to the current target: a February 29 start is February 28 in common
    /// years and must come back to February 29, exactly as
    /// <see cref="TargetEffectiveDatesThroughNext"/> generates it.
    /// </summary>
    public static DateTime UpcomingTargetEffectiveDate(
        DateTime initialEffectiveDate,
        DateTime asOf)
    {
        var current = CurrentTargetEffectiveDate(initialEffectiveDate, asOf);
        var yearsElapsed = current.Year - initialEffectiveDate.Date.Year;
        return initialEffectiveDate.Date.AddYears(yearsElapsed + 1);
    }

    /// <summary>
    /// Whether a document type is renewed by a new version that is prepared while
    /// the previous one is still in force. Such a type can have two obligations
    /// worth showing at once: the one in force and the renewal being developed.
    /// Quarterly reviews are excluded because a review never replaces an earlier
    /// review, and releases are tracked per recipient in their own workspace.
    /// </summary>
    public static bool HasRenewalOverlap(string formType) => formType is
        "PCP" or "ComprehensiveAssessment" or "Reclassification" or
        "SafetyPlan" or "PrivacyPractices";

    /// <summary>
    /// Whether the renewal for the upcoming target is under way on
    /// <paramref name="asOf"/>: its availability window has opened, or someone has
    /// already opened or completed it early. The renewal stops being a separate
    /// obligation on its own target date, when it becomes the one in force whether
    /// or not it is finished; callers get that by resolving the targets again.
    /// </summary>
    public static bool IsRenewalUnderway(
        DateTime renewalAvailableOn,
        DateTime? openedOn,
        DateTime? completedOn,
        DateTime asOf) =>
        asOf.Date >= renewalAvailableOn.Date ||
        openedOn is not null ||
        completedOn is not null;

    /// <summary>
    /// Enumerates every annual identity from admission through the next renewal.
    /// An implausibly large range fails explicitly instead of silently omitting
    /// older obligations, because an omitted cycle would look satisfied to billing.
    /// </summary>
    public static IReadOnlyList<DateTime> TargetEffectiveDatesThroughNext(
        DateTime initialEffectiveDate,
        DateTime asOf,
        int maximumCycles = 150)
    {
        if (initialEffectiveDate == default)
            throw new ArgumentException("An initial effective date is required.", nameof(initialEffectiveDate));
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCycles, 1);

        var effective = initialEffectiveDate.Date;
        var reference = asOf.Date;
        var yearsElapsed = reference.Year - effective.Year;
        if (reference < effective.AddYears(yearsElapsed))
            yearsElapsed--;

        var lastIndex = Math.Max(yearsElapsed + 1, 0);
        var cycleCount = lastIndex + 1;
        if (cycleCount > maximumCycles)
        {
            throw new InvalidOperationException(
                $"The effective date would create {cycleCount} annual compliance cycles; " +
                $"the supported maximum is {maximumCycles}. Correct the effective date before continuing.");
        }

        return Enumerable.Range(0, cycleCount)
            .Select(effective.AddYears)
            .ToArray();
    }

    public static DateTime DueDate(
        string formType,
        DateTime targetEffectiveDate,
        ComplianceScheduleSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formType);
        ArgumentNullException.ThrowIfNull(settings);
        var target = targetEffectiveDate.Date;

        return formType switch
        {
            "Q1R" => target.AddDays(90),
            "Q2R" => target.AddDays(180),
            "Q3R" => target.AddDays(270),
            "Q4R" => target.AddDays(360),
            "PCP" => target.AddDays(-settings.PcpDueDaysBeforeEffective),
            "ComprehensiveAssessment" => target.AddDays(
                -settings.ComprehensiveAssessmentDueDaysBeforeEffective),
            "Reclassification" => target.AddDays(
                -settings.ReclassificationDueDaysBeforeEffective),
            "SafetyPlan" => target.AddDays(-settings.SafetyPlanDueDaysBeforeEffective),
            "PrivacyPractices" => target.AddDays(
                -settings.PrivacyPracticesDueDaysBeforeEffective),
            "Release_Agency" => target.AddDays(-settings.AgencyReleaseDueDaysBeforeEffective),
            "Release_DHHS" => target.AddDays(-settings.DhhsReleaseDueDaysBeforeEffective),
            "Release_Medical" => target.AddDays(-settings.MedicalReleaseDueDaysBeforeEffective),
            _ => throw new ArgumentOutOfRangeException(nameof(formType), formType,
                "The compliance form type is not supported.")
        };
    }

    public static DateTime AvailableOn(
        string formType,
        DateTime dueDate,
        ComplianceScheduleSettings settings) =>
        dueDate.Date.AddDays(-OpenDaysBefore(formType, settings));

    public static int OpenDaysBefore(
        string formType,
        ComplianceScheduleSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formType);
        ArgumentNullException.ThrowIfNull(settings);
        return formType switch
        {
            "Q1R" or "Q2R" or "Q3R" or "Q4R" => settings.ReviewOpenDaysBefore,
            "PCP" => settings.PcpOpenDaysBefore,
            "ComprehensiveAssessment" => settings.ComprehensiveAssessmentOpenDaysBefore,
            "Reclassification" => settings.ReclassificationOpenDaysBefore,
            "SafetyPlan" => settings.SafetyPlanOpenDaysBefore,
            "PrivacyPractices" => settings.PrivacyPracticesOpenDaysBefore,
            "Release_Agency" => settings.AgencyReleaseOpenDaysBefore,
            "Release_DHHS" => settings.DhhsReleaseOpenDaysBefore,
            "Release_Medical" => settings.MedicalReleaseOpenDaysBefore,
            _ => throw new ArgumentOutOfRangeException(nameof(formType), formType,
                "The compliance form type is not supported.")
        };
    }
}
