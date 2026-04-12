using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Data;
using Sati.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Sati.ViewModels.Supervisor
{
    public partial class SupervisorDashboardViewModel : ObservableObject
    {
        // -------------------------------------------------------------------------
        // Services
        // -------------------------------------------------------------------------

        private readonly ISessionService _sessionService;
        private readonly IPersonService _personService;
        private readonly INoteService _noteService;
        private readonly IIncentiveService _incentiveService;
        private readonly ISettingsService _settingsService;
        private readonly IUpcomingEventService _upcomingEventService;

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public SupervisorDashboardViewModel(
            ISessionService sessionService,
            IPersonService personService,
            INoteService noteService,
            IIncentiveService incentiveService,
            ISettingsService settingsService,
            IUpcomingEventService upcomingEventService)
        {
            _sessionService = sessionService;
            _personService = personService;
            _noteService = noteService;
            _incentiveService = incentiveService;
            _settingsService = settingsService;
            _upcomingEventService = upcomingEventService;
        }

        // -------------------------------------------------------------------------
        // Observable properties
        // -------------------------------------------------------------------------

        [ObservableProperty] private CaseManagerSummaryViewModel? selectedCaseManager;

        // -------------------------------------------------------------------------
        // Collections
        // -------------------------------------------------------------------------

        public ObservableCollection<CaseManagerSummaryViewModel> CaseManagers { get; } = [];

        // -------------------------------------------------------------------------
        // Computed properties
        // -------------------------------------------------------------------------

        public string CurrentMonth => DateTime.Now.ToString("MMMM yyyy");

        public string TeamSizeLabel => CaseManagers.Count == 1
            ? "1 case manager"
            : $"{CaseManagers.Count} case managers";

        public int TotalClients => CaseManagers.Sum(cm => cm.ClientCount);
        public int TotalOverdue => CaseManagers.Sum(cm => cm.OverdueCount);
        public int TotalNotesThisMonth => CaseManagers.Sum(cm => cm.NotesThisMonth);

        public string AvgComplianceLabel
        {
            get
            {
                if (CaseManagers.Count == 0) return "—";
                var avg = CaseManagers.Average(cm => cm.ProgressPercent);
                return $"{avg:0}%";
            }
        }

        // -------------------------------------------------------------------------
        // Initialization
        // -------------------------------------------------------------------------

        public async Task InitializeAsync()
        {
            try
            {
                var supervisor = _sessionService.CurrentUser!;
                var supervisees = supervisor.Supervisees
                    .Where(u => u.Role == UserRole.CaseManager)
                    .ToList();

                var settings = await _settingsService.LoadAsync();
                var now = DateTime.Now;

                foreach (var user in supervisees)
                {
                    var people = await _personService.GetAllPeopleAsync(user.Id);
                    var monthlyNotes = await _noteService.GetMonthlyNotesAsync(user.Id);
                    var events = _upcomingEventService.GenerateEvents(people, settings);

                    var summary = new CaseManagerSummaryViewModel(
                        user, people, monthlyNotes, events);

                    var (incentive, _) = await _incentiveService.GetOrCreateAsync(
                        user.Id, now.Month, now.Year);

                    summary.SetThreshold(incentive?.Threshold ?? 0);
                    CaseManagers.Add(summary);
                }

                // Select first by default
                SelectedCaseManager = CaseManagers.FirstOrDefault();

                OnPropertyChanged(nameof(TeamSizeLabel));
                OnPropertyChanged(nameof(TotalClients));
                OnPropertyChanged(nameof(TotalOverdue));
                OnPropertyChanged(nameof(TotalNotesThisMonth));
                OnPropertyChanged(nameof(AvgComplianceLabel));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SupervisorDashboardViewModel.InitializeAsync failed: {ex.Message}");
            }
        }
    }
}