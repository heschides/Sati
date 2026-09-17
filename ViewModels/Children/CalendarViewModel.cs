using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Helpers;
using Sati.Models;
using Sati.Services;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sati.ViewModels.Children;

public partial class CalendarViewModel : ObservableObject
{
    internal const int MinimumYear = 2000;
    internal const int MaximumYear = 2200;

    private readonly IExemptDateService _exemptDateService;
    private readonly INoteService _noteService;
    private readonly ISessionService _sessionService;
    private readonly IServiceDayInclusionService? _serviceDayInclusionService;
    private readonly ISettingsService? _settingsService;
    private readonly IOutlookCalendarService? _outlookCalendarService;
    private readonly IOutlookCalendarFilePicker? _outlookCalendarFilePicker;
    private readonly LatestRequestTracker _yearLoadRequests = new();
    private readonly Func<DateTime> _today;

    private List<ExemptDate> _exemptDates = [];
    private List<Note> _yearNotes = [];
    private List<ImportedOutlookEvent> _yearOutlookEvents = [];
    private List<ServiceDayInclusion> _serviceDayInclusions = [];
    private int _documentationWindowDays = ProductivityForecast.DefaultDocumentationWindowDays;
    private Settings? _settings;

    // The dashboard refresh is part of the calendar operation, so it is a Task
    // rather than an async-void EventHandler. Each subscriber is awaited and
    // isolated below; a failed summary refresh must never reach WPF's dispatcher.
    public event Func<Task>? ExemptDateChanged;
    public event Func<DateTime, Task>? TimeOffScheduled;

    [ObservableProperty]
    private int currentYear = DateTime.Today.Year;

    [ObservableProperty]
    private CalendarDay? selectedDay;

    [ObservableProperty]
    private int selectedMonth = DateTime.Today.Month;

    [ObservableProperty]
    private bool isYearOverview;

    [ObservableProperty]
    private List<CalendarMonth> months = [];

    [ObservableProperty]
    private bool isDayFocused;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isUpdatingExemptDate;

    [ObservableProperty]
    private bool isUpdatingServiceDay;

    [ObservableProperty]
    private bool isImportingOutlookCalendar;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    public IReadOnlyList<CalendarNoteItem> SelectedDayNotes =>
        SelectedDay?.Notes ?? [];

    public IReadOnlyList<ImportedOutlookEvent> SelectedDayOutlookEvents =>
        SelectedDay?.OutlookEvents ?? [];

    public bool HasSelectedDay => SelectedDay is not null;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public int SelectedDayTotalMinutes =>
        SelectedDayNotes.Sum(note => note.Minutes ?? 0);

    public int SelectedDayTotalUnits =>
        SelectedDayNotes.Sum(note => note.Units ?? 0);

    public string SelectedDaySummary
    {
        get
        {
            var count = SelectedDayNotes.Count;
            if (count == 0)
                return "No service notes are dated this day.";

            var noteWord = count == 1 ? "note" : "notes";
            var minuteWord = SelectedDayTotalMinutes == 1 ? "minute" : "minutes";
            var unitWord = SelectedDayTotalUnits == 1 ? "unit" : "units";
            return $"{count} {noteWord} · {SelectedDayTotalMinutes} {minuteWord} · {SelectedDayTotalUnits} {unitWord}";
        }
    }

    public string SelectedDayExemptActionLabel =>
        SelectedDay?.IsExempt == true ? "Restore workday" : "Schedule time off";

    public List<ExemptDate> ExemptDaysForSelectedMonth =>
        _exemptDates
            .Where(entry => entry.Date.Month == SelectedMonth && entry.Date.Year == CurrentYear)
            .OrderBy(entry => entry.Date)
            .ToList();

    public string SelectedMonthName => SelectedMonth is >= 1 and <= 12
        ? CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(SelectedMonth)
        : string.Empty;

    public CalendarMonth? SelectedCalendarMonth =>
        Months.FirstOrDefault(month => month.Month == SelectedMonth);

