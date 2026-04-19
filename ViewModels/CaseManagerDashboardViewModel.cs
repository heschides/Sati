using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using Sati.Views;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace Sati.ViewModels
{
    public partial class CaseManagerDashboardViewModel : ObservableObject
    {

        // -------------------------------------------------------------------------
        // Services & private state
        // -------------------------------------------------------------------------

        private readonly IPersonService _personService;
        private readonly INoteService _noteService;
        private readonly ISettingsService _settingsService;
        private readonly IIncentiveService _incentiveService;
        private readonly ISessionService _sessionService;
        private readonly IUpcomingEventService _upcomingEventService;
        private readonly IFormService _formService;
        private readonly Func<string, UserMessageDialog> _validationDialog;
         
        private Settings? _settings;
        private Incentive? _incentive;
        private List<Note> _monthlyNotes = [];
        private DateTime _lastAbandonmentCheck = DateTime.Now;
        private SchedulerViewModel _schedulerViewModel;
        private int _daysWorkedToDate;

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public CaseManagerDashboardViewModel(
            IPersonService personService,
            INoteService noteService,
            ISettingsService settingsService,
            IIncentiveService incentiveService,
            ISessionService sessionService,
            IUpcomingEventService upcomingEventService,
            IFormService formService,
            Func<string, UserMessageDialog> validationDialog,
            SchedulerViewModel schedulerViewModel,
            NotesWindowViewModel notesWindowViewModel,
            NewClientViewModel newClientViewModel
            )
        {
            _personService = personService;
            _noteService = noteService;
            _settingsService = settingsService;
            _incentiveService = incentiveService;
            _sessionService = sessionService;
            _upcomingEventService = upcomingEventService;
            _formService = formService;
            _validationDialog = validationDialog;

            NotesView = CollectionViewSource.GetDefaultView(Notes);
            NotesView.Filter = FilterNotes;
            _schedulerViewModel = schedulerViewModel;
            NotesLog = notesWindowViewModel;
            Clients = newClientViewModel;
        }

        // -------------------------------------------------------------------------
        // Events
        // -------------------------------------------------------------------------

        public event EventHandler<bool>? PromptSchedulerRequested;
        public event EventHandler<FormType>? MarkFormCompleteRequested;

        // -------------------------------------------------------------------------
        // Properties
        // -------------------------------------------------------------------------

        public NotesWindowViewModel NotesLog { get; }
        public NewClientViewModel Clients { get; }
        public CaseloadMatrixViewModel? Matrix { get; private set; }

        public bool IsDashboardSubActive => CurrentSubViewModel is null;
        public bool IsClientsSubActive => CurrentSubViewModel is NewClientViewModel;
        public bool IsNotesLogSubActive => CurrentSubViewModel is NotesWindowViewModel;
        public bool IsMatrixSubActive => CurrentSubViewModel is CaseloadMatrixViewModel;
        public bool IsSubViewActive => CurrentSubViewModel is not null;

        [ObservableProperty] private object? currentSubViewModel;
        [ObservableProperty] private User? loggedInUser;
        [ObservableProperty] private Person? selectedPerson;
        [ObservableProperty] private Note? selectedNote;
        [ObservableProperty] private string? searchText;
        [ObservableProperty] private NoteStatus? filterStatus;
        [ObservableProperty] private NoteStatus? status;
        [ObservableProperty] private NoteType? selectedNoteType;
        [ObservableProperty] private FormType? selectedFormType;
        [ObservableProperty] private string? narrative;
        [ObservableProperty] private DateTime? eventDate;
        [ObservableProperty] private decimal? units;
        [ObservableProperty] private int? duration;
        [ObservableProperty] private bool isEditing;
        [ObservableProperty] private bool isSchedulerOpen;
        [ObservableProperty] private bool sortByDate = true;
        [ObservableProperty] private int daysScheduled;
        [ObservableProperty] private double narrativeFontSize = 14;

        // -------------------------------------------------------------------------
        // Property change callbacks
        // -------------------------------------------------------------------------

        partial void OnCurrentSubViewModelChanged(object? value)
        {
            OnPropertyChanged(nameof(IsDashboardSubActive));
            OnPropertyChanged(nameof(IsClientsSubActive));
            OnPropertyChanged(nameof(IsNotesLogSubActive));
            OnPropertyChanged(nameof(IsMatrixSubActive));
            OnPropertyChanged(nameof(IsSubViewActive));
        }



        partial void OnSelectedPersonChanged(Person? value)
        {
            LoadNotesForPersonAsync(value);
            RefreshComplianceFlags();
            IsEditing = false;
            Status = null;
            Narrative = string.Empty;
            EventDate = null;
            Units = null;
            SelectedNoteType = null;
            SelectedFormType = null;
        }

        partial void OnIsSchedulerOpenChanged(bool value)
        {
            if (value)
                _schedulerViewModel.Initialize();
            else
                _ = RefreshIncentiveAsync();
        }

        partial void OnSortByDateChanged(bool value)
        {
            OnPropertyChanged(nameof(FormEvents));
            OnPropertyChanged(nameof(VisitEvents));
            OnPropertyChanged(nameof(ContactEvents));
        }

        partial void OnSearchTextChanged(string? value) => NotesView.Refresh();
        partial void OnFilterStatusChanged(NoteStatus? value) => NotesView.Refresh();

        partial void OnSelectedNoteTypeChanged(NoteType? value)
        {
            OnPropertyChanged(nameof(IsFormNote));

            if (value != NoteType.Form)
                SelectedFormType = null;

            if (value is null || !string.IsNullOrWhiteSpace(Narrative))
                return;

            Narrative = value.Value switch
            {
                NoteType.Visit => _settings?.VisitTemplate ?? string.Empty,
                NoteType.Contact => _settings?.ContactTemplate ?? string.Empty,
                _ => string.Empty
            };
        }

        partial void OnSelectedFormTypeChanged(FormType? value)
        {
            if (value is null || SelectedPerson is null || !string.IsNullOrWhiteSpace(Narrative))
                return;

            var user = _sessionService.CurrentUser?.DisplayName ?? "Case Manager";
            var client = SelectedPerson.FullName;

            Narrative = value switch
            {
                FormType.Q1R => $"{user} completed the Q1 90-Day Review for {client}.",
                FormType.Q2R => $"{user} completed the Q2 90-Day Review for {client}.",
                FormType.Q3R => $"{user} completed the Q3 90-Day Review for {client}.",
                FormType.Q4R => $"{user} completed the Q4 90-Day Review for {client}.",
                FormType.PCP => $"{user} attached the signed PCP and set the plan's status to \"Complete.\"",
                FormType.ComprehensiveAssessment => $"{user} completed the Comprehensive Assessment for {client}.",
                FormType.Reclassification => $"{user} completed the Reclassification for {client}.",
                FormType.SafetyPlan => $"{user} received the signed Safety Plan from {client} and filed it with annual documentation.",
                FormType.PrivacyPractices => $"{user} received the signed Privacy Practices form from {client} and filed it with annual documentation.",
                FormType.Release_Agency => $"{user} received the signed Agency Release from {client} and filed it with annual documentation.",
                FormType.Release_DHHS => $"{user} received the signed DHHS Release from {client} and filed it with annual documentation.",
                FormType.Release_Medical => $"{user} received the signed Medical Release from {client} and filed it with annual documentation.",
                _ => string.Empty
            };
        }

        // -------------------------------------------------------------------------
        // Collections & computed properties
        // -------------------------------------------------------------------------

        public ObservableCollection<Note> Notes { get; } = [];
        public ObservableCollection<Person> People { get; } = [];
        public ObservableCollection<UpcomingEvent> UpcomingEvents { get; } = [];
        public SchedulerViewModel Scheduler => _schedulerViewModel;
        public record EffectiveDateGroup(string Label, bool IsCurrent, List<string> ClientNames);
        public double DailyAverageUnits
        {
            get
            {
                if (_daysWorkedToDate <= 0) return 0;
                var total = (PendingUnits ?? 0) + (LoggedUnits ?? 0);
                return Math.Round((double)total / _daysWorkedToDate, 1);
            }
        }
        public ICollectionView NotesView { get; }

        public static Array NoteStatusOptions => Enum.GetValues(typeof(NoteStatus));
        public Array FormTypes => Enum.GetValues(typeof(FormType));

        public IEnumerable<UpcomingEvent> FormEvents => SortByDate
            ? UpcomingEvents.Where(e => e.Kind is UpcomingEventKind.OpenReview or UpcomingEventKind.LateReview or UpcomingEventKind.ScheduledForm).OrderBy(e => e.Date)
            : UpcomingEvents.Where(e => e.Kind is UpcomingEventKind.OpenReview or UpcomingEventKind.LateReview or UpcomingEventKind.ScheduledForm).OrderBy(e => e.Kind);

        public IEnumerable<UpcomingEvent> VisitEvents => SortByDate
            ? UpcomingEvents.Where(e => e.Kind == UpcomingEventKind.ScheduledVisit).OrderBy(e => e.Date)
            : UpcomingEvents.Where(e => e.Kind == UpcomingEventKind.ScheduledVisit).OrderBy(e => e.Kind);

        public IEnumerable<UpcomingEvent> ContactEvents => SortByDate
            ? UpcomingEvents.Where(e => e.Kind == UpcomingEventKind.ScheduledContact).OrderBy(e => e.Date)
            : UpcomingEvents.Where(e => e.Kind == UpcomingEventKind.ScheduledContact).OrderBy(e => e.Kind);

        public bool IsFormNote => SelectedNoteType == NoteType.Form;
        public int Threshold => _incentive?.Threshold ?? 0;
        public int SafeThreshold => Threshold > 0 ? Threshold : 1;

        public decimal? PendingUnits => _monthlyNotes.Where(n => n.Status == NoteStatus.Pending).Sum(n => n.Units);
        public decimal? LoggedUnits => _monthlyNotes.Where(n => n.Status == NoteStatus.Logged).Sum(n => n.Units);
        public decimal? AbandonedUnits => _monthlyNotes.Where(n => n.Status == NoteStatus.Abandoned).Sum(n => n.Units);

        public decimal EstimatedIncentive => _incentive?.Calculate(LoggedUnits ?? 0) ?? 0;

        // -------------------------------------------------------------------------
        // Compliance flags
        // -------------------------------------------------------------------------

        public bool Q1RCompliant => SelectedPerson?.GetCurrentCycleForm(FormType.Q1R)?.IsCompliant ?? false;
        public bool Q2RCompliant => SelectedPerson?.GetCurrentCycleForm(FormType.Q2R)?.IsCompliant ?? false;
        public bool Q3RCompliant => SelectedPerson?.GetCurrentCycleForm(FormType.Q3R)?.IsCompliant ?? false;
        public bool Q4RCompliant => SelectedPerson?.GetCurrentCycleForm(FormType.Q4R)?.IsCompliant ?? false;
        public bool PcpCompliant => SelectedPerson?.GetCurrentCycleForm(FormType.PCP)?.IsCompliant ?? false;
        public bool CompAssessmentCompliant => SelectedPerson?.GetCurrentCycleForm(FormType.ComprehensiveAssessment)?.IsCompliant ?? false;
        public bool ReclassificationCompliant => SelectedPerson?.GetCurrentCycleForm(FormType.Reclassification)?.IsCompliant ?? false;
        public bool SafetyPlanCompliant => SelectedPerson?.GetCurrentCycleForm(FormType.SafetyPlan)?.IsCompliant ?? false;
        public bool PrivacyPracticesCompliant => SelectedPerson?.GetCurrentCycleForm(FormType.PrivacyPractices)?.IsCompliant ?? false;
        public bool ReleaseAgencyCompliant => SelectedPerson?.GetCurrentCycleForm(FormType.Release_Agency)?.IsCompliant ?? false;
        public bool ReleaseDhhsCompliant => SelectedPerson?.GetCurrentCycleForm(FormType.Release_DHHS)?.IsCompliant ?? false;
        public bool ReleaseMedicalCompliant => SelectedPerson?.GetCurrentCycleForm(FormType.Release_Medical)?.IsCompliant ?? false;

        // -------------------------------------------------------------------------
        // Commands
        // -------------------------------------------------------------------------
        [RelayCommand] private void NavigateToOverview() => CurrentSubViewModel = null;
        [RelayCommand] private void NavigateToClients() => CurrentSubViewModel = Clients;
        [RelayCommand] private void NavigateToNotesLog() => CurrentSubViewModel = NotesLog;
        [RelayCommand] private void NavigateToMatrix() => CurrentSubViewModel = Matrix;
        [RelayCommand] private void OpenScheduler() => IsSchedulerOpen = !IsSchedulerOpen;

        [RelayCommand] private void IncreaseNarrativeFont() => NarrativeFontSize = Math.Min(NarrativeFontSize + 2, 28);
        [RelayCommand] private void DecreaseNarrativeFont() => NarrativeFontSize = Math.Max(NarrativeFontSize - 2, 10);

        [RelayCommand]
        private async Task DeleteNote()
        {
            if (SelectedNote is null)
                return;

            await _noteService.DeleteNoteAsync(SelectedNote);
            Notes.Remove(SelectedNote);
            SelectedPerson?.Notes.Remove(SelectedNote);
            await LoadMonthlyNotesAsync();
            await LoadUpcomingEventsAsync();
            await NotesLog.ReloadAsync();
            await Clients.ReloadAsync();
            SelectedNote = null;
            ResetForm();
        }

        [RelayCommand]
        private async Task ToggleForm(FormType type)
        {
            if (SelectedPerson is null)
                return;

            var form = SelectedPerson.GetCurrentCycleForm(type);
            if (form is null)
                return;

            form.IsCompliant = !form.IsCompliant;
            form.CompletedDate = form.IsCompliant ? DateTime.Today : null;
            await _formService.UpdateFormAsync(form);
            RefreshComplianceFlags();
        }

        [RelayCommand]
        public async Task SubmitNote()
        {
            var errors = new List<string>();

            if (SelectedPerson is null) errors.Add("• Please select a client.");
            if (Status is null) errors.Add("• Please select a status.");
            if (EventDate is null) errors.Add("• Please enter a date.");
            if (string.IsNullOrWhiteSpace(Narrative)) errors.Add("• Please enter a narrative.");
            if (SelectedNoteType is null) errors.Add("• Please select a note type.");

            if (errors.Count > 0)
            {
                var dialog = new UserMessageDialog(string.Join("\n", errors)) { Owner = Application.Current.MainWindow };
                dialog.ShowDialog();
                return;
            }

            try
            {
                if (!IsEditing)
                    await SubmitNewNoteAsync();
                else
                    await SubmitEditedNoteAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SubmitNote failed: {ex.Message}");
                MessageBox.Show(
                    "Sati encountered an error saving your note. Please try again.",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // -------------------------------------------------------------------------
        // Initialization
        // -------------------------------------------------------------------------

        public void Initialize()
        {
            LoggedInUser = _sessionService.CurrentUser;
            _ = LoadAsync();
        }

        // -------------------------------------------------------------------------
        // Private methods
        // -------------------------------------------------------------------------

        private async Task LoadAsync()
        {
            try
            {
                if (LoggedInUser is null)
                    return;

                _settings = await _settingsService.LoadAsync();
                await LoadPeopleAsync();
                Matrix = new CaseloadMatrixViewModel(People, _settings);
                OnPropertyChanged(nameof(Matrix));
                OnPropertyChanged(nameof(EffectiveDateGroups));
                await _noteService.UpdateAbandonedNotesAsync(_settings.AbandonedAfterDays);
                await LoadMonthlyNotesAsync();
                await LoadUpcomingEventsAsync();

                var (_, wasCreated) = await _incentiveService.GetOrCreateAsync(
    LoggedInUser!.Id, DateTime.Now.Month, DateTime.Now.Year);
                await RefreshIncentiveAsync();
                _daysWorkedToDate = await _incentiveService.GetDaysWorkedToDateAsync(DateTime.Now.Month, DateTime.Now.Year);
                if (wasCreated)
                    PromptSchedulerRequested?.Invoke(this, true);

                StartAbandonmentTimer();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadAsync failed: {ex.Message}");
                MessageBox.Show(
                    "Sati encountered an error loading your data. Please restart the application.",
                    "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task SubmitNewNoteAsync()
        {
            var note = Note.Create(Narrative!, EventDate, Status, Units, SelectedPerson!.Id, SelectedFormType, SelectedNoteType);
            var savedNote = await _noteService.AddNoteAsync(note);
            Notes.Insert(0, savedNote);

            if (EventDate.HasValue &&
                (EventDate.Value.Month != DateTime.Now.Month || EventDate.Value.Year != DateTime.Now.Year))
            {
                MessageBox.Show(
                    $"This note's date ({EventDate.Value:MMM d, yyyy}) is outside the current month and will not appear in this month's productivity totals.",
                    "Note Outside Current Month", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            NotesView.Refresh();
            var formType = SelectedFormType;
            if (formType.HasValue && Status is NoteStatus.Pending or NoteStatus.Logged)
                MarkFormCompleteRequested?.Invoke(this, formType.Value);

            ResetForm();
            await LoadPeopleAsync();
            await LoadMonthlyNotesAsync();
            await LoadUpcomingEventsAsync();
            await NotesLog.ReloadAsync();
            await Clients.ReloadAsync();
        }

        private async Task SubmitEditedNoteAsync()
        {
            if (SelectedNote is null)
                return;

            SelectedNote.Narrative = Narrative!;
            SelectedNote.EventDate = EventDate;
            SelectedNote.Units = Units ?? 0;
            SelectedNote.Status = Status;
            SelectedNote.NoteType = SelectedNoteType;
            SelectedNote.FormType = SelectedFormType;

            await _noteService.UpdateNoteAsync(SelectedNote);
            NotesView.Refresh();

            var formType = SelectedNote.FormType;
            if (formType.HasValue && Status is NoteStatus.Pending or NoteStatus.Logged)
                MarkFormCompleteRequested?.Invoke(this, formType.Value);

            IsEditing = false;
            ResetForm();
            await LoadPeopleAsync();
            await LoadMonthlyNotesAsync();
            await LoadUpcomingEventsAsync();
            await NotesLog.ReloadAsync();
            await Clients.ReloadAsync();
        }

        private async void LoadNotesForPersonAsync(Person? person)
        {
            if (person is null)
            {
                Notes.Clear();
                return;
            }

            var notes = await _noteService.GetAllByPersonAsync(person.Id);
            Notes.Clear();
            foreach (var note in notes)
                Notes.Add(note);
        }

        public bool FilterNotes(object obj)
        {
            if (obj is not Note note)
                return false;

            var matchesText = string.IsNullOrWhiteSpace(SearchText) ||
                              note.Narrative.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
            var matchesStatus = FilterStatus is null || note.Status == FilterStatus;

            return matchesText && matchesStatus;
        }

        public async Task LoadPeopleAsync()
        {
            try
            {
                People.Clear();
                var people = await _personService.GetAllPeopleAsync(LoggedInUser!.Id);
                foreach (var person in people)
                    People.Add(person);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load people: {ex.Message}");
            }
        }

        private async Task LoadMonthlyNotesAsync()
        {
            _monthlyNotes = await _noteService.GetMonthlyNotesAsync(LoggedInUser!.Id);
            OnPropertyChanged(nameof(PendingUnits));
            OnPropertyChanged(nameof(LoggedUnits));
            OnPropertyChanged(nameof(AbandonedUnits));
            OnPropertyChanged(nameof(EstimatedIncentive));
            OnPropertyChanged(nameof(Threshold));
            OnPropertyChanged(nameof(SafeThreshold));
            OnPropertyChanged(nameof(DailyAverageUnits));
        }

        private async Task LoadUpcomingEventsAsync()
        {
            if (LoggedInUser is null)
                return;

            var settings = await _settingsService.LoadAsync();
            var events = _upcomingEventService.GenerateEvents(People, settings);
            UpcomingEvents.Clear();
            foreach (var e in events)
                UpcomingEvents.Add(e);

            OnPropertyChanged(nameof(FormEvents));
            OnPropertyChanged(nameof(VisitEvents));
            OnPropertyChanged(nameof(ContactEvents));
        }

        public async Task MarkFormCompleteAsync(FormType formType)
        {
            if (SelectedPerson is null)
                return;

            var form = SelectedPerson.GetCurrentCycleForm(formType);
            if (form is null)
                return;

            form.IsCompliant = true;
            await _formService.UpdateFormAsync(form);
            RefreshComplianceFlags();
        }

        private void RefreshComplianceFlags()
        {
            OnPropertyChanged(nameof(Q1RCompliant));
            OnPropertyChanged(nameof(Q2RCompliant));
            OnPropertyChanged(nameof(Q3RCompliant));
            OnPropertyChanged(nameof(Q4RCompliant));
            OnPropertyChanged(nameof(PcpCompliant));
            OnPropertyChanged(nameof(CompAssessmentCompliant));
            OnPropertyChanged(nameof(ReclassificationCompliant));
            OnPropertyChanged(nameof(SafetyPlanCompliant));
            OnPropertyChanged(nameof(PrivacyPracticesCompliant));
            OnPropertyChanged(nameof(ReleaseAgencyCompliant));
            OnPropertyChanged(nameof(ReleaseDhhsCompliant));
            OnPropertyChanged(nameof(ReleaseMedicalCompliant));
        }

        private void ResetForm()
        {
            SelectedPerson = null;
            Status = null;
            Narrative = string.Empty;
            EventDate = null;
            Units = null;
            Duration = null;
            SelectedFormType = null;
            SelectedNoteType = null;
        }

        public void EnterEditMode()
        {
            if (SelectedNote is null)
                return;

            IsEditing = true;
            Narrative = SelectedNote.Narrative;
            EventDate = SelectedNote.EventDate;
            Units = SelectedNote.Units;
            Status = SelectedNote.Status;
            SelectedNoteType = SelectedNote.NoteType;
            SelectedFormType = SelectedNote.FormType;
            SelectedPerson = People.First(p => p.Id == SelectedNote.PersonId);
        }

        public async Task RefreshIncentiveAsync()
        {
            var (incentive, _) = await _incentiveService.GetOrCreateAsync(
                LoggedInUser!.Id, DateTime.Now.Month, DateTime.Now.Year);
            _incentive = incentive;
            OnPropertyChanged(nameof(Threshold));
            OnPropertyChanged(nameof(SafeThreshold));
            OnPropertyChanged(nameof(EstimatedIncentive));
            OnPropertyChanged(nameof(DailyAverageUnits));
        }

        public void Reset()
        {
            LoggedInUser = null;
            People.Clear();
            Notes.Clear();
            UpcomingEvents.Clear();
            _monthlyNotes = [];
            _incentive = null;
            _settings = null;
            SelectedPerson = null;
            SelectedNote = null;
            IsEditing = false;
        }

        public IEnumerable<EffectiveDateGroup> EffectiveDateGroups => BuildEffectiveDateGroups();

        private IEnumerable<EffectiveDateGroup> BuildEffectiveDateGroups()
        {
            var today = DateTime.Today;
            var currentMonth = new DateTime(today.Year, today.Month, 1);

            return Enumerable.Range(0, 7)
                .Select(i => currentMonth.AddMonths(i))
                .Select(month => new EffectiveDateGroup(
                    Label: month.ToString("MMMM yyyy"),
                    IsCurrent: month.Year == today.Year && month.Month == today.Month,
                    ClientNames: People
                        .Where(p => p.EffectiveDate.HasValue &&
                                    p.EffectiveDate.Value.Month == month.Month)
                        .OrderBy(p => p.EffectiveDate!.Value.Day)
                        .Select(p => $"{p.FullName} ({p.EffectiveDate!.Value:MMM d})")
                        .ToList()))
                .Where(g => g.ClientNames.Count > 0)
                .ToList();
        }

        private void StartAbandonmentTimer()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            timer.Tick += async (s, e) =>
            {
                if ((DateTime.Now - _lastAbandonmentCheck).TotalHours >= 24)
                {
                    await _noteService.UpdateAbandonedNotesAsync(_settings?.AbandonedAfterDays ?? 8);
                    _lastAbandonmentCheck = DateTime.Now;
                }
            };
            timer.Start();
        }
    }
}