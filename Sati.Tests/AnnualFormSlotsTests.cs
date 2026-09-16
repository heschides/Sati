using Sati.Contracts.V1;
using Sati.Models;
using Sati.ViewModels;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The client profile shows the annual obligation in force and, while it is being
/// prepared, the renewal beside it. Dates follow the agency workbook: for a plan
/// starting October 15, 2026 the assessment is started by June 17 and due July 17,
/// the PCP opens July 17, and Reclass is due September 15.
/// </summary>
public sealed class AnnualFormSlotsTests
{
    private static readonly DateTime CurrentTarget = new(2025, 10, 15);
    private static readonly DateTime UpcomingTarget = new(2026, 10, 15);
    private static readonly ComplianceScheduleSettings Schedule = new();

    [Fact]
    public void AnOverdueRenewalAssessmentIsVisibleWhileTheCurrentOneIsComplete()
    {
        // The defect: the single checkbox showed the assessment for the plan in force,
        // which was complete, while next year's assessment was overdue and blocking billing.
        var person = PersonWith(
            new Form(FormType.ComprehensiveAssessment, CurrentTarget.AddDays(-90),
                completedOn: new DateTime(2025, 7, 1), targetEffectiveDate: CurrentTarget),
            new Form(FormType.ComprehensiveAssessment, UpcomingTarget.AddDays(-90),
                targetEffectiveDate: UpcomingTarget));
        var today = new DateTime(2026, 8, 1);

        var (current, renewal) = AnnualFormSlots.Resolve(
            person, FormType.ComprehensiveAssessment, today, Schedule);

        Assert.NotNull(current);
        Assert.True(current.IsComplete);
        Assert.NotNull(renewal);
        Assert.Equal(UpcomingTarget, renewal.TargetEffectiveDate);
        Assert.False(renewal.IsComplete);
        Assert.True(renewal.IsOverdue);
        Assert.True(renewal.NeedsAttention);
        Assert.StartsWith("OVERDUE", renewal.StatusText);

        // The profile and the gate now agree that something is wrong.
        Assert.Contains(
            "Comprehensive Assessment was due Jul 17, 2026 and is incomplete.",
            person.EvaluateComplianceGate(today).Reasons);
    }

    [Fact]
    public void TheRenewalAppearsOnTheFirstDayItIsAvailable()
    {
        var person = PersonWith(
            new Form(FormType.PCP, CurrentTarget, completedOn: CurrentTarget,
                targetEffectiveDate: CurrentTarget),
            new Form(FormType.PCP, UpcomingTarget, targetEffectiveDate: UpcomingTarget));

        Assert.Null(AnnualFormSlots.Resolve(
            person, FormType.PCP, new DateTime(2026, 7, 16), Schedule).Renewal);

        var renewal = AnnualFormSlots.Resolve(
            person, FormType.PCP, new DateTime(2026, 7, 17), Schedule).Renewal;
        Assert.NotNull(renewal);
        Assert.Equal("Renewal for 10/15/26", renewal.CheckBoxLabel);
        Assert.Contains("renewal for the plan starting 10/15/26", renewal.AutomationName);
    }

    [Fact]
    public void AnAlreadyOpenedRenewalIsVisibleBeforeItsWindow()
    {
        // An opening recorded under an earlier availability setting stays visible.
        var upcoming = new Form(FormType.PCP, UpcomingTarget, targetEffectiveDate: UpcomingTarget)
        {
            OpenedDate = new DateTime(2026, 7, 1)
        };
        var person = PersonWith(
            new Form(FormType.PCP, CurrentTarget, completedOn: CurrentTarget,
                targetEffectiveDate: CurrentTarget),
            upcoming);

        var renewal = AnnualFormSlots.Resolve(
            person, FormType.PCP, new DateTime(2026, 7, 5), Schedule).Renewal;

        Assert.NotNull(renewal);
        Assert.Same(upcoming, renewal.Form);
        Assert.Contains("Opened 07/01/26", renewal.StatusText);
    }

    [Fact]
    public void AnEarlyCompletionKeepsBothSlotsUntilTheTargetDate()
    {
        var person = PersonWith(
            new Form(FormType.PCP, CurrentTarget, completedOn: CurrentTarget,
                targetEffectiveDate: CurrentTarget),
            new Form(FormType.PCP, UpcomingTarget, completedOn: new DateTime(2026, 9, 1),
                targetEffectiveDate: UpcomingTarget));

        var (current, renewal) = AnnualFormSlots.Resolve(
            person, FormType.PCP, new DateTime(2026, 10, 14), Schedule);

        Assert.Equal(CurrentTarget, current!.TargetEffectiveDate);
        Assert.NotNull(renewal);
        Assert.True(renewal.IsComplete);
        Assert.Equal("Completed 09/01/26", renewal.StatusText);
    }

    [Fact]
    public void OnTheTargetDateTheRenewalBecomesTheOnlySlotWhetherOrNotItIsFinished()
    {
        var upcoming = new Form(FormType.PCP, UpcomingTarget, targetEffectiveDate: UpcomingTarget);
        var person = PersonWith(
            new Form(FormType.PCP, CurrentTarget, completedOn: CurrentTarget,
                targetEffectiveDate: CurrentTarget),
            upcoming);

        var (onTarget, noRenewal) = AnnualFormSlots.Resolve(
            person, FormType.PCP, UpcomingTarget, Schedule);
        Assert.Same(upcoming, onTarget!.Form);
        Assert.Equal(AnnualFormSlotRole.Current, onTarget.Role);
        Assert.False(onTarget.IsOverdue);
        Assert.Null(noRenewal);

        var dayAfter = AnnualFormSlots.Resolve(
            person, FormType.PCP, UpcomingTarget.AddDays(1), Schedule).Current;
        Assert.Same(upcoming, dayAfter!.Form);
        Assert.True(dayAfter.IsOverdue);
        Assert.False(dayAfter.IsComplete);
    }