    public CalendarViewModel(
        IExemptDateService exemptDateService,
        INoteService noteService,
        ISessionService sessionService,
        IOutlookCalendarService? outlookCalendarService = null,
        IOutlookCalendarFilePicker? outlookCalendarFilePicker = null,
        Func<DateTime>? today = null,
        IServiceDayInclusionService? serviceDayInclusionService = null,
        ISettingsService? settingsService = null)
    {
        _today = today ?? (() => DateTime.Today);
        _serviceDayInclusionService = serviceDayInclusionService;
        _settingsService = settingsService;
        _exemptDateService = exemptDateService;
        _noteService = noteService;
        _sessionService = sessionService;
        _outlookCalendarService = outlookCalendarService;
        _outlookCalendarFilePicker = outlookCalendarFilePicker;
    }

    public Task InitializeAsync() => LoadYearAsync();

    [RelayCommand]
    private Task Refresh() => LoadYearAsync();

    [RelayCommand]
    private async Task ImportOutlookCalendar()
    {
        if (IsImportingOutlookCalendar || _outlookCalendarService is null ||
            _outlookCalendarFilePicker is null)
        {
            return;
        }

        var user = _sessionService.CurrentUser;
        if (user is null)
        {
            StatusMessage = "Sign in again before importing an Outlook calendar.";
            return;
        }

        var filePath = _outlookCalendarFilePicker.PickCalendarFile();
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        IsImportingOutlookCalendar = true;
        try
        {
            var result = await _outlookCalendarService.ImportAsync(user.Id, filePath);
            await LoadYearAsync();
            var skipped = result.SkippedCount == 0
                ? string.Empty
                : $" {result.SkippedCount} cancelled, duplicate, or unsupported items were skipped.";
            StatusMessage = $"Imported {result.ImportedCount} Outlook events on this computer.{skipped}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidDataException or CryptographicException or JsonException)
        {
            Debug.WriteLine($"CalendarViewModel.ImportOutlookCalendar failed: {ex.GetType().Name}");
            StatusMessage = ex is InvalidDataException
                ? ex.Message
                : "The Outlook calendar could not be imported. Check the file and try again.";
        }
        finally
        {
            IsImportingOutlookCalendar = false;
        }
    }

    [RelayCommand]
    private void SelectDay(CalendarDay? day)
    {
        if (day is null || day.Date.Year != CurrentYear)
            return;

        SelectedDay = day;
        SelectedMonth = day.Date.Month;
        IsYearOverview = false;
    }

    [RelayCommand]
    private void OpenSelectedDay()
    {
        if (SelectedDay is not null)
        {
            IsYearOverview = false;
            IsDayFocused = true;
        }
    }

    [RelayCommand]
    private void ReturnToYear() => IsDayFocused = false;

    [RelayCommand]
    private void ShowMonth()
    {
        IsDayFocused = false;
        IsYearOverview = false;
    }

    [RelayCommand]
    private void ShowYear()
    {
        IsDayFocused = false;
        IsYearOverview = true;
    }

    [RelayCommand]
    private Task PreviousMonth() => MoveMonthAsync(-1);

    [RelayCommand]
    private Task NextMonth() => MoveMonthAsync(1);

    private async Task MoveMonthAsync(int offset)
    {
        if (IsLoading)
            return;

        var target = new DateTime(CurrentYear, SelectedMonth, 1).AddMonths(offset);
        if (target.Year is < MinimumYear or > MaximumYear)
            return;

        var yearChanged = target.Year != CurrentYear;
        CurrentYear = target.Year;
        SelectedMonth = target.Month;
        SelectedDay = null;
        IsDayFocused = false;
        IsYearOverview = false;

        if (yearChanged)
            await LoadYearAsync();
    }

    [RelayCommand]
    private async Task ToggleExempt(CalendarDay? day)
    {
        if (day is null || day.Date.Year != CurrentYear || IsUpdatingExemptDate)
            return;

        var user = _sessionService.CurrentUser;
        if (user is null)
        {
            StatusMessage = "Sign in again before changing an exempt day.";
            return;
        }

        IsUpdatingExemptDate = true;
        var scheduledTimeOff = false;
        try
        {
            // Read the canonical loaded collection instead of trusting a CalendarDay
            // instance. BuildMonths replaces day objects, so an old command parameter
            // can otherwise invert the wrong state or create a duplicate exemption.
            var existing = _exemptDates.FirstOrDefault(
                entry => entry.Date.Date == day.Date.Date);
            if (existing is not null)
            {
                await _exemptDateService.RemoveAsync(existing.Id);
                _exemptDates.RemoveAll(entry => entry.Date.Date == day.Date.Date);
            }
            else
            {
                var exempt = await _exemptDateService.AddAsync(user.Id, day.Date.Date);
                _exemptDates.RemoveAll(entry => entry.Date.Date == day.Date.Date);
                _exemptDates.Add(exempt);
                scheduledTimeOff = true;
            }

            BuildMonths();
            StatusMessage = string.Empty;
            if (!await NotifyExemptDateChangedAsync())
            {
                StatusMessage =
                    "The calendar changed, but the dashboard summary could not be refreshed. Refresh the dashboard before relying on its totals.";
            }
            if (scheduledTimeOff)
                await NotifyTimeOffScheduledAsync(day.Date.Date);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalendarViewModel.ToggleExempt failed: {ex.Message}");
            StatusMessage = "The exempt-day change could not be saved. Please try again.";
        }
        finally
        {
            IsUpdatingExemptDate = false;
        }
    }

    /// <summary>
    /// Includes or excludes one open day from the documented daily average. Returning a day to
    /// Sati's own reading (documented work, nothing still scheduled) clears the row rather than
    /// storing the same answer, so a later change of schedule is still followed.
    /// </summary>
    [RelayCommand]
    private async Task ToggleCountedDay(CalendarDay? day)
    {
        if (day is null || _serviceDayInclusionService is null || IsUpdatingServiceDay)
            return;
        if (!day.CanChooseCounted)
            return;

        var user = _sessionService.CurrentUser;
        if (user is null)
        {
            StatusMessage = "Sign in again before changing which days count.";
            return;
        }

        IsUpdatingServiceDay = true;
        var date = day.Date.Date;
        var wanted = !day.CountsTowardAverage;
        try
        {
            if (wanted == day.CountsByDefault)
            {
                await _serviceDayInclusionService.ClearAsync(user.Id, date);
                _serviceDayInclusions.RemoveAll(inclusion => inclusion.Date.Date == date);
            }
            else
            {
                var saved = await _serviceDayInclusionService.SetAsync(user.Id, date, wanted);
                _serviceDayInclusions.RemoveAll(inclusion => inclusion.Date.Date == date);
                _serviceDayInclusions.Add(saved);
            }

            BuildMonths();
            StatusMessage = string.Empty;
            if (!await NotifyExemptDateChangedAsync())
            {
                StatusMessage =
                    "The calendar changed, but the productivity summary could not be refreshed. Refresh it before relying on its average.";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalendarViewModel.ToggleCountedDay failed: {ex.Message}");
            StatusMessage = "That day could not be changed. Please try again.";
        }
        finally
        {
            IsUpdatingServiceDay = false;
        }
    }

    [RelayCommand]
    private async Task PreviousYear()
    {
        if (IsLoading || CurrentYear <= MinimumYear)
            return;

        CurrentYear--;
        SelectedDay = null;
        IsDayFocused = false;
        await LoadYearAsync();
    }

    [RelayCommand]
    private async Task NextYear()
    {
        if (IsLoading || CurrentYear >= MaximumYear)
            return;

        CurrentYear++;
        SelectedDay = null;
        IsDayFocused = false;
        await LoadYearAsync();
    }

    private async Task LoadYearAsync()
    {
        var request = _yearLoadRequests.Begin();
        var year = CurrentYear;
        IsLoading = true;

        try
        {
            if (year is < MinimumYear or > MaximumYear)
                throw new ArgumentOutOfRangeException(nameof(CurrentYear));

            var user = _sessionService.CurrentUser;
            if (user is null)
                throw new UnauthorizedAccessException("A signed-in user is required.");

            var exemptDatesTask = _exemptDateService.GetByYearAsync(user.Id, year);
            var notesTask = _noteService.GetByYearAsync(user.Id, year);
            var outlookTask = LoadOutlookEventsAsync(user.Id, year);
            var inclusionsTask = LoadServiceDayInclusionsAsync(user.Id, year);
            var settingsTask = LoadCalendarSettingsAsync();
            await Task.WhenAll(exemptDatesTask, notesTask, outlookTask, inclusionsTask, settingsTask);

            if (!_yearLoadRequests.IsCurrent(request) || CurrentYear != year)
                return;

            _serviceDayInclusions = await inclusionsTask;
            _settings = await settingsTask;
            _documentationWindowDays = ProductivityForecast.NormalizeDocumentationWindowDays(
                _settings?.AbandonedAfterDays);
            _exemptDates = await exemptDatesTask;
            _yearNotes = await notesTask;
            var outlookResult = await outlookTask;
            _yearOutlookEvents = outlookResult.Events;
            BuildMonths();
            StatusMessage = outlookResult.Warning;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalendarViewModel.LoadYearAsync failed: {ex.Message}");
            if (_yearLoadRequests.IsCurrent(request) && CurrentYear == year)
            {
                ClearLoadedYear();
                StatusMessage = _sessionService.CurrentUser is null
                    ? "Your calendar is unavailable because the session ended. Sign in again."
                    : "The calendar could not be loaded. Check the connection and choose Refresh.";
            }
        }
        finally
        {
            if (_yearLoadRequests.IsCurrent(request))
                IsLoading = false;
        }
    }

    private void BuildMonths()
    {
        var selectedDate = SelectedDay?.Date.Date;
        var notesByDate = _yearNotes
            .Where(note => note.EventDate.HasValue && note.EventDate.Value.Year == CurrentYear)
            .Select(note => new CalendarNoteItem(note))
            .GroupBy(note => note.EventDate.Date)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(note => note.StartTime ?? int.MaxValue)
                    .ThenBy(note => note.ClientName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(note => note.Id)
                    .ToList());
        var outlookByDate = _yearOutlookEvents
            .SelectMany(entry => CalendarDates(entry)
                .Select(date => (Date: date, Event: entry)))
            .Where(entry => entry.Date.Year == CurrentYear)
            .GroupBy(entry => entry.Date)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(entry => entry.Event)
                    .OrderBy(entry => entry.IsAllDay ? 0 : 1)
                    .ThenBy(entry => entry.Start)
                    .ThenBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ToList());
        var exemptByDate = _exemptDates
            .Where(entry => entry.Date.Year == CurrentYear)
            .GroupBy(entry => entry.Date.Date)
            .ToDictionary(group => group.Key, group => group.First());

        var today = _today();
        var choices = ChoicesByDate(_serviceDayInclusions);
        var result = new List<CalendarMonth>();
        for (var month = 1; month <= 12; month++)
        {
            result.Add(BuildMonth(
                CurrentYear, month, notesByDate, exemptByDate, outlookByDate,
                today, _documentationWindowDays, choices, _settings));
        }

        Months = result;
        SelectedDay = selectedDate.HasValue && selectedDate.Value.Year == CurrentYear
            ? FindDay(selectedDate.Value)
            : null;
        if (SelectedDay is null)
            IsDayFocused = false;

        NotifyCalendarComputedProperties();
    }

    /// <summary>
    /// A read-only month of the signed-in case manager's notes and exempt days, classified
    /// against <paramref name="today"/> the same way the full calendar is. The Overview
    /// thumbnail uses this with the dashboard's already-loaded month.
    /// </summary>
    public static CalendarMonth BuildMonth(
        int year,
        int month,
        IEnumerable<Note> notes,
        IEnumerable<ExemptDate> exemptDates,
        DateTime today,
        int documentationWindowDays = ProductivityForecast.DefaultDocumentationWindowDays,
        IEnumerable<ServiceDayInclusion>? serviceDayInclusions = null,
        Settings? settings = null)
    {
        var notesByDate = notes
            .Where(note => note.EventDate is DateTime date && date.Year == year && date.Month == month)
            .Select(note => new CalendarNoteItem(note))
            .GroupBy(note => note.EventDate.Date)
            .ToDictionary(group => group.Key, group => group.ToList());
        var exemptByDate = exemptDates
            .Where(entry => entry.Date.Year == year && entry.Date.Month == month)
            .GroupBy(entry => entry.Date.Date)
            .ToDictionary(group => group.Key, group => group.First());
        return BuildMonth(year, month, notesByDate, exemptByDate,
            new Dictionary<DateTime, List<ImportedOutlookEvent>>(), today,
            documentationWindowDays, ChoicesByDate(serviceDayInclusions), settings);
    }

    /// <summary>The stored decisions as the shared rule reads them.</summary>
    internal static IReadOnlyDictionary<DateTime, bool> ChoicesByDate(
        IEnumerable<ServiceDayInclusion>? inclusions) =>
        inclusions is null
            ? new Dictionary<DateTime, bool>()
            : inclusions
                .GroupBy(inclusion => inclusion.Date.Date)
                .ToDictionary(group => group.Key, group => group.Last().IsIncluded);

    private static CalendarMonth BuildMonth(
        int year,
        int month,
        IReadOnlyDictionary<DateTime, List<CalendarNoteItem>> notesByDate,
        IReadOnlyDictionary<DateTime, ExemptDate> exemptByDate,
        IReadOnlyDictionary<DateTime, List<ImportedOutlookEvent>> outlookByDate,
        DateTime today,
        int documentationWindowDays,
        IReadOnlyDictionary<DateTime, bool> choices,
        Settings? settings)
    {
        var firstDay = new DateTime(year, month, 1);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var cells = new List<CalendarDay?>();

        for (var index = 0; index < (int)firstDay.DayOfWeek; index++)
            cells.Add(null);

        for (var dayNumber = 1; dayNumber <= daysInMonth; dayNumber++)
        {
            var date = new DateTime(year, month, dayNumber);
            exemptByDate.TryGetValue(date, out var exemptEntry);
            notesByDate.TryGetValue(date, out var notes);
            outlookByDate.TryGetValue(date, out var outlookEvents);
            notes ??= [];
            var facts = notes.Select(note => note.ProductivityFact).ToList();
            var choice = choices.TryGetValue(date, out var stored) ? stored : (bool?)null;
            cells.Add(new CalendarDay
            {
                Date = date,
                IsExempt = exemptEntry is not null,
                ExemptDateId = exemptEntry?.Id,
                IsWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                Notes = notes,
                OutlookEvents = outlookEvents ?? [],
                ProductivityKind = ProductivityForecast.ClassifyDay(
                    date, facts, today, documentationWindowDays, choice),
                CanChooseCounted = ProductivityForecast.CanChooseDailyAverageDay(
                    date, facts, today, documentationWindowDays,
                    isEligibleWorkday: date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) &&
                                       exemptEntry is null &&
                                       (settings is null || !WorkdayHelper.IsAlwaysExcludedWorkday(date, settings))),
                CountsByDefault = ProductivityForecast.CountsInDailyAverage(
                    date, facts, today, documentationWindowDays, caseManagerChoice: null),
                HasCaseManagerChoice = choice is not null
            });
        }

        return new CalendarMonth
        {
            Name = firstDay.ToString("MMMM", CultureInfo.CurrentCulture),
            Month = month,
            Year = year,
            Cells = cells
        };
    }

