using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class FormAttestationImpactRulesTests
{
    private static readonly DateTime Due = new(2026, 9, 3);

    [Fact]
    public void RevocationOfLoggedNoteHoldsBillingAndCallsForSupervisorAttention()
    {
        var impact = Evaluate(NoteWorkflow.Logged, false, Due, Due, null);

        Assert.True(impact.AttestationDateChanged);
        Assert.True(impact.RequiresSupervisorAttention);
        Assert.False(impact.RequiresBillingAttention);
        Assert.True(impact.MustHoldBilling);
        Assert.Equal(FormAttestationBillingHoldReason.MissingAttestation,
            impact.BillingHoldReasons);
    }

    [Fact]
    public void LateCorrectedDateFlagsBothQueuesWhenNoteReachedBilling()
    {
        var impact = Evaluate(NoteWorkflow.Approved, true, Due, Due, Due.AddDays(1));

        Assert.True(impact.RequiresSupervisorAttention);
        Assert.True(impact.RequiresBillingAttention);
        Assert.Equal(
            FormAttestationBillingHoldReason.ActivityDateMismatch |
            FormAttestationBillingHoldReason.CompletedAfterDueDate,
            impact.BillingHoldReasons);
    }

    [Fact]
    public void ApprovedUnbilledNoteAlsoNeedsBillingAttentionOnCorrection()
    {
        var revisedDate = Due.AddDays(-1);
        var impact = Evaluate(NoteWorkflow.Approved, false, revisedDate, Due, revisedDate);

        Assert.True(impact.RequiresSupervisorAttention);
        Assert.True(impact.RequiresBillingAttention);
        Assert.False(impact.MustHoldBilling);
    }

    [Fact]
    public void CorrectedOnTimeDateCanRemainBillableButStillNeedsSupervisorReview()
    {
        var revisedDate = Due.AddDays(-1);
        var impact = Evaluate(NoteWorkflow.Logged, false, revisedDate, Due, revisedDate);

        Assert.True(impact.AttestationDateChanged);
        Assert.True(impact.RequiresSupervisorAttention);
        Assert.False(impact.RequiresBillingAttention);
        Assert.False(impact.MustHoldBilling);
    }

    [Fact]
    public void DraftNoteDoesNotNotifyReviewQueuesOnDateChange()
    {
        var impact = Evaluate(NoteWorkflow.Pending, false, Due, Due, Due.AddDays(1));

        Assert.False(impact.RequiresSupervisorAttention);
        Assert.False(impact.RequiresBillingAttention);
        Assert.True(impact.MustHoldBilling);
    }

    [Fact]
    public void ClaimRecordCallsForBillingAttentionEvenIfNoteStatusIsUnexpected()
    {
        var impact = Evaluate(NoteWorkflow.Returned, true, Due, Due, Due.AddDays(1));

        Assert.True(impact.RequiresSupervisorAttention);
        Assert.True(impact.RequiresBillingAttention);
    }

    [Fact]
    public void SameCalendarDateDoesNotCreateAChangeAlert()
    {
        var impact = Evaluate(NoteWorkflow.Approved, true,
            Due.AddHours(8), Due.AddHours(9), Due.AddHours(11));

        Assert.False(impact.AttestationDateChanged);
        Assert.False(impact.RequiresSupervisorAttention);
        Assert.False(impact.RequiresBillingAttention);
        Assert.False(impact.MustHoldBilling);
    }

    [Fact]
    public void MissingNoteActivityDateHoldsBilling()
    {
        var impact = Evaluate(NoteWorkflow.Logged, false, null, Due, Due);

        Assert.False(impact.AttestationDateChanged);
        Assert.True(impact.MustHoldBilling);
        Assert.Equal(FormAttestationBillingHoldReason.MissingActivityDate,
            impact.BillingHoldReasons);
    }

    private static FormAttestationImpact Evaluate(
        int status,
        bool hasReachedBilling,
        DateTime? noteActivityDate,
        DateTime? previousAttestationDate,
        DateTime? revisedAttestationDate) =>
        FormAttestationImpactRules.Evaluate(
            status, hasReachedBilling, noteActivityDate,
            previousAttestationDate, revisedAttestationDate, Due);
}
