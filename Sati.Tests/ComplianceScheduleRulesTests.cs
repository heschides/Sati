using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class ComplianceScheduleRulesTests
{
    private static readonly ComplianceScheduleSettings Defaults = new();

    [Fact]
    public void MarchSeventhAnnualScheduleUsesTheConfirmedCalendarDayRules()
    {
        var target = new DateTime(2027, 3, 7);

        Assert.Equal(target,
            ComplianceScheduleRules.DueDate("PCP", target, Defaults));
        Assert.Equal(new DateTime(2026, 12, 7),
            ComplianceScheduleRules.DueDate("ComprehensiveAssessment", target, Defaults));
        Assert.Equal(new DateTime(2027, 2, 5),
            ComplianceScheduleRules.DueDate("Reclassification", target, Defaults));
        Assert.Equal(target,
            ComplianceScheduleRules.DueDate("SafetyPlan", target, Defaults));
        Assert.Equal(target,
            ComplianceScheduleRules.DueDate("PrivacyPractices", target, Defaults));
        Assert.Equal(target,
            ComplianceScheduleRules.DueDate("Release_DHHS", target, Defaults));

        Assert.Equal(target.AddDays(90),
            ComplianceScheduleRules.DueDate("Q1R", target, Defaults));
        Assert.Equal(target.AddDays(180),
            ComplianceScheduleRules.DueDate("Q2R", target, Defaults));
        Assert.Equal(target.AddDays(270),
            ComplianceScheduleRules.DueDate("Q3R", target, Defaults));
        Assert.Equal(target.AddDays(360),
            ComplianceScheduleRules.DueDate("Q4R", target, Defaults));
    }

    [Fact]
    public void IndependentDocumentsMayShareTheSameDecemberSeventhAvailabilityDate()
    {
        var target = new DateTime(2027, 3, 7);
        var types = new[]
        {
            "PCP", "Reclassification", "SafetyPlan", "PrivacyPractices",
            "Release_Agency", "Release_DHHS", "Release_Medical"
        };

        foreach (var type in types)
        {
            var due = ComplianceScheduleRules.DueDate(type, target, Defaults);
            Assert.Equal(new DateTime(2026, 12, 7),
                ComplianceScheduleRules.AvailableOn(type, due, Defaults));
        }

        var assessmentDue = ComplianceScheduleRules.DueDate(
            "ComprehensiveAssessment", target, Defaults);
        Assert.Equal(new DateTime(2026, 11, 7),
            ComplianceScheduleRules.AvailableOn(
                "ComprehensiveAssessment", assessmentDue, Defaults));
    }

    [Fact]
    public void CurrentAndNextCycleIdentityComesFromTheAnnualDateNotADeadline()
    {
        var initial = new DateTime(2024, 3, 7);

        Assert.Equal(initial,
            ComplianceScheduleRules.CurrentTargetEffectiveDate(
                initial, new DateTime(2024, 12, 7)));
        Assert.Equal(new DateTime(2025, 3, 7),
            ComplianceScheduleRules.CurrentTargetEffectiveDate(
                initial, new DateTime(2025, 3, 7)));

        Assert.Equal(
            [
                new DateTime(2024, 3, 7),
                new DateTime(2025, 3, 7),
                new DateTime(2026, 3, 7)
            ],
            ComplianceScheduleRules.TargetEffectiveDatesThroughNext(
                initial, new DateTime(2025, 12, 7)));

        Assert.Equal(
            [initial],
            ComplianceScheduleRules.TargetEffectiveDatesThroughNext(
                initial, new DateTime(2023, 12, 7)));
    }

    [Fact]
    public void LongTenureIncludesTheFirstCycleRatherThanSilentlyDroppingIt()
    {
        var initial = new DateTime(1990, 3, 7);

        var targets = ComplianceScheduleRules.TargetEffectiveDatesThroughNext(
            initial, new DateTime(2026, 9, 14));

        Assert.Equal(initial, targets[0]);
        Assert.Equal(new DateTime(2027, 3, 7), targets[^1]);
        Assert.Equal(38, targets.Count);
    }

    [Fact]
    public void ImplausiblyLargeCycleRangeFailsInsteadOfCreatingAHiddenGap()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ComplianceScheduleRules.TargetEffectiveDatesThroughNext(
                new DateTime(1900, 3, 7),
                new DateTime(2026, 9, 14),
                maximumCycles: 25));

        Assert.Contains("Correct the effective date", error.Message);
    }
}
