using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Reproduces the annual-cycle defect with a wholly synthetic consumer. The annual
/// effective date identifies the document year; a renewal being prepared for the
/// next anniversary must never replace the plan that is currently in force.
/// </summary>
public sealed class AnnualCycleIdentityRegressionTests
{
    private static readonly DateTime Effective2026 = new(2026, 3, 7);
    private static readonly DateTime Effective2027 = new(2027, 3, 7);
    private static readonly DateTime September2026 = new(2026, 9, 14);

    [Fact]
    public void CurrentPcpRemainsThe2026PlanWhileThe2027RenewalIsSeparateAndOutstanding()
    {
        var person = PersonWithNoForms(Effective2026);

        Assert.True(person.EnsureCurrentCycleForms(September2026, AnnualSettings()));

        var current = person.GetCurrentCycleForm(FormType.PCP, September2026);
        Assert.NotNull(current);
        Assert.Equal(Effective2026, current!.DueDate);
        Assert.Null(current.CompletedDate);

        var upcoming = Assert.Single(person.Forms, form =>
            form.Type == FormType.PCP && form.DueDate == Effective2027);
        Assert.Null(upcoming.CompletedDate);
    }

    [Fact]
    public void Upcoming2027PcpOpensNinetyDaysBeforeItsEffectiveDate()
    {
        var person = PersonWithNoForms(Effective2026);
        var settings = AnnualSettings();
        person.EnsureCurrentCycleForms(September2026, settings);

        var openDate = Effective2027.AddDays(-90);
        var item = Assert.Single(
            new UpcomingEventService().GenerateEvents([person], settings, openDate),
            candidate => candidate.FormType == FormType.PCP);

        Assert.Equal(Effective2027, item.Date);
        Assert.Equal(openDate, item.OpenDate);
        Assert.Equal(UpcomingEventKind.OpenReview, item.Kind);
        Assert.Equal(Effective2027, item.TargetEffectiveDate);
    }

    [Fact]
    public void ExplicitMissedHistoricalCycleRemainsVisibleAfterTheLateWindowExpires()
    {
        var person = PersonWithNoForms(Effective2026.AddYears(-1));
        person.Forms.Clear();
        var historicalTarget = Effective2026.AddYears(-1);
        var missed = new Form(
            FormType.PrivacyPractices,
            historicalTarget,
            targetEffectiveDate: historicalTarget)
        {
            Id = 93
        };
        person.Forms.Add(missed);
        var settings = AnnualSettings();
        settings.PrivacyPracticesDaysAfterDue = 3;

        var item = Assert.Single(
            new UpcomingEventService().GenerateEvents([person], settings, September2026),
            candidate => candidate.FormId == missed.Id);

        Assert.Equal(UpcomingEventKind.LateReview, item.Kind);
        Assert.Equal(historicalTarget, item.TargetEffectiveDate);
    }

    [Fact]
    public void AnnualMilestonesAreCalculatedFromTheEffectiveDateTheyRenew()
    {
        var settings = AnnualSettings();

        Assert.Equal(Effective2027, FormDueDateCalculator.Compute(
            FormType.PCP, Effective2027, settings));
        Assert.Equal(Effective2027.AddDays(-90), FormDueDateCalculator.Compute(
            FormType.ComprehensiveAssessment, Effective2027, settings));
        Assert.Equal(Effective2027.AddDays(-30), FormDueDateCalculator.Compute(
            FormType.Reclassification, Effective2027, settings));
        Assert.Equal(Effective2027.AddDays(360), FormDueDateCalculator.Compute(
            FormType.Q4R, Effective2027, settings));
        Assert.Equal(Effective2027.AddDays(-120), FormDueDateCalculator.ComputeAvailableDate(
            FormType.ComprehensiveAssessment, Effective2027, settings));
        Assert.Equal(Effective2027.AddDays(-90), FormDueDateCalculator.ComputeAvailableDate(
            FormType.PCP, Effective2027, settings));
        Assert.Equal(Effective2027.AddDays(-90), FormDueDateCalculator.ComputeAvailableDate(
            FormType.Reclassification, Effective2027, settings));
    }

    private static Settings AnnualSettings() => new()
    {
        PcpOpenDaysBefore = 90,
        PcpDaysBeforeAnniversary = 0,
        CompAssessmentOpenDaysBefore = 30,
        CompAssessmentDaysBeforeAnniversary = 90,
        ReclassificationOpenDaysBefore = 60,
        ReclassificationDaysBeforeAnniversary = 30,
        SafetyPlanOpenDaysBefore = 90,
        SafetyPlanDaysBeforeAnniversary = 0,
        PrivacyPracticesOpenDaysBefore = 90,
        PrivacyPracticesDaysBeforeAnniversary = 0,
        ReleaseAgencyOpenDaysBefore = 90,
        ReleaseAgencyDaysBeforeAnniversary = 0,
        ReleaseDhhsOpenDaysBefore = 90,
        ReleaseDhhsDaysBeforeAnniversary = 0,
        ReleaseMedicalOpenDaysBefore = 90,
        ReleaseMedicalDaysBeforeAnniversary = 0,
        ReviewOpenDaysBefore = 10,
        ReviewDaysAfterDue = 30
    };

    private static Person PersonWithNoForms(DateTime effective)
    {
        var person = Person.CreatePerson(
            1,
            "Synthetic",
            "Consumer",
            "",
            new DateTime(1990, 1, 1),
            null,
            WaiverType.Section21,
            AnnualSettings());
        person.EffectiveDate = effective;
        return person;
    }
}
