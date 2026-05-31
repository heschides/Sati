using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Helpers;
using Sati.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Sati.ViewModels
{
    public partial class SchedulerViewModel : ObservableObject
    {

        //FIELDS
        private readonly ISessionService _sessionService;
        private readonly IIncentiveService _incentiveService;
        private readonly ISettingsService _settingsService;
        private Incentive? _incentive;
        private Settings? _settings;


        //EVENTS

        //PROPERTIES
        [ObservableProperty] private int currentMonth;
        [ObservableProperty] private int currentYear;
        [ObservableProperty] private string monthLabel = string.Empty;
        public int DaysScheduled => _incentive?.DaysScheduled ?? 0;

        public ObservableCollection<WorkdayTile> Tiles { get; } = [];


        //CONSTRUCTOR
        public SchedulerViewModel(IIncentiveService incentiveService, ISettingsService settingsService, ISessionService sessionService)
        {
            _incentiveService = incentiveService;
            _settingsService = settingsService;
            CurrentMonth = DateTime.Now.Month;
            CurrentYear = DateTime.Now.Year;
            _sessionService = sessionService;
        }

        //RELAY METHODS
        [RelayCommand]
        private async Task ToggleTile(WorkdayTile tile)
        {
            if (!tile.IsInteractable) return;
            if (_incentive is null) return;

            tile.IsExcluded = !tile.IsExcluded;

            // ExcludedDates getter deserializes; setter re-serializes.
            // We must read, mutate, then write back to trigger serialization.
            var excluded = _incentive.ExcludedDates;
            if (tile.IsExcluded)
                excluded.Add(tile.Date);
            else
                excluded.Remove(tile.Date);
            _incentive.ExcludedDates = excluded;

            _incentive.DaysScheduled = Tiles.Count(t => !t.IsExcluded);
            await _incentiveService.SaveAsync(_incentive);
            OnPropertyChanged(nameof(DaysScheduled));
        }

        [RelayCommand]
        private async Task PreviousMonth()
        {
            if (CurrentMonth == 1)
            {
                CurrentMonth = 12;
                CurrentYear--;
            }
            else
            {
                CurrentMonth--;
            }
            await LoadMonthAsync();
        }

        [RelayCommand]
        private async Task NextMonth()
        {
            if (CurrentMonth == 12)
            {
                CurrentMonth = 1;
                CurrentYear++;
            }
            else
            {
                CurrentMonth++;
            }
            await LoadMonthAsync();
        }



        //FUNCTIONS
        private async Task LoadMonthAsync()
        {
            try
            {
                var userId = _sessionService.CurrentUser!.Id;
                _settings = await _settingsService.LoadAsync();
                var (incentive, _) = await _incentiveService.GetOrCreateAsync(userId, CurrentMonth, CurrentYear);
                MonthLabel = new DateTime(CurrentYear, CurrentMonth, 1).ToString("MMMM yyyy");
                _incentive = incentive;
                BuildTiles();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadMonthAsync failed: {ex.Message}");
            }
        }

        private void BuildTiles()
        {
            Tiles.Clear();
            var daysInMonth = DateTime.DaysInMonth(CurrentYear, CurrentMonth);

            // Normalize stored excluded dates to midnight for reliable comparison.
            var manuallyExcluded = (_incentive?.ExcludedDates ?? [])
                .Select(d => d.Date)
                .ToHashSet();

            for (int day = 1; day <= daysInMonth; day++)
            {
                var date = new DateTime(CurrentYear, CurrentMonth, day);
                var dow = date.DayOfWeek;

                if (dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday)
                    continue;

                var letter = dow switch
                {
                    DayOfWeek.Monday => "M",
                    DayOfWeek.Tuesday => "T",
                    DayOfWeek.Wednesday => "W",
                    DayOfWeek.Thursday => "Th",
                    _ => "F"
                };

                // Always-excluded: weekday flags or federal holidays from Settings.
                // Non-interactable so the user cannot toggle them.
                var alwaysExcluded = WorkdayHelper.IsAlwaysExcludedWorkday(date, _settings!);

                Tiles.Add(new WorkdayTile
                {
                    Date = date,
                    Letter = letter,
                    IsInteractable = !alwaysExcluded,
                    IsExcluded = alwaysExcluded || manuallyExcluded.Contains(date)
                });
            }

            // Auto-correct a stale DaysScheduled (e.g. existing records created before
            // this fix). Only saves when the count has actually drifted.
            if (_incentive is not null)
            {
                var computed = Tiles.Count(t => !t.IsExcluded);
                if (computed != _incentive.DaysScheduled)
                {
                    _incentive.DaysScheduled = computed;
                    _ = _incentiveService.SaveAsync(_incentive);
                }
                OnPropertyChanged(nameof(DaysScheduled));
            }
        }

        public void Initialize()
        {
            _ = LoadMonthAsync();
        }

    }
}
