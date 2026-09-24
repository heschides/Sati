using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class FormNoteAttestationRuleTests
{
    [Fact]
    public void OnlyLoggedExactNonReleaseFormActivityAttestsOnLog()
    {
        Assert.True(FormNoteAttestationRules.AttestsExactFormOnLog(
            "Logged", (int)(NoteActivity.Form | NoteActivity.Phone), "Form",
            "ComprehensiveAssessment", 42));
        Assert.False(FormNoteAttestationRules.AttestsExactFormOnLog(
            "Pending", (int)NoteActivity.Form, "Form", "ComprehensiveAssessment", 42));
        Assert.False(FormNoteAttestationRules.AttestsExactFormOnLog(
            "Logged", (int)NoteActivity.Phone, "Phone", "ComprehensiveAssessment", 42));
        Assert.False(FormNoteAttestationRules.AttestsExactFormOnLog(
            "Logged", (int)NoteActivity.Form, "Form", "ComprehensiveAssessment", null));
        Assert.False(FormNoteAttestationRules.AttestsExactFormOnLog(
            "Logged", (int)NoteActivity.Form, "Form", null, 42));
        Assert.False(FormNoteAttestationRules.AttestsExactFormOnLog(
            "Logged", (int)NoteActivity.Form, "Form", "Release_DHHS", 42));
    }

    [Fact]
    public void OlderOverlapIsAmbiguousOnlyAfterIncompleteRenewalBecomesAvailable()
    {
        var activityDate = new DateTime(2026, 9, 17);
        var olderTarget = new DateTime(2025, 12, 16);
        var renewalTarget = new DateTime(2026, 12, 16);
        var forms = new[]
        {
            new FormFact(7054, 1038, "ComprehensiveAssessment",
                new DateTime(2025, 9, 17), null, olderTarget),
            new FormFact(8807, 1038, "ComprehensiveAssessment",
                activityDate, null, renewalTarget)
        };
        var schedule = new ComplianceScheduleSettings(
            ComprehensiveAssessmentOpenDaysBefore: 30);

        var ambiguity = AnnualFormCycleDisambiguationRules.Evaluate(
            1038, "ComprehensiveAssessment", 7054, activityDate, forms, schedule);

        Assert.NotNull(ambiguity);
        Assert.Equal(7054, ambiguity.SelectedFormId);
        Assert.Equal(8807, ambiguity.RenewalFormId);
        Assert.Contains("December 16, 2025", ambiguity.Reason, StringComparison.Ordinal);
        Assert.Contains("December 16, 2026", ambiguity.Reason, StringComparison.Ordinal);
        Assert.Null(AnnualFormCycleDisambiguationRules.Evaluate(
            1038, "ComprehensiveAssessment", 8807, activityDate, forms, schedule));
        Assert.Null(AnnualFormCycleDisambiguationRules.Evaluate(
            1038, "ComprehensiveAssessment", 7054,
            activityDate.AddDays(-31), forms, schedule));
    }
}
