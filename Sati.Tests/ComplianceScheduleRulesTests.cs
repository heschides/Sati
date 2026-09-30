using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class ComplianceScheduleRulesTests
{
    private static readonly ComplianceScheduleSettings Defaults = new();

    [Theory]
    [InlineData("ComprehensiveAssessment", 0, 30)]
    [InlineData("ComprehensiveAssessment", 10, 30)]
    [InlineData("ComprehensiveAssessment", 30, 30)]
    [InlineData("ComprehensiveAssessment", 45, 45)]
    [InlineData("PCP", 0, 90)]
    [InlineData("PCP", 90, 90)]
    [InlineData("PCP", 120, 120)]
    public void AvailabilityCannotFallAfterTheRequiredOpeningDeadline(
        string type, int configuredLead, int expectedLead)
    {
        var settings = Defaults with
        {
            ComprehensiveAssessmentOpenDaysBefore = configuredLead,
            PcpOpenDaysBefore = configuredLead
        };
        var due = new DateTime(2026, 10, 22);

        Assert.Equal(expectedLead, ComplianceScheduleRules.OpenDaysBefore(type, settings));
        Assert.Equal(due.AddDays(-expectedLead),
            ComplianceScheduleRules.AvailableOn(type, due, settings));
        // Configuring earlier availability never moves the fixed opening obligation.
        Assert.Equal(due.AddDays(type == "PCP" ? -90 : -30),
            BillingComplianceGate.OpeningDeadline(type, due));
    }

    [Fact]
    public void LegacyZeroWindowAllowsAssessmentOpeningAndCompletionOnSeptember22()
    {
        var target = new DateTime(2027, 1, 20);
        var settings = Defaults with { ComprehensiveAssessmentOpenDaysBefore = 0 };
        var due = ComplianceScheduleRules.DueDate("ComprehensiveAssessment", target, settings);
        var available = ComplianceScheduleRules.AvailableOn("ComprehensiveAssessment", due, settings);
        var completed = new DateTime(2026, 9, 22);
        var today = new DateTime(2026, 9, 29);

        Assert.Equal(new DateTime(2026, 10, 22), due);
        Assert.Null(FormOpeningRules.Validate(completed, available, today));
        var decision = FormAttestationRules.Evaluate(
            "ComprehensiveAssessment", completed, target.AddYears(-1), today,
            AttestationActorKind.CaseManager, [], targetEffectiveDate: target, availableOn: available);
        Assert.True(decision.Accepted, decision.DateError);
        Assert.NotNull(FormOpeningRules.Validate(completed.AddDays(-1), available, today));
        Assert.NotNull(FormAttestationRules.ValidateCompletionDate(
            completed.AddDays(-1), target.AddYears(-1), today, available));
        Assert.NotNull(FormAttestationRules.ValidateCompletionDate(
            today.AddDays(1), target.AddYears(-1), today, available));
    }

    [Theory]
    [InlineData("Q1R")]
    [InlineData("Reclassification")]
    [InlineData("SafetyPlan")]
    public void FormsWithoutAnOpeningDeadlineKeepTheirConfiguredWindow(string type)
    {
        var settings = Defaults with
        {
            ReviewOpenDaysBefore = 0,
            ReclassificationOpenDaysBefore = 0,
            SafetyPlanOpenDaysBefore = 0
        };
        var due = new DateTime(2026, 10, 22);

        Assert.Equal(due, ComplianceScheduleRules.AvailableOn(type, due, settings));
    }

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
