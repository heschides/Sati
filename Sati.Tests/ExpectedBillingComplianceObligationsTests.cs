using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class ExpectedBillingComplianceObligationsTests
{
    private static readonly DateTime EffectiveOn = new(2026, 3, 7);
    private static readonly ComplianceScheduleSettings Schedule = new();

    [Fact]
    public void MissingPcpFailsClosedBeginningTheDayAfterItsDueDate()
    {
        var snapshots = ExpectedBillingComplianceObligations.IncludeMissingForms(
            EffectiveOn, [], EffectiveOn.AddDays(1), Schedule);

        Assert.Empty(BillingComplianceGate.EvaluateBillingWindow(
            snapshots, EffectiveOn, BillingComplianceRequirements.Pcp));
        var result = BillingComplianceGate.EvaluateBillingWindowDetailed(
            snapshots, EffectiveOn.AddDays(1), BillingComplianceRequirements.Pcp);

        var blocker = Assert.Single(result.Blockers!);
        Assert.Equal("PCP", blocker.Name);
        Assert.Equal(
            ExpectedBillingComplianceObligations.MissingFormObligationId("PCP", EffectiveOn),
            blocker.ObligationId);
    }

    [Fact]
    public void MissingQuarterlyReviewFailsClosedAfterItsExactReviewDeadline()
    {
        var serviceDate = EffectiveOn.AddDays(91);
        var snapshots = ExpectedBillingComplianceObligations.IncludeMissingForms(
            EffectiveOn, [], serviceDate, Schedule);

        var result = BillingComplianceGate.EvaluateBillingWindowDetailed(
            snapshots, serviceDate, BillingComplianceRequirements.QuarterlyReviews);

        var blocker = Assert.Single(result.Blockers!);
        Assert.Equal("Q1R", blocker.Type);
        Assert.Equal(EffectiveOn.AddDays(90), blocker.DueDate);
    }

    [Fact]
    public void ExistingTargetedCompletionWinsAndIsNotReplacedByAMissingSnapshot()
    {
        var completedPcp = new ComplianceFormSnapshot(
            "PCP",
            EffectiveOn,
            EffectiveOn,
            ObligationId: "form:17",
            TargetEffectiveDate: EffectiveOn);
        var snapshots = ExpectedBillingComplianceObligations.IncludeMissingForms(
            EffectiveOn, [completedPcp], EffectiveOn.AddDays(1), Schedule);

        Assert.Single(snapshots, item =>
            item.Type == "PCP" && item.TargetEffectiveDate == EffectiveOn);
        Assert.Empty(BillingComplianceGate.EvaluateBillingWindow(
            snapshots, EffectiveOn.AddDays(1), BillingComplianceRequirements.Pcp));
    }

    [Fact]
    public void MissingUniversalDhhsReleaseFailsClosedButRemainsASoftRequirementByDefault()
    {
        var facts = ExpectedBillingComplianceObligations.IncludeMissingDhhs(
            EffectiveOn, [], EffectiveOn.AddDays(1));
        var snapshots = ReleaseBillingRules.BuildComplianceSnapshots(
            facts, EffectiveOn.AddDays(1));

        Assert.NotEmpty(BillingComplianceGate.EvaluateBillingWindow(
            snapshots, EffectiveOn.AddDays(1), BillingComplianceRequirements.DhhsRelease));
        Assert.Empty(BillingComplianceGate.EvaluateBillingWindow(
            snapshots, EffectiveOn.AddDays(1), BillingComplianceGate.DefaultRequirements));
    }

    [Fact]
    public void MissingEffectiveDateDoesNotInventAnnualObligations()
    {
        Assert.Empty(ExpectedBillingComplianceObligations.IncludeMissingForms(
            null, [], EffectiveOn, Schedule));
        Assert.Empty(ExpectedBillingComplianceObligations.IncludeMissingDhhs(
            null, [], EffectiveOn));
    }

    [Fact]
    public void RecipientNameFlowsIntoBillingAndRecoveryLabels()
    {
        var fact = new ReleaseComplianceFact(
            "release:v1:2026-03-07:medical:annual:provider-17",
            ReleaseObligationCategory.Medical,
            EffectiveOn,
            EffectiveOn,
            null,
            [],
            TargetEffectiveDate: EffectiveOn,
            RecipientDisplayName: "Dr. Example");
        var blocker = Assert.Single(BillingComplianceGate.EvaluateBillingWindowDetailed(
            ReleaseBillingRules.BuildComplianceSnapshots([fact], EffectiveOn.AddDays(1)),
            EffectiveOn.AddDays(1),
            BillingComplianceRequirements.MedicalRelease).Blockers!);
        var recovery = Assert.Single(ReleaseBillingRules.BuildRecoveryObligations(9, [fact]));

        Assert.Equal("Medical Release — Dr. Example", blocker.Name);
        Assert.Equal("Medical Release — Dr. Example", recovery.Name);
    }
}
