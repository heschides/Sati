using Sati.Contracts.V1;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

public sealed class ConsumerScheduleCalendarTests
{
    [Fact]
    public void ExpandsOnlyOwnEntriesWithinWeeklyEffectiveDates()
    {
        var rows = new[]
        {
            Entry(1, 40, ConsumerScheduleKind.DayProgram, "Creative Trails", null,
                new DateTime(2026, 10, 8), new DateTime(2026, 10, 21),
                ScheduleWeekdays.Monday | ScheduleWeekdays.Wednesday, 540, 840),
            Entry(2, 40, ConsumerScheduleKind.DoctorAppointment, "Dental visit",
                new DateTime(2026, 10, 12), null, null, ScheduleWeekdays.None, null, null),
            Entry(3, 41, ConsumerScheduleKind.DoctorAppointment, "Someone else's visit",
                new DateTime(2026, 10, 12), null, null, ScheduleWeekdays.None, null, null)
        };

        var days = ConsumerScheduleCalendar.Build(40, new DateTime(2026, 10, 1),
            new DateTime(2026, 10, 12), rows, new DateTime(2026, 9, 30));

        Assert.Equal(35, days.Count);
        Assert.Empty(Day(7).Activities);
        Assert.Equal(new[] { "Dental visit", "Creative Trails" },
            Day(12).Activities.Select(x => x.Entry.Title));
        Assert.Single(Day(14).Activities);
        Assert.Single(Day(21).Activities);
        Assert.Empty(Day(26).Activities);
        Assert.True(Day(12).IsSelected);
        Assert.DoesNotContain(days.SelectMany(x => x.Activities),
            x => x.Entry.PersonId != 40);

        ConsumerScheduleCalendarDay Day(int day) =>
            Assert.Single(days, x => x.Date == new DateTime(2026, 10, day));
    }

    [Fact]
    public void CalendarDayMakesRideWorkVisibleWithoutClaimingItIsBooked()
    {
        var row = Entry(1, 40, ConsumerScheduleKind.DoctorAppointment, "Specialist",
            new DateTime(2026, 10, 5), null, null, ScheduleWeekdays.None, 600, null)
            with { RideStatus = ModivcareRideStatus.NeedsBooking, OutboundPickupMinute = 540 };

        var day = Assert.Single(ConsumerScheduleCalendar.Build(40,
            new DateTime(2026, 10, 1), new DateTime(2026, 10, 5),
            [row], new DateTime(2026, 10, 5)),
            x => x.Date == new DateTime(2026, 10, 5));

        var activity = Assert.Single(day.Activities);
        Assert.Contains("needs booking", activity.RideLabel);
        Assert.Contains("Pickup", activity.PickupLabel);
        Assert.True(day.HasRideToBook);
        Assert.Contains("today", day.AutomationName);
    }

    [Fact]
    public void SixWeekMonthKeepsItsLastDatesVisible()
    {
        var days = ConsumerScheduleCalendar.Build(40, new DateTime(2026, 8, 1),
            new DateTime(2026, 8, 31), [], new DateTime(2026, 9, 30));

        Assert.Equal(42, days.Count);
        Assert.Contains(days, x => x.Date == new DateTime(2026, 8, 31));
        Assert.True(Assert.Single(days, x => x.Date == new DateTime(2026, 8, 31)).IsSelected);
    }

    private static ConsumerScheduleEntryDto Entry(int id, int personId,
        ConsumerScheduleKind kind, string title, DateTime? date, DateTime? start,
        DateTime? end, ScheduleWeekdays weekdays, int? startMinute, int? endMinute) =>
        new(id, personId, kind, title, null, date, start, end, weekdays,
            startMinute, endMinute, ModivcareRideStatus.NoRide, null, null, null, 1);
}
