using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// A consumer whose plan began on February 29 has targets on February 28 in common
/// years and February 29 in leap years, because generation counts every target from
/// the initial date. Adding a year to a February 28 target lands on February 28 of
/// the leap year, a date no generated row carries.
/// </summary>
public sealed class LeapDayRenewalTargetTests
{
    private static readonly DateTime Admission = new(2024, 2, 29);
    private static readonly DateTime CurrentTarget = new(2027, 2, 28);
    private static readonly DateTime LeapRenewal = new(2028, 2, 29);
    // The PCP for the leap-year plan became available 90 days earlier, on December 1.
    private static readonly DateTime Today = new(2027, 12, 15);

    [Fact]
    public void ReconciledTargetsAreTheGeneratedOnes()
    {
        Assert.Equal(
            [CurrentTarget, LeapRenewal],
            ComplianceScheduleRules.CurrentAndUpcomingTargetEffectiveDates(Admission, Today));
        Assert.Equal(
            ComplianceScheduleRules.TargetEffectiveDatesThroughNext(Admission, Today).TakeLast(2),
            ComplianceScheduleRules.CurrentAndUpcomingTargetEffectiveDates(Admission, Today));

        // The day before the leap-year target the pair is still 2027/2028; on it, 2028/2029.
        Assert.Equal(
            [CurrentTarget, LeapRenewal],
            ComplianceScheduleRules.CurrentAndUpcomingTargetEffectiveDates(
                Admission, new DateTime(2028, 2, 28)));
        Assert.Equal(
            [LeapRenewal, new DateTime(2029, 2, 28)],
            ComplianceScheduleRules.CurrentAndUpcomingTargetEffectiveDates(Admission, LeapRenewal));
    }

    [Fact]
    public void CycleBoundariesEndOnTheLeapYearTarget()
    {
        var person = LeapDayPerson();

        Assert.Equal((CurrentTarget, LeapRenewal), person.GetCurrentCycleBoundaries(Today));
    }

    [Fact]
    public void UpcomingEventsIncludeTheLeapYearRenewal()
    {
        var person = LeapDayPerson();
        var renewal = new Form(FormType.PCP, LeapRenewal, targetEffectiveDate: LeapRenewal);
        person.Forms.Add(renewal);

        var events = new UpcomingEventService().GenerateEvents([person], new Settings(), Today);

        Assert.Contains(events, item =>
            item.FormType == FormType.PCP && item.TargetEffectiveDate == LeapRenewal);
    }

    [Fact]
    public void UpcomingEventsIncludeTheLeapYearReleaseRenewal()
    {
        var person = LeapDayPerson();
        person.ReleaseComplianceSnapshots = [LeapRelease()];

        var events = new UpcomingEventService().GenerateEvents([person], new Settings(), Today);

        Assert.Contains(events, item => item.TargetEffectiveDate == LeapRenewal);
    }

    [Fact]
    public void TaskBoardSelectsTheLeapYearRenewal()
    {
        var person = LeapDayPerson();
        var renewal = new Form(FormType.PCP, LeapRenewal, targetEffectiveDate: LeapRenewal);
        person.Forms.Add(renewal);

        Assert.Same(renewal, CaseManagerDashboardViewModel.SelectBoardForm(
            person, FormType.PCP, Today));
    }

    [Fact]
    public void TaskBoardListsTheLeapYearReleaseRenewal()
    {
        var person = LeapDayPerson();
        var release = LeapRelease();
        person.ReleaseComplianceSnapshots = [release];

        var rows = CaseManagerDashboardViewModel.BuildReleaseRowsForPerson(
            person, new Settings(), Today);

        Assert.Contains(rows, row => row.ObligationId == release.ObligationId);
    }

    [Fact]
    public void NoCodeAddsAYearToTheCurrentTarget()
    {
        // Release reconciliation runs against DateTime.Today (desktop) or the API clock,
        // so it cannot be driven to 2027 here. Every such caller must use the shared
        // CurrentAndUpcomingTargetEffectiveDates or UpcomingTargetEffectiveDate instead.
        var root = RenderedViews.RepositoryRoot();
        var offenders = new[] { "Data", "ViewModels", "Services", "Sati.Api", "Sati.Persistence" }
            .Select(folder => Path.Combine(root, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .Where(path => File.ReadAllText(path).Contains(
                "currentTarget.AddYears(1)", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static Person LeapDayPerson()
    {
        var person = Person.CreatePerson(
            31, "Leap", "Day", string.Empty, new DateTime(1990, 1, 1),
            Admission, WaiverType.None, new Settings());
        person.Forms.Clear();
        return person;
    }

    private static ReleaseComplianceFact LeapRelease() => new(
        "leap-dhhs",
        ReleaseObligationCategory.Dhhs,
        LeapRenewal,
        LeapRenewal,
        null,
        [],
        Guid.NewGuid(),
        LeapRenewal,
        LeapRenewal.AddDays(-90),
        null);
}
