using Sati.Contracts.V1;
using Sati.Models.Assessments;
using Xunit;

namespace Sati.Api.Tests;

public sealed class AssessmentCompletenessRulesTests
{
    [Fact]
    public void FrozenReviewAndPdfPromptsResolveEveryTemplatePlaceholder()
    {
        Assert.All(AssessmentCatalog.Questions, q => Assert.DoesNotContain("{", AssessmentCatalog.DisplayPrompt(q.Prompt, "Synthetic person")));
    }
    [Fact] public void ReasonedDispositionsAndExplicitNoNeedsExplanationCanBeComplete() =>
        Assert.Empty(AssessmentReviewRules.Validate(AssessmentReviewApiTests.CompleteDocument()));

    [Theory]
    [InlineData("missing-question")]
    [InlineData("follow-up")]
    [InlineData("invalid-disposition")]
    [InlineData("blank-disposition-reason")]
    [InlineData("support-conflict")]
    [InlineData("varies-alone")]
    [InlineData("invalid-therapy-option")]
    [InlineData("no-contributors")]
    [InlineData("blank-contributor")]
    [InlineData("duplicate-contributor")]
    [InlineData("unexplained-no-needs")]
    [InlineData("incomplete-need")]
    [InlineData("provider-without-identity")]
    [InlineData("unidentified-dissent")]
    [InlineData("unknown-question")]
    public void InvalidContentCannotClaimCompleteness(string defect)
    {
        var d = AssessmentReviewApiTests.CompleteDocument(); var a = d.Answers["community-access"];
        switch (defect)
        {
            case "missing-question": d.Answers.Remove("self-view"); break;
            case "follow-up": a.Status = AssessmentAnswerStatus.FollowUpRequired; break;
            case "invalid-disposition": a.Status = (AssessmentAnswerStatus)999; break;
            case "blank-disposition-reason": a.ExceptionReason = " "; break;
            case "support-conflict": a.Status = AssessmentAnswerStatus.Answered; a.Narrative = "A synthetic answer"; a.Supports = SupportMethod.NoSupportCurrentlyNeeded | SupportMethod.HandsOnAssistance; break;
            case "varies-alone": a.Status = AssessmentAnswerStatus.Answered; a.Narrative = "A synthetic answer"; a.Supports = SupportMethod.Varies; a.SupportDetails = "Different situations"; break;
            case "invalid-therapy-option": a.TherapySessionFormat = (TherapySessionFormat)999; break;
            case "no-contributors": d.Contributors.Clear(); break;
            case "blank-contributor": d.Contributors[0].Relationship = ""; break;
            case "duplicate-contributor": d.Contributors.Add(d.Contributors[0]); break;
            case "unexplained-no-needs": d.NoIdentifiedNeedsReason = ""; break;
            case "incomplete-need": d.NoIdentifiedNeedsReason = ""; d.Needs.Add(new()); break;
            case "provider-without-identity": d.NoIdentifiedNeedsReason = ""; d.Needs.Add(new() { Description = "Synthetic need", DesiredResult = "Synthetic result", AssociateProvider = true, ProviderNameSnapshot = "Unverified provider" }); break;
            case "unidentified-dissent": a.DissentingOpinion = "A differing synthetic view"; break;
            case "unknown-question": d.Answers["unrecognized"] = new(); break;
        }
        Assert.NotEmpty(AssessmentReviewRules.Validate(d));
    }
    [Fact]
    public void CompleteActivityAndTherapyAnswersRequireTheirActualFields()
    {
        var doc = AssessmentReviewApiTests.CompleteDocument();
        var home = doc.Answers["home-support-levels"] = new() { Status = AssessmentAnswerStatus.Answered };
        Assert.NotEmpty(AssessmentReviewRules.Validate(doc));
        home.ActivitySupportLevels = AssessmentCatalog.Questions.Single(q => q.Key == "home-support-levels").Activities.ToDictionary(n => n, _ => ActivitySupportLevel.Independent);
        var therapy = doc.Answers["therapy"] = new() { Status = AssessmentAnswerStatus.Answered, YesNoResponse = true, FollowUpYesNoResponse = true,
            TherapySessionFormat = TherapySessionFormat.Telehealth, WantsOtherSessionFormat = false, WantsFrequencyChange = true };
        Assert.NotEmpty(AssessmentReviewRules.Validate(doc)); therapy.TherapyFrequencyDirection = TherapyFrequencyDirection.Increase;
        Assert.Empty(AssessmentReviewRules.Validate(doc));
        home.ActivitySupportLevels["not-a-real-activity"] = ActivitySupportLevel.TotalCare; Assert.NotEmpty(AssessmentReviewRules.Validate(doc));
    }
    [Fact]
    public void ANewProviderCannotBeForgedAndAnUnchangedHistoricalSnapshotIsPreserved()
    {
        var doc = AssessmentReviewApiTests.CompleteDocument(); doc.NoIdentifiedNeedsReason = "";
        doc.Needs.Add(new() { Description = "Synthetic need", DesiredResult = "Synthetic result", AssociateProvider = true, ProviderId = 19, ProviderNameSnapshot = "Recorded provider" });
        Assert.NotEmpty(AssessmentReviewRules.ValidateProviders(doc, new(), new Dictionary<int, ProviderAffiliation.ProviderSnapshot>()));
        var prior = AssessmentReviewRules.Parse(System.Text.Json.JsonSerializer.Serialize(doc, AssessmentReviewRules.JsonOptions));
        Assert.Empty(AssessmentReviewRules.ValidateProviders(doc, prior, new Dictionary<int, ProviderAffiliation.ProviderSnapshot>()));
        doc.Needs[0].ProviderNameSnapshot = "Altered historical name";
        Assert.NotEmpty(AssessmentReviewRules.ValidateProviders(doc, prior, new Dictionary<int, ProviderAffiliation.ProviderSnapshot>()));
    }
    [Fact]
    public void IndependentReviewerPermissionIsSeparateFromCaseManagementAndAgencyAdministration()
    {
        var owner = new CaseloadParticipant(12, 1, UserPermissions.CaseManagement, 13);
        Assert.True(AssessmentReviewRules.CanReview(new(13, 1, UserPermissions.Supervision), owner, 12));
        Assert.False(AssessmentReviewRules.CanReview(new(12, 1, UserPermissions.CaseManagement | UserPermissions.Supervision | UserPermissions.Administration), owner, 12));
        Assert.False(AssessmentReviewRules.CanReview(new(13, 1, UserPermissions.Administration), owner, 12));
        Assert.False(AssessmentReviewRules.CanReview(new(23, 2, UserPermissions.AllAgencyPermissions), owner, 12));
    }
}
