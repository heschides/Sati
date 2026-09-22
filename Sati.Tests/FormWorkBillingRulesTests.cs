using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class FormWorkBillingRulesTests
{
    private static readonly DateTime Due = new(2026, 9, 3);

    [Theory]
    [InlineData("Q1R")]
    [InlineData("Q2R")]
    [InlineData("Q3R")]
    [InlineData("Q4R")]
    [InlineData("ComprehensiveAssessment")]
    [InlineData("PCP")]
    [InlineData("Reclassification")]
    [InlineData("SafetyPlan")]
    [InlineData("PrivacyPractices")]
    public void LateCompletionBlocksTheExactFormWorkNote(string formType)
    {
        var completed = Due.AddDays(1);
        var note = new FormWorkNoteFact(7, formType, completed, 17);
        var form = new FormWorkObligationFact(17, 7, formType, Due, completed);

        var result = FormWorkBillingRules.Evaluate(note, form);

        Assert.False(result.Passed);
        Assert.Contains(result.Reasons, reason => reason.Contains("after", StringComparison.Ordinal));
        // The ordinary historical window ends on the completion date, which
        // explains why the existing billing gate accepts this late form note.
        Assert.False(BillingComplianceGate.IsWithinBlockedInterval(Due, completed, completed));
    }

    [Fact]
    public void LaterNoteDateDoesNotRestoreBillabilityOfLateReview()
    {
        var form = new FormWorkObligationFact(17, 7, "Q3R", Due, Due.AddDays(1));
        var note = new FormWorkNoteFact(7, "Q3R", Due.AddDays(11), 17);

        var result = FormWorkBillingRules.Evaluate(note, form);

        Assert.False(result.Passed);
        Assert.Equal(2, result.Reasons.Count);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void OnTimeCompletionWithMatchingActivityDatePasses(int offset)
    {
        var completed = Due.AddDays(offset);
        var form = new FormWorkObligationFact(17, 7, "Q3R", Due, completed);

        var result = FormWorkBillingRules.Evaluate(
            new FormWorkNoteFact(7, "Q3R", completed, 17), form);

        Assert.Equal(offset <= 0, result.Passed);
    }

    [Fact]
    public void RevocationBlocksPreviouslyLinkedNote()
    {
        var result = FormWorkBillingRules.Evaluate(
            new FormWorkNoteFact(7, "Q3R", Due, 17),
            new FormWorkObligationFact(17, 7, "Q3R", Due, null));

        Assert.False(result.Passed);
        Assert.Contains(result.Reasons, reason => reason.Contains("no current completion", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, 17, 7, "Q3R")]
    [InlineData(17, 18, 7, "Q3R")]
    [InlineData(17, 17, 8, "Q3R")]
    [InlineData(17, 17, 7, "Q2R")]
    public void MissingOrMismatchedExactObligationBlocks(
        int? linkedId, int formId, int personId, string formType)
    {
        var result = FormWorkBillingRules.Evaluate(
            new FormWorkNoteFact(7, "Q3R", Due, linkedId),
            new FormWorkObligationFact(formId, personId, formType, Due, Due));

        Assert.False(result.Passed);
    }

    [Fact]
    public void IncorrectActivityDateBlocksEvenWhenFormWasOnTime()
    {
        var result = FormWorkBillingRules.Evaluate(
            new FormWorkNoteFact(7, "Q3R", Due.AddDays(1), 17),
            new FormWorkObligationFact(17, 7, "Q3R", Due, Due));

        Assert.False(result.Passed);
        Assert.Contains(result.Reasons, reason => reason.Contains("does not match", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Release_Agency")]
    [InlineData("Release_DHHS")]
    [InlineData("Release_Medical")]
    public void ReleaseNotesRemainOutsideThisSpecificRule(string formType)
    {
        var result = FormWorkBillingRules.Evaluate(
            new FormWorkNoteFact(7, formType, Due.AddDays(1), null), null);

        Assert.True(result.Passed);
    }
}
