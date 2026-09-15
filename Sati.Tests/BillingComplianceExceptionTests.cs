using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class BillingComplianceExceptionTests
{
    [Fact]
    public void DetailedGateCarriesStableExactObligationIds()
    {
        var due = new DateTime(2026, 3, 7);
        ComplianceFormSnapshot[] forms =
        [
            new("PCP", due, null, ObligationId: "form:41"),
            new("ComprehensiveAssessment", due.AddDays(-90), null,
                ObligationId: "form:42")
        ];

        var result = BillingComplianceGate.EvaluateBillingWindowDetailed(
            forms,
            due.AddDays(1));

        Assert.False(result.Passed);
        Assert.Equal(["form:42", "form:41"],
            result.Blockers!.Select(blocker => blocker.ObligationId));
    }

    [Fact]
    public void NarrowExceptionIsAcceptedButReturnsUnselectedBlockerForBilling()
    {
        BillingComplianceBlocker[] blockers =
        [
            new("form:41", "PCP", "PCP", new DateTime(2026, 3, 7)),
            new("form:42", "ComprehensiveAssessment", "Comprehensive Assessment",
                new DateTime(2025, 12, 7))
        ];

        var result = BillingComplianceExceptionRules.Validate(
            blockers,
            ["form:41"],
            "A narrowly reviewed exception.",
            attestationConfirmed: true);

        Assert.True(result.Accepted);
        var remaining = Assert.Single(
            BillingComplianceExceptionRules.RemainingBlockers(
                blockers, result.SelectedObligationIds));
        Assert.Equal("form:42", remaining.ObligationId);
    }

    [Fact]
    public void ExactSelectionExplanationAndAttestationAreAllRequired()
    {
        BillingComplianceBlocker[] blockers =
        [
            new("form:41", "PCP", "PCP", new DateTime(2026, 3, 7))
        ];

        Assert.False(BillingComplianceExceptionRules.Validate(
            blockers, ["form:41"], "Reason", false).Accepted);
        Assert.False(BillingComplianceExceptionRules.Validate(
            blockers, ["forged"], "Reason", true).Accepted);
        Assert.False(BillingComplianceExceptionRules.Validate(
            blockers, ["form:41"], " ", true).Accepted);

        var accepted = BillingComplianceExceptionRules.Validate(
            blockers, ["form:41", "form:41"], "  Reason  ", true);
        Assert.True(accepted.Accepted);
        Assert.Equal(["form:41"], accepted.SelectedObligationIds);
    }

    [Fact]
    public void PcpOpeningHasItsOwnExactIdAndDeadline()
    {
        var pcp = new ComplianceFormSnapshot(
            "PCP",
            new DateTime(2026, 3, 7),
            null,
            OpenedDate: null,
            ObligationId: "form:41");

        var result = BillingComplianceGate.EvaluateBillingWindowDetailed(
            BillingComplianceGate.IncludePcpOpeningObligations([pcp]),
            new DateTime(2025, 12, 8),
            BillingComplianceRequirements.PcpOpening);

        var blocker = Assert.Single(result.Blockers!);
        Assert.Equal("form:41/opening", blocker.ObligationId);
        Assert.Equal(new DateTime(2025, 12, 7), blocker.DueDate);
    }
}
