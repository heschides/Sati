using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class PlanYearOverviewTests
{
    private static readonly DateTime Effective = new(2024, 8, 11);
    private static readonly DateTime Today = new(2026, 9, 26);
    private static readonly ComplianceScheduleSettings Schedule = new();

    [Fact]
    public void PlanYearsRunFromAdmissionThroughTheNextRenewalAndDefaultToTheOneInForce()
    {
        var starts = PlanYearOverview.Starts(Effective, Today);

        Assert.Equal([new(2024, 8, 11), new(2025, 8, 11), new(2026, 8, 11), new DateTime(2027, 8, 11)], starts);
        Assert.Equal(new DateTime(2026, 8, 11), PlanYearOverview.InForceStart(Effective, Today));
        Assert.Equal("2026–27", PlanYearOverview.Label(new DateTime(2026, 8, 11)));
        Assert.Equal(new DateTime(2027, 8, 10), PlanYearOverview.EndInclusive(Effective, new DateTime(2026, 8, 11)));
    }

    [Fact]
    public void BeforeAdmissionTheFirstYearIsShownAndNoEarlierYearIsInvented()
    {
        var starts = PlanYearOverview.Starts(Effective, new DateTime(2024, 7, 1));

        Assert.Equal(Effective, starts[0]);
        Assert.Equal(Effective, PlanYearOverview.InForceStart(Effective, new DateTime(2024, 7, 1)));
        var year = PlanYearOverview.Build(Effective, Effective, new DateTime(2024, 7, 1), [], [], Schedule);
        Assert.Equal(PlanYearPosition.InForce, year.Position);
    }

    [Fact]
    public void AFebruary29AdmissionKeepsItsLeapDayAnniversary()
    {
        var leap = new DateTime(2024, 2, 29);
        var starts = PlanYearOverview.Starts(leap, new DateTime(2028, 3, 1));

        Assert.Equal(new DateTime(2025, 2, 28), starts[1]);
        Assert.Equal(new DateTime(2028, 2, 29), starts[4]);
        Assert.Equal(new DateTime(2025, 2, 27), PlanYearOverview.EndInclusive(leap, new DateTime(2024, 2, 29)));
    }

    [Fact]
    public void ItemsBelongToTheYearTheyServeNotTheYearTheirDueDateFallsIn()
    {
        var target = new DateTime(2026, 8, 11);
        var year = PlanYearOverview.Build(Effective, target, Today, [], [], Schedule);

        var assessment = Assert.Single(year.Items, item => item.Title == "Comprehensive Assessment");
        Assert.True(assessment.DueOn < target, "The assessment is prepared before its year begins.");
        var review = Assert.Single(year.Items, item => item.Title == "90-day review 1");
        Assert.True(review.DueOn > target, "Reviews fall inside the year.");
        Assert.Equal(PlanYearGroup.Plan, assessment.Group);
        Assert.Equal(PlanYearGroup.CheckIns, review.Group);
        Assert.Equal(PlanYearWorkspace.ClientOverview, review.Workspace);
    }

    [Fact]
    public void MissingRowsAppearAsNotDoneInsteadOfDisappearing()
    {
        // No stored rows at all: every expected form still shows, none of them done.
        var year = PlanYearOverview.Build(Effective, new DateTime(2025, 8, 11), Today, [], [], Schedule);

        Assert.Equal(PersonSaveRules.FormTypes.Count(type => !type.StartsWith("Release_", StringComparison.Ordinal)),
            year.Items.Count(item => item.Group != PlanYearGroup.Releases));
        Assert.DoesNotContain(year.Items, item => item.State == PlanYearItemState.Done);
        Assert.All(year.Items.Where(item => item.DueOn < Today), item => Assert.Equal(PlanYearItemState.Overdue, item.State));
    }

    [Fact]
    public void StatesFollowTheDatesAndAreStatedInWords()
    {
        var target = new DateTime(2026, 8, 11);
        var q1Due = ComplianceScheduleRules.DueDate("Q1R", target, Schedule);
        var q1Opens = ComplianceScheduleRules.AvailableOn("Q1R", q1Due, Schedule);
        var pcpDue = ComplianceScheduleRules.DueDate("PCP", target, Schedule);
        var forms = new[]
        {
            Form("PCP", target, completed: pcpDue.AddDays(-3)),
            Form("Q1R", target),
            Form("Q2R", target)
        };

        var beforeQ1Opens = PlanYearOverview.Build(Effective, target, q1Opens.AddDays(-1), forms, [], Schedule);
        var q1 = Assert.Single(beforeQ1Opens.Items, item => item.Title == "90-day review 1");
        Assert.Equal(PlanYearItemState.ComingUp, q1.State);
        Assert.StartsWith("Opens ", q1.WhenText);
        Assert.Equal("", q1.ActionLabel);

        var q1Open = Assert.Single(PlanYearOverview.Build(Effective, target, q1Due, forms, [], Schedule).Items,
            item => item.Title == "90-day review 1");
        Assert.Equal(PlanYearItemState.Open, q1Open.State);
        Assert.Equal("Open", q1Open.StateLabel);
        Assert.StartsWith("Due today", q1Open.WhenText);

        var q1Late = Assert.Single(PlanYearOverview.Build(Effective, target, q1Due.AddDays(1), forms, [], Schedule).Items,
            item => item.Title == "90-day review 1");
        Assert.Equal(PlanYearItemState.Overdue, q1Late.State);
        Assert.StartsWith("Overdue since", q1Late.WhenText);

        var pcp = Assert.Single(beforeQ1Opens.Items, item => item.Title == "Person-Centered Plan");
        Assert.Equal(PlanYearItemState.Done, pcp.State);
        Assert.Equal("Done", pcp.StateLabel);
        Assert.Equal("View", pcp.ActionLabel);
    }

    [Fact]
    public void AReleaseIsDoneOnlyWhenItsObligationIsAttestedNotWhenAPdfExists()
    {
        var target = new DateTime(2026, 8, 11);
        var signed = Release(target, ReleaseObligationCategory.Dhhs, attestedOn: new DateTime(2026, 8, 11));
        var drafted = Release(target, ReleaseObligationCategory.Agency, recipient: "Wakanda Outreach and Mobility");
        var documents = new PlanYearDocumentFacts(
            [Artifact(1, AnnualDocumentKind.ReleaseDhhs, DocumentArtifactOrigin.Draft, target),
             Artifact(2, AnnualDocumentKind.ReleaseAgency, DocumentArtifactOrigin.GeneratedInSati, target)],
            [],
            AuthorizedRepresentativeOnFile: true);

        var year = PlanYearOverview.Build(Effective, target, Today, [], [signed, drafted], Schedule, documents);

        var dhhs = Assert.Single(year.Items, item => item.Title == "DHHS release");
        Assert.Equal(PlanYearItemState.Done, dhhs.State);
        Assert.Equal("Signed", dhhs.StateLabel);
        var agency = Assert.Single(year.Items, item => item.Title == "Agency release · Wakanda Outreach and Mobility");
        Assert.NotEqual(PlanYearItemState.Done, agency.State);
        Assert.Equal(PlanYearWorkspace.Releases, agency.Workspace);
        Assert.Equal(drafted.ObligationId, agency.ReleaseObligationId);
    }

    [Fact]
    public void RecipientSpecificReleasesReplaceTheOldFixedReleaseRows()
    {
        var target = new DateTime(2026, 8, 11);
        var forms = new[] { Form("Release_Agency", target), Form("Release_DHHS", target), Form("Release_Medical", target) };

        var withObligations = PlanYearOverview.Build(Effective, target, Today, forms,
            [Release(target, ReleaseObligationCategory.Dhhs)], Schedule);
        Assert.Single(withObligations.Items, item => item.Group == PlanYearGroup.Releases);

        var legacyOnly = PlanYearOverview.Build(new DateTime(2020, 8, 11), new DateTime(2020, 8, 11), new DateTime(2020, 9, 1),
            [Form("Release_Agency", new DateTime(2020, 8, 11))], [], Schedule);
        // The universal DHHS obligation is always expected, so it too suppresses the legacy row.
        Assert.DoesNotContain(legacyOnly.Items, item => item.Title == "Agency release");
        Assert.Contains(legacyOnly.Items, item => item.Title == "DHHS release");
    }

    [Fact]
    public void TheOneTimeAuthorizedRepresentativeShowsOnlyInTheYearInForceUntilItIsOnFile()
    {
        var notOnFile = new PlanYearDocumentFacts([], [], AuthorizedRepresentativeOnFile: false);

        var current = PlanYearOverview.Build(Effective, new DateTime(2026, 8, 11), Today, [], [], Schedule, notOnFile);
        var once = Assert.Single(current.Items, item => item.Group == PlanYearGroup.OneTime);
        Assert.True(once.NeedsYou);
        Assert.Null(once.DueOn);
        Assert.Equal(PlanYearWorkspace.DhhsDocuments, once.Workspace);

        var past = PlanYearOverview.Build(Effective, new DateTime(2025, 8, 11), Today, [], [], Schedule, notOnFile);
        Assert.DoesNotContain(past.Items, item => item.Group == PlanYearGroup.OneTime);

        var onFile = PlanYearOverview.Build(Effective, new DateTime(2026, 8, 11), Today, [], [], Schedule,
            notOnFile with { AuthorizedRepresentativeOnFile = true });
        Assert.DoesNotContain(onFile.Items, item => item.Group == PlanYearGroup.OneTime);
    }

    [Fact]
    public void PrivacyPracticesDetailFollowsTheNoticeAndItsReceipt()
    {
        var target = new DateTime(2026, 8, 11);
        var notice = Artifact(7, AnnualDocumentKind.PrivacyPractices, DocumentArtifactOrigin.GeneratedInSati, target);

        var generated = PlanYearOverview.Build(Effective, target, Today, [], [], Schedule,
            new PlanYearDocumentFacts([notice], [], true));
        var item = Assert.Single(generated.Items, x => x.Title == "Privacy Practices notice");
        Assert.StartsWith("Notice generated", item.Detail);
        Assert.Equal("Record receipt", item.ActionLabel);

        var received = PlanYearOverview.Build(Effective, target, Today, [], [], Schedule,
            new PlanYearDocumentFacts([notice], [7], true));
        Assert.Equal("Receipt recorded", Assert.Single(received.Items, x => x.Title == "Privacy Practices notice").Detail);
    }

    [Fact]
    public void GeneratedArtifactLabelsUseMaineDateAtUtcMidnight()
    {
        var target = new DateTime(2026, 8, 11);
        var generatedAt = new DateTime(2026, 7, 1, 0, 30, 0, DateTimeKind.Utc);
        var notice = Artifact(7, AnnualDocumentKind.PrivacyPractices,
            DocumentArtifactOrigin.GeneratedInSati, target) with { GeneratedAtUtc = generatedAt };
        var safetyPlan = Artifact(8, AnnualDocumentKind.SafetyPlan,
            DocumentArtifactOrigin.Draft, target) with { GeneratedAtUtc = generatedAt };
        var year = PlanYearOverview.Build(Effective, target, Today, [], [], Schedule,
            new PlanYearDocumentFacts([notice, safetyPlan], [], true));

        Assert.Contains("Jun 30, 2026", Assert.Single(year.Items,
            item => item.Title == "Privacy Practices notice").Detail);
        Assert.Contains("Jun 30, 2026", Assert.Single(year.Items,
            item => item.Title == "Safety Plan").Detail);
    }

    [Fact]
    public void PositionsAndCountsDescribeTheYear()
    {
        var past = PlanYearOverview.Build(Effective, new DateTime(2025, 8, 11), Today, [], [], Schedule);
        var upcoming = PlanYearOverview.Build(Effective, new DateTime(2027, 8, 11), Today, [], [], Schedule);

        Assert.Equal(PlanYearPosition.Past, past.Position);
        Assert.Equal(PlanYearPosition.Upcoming, upcoming.Position);
        Assert.Equal(past.Items.Count, past.NeedsYouCount + past.ComingUpCount + past.DoneCount);
        Assert.True(upcoming.WorkOpensOn > Today);
        Assert.Equal(0, upcoming.NeedsYouCount);
    }

    private static ComplianceFormSnapshot Form(string type, DateTime target, DateTime? completed = null) =>
        new(type, ComplianceScheduleRules.DueDate(type, target, Schedule), completed,
            ObligationId: $"form:{type}:{target:yyyyMMdd}", TargetEffectiveDate: target);

    private static ReleaseComplianceFact Release(
        DateTime target, ReleaseObligationCategory category, DateTime? attestedOn = null, string? recipient = null)
    {
        var key = category == ReleaseObligationCategory.Dhhs
            ? $"release:v1:{target:yyyy-MM-dd}:dhhs:annual"
            : $"release:v1:{target:yyyy-MM-dd}:{category.ToString().ToLowerInvariant()}:annual:link-1";
        return new ReleaseComplianceFact(
            key, category, target, target.AddDays(-90), null,
            attestedOn is DateTime on ? [new ReleaseAttestationFact(key, on, DateTime.UtcNow)] : [],
            Guid.NewGuid(), target, target.AddDays(-90), recipient);
    }

    private static DocumentArtifactDto Artifact(int id, AnnualDocumentKind kind, DocumentArtifactOrigin origin, DateTime cycle) =>
        new(id, 1, 1, kind.ToString(), cycle, origin.ToString(), new DateTime(2026, 9, 20, 13, 0, 0, DateTimeKind.Utc),
            1, null, null, null, [], null);
}
