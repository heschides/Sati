using Sati.Contracts.V1;
using Sati.Models;
using Sati.ViewModels.Children;
using Sati.ViewModels.Supervisor;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Scheduled work whose day has passed is lapsed: still stored as Scheduled, still offered by
/// the leftover-work prompt, but no longer shown on the calendar or counted anywhere.
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
    public void TheCalendarLeavesOutScheduledWorkWhoseDayHasPassed()
    {
        var month = CalendarViewModel.BuildMonth(Today.Year, Today.Month,
        [
            Note.Create("Missed visit.", Today.AddDays(-2), NoteStatus.Scheduled, 60, 1, noteType: NoteType.Visit),
            Note.Create("Written up.", Today.AddDays(-2), NoteStatus.Logged, 30, 1, noteType: NoteType.Contact),
            Note.Create("Old reminder.", Today.AddDays(-1), NoteStatus.Scheduled, null, 1, noteType: NoteType.Reminder),
            Note.Create("This afternoon.", Today, NoteStatus.Scheduled, 60, 1, noteType: NoteType.Visit),
            Note.Create("Next week.", Today.AddDays(5), NoteStatus.Scheduled, null, 1, noteType: NoteType.Reminder)
        ], [], Today);

        var days = month.Cells.OfType<CalendarDay>().ToDictionary(day => day.Date);
        Assert.Equal(["Written up."], days[Today.AddDays(-2)].Notes.Select(note => note.Narrative));
        Assert.Empty(days[Today.AddDays(-1)].Notes);
        Assert.Single(days[Today].Notes);
        Assert.Single(days[Today.AddDays(5)].Notes);
        // With the missed visit disregarded, the documented day counts.
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
