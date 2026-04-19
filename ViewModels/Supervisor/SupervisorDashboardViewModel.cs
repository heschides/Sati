using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;

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
        private readonly IUserService _userService;

        // -------------------------------------------------------------------------
        // Sub-view ViewModels
        // -------------------------------------------------------------------------

        private readonly TeamOverviewViewModel _teamOverviewViewModel;
        private readonly OverdueItemsViewModel _overdueItemsViewModel;
        private readonly MonthlyProductivityViewModel _monthlyProductivityViewModel;
        private readonly UserManagementViewModel _userManagementViewModel;

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public SupervisorDashboardViewModel(
            ISessionService sessionService,
            IPersonService personService,
            INoteService noteService,
            IIncentiveService incentiveService,
            ISettingsService settingsService,
            IUpcomingEventService upcomingEventService,
            IUserService userService,
            UserManagementViewModel userManagementViewModel)
        {
            _sessionService = sessionService;
            _personService = personService;
            _noteService = noteService;
            _incentiveService = incentiveService;
            _settingsService = settingsService;
            _upcomingEventService = upcomingEventService;
            _userService = userService;
            _userManagementViewModel = userManagementViewModel;

            _teamOverviewViewModel = new TeamOverviewViewModel();
            _overdueItemsViewModel = new OverdueItemsViewModel();
            _monthlyProductivityViewModel = new MonthlyProductivityViewModel();

            // Start on team overview
            CurrentSubView = _teamOverviewViewModel;
        }

        // -------------------------------------------------------------------------
        // Observable properties
        // -------------------------------------------------------------------------

        [ObservableProperty] private object? currentSubView;
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

        // Active sub-view indicators for toolbar highlighting
        public bool IsTeamOverviewActive => CurrentSubView is TeamOverviewViewModel;
        public bool IsOverdueItemsActive => CurrentSubView is OverdueItemsViewModel;
        public bool IsMonthlyProductivityActive => CurrentSubView is MonthlyProductivityViewModel;
        public bool IsUserManagementActive => CurrentSubView is UserManagementViewModel;

        // -------------------------------------------------------------------------
        // Property change callbacks
        // -------------------------------------------------------------------------

        partial void OnCurrentSubViewChanged(object? value)
        {
            OnPropertyChanged(nameof(IsTeamOverviewActive));
            OnPropertyChanged(nameof(IsOverdueItemsActive));
            OnPropertyChanged(nameof(IsMonthlyProductivityActive));
            OnPropertyChanged(nameof(IsUserManagementActive));
        }

        // -------------------------------------------------------------------------
        // Navigation commands
        // -------------------------------------------------------------------------
        [RelayCommand]
        private async Task NavigateToTeamOverview()
        {
            CurrentSubView = null;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            _teamOverviewViewModel.Refresh(CaseManagers);
            CurrentSubView = _teamOverviewViewModel;
        }

        [RelayCommand]
        private async Task NavigateToOverdueItems()
        {
            CurrentSubView = null;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            _overdueItemsViewModel.Refresh(CaseManagers);
            CurrentSubView = _overdueItemsViewModel;
        }

        [RelayCommand]
        private async Task NavigateToMonthlyProductivity()
        {
            CurrentSubView = null;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            _monthlyProductivityViewModel.Refresh(CaseManagers);
            CurrentSubView = _monthlyProductivityViewModel;
        }

        [RelayCommand]
        private async Task NavigateToUserManagement()
        {
            if (CurrentSubView is UserManagementViewModel)
            {
                CurrentSubView = null;
                return;
            }

            CurrentSubView = null;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await _userManagementViewModel.InitializeAsync();
            CurrentSubView = _userManagementViewModel;
        }


   
        // -------------------------------------------------------------------------
        // Initialization
        // -------------------------------------------------------------------------

        public async Task InitializeAsync()
        {
            try
            {
                var supervisor = _sessionService.CurrentUser!;
                var supervisees = await _userService.GetSuperviseesAsync(supervisor.Id);


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

                SelectedCaseManager = CaseManagers.FirstOrDefault();

                OnPropertyChanged(nameof(TeamSizeLabel));
                OnPropertyChanged(nameof(TotalClients));
                OnPropertyChanged(nameof(TotalOverdue));
                OnPropertyChanged(nameof(TotalNotesThisMonth));
                OnPropertyChanged(nameof(AvgComplianceLabel));

                _teamOverviewViewModel.Refresh(CaseManagers);
                _overdueItemsViewModel.Refresh(CaseManagers);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Init failed: {ex.Message}\n{ex.StackTrace}");
                Debug.WriteLine($"SupervisorDashboardViewModel.InitializeAsync failed: {ex.Message}");
            }
        }

        // -------------------------------------------------------------------------
        // Local Methods
        // -------------------------------------------------------------------------
        public void ClearCharts()
        {
            _teamOverviewViewModel.ComplianceChartModel = null;
            _monthlyProductivityViewModel.StatusChartModel = null;
        }

    }
}