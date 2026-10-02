using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Services;

namespace Sati.ViewModels.Children;

public sealed class ConsumerScheduleRow
{
    public ConsumerScheduleRow(ConsumerScheduleEntryDto entry) => Entry = entry;
    public ConsumerScheduleEntryDto Entry { get; }
    public string Kind => Entry.Kind switch
    {
        ConsumerScheduleKind.DoctorAppointment => "Doctor appointment",
        ConsumerScheduleKind.DayProgram => "Day program",
        _ => "Work"
    };
    public string When => Entry.Kind == ConsumerScheduleKind.DoctorAppointment
        ? Entry.Date?.ToString("d", CultureInfo.CurrentCulture) ?? "Date missing"
        : $"{Weekdays(Entry.Weekdays)} · {Entry.EffectiveStart:d}" +
          (Entry.EffectiveEnd is null ? " onward" : $" to {Entry.EffectiveEnd:d}");
    public string Time => Entry.StartMinute is not int start
        ? "Time not recorded"
        : Entry.EndMinute is int end ? $"{Clock(start)}–{Clock(end)}" : Clock(start);
    public string Ride => Entry.RideStatus switch
    {
        ModivcareRideStatus.NoRide => "No ModivCare ride",
        ModivcareRideStatus.NeedsBooking => "ModivCare ride needs booking",
        ModivcareRideStatus.Requested => "ModivCare ride requested",
        _ => "ModivCare ride confirmed"
    };
    public string AutomationName => $"{Kind}, {Entry.Title}, {When}, {Time}, {Ride}";
    public DateTime? NextOccurrence
    {
        get
        {
            var today = DateTime.Today;
            if (Entry.Kind == ConsumerScheduleKind.DoctorAppointment)
                return Entry.Date >= today ? Entry.Date : null;
            var from = Entry.EffectiveStart is DateTime start && start > today ? start : today;
            for (var day = from; day < from.AddDays(7); day = day.AddDays(1))
            {
                if (Entry.EffectiveEnd is DateTime end && day > end) break;
                var flag = day.DayOfWeek switch
                {
                    DayOfWeek.Monday => ScheduleWeekdays.Monday,
                    DayOfWeek.Tuesday => ScheduleWeekdays.Tuesday,
                    DayOfWeek.Wednesday => ScheduleWeekdays.Wednesday,
                    DayOfWeek.Thursday => ScheduleWeekdays.Thursday,
                    DayOfWeek.Friday => ScheduleWeekdays.Friday,
                    DayOfWeek.Saturday => ScheduleWeekdays.Saturday,
                    _ => ScheduleWeekdays.Sunday
                };
                if (Entry.Weekdays.HasFlag(flag)) return day;
            }
            return null;
        }
    }

    public static string Clock(int minute) =>
        DateTime.Today.AddMinutes(minute).ToString("h:mm tt", CultureInfo.CurrentCulture);

    public static string Weekdays(ScheduleWeekdays days) => string.Join(", ",
        new[] {
            (ScheduleWeekdays.Monday, "Mon"), (ScheduleWeekdays.Tuesday, "Tue"),
            (ScheduleWeekdays.Wednesday, "Wed"), (ScheduleWeekdays.Thursday, "Thu"),
            (ScheduleWeekdays.Friday, "Fri"), (ScheduleWeekdays.Saturday, "Sat"),
            (ScheduleWeekdays.Sunday, "Sun")
        }.Where(x => days.HasFlag(x.Item1)).Select(x => x.Item2));
}

public sealed record ScheduleChoice<T>(T Value, string Label);

public partial class ConsumerScheduleViewModel : ObservableObject
{
    private readonly IConsumerScheduleService _service;
    private readonly ISessionService _session;
    private readonly LatestRequestTracker _loads = new();
    private int? _personId;

    public ConsumerScheduleViewModel(IConsumerScheduleService service, ISessionService session)
    {
        _service = service;
        _session = session;
        CalendarMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        SelectedCalendarDate = DateTime.Today;
        RebuildCalendar();
        ResetEditor();
    }

