using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using System.Diagnostics;
using System.Windows.Media;

namespace Sati.ViewModels.Children
{
    public partial class CalendarViewModel : ObservableObject
    {
        // -------------------------------------------------------------------------
        // Services
        // -------------------------------------------------------------------------

        private readonly IExemptDateService _exemptDateService;
        private readonly INoteService _noteService;
        private readonly ISessionService _sessionService;

        // -------------------------------------------------------------------------
        // Private state
        // -------------------------------------------------------------------------

        private List<ExemptDate> _exemptDates = [];
        private List<Note> _yearNotes = [];

        // -------------------------------------------------------------------------
        // Observable properties
        // -------------------------------------------------------------------------

        [ObservableProperty] private int currentYear = DateTime.Today.Year;
        [ObservableProperty] private CalendarDay? selectedDay;
        [ObservableProperty] private int selectedMonth = DateTime.Today.Month;
        [ObservableProperty] private List<CalendarMonth> months = [];

        // -------------------------------------------------------------------------
        // Computed properties
        // -------------------------------------------------------------------------

        public IEnumerable<Note> SelectedDayNotes =>
            SelectedDay?.Notes ?? Enumerable.Empty<Note>();

        public List<ExemptDate> ExemptDaysForSelectedMonth =>
     _exemptDates
         .Where(e => e.Date.Month == SelectedMonth && e.Date.Year == CurrentYear)
         .OrderBy(e => e.Date)
         .ToList();

        public string SelectedMonthName =>
            new DateTime(CurrentYear, SelectedMonth, 1).ToString("MMMM");

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public CalendarViewModel(
            IExemptDateService exemptDateService,
            INoteService noteService,
            ISessionService sessionService)
        {
            _exemptDateService = exemptDateService;
            _noteService = noteService;
            _sessionService = sessionService;
        }

        // -------------------------------------------------------------------------
        // Initialization
        // -------------------------------------------------------------------------

        public async Task InitializeAsync()
        {
            await LoadYearAsync();
        }

        // -------------------------------------------------------------------------
        // Commands
        // -------------------------------------------------------------------------

        [RelayCommand]
        private void SelectDay(CalendarDay? day)
        {
            if (day is null) return;
            SelectedDay = day;
            SelectedMonth = day.Date.Month;
            OnPropertyChanged(nameof(SelectedDayNotes));
            OnPropertyChanged(nameof(ExemptDaysForSelectedMonth));
            OnPropertyChanged(nameof(SelectedMonthName));
        }

        [RelayCommand]
        private async Task ToggleExempt(CalendarDay? day)
        {
            if (day is null) return;
            var userId = _sessionService.CurrentUser!.Id;

            if (day.IsExempt && day.ExemptDateId.HasValue)
            {
                await _exemptDateService.RemoveAsync(day.ExemptDateId.Value);
                _exemptDates.RemoveAll(e => e.Date.Date == day.Date.Date);
                day.IsExempt = false;
                day.ExemptDateId = null;
            }
            else
            {
                var exempt = await _exemptDateService.AddAsync(userId, day.Date);
                _exemptDates.Add(exempt);
                day.IsExempt = true;
                day.ExemptDateId = exempt.Id;
            }

            // Rebuild so the calendar grid reflects the change
            BuildMonths();
            OnPropertyChanged(nameof(ExemptDaysForSelectedMonth));
        }

        [RelayCommand]
        private async Task PreviousYear()
        {
            CurrentYear--;
            SelectedDay = null;
            await LoadYearAsync();
        }

        [RelayCommand]
        private async Task NextYear()
        {
            CurrentYear++;
            SelectedDay = null;
            await LoadYearAsync();
        }

        // -------------------------------------------------------------------------
        // Private methods
        // -------------------------------------------------------------------------

        private async Task LoadYearAsync()
        {
            try
            {
                var userId = _sessionService.CurrentUser!.Id;
                _exemptDates = await _exemptDateService.GetByYearAsync(userId, CurrentYear);
                _yearNotes = await _noteService.GetByYearAsync(userId, CurrentYear);
                BuildMonths();
                OnPropertyChanged(nameof(ExemptDaysForSelectedMonth));
                OnPropertyChanged(nameof(SelectedMonthName));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CalendarViewModel.LoadYearAsync failed: {ex.Message}");
            }
        }

        private void BuildMonths()
        {
            var result = new List<CalendarMonth>();

            for (int m = 1; m <= 12; m++)
            {
                var firstDay = new DateTime(CurrentYear, m, 1);
                var daysInMonth = DateTime.DaysInMonth(CurrentYear, m);
                var startOffset = (int)firstDay.DayOfWeek; // 0 = Sunday

                var cells = new List<CalendarDay?>();

                // Leading nulls to align the first day to the correct column
                for (int i = 0; i < startOffset; i++)
                    cells.Add(null);

                for (int d = 1; d <= daysInMonth; d++)
                {
                    var date = new DateTime(CurrentYear, m, d);
                    var exemptEntry = _exemptDates.FirstOrDefault(e => e.Date.Date == date.Date);
                    var notesForDay = _yearNotes
                        .Where(n => n.EventDate.HasValue && n.EventDate.Value.Date == date.Date)
                        .ToList();

                    cells.Add(new CalendarDay
                    {
                        Date = date,
                        IsExempt = exemptEntry is not null,
                        ExemptDateId = exemptEntry?.Id,
                        IsWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                        Notes = notesForDay
                    });
                }

                result.Add(new CalendarMonth
                {
                    Name = firstDay.ToString("MMMM"),
                    Month = m,
                    Year = CurrentYear,
                    Cells = cells
                });
            }

            Months = result;
        }
    }
}