    [Fact]
    public void AMissingRenewalRowIsReportedInsteadOfBorrowed()
    {
        var person = PersonWith(
            new Form(FormType.Reclassification, CurrentTarget.AddDays(-30),
                completedOn: new DateTime(2025, 9, 1), targetEffectiveDate: CurrentTarget));

        var renewal = AnnualFormSlots.Resolve(
            person, FormType.Reclassification, new DateTime(2026, 8, 1), Schedule).Renewal;

        Assert.NotNull(renewal);
        Assert.True(renewal.IsMissing);
        Assert.True(renewal.NeedsAttention);
        Assert.False(renewal.IsComplete);
        Assert.Equal("No record exists for this plan", renewal.StatusText);
    }

    [Fact]
    public void ALateAssessmentStartIsWordedAndAnOnTimeOneIsNot()
    {
        var person = PersonWith(
            new Form(FormType.ComprehensiveAssessment, CurrentTarget.AddDays(-90),
                completedOn: new DateTime(2025, 7, 1), targetEffectiveDate: CurrentTarget),
            new Form(FormType.ComprehensiveAssessment, UpcomingTarget.AddDays(-90),
                targetEffectiveDate: UpcomingTarget));

        var onTime = AnnualFormSlots.Resolve(
            person, FormType.ComprehensiveAssessment, new DateTime(2026, 6, 17), Schedule).Renewal;
        Assert.NotNull(onTime);
        Assert.False(onTime.IsOpeningLate);
        Assert.Equal("Due 07/17/26 · Start by 06/17/26", onTime.StatusText);

        var late = AnnualFormSlots.Resolve(
            person, FormType.ComprehensiveAssessment, new DateTime(2026, 6, 18), Schedule).Renewal;
        Assert.NotNull(late);
        Assert.True(late.IsOpeningLate);
        Assert.True(late.NeedsAttention);
        Assert.Equal(
            "Due 07/17/26 · LATE, not started; was due to start by 06/17/26",
            late.StatusText);
    }

    [Theory]
    [InlineData(FormType.Q1R)]
    [InlineData(FormType.Q4R)]
    [InlineData(FormType.Release_Medical)]
    public void ReviewsAndReleasesNeverShowARenewal(FormType type)
    {
        var person = PersonWith();

        var (current, renewal) = AnnualFormSlots.Resolve(
            person, type, UpcomingTarget.AddDays(-1), Schedule);

        Assert.NotNull(current);
        Assert.Null(renewal);
    }

    [Fact]
    public void BeforeAdmissionOnlyTheFirstPlanIsShown()
    {
        var first = new DateTime(2026, 10, 15);
        var person = Person.CreatePerson(
            31, "Not", "Started", string.Empty, new DateTime(1990, 1, 1),
            first, WaiverType.None, new Settings());
        person.Forms.Clear();
        var pcp = new Form(FormType.PCP, first, targetEffectiveDate: first);
        person.Forms.Add(pcp);

        var (current, renewal) = AnnualFormSlots.Resolve(
            person, FormType.PCP, new DateTime(2026, 7, 1), Schedule);

        Assert.Same(pcp, current!.Form);
        Assert.Null(renewal);
        Assert.Equal("Due 10/15/26 · Open by 07/17/26", current.StatusText);

        // The pre-service plan has the same opening deadline as any other.
        var late = AnnualFormSlots.Resolve(
            person, FormType.PCP, new DateTime(2026, 8, 1), Schedule).Current;
        Assert.True(late!.IsOpeningLate);
    }

    [Fact]
    public void AFebruaryTwentyNinthStartFindsItsLeapYearRenewal()
    {
        var first = new DateTime(2024, 2, 29);
        var today = new DateTime(2027, 12, 1);
        Assert.Equal(new DateTime(2027, 2, 28),
            ComplianceScheduleRules.CurrentTargetEffectiveDate(first, today));
        Assert.Equal(new DateTime(2028, 2, 29),
            ComplianceScheduleRules.UpcomingTargetEffectiveDate(first, today));
        Assert.Contains(new DateTime(2028, 2, 29),
            ComplianceScheduleRules.TargetEffectiveDatesThroughNext(first, today));

        var person = Person.CreatePerson(
            31, "Leap", "Year", string.Empty, new DateTime(1990, 1, 1),
            first, WaiverType.None, new Settings());
        person.Forms.Clear();
        var renewalPcp = new Form(FormType.PCP, new DateTime(2028, 2, 29),
            targetEffectiveDate: new DateTime(2028, 2, 29));
        person.Forms.Add(renewalPcp);

        var renewal = AnnualFormSlots.Resolve(person, FormType.PCP, today, Schedule).Renewal;

        Assert.NotNull(renewal);
        Assert.Same(renewalPcp, renewal.Form);
    }

    [Fact]
    public void OnlyDocumentsThatReplaceAPredecessorHaveRenewals()
    {
        Assert.Equal(
            AnnualFormSlots.Types.Select(type => type.ToString()).ToHashSet(),
            Enum.GetValues<FormType>()
                .Select(type => type.ToString())
                .Where(ComplianceScheduleRules.HasRenewalOverlap)
                .ToHashSet());
    }

    private static Person PersonWith(params Form[] forms)
    {
        var person = Person.CreatePerson(
            31, "Annual", "Slots", string.Empty, new DateTime(1990, 1, 1),
            CurrentTarget.AddYears(-1), WaiverType.None, new Settings());
        person.Forms.Clear();
        person.Forms.AddRange(forms);
        return person;
    }
}
