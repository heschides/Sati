using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Which correction a claim allows, and what a recorded bank deposit may say. The API
/// enforces both; the desktop offers only what they allow.
/// </summary>
public sealed class ClaimCorrectionRulesTests
{
    private static ClaimSubmissionFacts Original(
        ClaimFileVerdict file = ClaimFileVerdict.Accepted,
        ClaimAcknowledgementDisposition? claim = ClaimAcknowledgementDisposition.Accepted,
        RemittanceClaimStatus? remittance = null,
        string? payerClaimNumber = null) =>
        new(null, file, claim, remittance, payerClaimNumber);

    [Fact]
    public void AClaimTurnedAwayBeforeReviewIsResentAsANewClaim()
    {
        var rejected = ClaimCorrectionRules.Evaluate(
            [Original(claim: ClaimAcknowledgementDisposition.Rejected)], hasUnsentCorrection: false);

        Assert.Equal(ClaimLifecycleState.ClaimRejected, rejected.State);
        Assert.Equal([ClaimCorrectionAction.Resubmit], rejected.AllowedActions);
        Assert.Null(rejected.PayerClaimControlNumber);
    }

    [Fact]
    public void AFileRejectionIsTheSameSituationAsAClaimRejection()
    {
        var options = ClaimCorrectionRules.Evaluate(
            [Original(file: ClaimFileVerdict.Rejected, claim: null)], hasUnsentCorrection: false);

        Assert.Equal(ClaimLifecycleState.FileRejected, options.State);
        Assert.Equal([ClaimCorrectionAction.Resubmit], options.AllowedActions);
    }

    [Theory]
    [InlineData(RemittanceClaimStatus.Paid, ClaimLifecycleState.Paid, 2)]
    [InlineData(RemittanceClaimStatus.PartiallyPaid, ClaimLifecycleState.PartiallyPaid, 2)]
    [InlineData(RemittanceClaimStatus.Denied, ClaimLifecycleState.Denied, 1)]
    public void AnAdjudicatedClaimIsReplacedOrVoidedCitingThePayersNumber(
        RemittanceClaimStatus status, ClaimLifecycleState state, int allowedCount)
    {
        var options = ClaimCorrectionRules.Evaluate(
            [Original(remittance: status, payerClaimNumber: "ICN-1")], hasUnsentCorrection: false);

        Assert.Equal(state, options.State);
        Assert.Equal("ICN-1", options.PayerClaimControlNumber);
        Assert.Equal(allowedCount, options.AllowedActions.Count);
        Assert.Contains(ClaimCorrectionAction.Replace, options.AllowedActions);
        // A denied claim was never paid, so there is no payment to take back.
        Assert.Equal(status != RemittanceClaimStatus.Denied,
            options.AllowedActions.Contains(ClaimCorrectionAction.Void));
    }

    [Fact]
    public void WithoutAPayerClaimNumberAnAdjudicatedClaimOffersNothingToReplace()
    {
        var options = ClaimCorrectionRules.Evaluate(
            [Original(remittance: RemittanceClaimStatus.Paid)], hasUnsentCorrection: false);

        Assert.Equal(ClaimLifecycleState.Paid, options.State);
        Assert.Empty(options.AllowedActions);
        Assert.Contains("payer claim number", options.Explanation);
    }

    [Fact]
    public void AVoidedClaimLeavesNothingStandingSoTheServiceIsBilledAsNew()
    {
        IReadOnlyList<ClaimSubmissionFacts> submissions =
        [
            Original(remittance: RemittanceClaimStatus.Paid, payerClaimNumber: "ICN-1"),
            new(ClaimCorrectionAction.Void, ClaimFileVerdict.Accepted,
                ClaimAcknowledgementDisposition.Accepted, RemittanceClaimStatus.Reversed, "ICN-2")
        ];

        var options = ClaimCorrectionRules.Evaluate(submissions, hasUnsentCorrection: false);

        Assert.Equal(ClaimLifecycleState.Reversed, options.State);
        Assert.Equal([ClaimCorrectionAction.Resubmit], options.AllowedActions);
        Assert.Null(options.PayerClaimControlNumber);
    }

    [Fact]
    public void ARejectedReplacementStillLeavesTheEarlierClaimStandingWithThePayer()
    {
        IReadOnlyList<ClaimSubmissionFacts> submissions =
        [
            Original(remittance: RemittanceClaimStatus.Denied, payerClaimNumber: "ICN-1"),
            new(ClaimCorrectionAction.Replace, ClaimFileVerdict.Accepted,
                ClaimAcknowledgementDisposition.Rejected, null, null)
        ];

        var options = ClaimCorrectionRules.Evaluate(submissions, hasUnsentCorrection: false);

        Assert.Equal(ClaimLifecycleState.ClaimRejected, options.State);
        Assert.Equal("ICN-1", options.PayerClaimControlNumber);
        Assert.Equal([ClaimCorrectionAction.Replace], options.AllowedActions);
    }