    public ObservableCollection<ConsumerScheduleRow> Entries { get; } = [];
    public ObservableCollection<ConsumerScheduleCalendarDay> CalendarDays { get; } = [];
    public ObservableCollection<ConsumerScheduleCalendarActivity> SelectedDayActivities { get; } = [];
    public DateTime CalendarMonth { get; private set; }
    public DateTime SelectedCalendarDate { get; private set; }
    public string CalendarMonthLabel => CalendarMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
    public string SelectedDayLabel => SelectedCalendarDate.ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture);
    public bool SelectedDayHasActivities => SelectedDayActivities.Count > 0;
    public IReadOnlyList<ScheduleChoice<ConsumerScheduleKind>> KindOptions { get; } =
    [
        new(ConsumerScheduleKind.DoctorAppointment, "Doctor appointment"),
        new(ConsumerScheduleKind.DayProgram, "Day program"),
        new(ConsumerScheduleKind.Work, "Work")
    ];
    public IReadOnlyList<ScheduleChoice<ModivcareRideStatus>> RideOptions { get; } =
    [
        new(ModivcareRideStatus.NoRide, "No ModivCare ride"),
        new(ModivcareRideStatus.NeedsBooking, "Needs booking"),
        new(ModivcareRideStatus.Requested, "Requested"),
        new(ModivcareRideStatus.Confirmed, "Confirmed")
    ];
    public bool HasPerson => _personId is not null;
    public bool CanEdit => HasPerson && !IsBusy;
    public bool IsWeekly => Kind is ConsumerScheduleKind.DayProgram or ConsumerScheduleKind.Work;
    public bool HasRide => RideStatus != ModivcareRideStatus.NoRide;
    public bool IsEditing => SelectedRow is not null;
    public bool HasRideAction => Entries.Any(x =>
        x.Entry.RideStatus == ModivcareRideStatus.NeedsBooking && x.NextOccurrence is not null);

    [ObservableProperty] private ConsumerScheduleRow? selectedRow;
    [ObservableProperty] private ConsumerScheduleKind kind;
    [ObservableProperty] private string title = string.Empty;
    [ObservableProperty] private string? location;
    [ObservableProperty] private DateTime? date;
    [ObservableProperty] private DateTime? effectiveStart;
    [ObservableProperty] private DateTime? effectiveEnd;
    [ObservableProperty] private bool monday;
    [ObservableProperty] private bool tuesday;
    [ObservableProperty] private bool wednesday;
    [ObservableProperty] private bool thursday;
    [ObservableProperty] private bool friday;
    [ObservableProperty] private bool saturday;
    [ObservableProperty] private bool sunday;
    [ObservableProperty] private string? startTime;
    [ObservableProperty] private string? endTime;
    [ObservableProperty] private ModivcareRideStatus rideStatus;
    [ObservableProperty] private string? outboundPickupTime;
    [ObservableProperty] private string? returnPickupTime;
    [ObservableProperty] private string? rideReference;
    [ObservableProperty] private string? statusMessage;
    [ObservableProperty] private bool isBusy;

    partial void OnKindChanged(ConsumerScheduleKind value) => OnPropertyChanged(nameof(IsWeekly));
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanEdit));
    partial void OnRideStatusChanged(ModivcareRideStatus value) => OnPropertyChanged(nameof(HasRide));
    partial void OnSelectedRowChanged(ConsumerScheduleRow? value)
    {
        OnPropertyChanged(nameof(IsEditing));
        if (value is null) return;
        var x = value.Entry;
        Kind = x.Kind;
        Title = x.Title;
        Location = x.Location;
        Date = x.Date;
        EffectiveStart = x.EffectiveStart;
        EffectiveEnd = x.EffectiveEnd;
        Monday = x.Weekdays.HasFlag(ScheduleWeekdays.Monday);
        Tuesday = x.Weekdays.HasFlag(ScheduleWeekdays.Tuesday);
        Wednesday = x.Weekdays.HasFlag(ScheduleWeekdays.Wednesday);
        Thursday = x.Weekdays.HasFlag(ScheduleWeekdays.Thursday);
        Friday = x.Weekdays.HasFlag(ScheduleWeekdays.Friday);
        Saturday = x.Weekdays.HasFlag(ScheduleWeekdays.Saturday);
        Sunday = x.Weekdays.HasFlag(ScheduleWeekdays.Sunday);
        StartTime = x.StartMinute is int start ? ConsumerScheduleRow.Clock(start) : null;
        EndTime = x.EndMinute is int end ? ConsumerScheduleRow.Clock(end) : null;
        RideStatus = x.RideStatus;
        OutboundPickupTime = x.OutboundPickupMinute is int outbound
            ? ConsumerScheduleRow.Clock(outbound) : null;
        ReturnPickupTime = x.ReturnPickupMinute is int inbound
            ? ConsumerScheduleRow.Clock(inbound) : null;
        RideReference = x.RideReference;
        StatusMessage = null;
    }

    public void SetPerson(Person? person)
    {
        var request = _loads.Begin();
        _personId = person?.Id;
        Entries.Clear();
        SelectedRow = null;
        CalendarMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        SelectedCalendarDate = DateTime.Today;
        OnPropertyChanged(nameof(CalendarMonthLabel));
        OnPropertyChanged(nameof(SelectedDayLabel));
        RebuildCalendar();
        ResetEditor();
        OnPropertyChanged(nameof(HasPerson));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(HasRideAction));
        if (person is not null) _ = LoadAsync(request, person.Id, _session.CurrentUser);
    }

    [RelayCommand]
    private void NewEntry()
    {
        SelectedRow = null;
        ResetEditor();
    }

    [RelayCommand]
    private void PreviousCalendarMonth() => ChangeCalendarMonth(CalendarMonth.AddMonths(-1));

    [RelayCommand]
    private void NextCalendarMonth() => ChangeCalendarMonth(CalendarMonth.AddMonths(1));

    [RelayCommand]
    private void CalendarToday()
    {
        var today = DateTime.Today;
        CalendarMonth = new DateTime(today.Year, today.Month, 1);
        SelectedCalendarDate = today;
        OnPropertyChanged(nameof(CalendarMonthLabel));
        OnPropertyChanged(nameof(SelectedDayLabel));
        RebuildCalendar();
    }

    [RelayCommand]
    private void SelectCalendarDay(ConsumerScheduleCalendarDay? day)
    {
        if (day is null || !CalendarDays.Contains(day)) return;
        foreach (var date in CalendarDays)
            date.IsSelected = ReferenceEquals(date, day);
        SelectedCalendarDate = day.Date;
        OnPropertyChanged(nameof(SelectedDayLabel));
        SelectedDayActivities.Clear();
        foreach (var activity in day.Activities) SelectedDayActivities.Add(activity);
        OnPropertyChanged(nameof(SelectedDayHasActivities));
    }

    private void ChangeCalendarMonth(DateTime month)
    {
        CalendarMonth = new DateTime(month.Year, month.Month, 1);
        SelectedCalendarDate = CalendarMonth;
        OnPropertyChanged(nameof(CalendarMonthLabel));
        OnPropertyChanged(nameof(SelectedDayLabel));
        RebuildCalendar();
    }

    private void RebuildCalendar()
    {
        var days = ConsumerScheduleCalendar.Build(_personId ?? 0, CalendarMonth,
            SelectedCalendarDate, Entries.Select(x => x.Entry), DateTime.Today);
        CalendarDays.Clear();
        foreach (var day in days) CalendarDays.Add(day);
        SelectedDayActivities.Clear();
        foreach (var activity in days.FirstOrDefault(x => x.Date == SelectedCalendarDate)?.Activities ?? [])
            SelectedDayActivities.Add(activity);
        OnPropertyChanged(nameof(SelectedDayHasActivities));
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_personId is int personId)
            await LoadAsync(_loads.Begin(), personId, _session.CurrentUser);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_personId is not int personId || IsBusy) return;
        if (!TryOptionalMinute(StartTime, out var start) ||
            !TryOptionalMinute(EndTime, out var end) ||
            !TryOptionalMinute(OutboundPickupTime, out var outbound) ||
            !TryOptionalMinute(ReturnPickupTime, out var inbound))
        {
            StatusMessage = "Enter times such as 9:00 AM or 14:30.";
            return;
        }
        var selected = SelectedRow?.Entry;
        var request = new SaveConsumerScheduleEntryRequest(
            Kind, Title, Location,
            IsWeekly ? null : Date, IsWeekly ? EffectiveStart : null,
            IsWeekly ? EffectiveEnd : null, IsWeekly ? SelectedWeekdays() : ScheduleWeekdays.None,
            start, end, RideStatus, HasRide ? outbound : null,
            HasRide ? inbound : null, HasRide ? RideReference : null, selected?.Revision ?? 0);
        var errors = ConsumerScheduleRules.Validate(request);
        if (errors.Count != 0)
        {
            StatusMessage = string.Join(" ", errors.Values.SelectMany(x => x));
            return;
        }
        var account = _session.CurrentUser;
        IsBusy = true;
        try
        {
            await _service.SaveAsync(personId, selected?.Id, request);
            if (_personId != personId || !ReferenceEquals(account, _session.CurrentUser)) return;
            await LoadAsync(_loads.Begin(), personId, account);
            SelectedRow = null;
            ResetEditor();
            StatusMessage = "Schedule entry saved.";
        }
        catch (Exception error)
        {
            if (_personId == personId && ReferenceEquals(account, _session.CurrentUser))
                StatusMessage = error.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RemoveAsync()
    {
        if (_personId is not int personId || SelectedRow is not { } selected || IsBusy) return;
        var account = _session.CurrentUser;
        IsBusy = true;
        try
        {
            await _service.DeleteAsync(personId, selected.Entry.Id, selected.Entry.Revision);
            if (_personId != personId || !ReferenceEquals(account, _session.CurrentUser)) return;
            await LoadAsync(_loads.Begin(), personId, account);
            SelectedRow = null;
            ResetEditor();
            StatusMessage = "Schedule entry removed.";
        }
        catch (Exception error)
        {
            if (_personId == personId && ReferenceEquals(account, _session.CurrentUser))
                StatusMessage = error.Message;
        }
        finally { IsBusy = false; }
    }

    private async Task LoadAsync(int request, int personId, User? account)
    {
        try
        {
            var rows = await _service.GetAsync(personId);
            if (!_loads.IsCurrent(request) || _personId != personId ||
                !ReferenceEquals(account, _session.CurrentUser)) return;
            var selectedId = SelectedRow?.Entry.Id;
            Entries.Clear();
            foreach (var row in rows.Select(x => new ConsumerScheduleRow(x))
                         .OrderBy(x => x.NextOccurrence is null)
                         .ThenBy(x => x.NextOccurrence)
                         .ThenBy(x => x.Entry.StartMinute))
                Entries.Add(row);
            SelectedRow = Entries.FirstOrDefault(x => x.Entry.Id == selectedId);
            if (SelectedRow is null) ResetEditor();
            RebuildCalendar();
            OnPropertyChanged(nameof(HasRideAction));
        }
        catch (Exception error)
        {
            if (_loads.IsCurrent(request) && _personId == personId &&
                ReferenceEquals(account, _session.CurrentUser))
                StatusMessage = $"Could not load this consumer's schedule: {error.Message}";
        }
    }

    private void ResetEditor()
    {
        Kind = ConsumerScheduleKind.DoctorAppointment;
        Title = string.Empty;
        Location = null;
        Date = DateTime.Today;
        EffectiveStart = DateTime.Today;
        EffectiveEnd = null;
        Monday = Tuesday = Wednesday = Thursday = Friday = Saturday = Sunday = false;
        StartTime = null;
        EndTime = null;
        RideStatus = ModivcareRideStatus.NoRide;
        OutboundPickupTime = null;
        ReturnPickupTime = null;
        RideReference = null;
        StatusMessage = null;
    }

    private ScheduleWeekdays SelectedWeekdays() =>
        (Monday ? ScheduleWeekdays.Monday : 0) |
        (Tuesday ? ScheduleWeekdays.Tuesday : 0) |
        (Wednesday ? ScheduleWeekdays.Wednesday : 0) |
        (Thursday ? ScheduleWeekdays.Thursday : 0) |
        (Friday ? ScheduleWeekdays.Friday : 0) |
        (Saturday ? ScheduleWeekdays.Saturday : 0) |
        (Sunday ? ScheduleWeekdays.Sunday : 0);

    private static bool TryMinute(string? text, out int minute)
    {
        minute = 0;
        if (!TimeOnly.TryParseExact(text?.Trim(),
                ["h:mm tt", "h:mmtt", "H:mm", "HH:mm", "h tt"],
                CultureInfo.CurrentCulture, DateTimeStyles.None, out var time)) return false;
        minute = time.Hour * 60 + time.Minute;
        return true;
    }

    private static bool TryOptionalMinute(string? text, out int? minute)
    {
        minute = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!TryMinute(text, out var parsed)) return false;
        minute = parsed;
        return true;
    }
}