    private CalendarDay? FindDay(DateTime date) =>
        Months
            .Where(month => month.Month == date.Month)
            .SelectMany(month => month.Cells)
            .FirstOrDefault(day => day?.Date.Date == date.Date);

    private static IEnumerable<DateTime> CalendarDates(ImportedOutlookEvent entry)
    {
        yield return entry.Start.Date;
        if (!entry.IsAllDay)
            yield break;

        // iCalendar DTEND is exclusive for all-day events. A three-day event therefore
        // appears on Start, Start + 1, and the day immediately before End.
        for (var date = entry.Start.Date.AddDays(1); date < entry.End.Date; date = date.AddDays(1))
            yield return date;
    }

    private void ClearLoadedYear()
    {
        _exemptDates = [];
        _yearNotes = [];
        _yearOutlookEvents = [];
        Months = [];
        SelectedDay = null;
        IsDayFocused = false;
        NotifyCalendarComputedProperties();
    }

    private async Task<bool> NotifyExemptDateChangedAsync()
    {
        var handlers = ExemptDateChanged;
        if (handlers is null)
            return true;

        var succeeded = true;
        foreach (var handler in handlers.GetInvocationList().Cast<Func<Task>>())
        {
            try
            {
                await handler();
            }
            catch (Exception ex)
            {
                succeeded = false;
                Debug.WriteLine($"CalendarViewModel.ExemptDateChanged subscriber failed: {ex.Message}");
            }
        }

        return succeeded;
    }

