using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class BillingComplianceGateTests
{
    [Fact]
    public void PcpOpeningIsASeparateOptionalHistoricalGate()
    {
        var pcp = new ComplianceFormSnapshot(
            "PCP",
            new DateTime(2027, 3, 7),
            CompletedDate: null,
            OpenedDate: new DateTime(2026, 12, 12));
        var obligations = BillingComplianceGate.IncludeOpeningObligations([pcp]);

        Assert.Empty(BillingComplianceGate.EvaluateBillingWindow(
            obligations,
            new DateTime(2026, 12, 8),
            BillingComplianceGate.DefaultRequirements));
        Assert.Empty(BillingComplianceGate.EvaluateBillingWindow(
            obligations,
            new DateTime(2026, 12, 7),
            BillingComplianceRequirements.PcpOpening));
        Assert.NotEmpty(BillingComplianceGate.EvaluateBillingWindow(
            obligations,
            new DateTime(2026, 12, 8),
            BillingComplianceRequirements.PcpOpening));
        Assert.Empty(BillingComplianceGate.EvaluateBillingWindow(
            obligations,
            new DateTime(2026, 12, 12),
            BillingComplianceRequirements.PcpOpening));
    }

    [Fact]
    public void PcpOpeningBillingDeadlineRemainsTheFixedNinetyDayRule()
    {
        var obligations = BillingComplianceGate.IncludeOpeningObligations(
            [new ComplianceFormSnapshot("PCP", new DateTime(2027, 3, 7), null)]);

        var opening = Assert.Single(obligations, item =>
            item.Type == BillingComplianceObligationTypes.PcpOpening);
        Assert.Equal(new DateTime(2026, 12, 7), opening.DueDate);
    }

    [Fact]
    public void OpeningDateMustBeActualAndInsideTheAvailableWindow()
    {
        var available = new DateTime(2026, 12, 7);
        var today = new DateTime(2026, 12, 12);

        Assert.NotNull(FormOpeningRules.Validate(available.AddDays(-1), available, today));
        Assert.Null(FormOpeningRules.Validate(available, available, today));
        Assert.Null(FormOpeningRules.Validate(today, available, today));
        Assert.NotNull(FormOpeningRules.Validate(today.AddDays(1), available, today));
    }

    public static TheoryData<string, BillingComplianceRequirements> EveryRequirement => new()
    {
        { "Q1R", BillingComplianceRequirements.QuarterlyReviews },
        { "Q2R", BillingComplianceRequirements.QuarterlyReviews },
        { "Q3R", BillingComplianceRequirements.QuarterlyReviews },
        { "Q4R", BillingComplianceRequirements.QuarterlyReviews },
        { "PCP", BillingComplianceRequirements.Pcp },
        { "ComprehensiveAssessment", BillingComplianceRequirements.ComprehensiveAssessment },
        { "Reclassification", BillingComplianceRequirements.Reclassification },
        { "SafetyPlan", BillingComplianceRequirements.SafetyPlan },
        { "PrivacyPractices", BillingComplianceRequirements.PrivacyPractices },
        { "Release_Agency", BillingComplianceRequirements.AgencyRelease },
        { "Release_DHHS", BillingComplianceRequirements.DhhsRelease },
        { "Release_Medical", BillingComplianceRequirements.MedicalRelease },
        { BillingComplianceObligationTypes.PcpOpening, BillingComplianceRequirements.PcpOpening },
        {
            BillingComplianceObligationTypes.ComprehensiveAssessmentOpening,
            BillingComplianceRequirements.ComprehensiveAssessmentOpening
        }
    };

    [Fact]
    public void AssessmentStartIsDueOneHundredTwentyDaysBeforeThePlan()
    {
        // Plan March 7, 2027; assessment due December 7, 2026 (90 days before);
        // start due November 7, 2026 (120 days before), per the agency workbook.
        var assessment = new ComplianceFormSnapshot(
            "ComprehensiveAssessment", new DateTime(2026, 12, 7), null, ObligationId: "form:77");

        var obligations = BillingComplianceGate.IncludeOpeningObligations([assessment]);

        var start = Assert.Single(obligations, item =>
            item.Type == BillingComplianceObligationTypes.ComprehensiveAssessmentOpening);
        Assert.Equal(new DateTime(2026, 11, 7), start.DueDate);
        Assert.Equal("form:77/opening", start.ObligationId);
        Assert.Equal(
            new DateTime(2026, 11, 7),
            BillingComplianceGate.OpeningDeadline("ComprehensiveAssessment", assessment.DueDate));
        Assert.Null(BillingComplianceGate.OpeningDeadline("Reclassification", assessment.DueDate));
    }

    [Fact]
    public void AssessmentStartIsASeparateOptionalHistoricalGate()
    {
        var assessment = new ComplianceFormSnapshot(
            "ComprehensiveAssessment",
            new DateTime(2026, 12, 7),
            CompletedDate: null,
            OpenedDate: new DateTime(2026, 11, 12));
        var obligations = BillingComplianceGate.IncludeOpeningObligations([assessment]);

        // Off by default: a late start never blocks unless the agency chooses it.
        Assert.Empty(BillingComplianceGate.EvaluateBillingWindow(
            obligations, new DateTime(2026, 11, 8), BillingComplianceGate.DefaultRequirements));
        Assert.Empty(BillingComplianceGate.EvaluateBillingWindow(
            obligations, new DateTime(2026, 11, 7),
            BillingComplianceRequirements.ComprehensiveAssessmentOpening));
        Assert.NotEmpty(BillingComplianceGate.EvaluateBillingWindow(
            obligations, new DateTime(2026, 11, 8),
            BillingComplianceRequirements.ComprehensiveAssessmentOpening));
        Assert.Empty(BillingComplianceGate.EvaluateBillingWindow(
            obligations, new DateTime(2026, 11, 12),
            BillingComplianceRequirements.ComprehensiveAssessmentOpening));

        // Turning on the PCP opening gate does not turn on the assessment's.
        Assert.Empty(BillingComplianceGate.EvaluateBillingWindow(
            obligations, new DateTime(2026, 11, 8),
            BillingComplianceRequirements.PcpOpening | BillingComplianceRequirements.Pcp));
        Assert.False(BillingComplianceGate.IsRequired(
            "ComprehensiveAssessment",
            BillingComplianceRequirements.ComprehensiveAssessmentOpening));
    }

    [Fact]
    public void DefaultRequirementsContainOnlyTheThreeConfirmedBillingGates()
    {
        var expected =
            BillingComplianceRequirements.QuarterlyReviews |
            BillingComplianceRequirements.Pcp |
            BillingComplianceRequirements.ComprehensiveAssessment;

        Assert.Equal(BillingComplianceGate.DefaultRequirements, expected);
    }

    [Theory]
    [MemberData(nameof(EveryRequirement))]
    public void EveryEnabledIncompleteOverdueDocumentBlocks(
        string type,
        BillingComplianceRequirements requirement)
    {
        var result = BillingComplianceGate.Evaluate(
            new DateTime(2025, 1, 1),
            [new ComplianceFormSnapshot(type, new DateTime(2026, 8, 1), null)],
            new DateTime(2026, 8, 2),
            requirements: requirement);

        Assert.False(result.Passed);
        Assert.Single(result.Reasons);
    }

    [Theory]
    [MemberData(nameof(EveryRequirement))]
    public void EveryDisabledRequirementIsExcluded(
        string type,
        BillingComplianceRequirements requirement)
    {
        var result = BillingComplianceGate.Evaluate(
            new DateTime(2025, 1, 1),
            [new ComplianceFormSnapshot(type, new DateTime(2026, 8, 1), null)],
            new DateTime(2026, 8, 2),
            requirements: BillingComplianceRequirements.All & ~requirement);

        Assert.True(result.Passed, string.Join("; ", result.Reasons));
    }

    [Fact]
    public void DueDateItselfDoesNotCountAsOverdue()
    {
        var dueDate = new DateTime(2026, 8, 27);

        var result = BillingComplianceGate.Evaluate(
            new DateTime(2025, 1, 1),
            [new ComplianceFormSnapshot("PCP", dueDate, null)],
            dueDate,
            requirements: BillingComplianceRequirements.Pcp);

        Assert.True(result.Passed);
    }

    [Fact]
    public void PcpOpeningIsIndependentFromPcpCompletion()
    {
        Assert.True(BillingComplianceGate.IsRequired(
            BillingComplianceObligationTypes.PcpOpening,
            BillingComplianceRequirements.PcpOpening));
        Assert.False(BillingComplianceGate.IsRequired(
            BillingComplianceObligationTypes.PcpOpening,
            BillingComplianceRequirements.Pcp));
        Assert.False(BillingComplianceGate.IsRequired(
            "PCP",
            BillingComplianceRequirements.PcpOpening));
    }

    [Fact]
    public void CompletionRemovesCurrentBlockButDoesNotRetroactivelyCureEarlierServiceDates()
    {
        var completedDate = new DateTime(2026, 8, 10);
        var form = new ComplianceFormSnapshot(
            "PCP", new DateTime(2026, 8, 1), completedDate);

        var current = BillingComplianceGate.Evaluate(
            new DateTime(2025, 1, 1), [form], new DateTime(2026, 8, 27),
            requirements: BillingComplianceRequirements.Pcp);

        Assert.True(current.Passed);
        Assert.False(BillingComplianceGate.IsBillingWindowBlocked(
            form.Type, form.DueDate, form.CompletedDate, form.DueDate,
            BillingComplianceRequirements.Pcp));
        Assert.True(BillingComplianceGate.IsBillingWindowBlocked(
            form.Type, form.DueDate, form.CompletedDate, new DateTime(2026, 8, 9),
            BillingComplianceRequirements.Pcp));
        Assert.False(BillingComplianceGate.IsBillingWindowBlocked(
            form.Type, form.DueDate, form.CompletedDate, completedDate,
            BillingComplianceRequirements.Pcp));
    }

    [Fact]
    public void LaterOverdueDocumentDoesNotReachBackwardBeforeItsDueDate()
    {
        var form = new ComplianceFormSnapshot(
            "PCP", new DateTime(2026, 9, 6), new DateTime(2026, 9, 9));

        Assert.False(BillingComplianceGate.IsBillingWindowBlocked(
            form.Type, form.DueDate, form.CompletedDate, new DateTime(2026, 9, 4),
            BillingComplianceRequirements.Pcp));
        Assert.False(BillingComplianceGate.IsBillingWindowBlocked(
            form.Type, form.DueDate, form.CompletedDate, new DateTime(2026, 9, 6),
            BillingComplianceRequirements.Pcp));
        Assert.True(BillingComplianceGate.IsBillingWindowBlocked(
            form.Type, form.DueDate, form.CompletedDate, new DateTime(2026, 9, 8),
            BillingComplianceRequirements.Pcp));
        Assert.False(BillingComplianceGate.IsBillingWindowBlocked(
            form.Type, form.DueDate, form.CompletedDate, new DateTime(2026, 9, 9),
            BillingComplianceRequirements.Pcp));
    }

    [Fact]
    public void EarlierCycleDocumentStillBlocksUntilItIsCompleted()
    {
        var result = BillingComplianceGate.Evaluate(
            new DateTime(2024, 1, 1),
            [new ComplianceFormSnapshot("PCP", new DateTime(2025, 1, 1), null)],
            new DateTime(2026, 8, 27),
            requirements: BillingComplianceRequirements.Pcp);

        Assert.False(result.Passed);
    }

    [Fact]
    public void CompletingAFormExemptsOnlyOneMatchingOverdueDocument()
    {
        var result = BillingComplianceGate.Evaluate(
            new DateTime(2024, 1, 1),
            [
                new ComplianceFormSnapshot("PCP", new DateTime(2025, 1, 1), null),
                new ComplianceFormSnapshot("PCP", new DateTime(2026, 1, 1), null)
            ],
            new DateTime(2026, 8, 27),
            beingCompleted: "PCP",
            requirements: BillingComplianceRequirements.Pcp);

        Assert.False(result.Passed);
        Assert.Single(result.Reasons);
        Assert.Contains("Jan 1, 2025", result.Reasons[0]);
    }

    [Fact]
    public void UnknownDocumentsNeverEnterTheBillingGate()
    {
        var result = BillingComplianceGate.Evaluate(
            new DateTime(2025, 1, 1),
            [new ComplianceFormSnapshot("Unknown", new DateTime(2025, 1, 2), null)],
            new DateTime(2026, 8, 27),
            requirements: BillingComplianceRequirements.All);

        Assert.True(result.Passed);
    }

    [Fact]
    public void MissingEffectiveDateDoesNotMasqueradeAsAnOverdueDocument()
    {
        var result = BillingComplianceGate.Evaluate(
            null,
            [],
            new DateTime(2026, 8, 27),
            requirements: BillingComplianceRequirements.All);

        Assert.True(result.Passed);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void UnsupportedSettingBitsAreRejected()
    {
        var invalid = BillingComplianceRequirements.All |
                      (BillingComplianceRequirements)(1 << 20);

        Assert.False(BillingComplianceGate.IsSupported(invalid));
        Assert.True(BillingComplianceGate.IsSupported(BillingComplianceRequirements.All));
    }
}