    [Theory]
    [InlineData(null, ClaimAcknowledgementDisposition.Accepted, ClaimLifecycleState.AwaitingPayment)]
    [InlineData(null, ClaimAcknowledgementDisposition.Received, ClaimLifecycleState.ClaimReceived)]
    [InlineData(null, ClaimAcknowledgementDisposition.NeedsReview, ClaimLifecycleState.NeedsReview)]
    [InlineData(RemittanceClaimStatus.NeedsReview, null, ClaimLifecycleState.NeedsReview)]
    public void AClaimNobodyHasFinishedAnsweringOffersNoCorrection(
        RemittanceClaimStatus? remittance, ClaimAcknowledgementDisposition? claim, ClaimLifecycleState state)
    {
        var options = ClaimCorrectionRules.Evaluate(
            [Original(claim: claim, remittance: remittance)], hasUnsentCorrection: false);

        Assert.Equal(state, options.State);
        Assert.Empty(options.AllowedActions);
    }

    [Fact]
    public void AClaimWithACorrectionAlreadyWaitingTakesNoSecondOne()
    {
        var options = ClaimCorrectionRules.Evaluate(
            [Original(remittance: RemittanceClaimStatus.Paid, payerClaimNumber: "ICN-1")],
            hasUnsentCorrection: true);

        Assert.Equal(ClaimLifecycleState.CorrectionWaitingToSend, options.State);
        Assert.Empty(options.AllowedActions);
    }

    [Fact]
    public void AClaimThatWasNeverSentIsNotACorrectionCandidate()
    {
        var options = ClaimCorrectionRules.Evaluate([], hasUnsentCorrection: false);

        Assert.Equal(ClaimLifecycleState.NotSent, options.State);
        Assert.Empty(options.AllowedActions);
    }

    [Theory]
    [InlineData(ClaimCorrectionAction.Resubmit, "1")]
    [InlineData(ClaimCorrectionAction.Replace, "7")]
    [InlineData(ClaimCorrectionAction.Void, "8")]
    public void TheFrequencyCodeIsTheActionItself(ClaimCorrectionAction action, string code)
    {
        Assert.Equal(code, ClaimCorrectionRules.FrequencyCode(action));
        Assert.Equal("1", ClaimCorrectionRules.FrequencyCode(null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  x  ")]
    public void ACorrectionNeedsARealReason(string? reason) =>
        Assert.NotNull(ClaimCorrectionRules.ValidateReason(reason));

    [Fact]
    public void AReasonLongerThanTheContractAllowsIsRefused() =>
        Assert.NotNull(ClaimCorrectionRules.ValidateReason(
            new string('x', ClaimCorrectionRules.ReasonMaxLength + 1)));

    // -------------------------------------------------------------------------
    // Bank deposit entries
    // -------------------------------------------------------------------------

    private static readonly DateTime Today = new(2026, 9, 19);

    [Fact]
    public void AFirstDepositEntryNeedsOnlyAnAmountAndADate() =>
        Assert.Empty(EftDepositRules.Validate(25.60m, Today, Today, null, null, isCorrection: false));

    [Fact]
    public void ACorrectionToARecordedDepositHasToSayWhy()
    {
        Assert.Contains("note", EftDepositRules.Validate(
            25.60m, Today, Today, null, null, isCorrection: true).Keys);
        Assert.Empty(EftDepositRules.Validate(
            25.60m, Today, Today, null, "Bank statement says $25.60.", isCorrection: true));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(25.601)]
    public void AnImpossibleAmountIsRefused(double amount) =>
        Assert.Contains("amount", EftDepositRules.Validate(
            (decimal)amount, Today, Today, null, null, isCorrection: false).Keys);

    [Fact]
    public void ADepositCannotHaveArrivedTomorrow() =>
        Assert.Contains("depositDate", EftDepositRules.Validate(
            25.60m, Today.AddDays(1), Today, null, null, isCorrection: false).Keys);

    [Fact]
    public void AZeroDepositIsAllowedBecauseANonpaymentRemittanceReportsOne() =>
        Assert.Empty(EftDepositRules.Validate(0m, Today, Today, null, null, isCorrection: false));
}
