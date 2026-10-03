using Sati.Contracts.V1;
using Sati.Models;
using Sati.ViewModels.Children;
using Sati.ViewModels.Supervisor;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Scheduled work whose day has passed remains a scheduled record, but the calendar
/// describes its uncompleted units separately from documented work.
/// </summary>
public sealed class LapsedScheduledWorkTests
{
    private static readonly DateTime Today = new(2026, 9, 19);

    [Theory]
    [InlineData("Scheduled", -1, true)]
    [InlineData("Scheduled", 0, false)]
    [InlineData("Scheduled", 1, false)]
    [InlineData("Pending", -1, false)]
    [InlineData("Logged", -5, false)]
    public void OnlyScheduledWorkBeforeTodayIsLapsed(string status, int offsetDays, bool lapsed) =>
        Assert.Equal(lapsed, NoteSchedulingPolicy.IsLapsedScheduled(status, Today.AddDays(offsetDays), Today));

    [Fact]
    public void UndatedScheduledWorkIsNotLapsed() =>
        Assert.False(NoteSchedulingPolicy.IsLapsedScheduled("Scheduled", null, Today));

    [Fact]
    public void TheCalendarShowsPastPlannedUnitsAsRescheduledWithoutCountingThemAsWork()
    {
        var month = CalendarViewModel.BuildMonth(Today.Year, Today.Month,
        [
            Note.Create("Missed visit.", Today.AddDays(-2), NoteStatus.Scheduled, 60, 1, noteType: NoteType.Visit),
            Note.Create("Written up.", Today.AddDays(-2), NoteStatus.Logged, 30, 1, noteType: NoteType.Contact),
            Note.Create("Old reminder.", Today.AddDays(-1), NoteStatus.Scheduled, null, 1, noteType: NoteType.Reminder),
            Note.Create("This afternoon.", Today, NoteStatus.Scheduled, 60, 1, noteType: NoteType.Visit),
            Note.Create("Later visit.", Today.AddDays(5), NoteStatus.Scheduled, 30, 1, noteType: NoteType.Visit),
            Note.Create("Next week.", Today.AddDays(5), NoteStatus.Scheduled, null, 1, noteType: NoteType.Reminder)
        ], [], Today);

        var days = month.Cells.OfType<CalendarDay>().ToDictionary(day => day.Date);
        Assert.Equal(["Missed visit.", "Written up."],
            days[Today.AddDays(-2)].Notes.Select(note => note.Narrative));
        Assert.Equal("Rescheduled 4 · Logged 2", days[Today.AddDays(-2)].UnitsByStatusLabel);
        Assert.DoesNotContain("Scheduled", days[Today.AddDays(-2)].UnitsByStatusLabel);
        Assert.Equal(2, days[Today.AddDays(-2)].TotalUnits);
        Assert.Empty(days[Today.AddDays(-1)].Notes);
        Assert.Single(days[Today].Notes);
        Assert.Equal(2, days[Today.AddDays(5)].NoteCount);
        Assert.Equal("Scheduled 4", days[Today].UnitsByStatusLabel);
        Assert.Equal("Scheduled 2", days[Today.AddDays(5)].UnitsByStatusLabel);
        // The missed visit is visible, but the documented day still counts.
        Assert.Equal(ProductivityDayKind.CountedWithSecuredUnits, days[Today.AddDays(-2)].ProductivityKind);
    }

    [Fact]
    public void TheSupervisorsScheduledCountLeavesOutLapsedWork()
    {
        var today = DateTime.Today;
        var user = User.Create(31, "cm", "Case Manager", "hash", "salt", UserRole.CaseManager, null, 1);
        var summary = new CaseManagerSummaryViewModel(user, [],
        [
            Note.Create("Missed.", today.AddDays(-1), NoteStatus.Scheduled, 60, 1),
            Note.Create("Today.", today, NoteStatus.Scheduled, 60, 1),
            Note.Create("Later.", today.AddDays(2), NoteStatus.Scheduled, 60, 1)
        ], []);

        Assert.Equal(2, summary.ScheduledCount);
    }
}
