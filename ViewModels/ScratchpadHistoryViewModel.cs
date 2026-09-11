using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Sati.ViewModels
{
    public partial class ScratchpadHistoryViewModel : ObservableObject
    {
        private readonly IScratchpadService _scratchpadService;
        private readonly ISessionService _sessionService;
        private readonly List<Scratchpad> _allEntries = [];
        private readonly LatestRequestTracker _historyLoads = new();
        private int? _loadedUserId;

        public ScratchpadHistoryViewModel(
            IScratchpadService scratchpadService,
            ISessionService sessionService)
        {
            _scratchpadService = scratchpadService;
            _sessionService = sessionService;
        }

        [ObservableProperty] private DateTime? selectionStart;
        [ObservableProperty] private DateTime? selectionEnd;
        [ObservableProperty] private bool isEditorUnlocked;
        [ObservableProperty] private bool isLoading;
        [ObservableProperty] private bool isSaving;
        [ObservableProperty] private string newComment = string.Empty;
        [ObservableProperty] private string? statusMessage;

        public ObservableCollection<Scratchpad> VisibleEntries { get; } = [];
        public DateTime LatestSelectableDate => DateTime.Today.AddDays(-1);
        public DateTime InitialDisplayDate { get; private set; } = DateTime.Today.AddDays(-1);
        public DateTime InitialSelectedDate { get; private set; } = DateTime.Today.AddDays(-1);

        public bool HasVisibleEntries => VisibleEntries.Count > 0;
        public bool IsSelectionEmpty => !HasVisibleEntries;
        public bool IsSingleDateSelection =>
            SelectionStart.HasValue && SelectionStart.Value.Date == SelectionEnd?.Date;
        public bool CanToggleEditor =>
            IsEditorUnlocked || (IsSingleDateSelection && VisibleEntries.Count == 1);
        public bool CanSaveComment =>
            IsEditorUnlocked && !IsSaving && !string.IsNullOrWhiteSpace(NewComment);

        public string SelectionSummary
        {
            get
            {
                if (!SelectionStart.HasValue)
                    return "Choose a date";

                if (SelectionStart.Value.Date == SelectionEnd?.Date)
                    return SelectionStart.Value.ToString("dddd, MMMM d, yyyy");

                return $"{SelectionStart.Value:MMM d, yyyy} – {SelectionEnd:MMM d, yyyy}";
            }
        }

        public string EntryCountSummary => VisibleEntries.Count switch
        {
            0 => "No scratchpad entries in this selection",
            1 => "1 scratchpad entry",
            _ => $"{VisibleEntries.Count} scratchpad entries"
        };

        public string EditorToolTip => IsEditorUnlocked
            ? "Lock retrospective comments"
            : CanToggleEditor
                ? "Unlock to add a clearly marked retrospective comment"
                : "Select one date with a scratchpad entry to add a comment";

        public async Task InitializeAsync()
        {
            var user = _sessionService.CurrentUser;
            if (user is null)
            {
                Clear();
                return;
            }

            var request = _historyLoads.Begin();
            var previousStart = _loadedUserId == user.Id ? SelectionStart : null;
            var previousEnd = _loadedUserId == user.Id ? SelectionEnd : null;
            IsLoading = true;
            StatusMessage = null;

            try
            {
                var entries = await _scratchpadService.GetHistoryAsync(user.Id);
                if (!_historyLoads.IsCurrent(request) ||
                    _sessionService.CurrentUser?.Id != user.Id)
                {
                    return;
                }

                _allEntries.Clear();
                _allEntries.AddRange(entries);
                _loadedUserId = user.Id;

                var initialDate = _allEntries.FirstOrDefault()?.Date.Date ?? LatestSelectableDate;
                InitialDisplayDate = previousStart ?? initialDate;
                InitialSelectedDate = previousStart ?? initialDate;
                SetSelectedDates(previousStart.HasValue && previousEnd.HasValue
                    ? DatesInRange(previousStart.Value, previousEnd.Value)
                    : [initialDate]);
            }
            catch (Exception ex)
            {
                if (!_historyLoads.IsCurrent(request) ||
                    _sessionService.CurrentUser?.Id != user.Id)
                {
                    return;
                }

                Debug.WriteLine($"ScratchpadHistoryViewModel.InitializeAsync failed: {ex.Message}");
                var reference = AppErrorLog.Record(ex, "scratchpad.history.load");
                _allEntries.Clear();
                VisibleEntries.Clear();
                NotifySelectionStateChanged();
                StatusMessage =
                    "Scratchpad history could not be loaded. Return to History to try again. " +
                    $"Support reference: {reference}.";
            }
            finally
            {
                if (_historyLoads.IsCurrent(request))
                    IsLoading = false;
            }
        }

        public void Clear()
        {
            _historyLoads.Invalidate();
            _loadedUserId = null;
            _allEntries.Clear();
            VisibleEntries.Clear();
            SelectionStart = null;
            SelectionEnd = null;
            IsEditorUnlocked = false;
            IsLoading = false;
            IsSaving = false;
            NewComment = string.Empty;
            StatusMessage = null;
            InitialDisplayDate = LatestSelectableDate;
            InitialSelectedDate = LatestSelectableDate;
            NotifySelectionStateChanged();
        }

        public void SetSelectedDates(IEnumerable<DateTime> dates)
        {
            var selectedDates = dates
                .Select(date => date.Date)
                .Distinct()
                .OrderBy(date => date)
                .ToList();

            SelectionStart = selectedDates.FirstOrDefault();
            SelectionEnd = selectedDates.LastOrDefault();
            if (selectedDates.Count == 0)
            {
                SelectionStart = null;
                SelectionEnd = null;
            }

            IsEditorUnlocked = false;
            NewComment = string.Empty;
            StatusMessage = null;
            RefreshVisibleEntries();
        }

        [RelayCommand]
        private void ToggleEditor()
        {
            if (!CanToggleEditor)
                return;

            IsEditorUnlocked = !IsEditorUnlocked;
            StatusMessage = null;
            NotifyEditorStateChanged();
        }

        [RelayCommand]
        private void CancelComment()
        {
            NewComment = string.Empty;
            IsEditorUnlocked = false;
            StatusMessage = null;
            NotifyEditorStateChanged();
        }

        [RelayCommand]
        private async Task SaveCommentAsync()
        {
            if (!CanSaveComment || VisibleEntries.Count != 1)
                return;

            var user = _sessionService.CurrentUser;
            if (user is null)
                return;

            try
            {
                IsSaving = true;
                StatusMessage = null;
                var entry = VisibleEntries[0];
                var savedComment = await _scratchpadService.AddCommentAsync(
                    entry.Id,
                    user.Id,
                    user.DisplayName,
                    NewComment);
                if (_sessionService.CurrentUser?.Id != user.Id)
                    return;

                entry.Comments.Add(savedComment);
                NewComment = string.Empty;
                IsEditorUnlocked = false;
                StatusMessage = "Comment saved.";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ScratchpadHistoryViewModel.SaveCommentAsync failed: {ex.Message}");
                StatusMessage = "The retrospective comment could not be saved. Your original entry was not changed.";
            }
            finally
            {
                IsSaving = false;
                NotifyEditorStateChanged();
            }
        }

        partial void OnNewCommentChanged(string value) => NotifyEditorStateChanged();
        partial void OnIsEditorUnlockedChanged(bool value) => NotifyEditorStateChanged();
        partial void OnIsSavingChanged(bool value) => NotifyEditorStateChanged();

        private void RefreshVisibleEntries()
        {
            VisibleEntries.Clear();

            if (SelectionStart.HasValue && SelectionEnd.HasValue)
            {
                foreach (var entry in _allEntries.Where(entry =>
                             entry.Date.Date >= SelectionStart.Value.Date &&
                             entry.Date.Date <= SelectionEnd.Value.Date))
                {
                    VisibleEntries.Add(entry);
                }
            }

            NotifySelectionStateChanged();
        }

        private void NotifySelectionStateChanged()
        {
            OnPropertyChanged(nameof(HasVisibleEntries));
            OnPropertyChanged(nameof(IsSelectionEmpty));
            OnPropertyChanged(nameof(IsSingleDateSelection));
            OnPropertyChanged(nameof(CanToggleEditor));
            OnPropertyChanged(nameof(SelectionSummary));
            OnPropertyChanged(nameof(EntryCountSummary));
            OnPropertyChanged(nameof(EditorToolTip));
            NotifyEditorStateChanged();
        }

        private static IEnumerable<DateTime> DatesInRange(DateTime start, DateTime end)
        {
            for (var date = start.Date; date <= end.Date; date = date.AddDays(1))
                yield return date;
        }

        private void NotifyEditorStateChanged()
        {
            OnPropertyChanged(nameof(CanToggleEditor));
            OnPropertyChanged(nameof(CanSaveComment));
            OnPropertyChanged(nameof(EditorToolTip));
        }
    }
}
