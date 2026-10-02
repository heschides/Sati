using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Contracts.V1;

namespace Sati.ViewModels.Children;

// A bounded, display-only expansion of the selected consumer's saved patterns.
// It does not assert attendance or create dated records.
public static class ConsumerScheduleCalendar
{
    public static IReadOnlyList<ConsumerScheduleCalendarDay> Build(
        int personId, DateTime month, DateTime selectedDate,
        IEnumerable<ConsumerScheduleEntryDto> entries, DateTime today)
    {
        var first = new DateTime(month.Year, month.Month, 1);
        var gridStart = first.AddDays(-((int)first.DayOfWeek + 6) % 7);
        var last = first.AddMonths(1).AddDays(-1);
        var days = (last - gridStart).Days + 1;
        var cellCount = Math.Max(35, ((days + 6) / 7) * 7);
        var ownEntries = entries.Where(x => x.PersonId == personId).ToArray();
        var result = new List<ConsumerScheduleCalendarDay>(cellCount);

        for (var index = 0; index < cellCount; index++)
        {
            var date = gridStart.AddDays(index);
            var activities = ownEntries
                .Where(x => OccursOn(x, date))
                .OrderBy(x => x.StartMinute ?? -1)
                .ThenBy(x => x.Kind)
                .ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => new ConsumerScheduleCalendarActivity(x))
                .ToArray();
            result.Add(new ConsumerScheduleCalendarDay(
                date, date.Month == first.Month, date.Date == today.Date,
                date.Date == selectedDate.Date, activities));
        }

        return result;
    }

    private static bool OccursOn(ConsumerScheduleEntryDto entry, DateTime date)
    {
        if (entry.Kind == ConsumerScheduleKind.DoctorAppointment)
            return entry.Date?.Date == date.Date;
        if (entry.Kind is not (ConsumerScheduleKind.DayProgram or ConsumerScheduleKind.Work) ||
            entry.EffectiveStart is not DateTime start || date.Date < start.Date ||
            entry.EffectiveEnd is DateTime end && date.Date > end.Date)
            return false;
        var weekday = date.DayOfWeek switch
        {
            DayOfWeek.Monday => ScheduleWeekdays.Monday,
            DayOfWeek.Tuesday => ScheduleWeekdays.Tuesday,
            DayOfWeek.Wednesday => ScheduleWeekdays.Wednesday,
            DayOfWeek.Thursday => ScheduleWeekdays.Thursday,
            DayOfWeek.Friday => ScheduleWeekdays.Friday,
            DayOfWeek.Saturday => ScheduleWeekdays.Saturday,
            _ => ScheduleWeekdays.Sunday
        };
        return (entry.Weekdays & weekday) != 0;
    }
}

public sealed class ConsumerScheduleCalendarDay(
    DateTime date, bool isCurrentMonth, bool isToday, bool isSelected,
    IReadOnlyList<ConsumerScheduleCalendarActivity> activities) : ObservableObject
{
    private bool _isSelected = isSelected;
    public DateTime Date { get; } = date;
    public string DayNumber => Date.Day.ToString(CultureInfo.CurrentCulture);
    public bool IsCurrentMonth { get; } = isCurrentMonth;
    public bool IsToday { get; } = isToday;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
                OnPropertyChanged(nameof(AutomationName));
        }
    }
    public IReadOnlyList<ConsumerScheduleCalendarActivity> Activities { get; } = activities;
    public IReadOnlyList<ConsumerScheduleCalendarActivity> PreviewActivities => Activities.Take(2).ToArray();
    public bool HasRideToBook => Activities.Any(x => x.Entry.RideStatus == ModivcareRideStatus.NeedsBooking);
    public bool HasOverflow => Activities.Count > 2;
    public string OverflowLabel => $"+{Activities.Count - 2} more";
    public string ActivityCountLabel => Activities.Count == 0
        ? "No activities" : Activities.Count == 1 ? "1 activity" : $"{Activities.Count} activities";
    public string AutomationName => $"{Date:dddd, MMMM d, yyyy}, {ActivityCountLabel}" +
        (IsToday ? ", today" : string.Empty) +
        (IsSelected ? ", selected" : string.Empty) +
        (Activities.Count == 0 ? string.Empty : ", " + string.Join("; ", Activities.Select(x => x.AutomationName)));
}

public sealed class ConsumerScheduleCalendarActivity(ConsumerScheduleEntryDto entry)
{
    public ConsumerScheduleEntryDto Entry { get; } = entry;
    public string KindLabel => Entry.Kind switch
    {
        ConsumerScheduleKind.DoctorAppointment => "Doctor appointment",
        ConsumerScheduleKind.DayProgram => "Day program",
        _ => "Work"
    };
    public string KindShort => Entry.Kind switch
    {
        ConsumerScheduleKind.DoctorAppointment => "DR",
        ConsumerScheduleKind.DayProgram => "DAY",
        _ => "WORK"
    };
    public string HeaderLabel => $"{KindLabel} · {Entry.Title}";
    public string TimeLabel => Entry.StartMinute is int start
        ? Entry.EndMinute is int end
            ? $"{ConsumerScheduleRow.Clock(start)}–{ConsumerScheduleRow.Clock(end)}"
            : ConsumerScheduleRow.Clock(start)
        : "Time to be set";
    public string LocationLabel => string.IsNullOrWhiteSpace(Entry.Location)
        ? "Location not recorded" : Entry.Location;
    public string RideLabel => Entry.RideStatus switch
    {
        ModivcareRideStatus.NeedsBooking => "ModivCare · needs booking",
        ModivcareRideStatus.Requested => "ModivCare · requested",
        ModivcareRideStatus.Confirmed => "ModivCare · confirmed",
        _ => "No ModivCare ride"
    };
    public bool HasRide => Entry.RideStatus != ModivcareRideStatus.NoRide;
    public string PickupLabel
    {
        get
        {
            var pickups = new List<string>();
            if (Entry.OutboundPickupMinute is int outbound)
                pickups.Add($"Pickup {ConsumerScheduleRow.Clock(outbound)}");
            if (Entry.ReturnPickupMinute is int inbound)
                pickups.Add($"Return {ConsumerScheduleRow.Clock(inbound)}");
            return string.Join(" · ", pickups);
        }
    }
    public bool HasPickup => Entry.OutboundPickupMinute is not null || Entry.ReturnPickupMinute is not null;
    public bool HasRideReference => !string.IsNullOrWhiteSpace(Entry.RideReference);
    public string RideReferenceLabel => $"Reference: {Entry.RideReference}";
    public string PreviewLabel => $"{KindShort} · {Entry.Title}";
    public string AutomationName => $"{KindLabel}, {Entry.Title}, {TimeLabel}, {RideLabel}";
}