    private async Task NotifyTimeOffScheduledAsync(DateTime date)
    {
        var handlers = TimeOffScheduled;
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Func<DateTime, Task>>())
        {
            try
            {
                await handler(date);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CalendarViewModel.TimeOffScheduled subscriber failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// The case manager's own decisions about open days. A failure here leaves the calendar
    /// showing Sati's default for each day rather than refusing to load the year.
    /// </summary>
    private async Task<List<ServiceDayInclusion>> LoadServiceDayInclusionsAsync(int userId, int year)
    {
        if (_serviceDayInclusionService is null)
            return [];

        try
        {
            return await _serviceDayInclusionService.GetByYearAsync(userId, year);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalendarViewModel.LoadServiceDayInclusions failed: {ex.GetType().Name}");
            return [];
        }
    }

    /// <summary>
    /// The agency's documentation window and workday calendar. Both decide which squares offer a
    /// decision, so a failure leaves the defaults rather than refusing to show the year.
    /// </summary>
    private async Task<Settings?> LoadCalendarSettingsAsync()
    {
        if (_settingsService is null)
            return null;

        try
        {
            return await _settingsService.LoadAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalendarViewModel.LoadCalendarSettings failed: {ex.GetType().Name}");
            return null;
        }
    }

    private async Task<(List<ImportedOutlookEvent> Events, string Warning)> LoadOutlookEventsAsync(
        int userId,
        int year)
    {
        if (_outlookCalendarService is null)
            return ([], string.Empty);

        try
        {
            var events = await _outlookCalendarService.GetByYearAsync(userId, year);
            return (events.ToList(), string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   CryptographicException or JsonException)
        {
            Debug.WriteLine($"CalendarViewModel.LoadOutlookEvents failed: {ex.GetType().Name}");
            return ([], "Sati loaded its calendar, but the saved Outlook import could not be read on this Windows account.");
        }
    }

    private void NotifyCalendarComputedProperties()
    {
        OnPropertyChanged(nameof(SelectedDayNotes));
        OnPropertyChanged(nameof(SelectedDayOutlookEvents));
        OnPropertyChanged(nameof(SelectedDayTotalMinutes));
        OnPropertyChanged(nameof(SelectedDayTotalUnits));
        OnPropertyChanged(nameof(SelectedDaySummary));
        OnPropertyChanged(nameof(SelectedDayExemptActionLabel));
        OnPropertyChanged(nameof(ExemptDaysForSelectedMonth));
        OnPropertyChanged(nameof(SelectedMonthName));
        OnPropertyChanged(nameof(SelectedCalendarMonth));
        OnPropertyChanged(nameof(HasSelectedDay));
    }

    partial void OnSelectedDayChanged(CalendarDay? value) =>
        NotifyCalendarComputedProperties();

    partial void OnSelectedMonthChanged(int value)
    {
        OnPropertyChanged(nameof(ExemptDaysForSelectedMonth));
        OnPropertyChanged(nameof(SelectedMonthName));
        OnPropertyChanged(nameof(SelectedCalendarMonth));
    }

    partial void OnCurrentYearChanged(int value)
    {
        OnPropertyChanged(nameof(ExemptDaysForSelectedMonth));
        OnPropertyChanged(nameof(SelectedMonthName));
        OnPropertyChanged(nameof(SelectedCalendarMonth));
    }

    partial void OnStatusMessageChanged(string value) =>
        OnPropertyChanged(nameof(HasStatusMessage));
}
