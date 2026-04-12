using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.ViewModels.Children;
using Sati.ViewModels.Supervisor;
using System.Windows.Media;
using System.Windows;

namespace Sati.ViewModels
{
    public partial class ShellViewModel : ObservableObject
    {
        // -------------------------------------------------------------------------
        // Services & private state
        // -------------------------------------------------------------------------

        private readonly MainWindowViewModel _notesViewModel;
        private readonly SupervisorDashboardViewModel _supervisorViewModel;
        private readonly GuidanceViewModel _guidanceViewModel;
        private readonly HelpersViewModel _helpersViewModel;
        private readonly ISessionService _sessionService;

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public ShellViewModel(
            MainWindowViewModel notesViewModel,
            ScratchpadViewModel scratchpadViewModel,
            SupervisorDashboardViewModel supervisorViewModel,
            GuidanceViewModel guidanceViewModel,
            HelpersViewModel helpersViewModel,
            ISessionService sessionService)
        {
            _notesViewModel = notesViewModel;
            _supervisorViewModel = supervisorViewModel;
            _guidanceViewModel = guidanceViewModel;
            _helpersViewModel = helpersViewModel;
            _sessionService = sessionService;
            Scratchpad = scratchpadViewModel;
        }

        // -------------------------------------------------------------------------
        // Events
        // -------------------------------------------------------------------------

        public event EventHandler? SwitchUserRequested;

        // -------------------------------------------------------------------------
        // Observable properties
        // -------------------------------------------------------------------------

        [ObservableProperty] private object? currentViewModel;

        // -------------------------------------------------------------------------
        // Child ViewModels
        // -------------------------------------------------------------------------

        public ScratchpadViewModel Scratchpad { get; }
        public MainWindowViewModel NotesViewModel => _notesViewModel;

        // -------------------------------------------------------------------------
        // Computed properties
        // -------------------------------------------------------------------------

        public bool IsScratchpadVisible => true;

        public bool IsSupervisionAvailable =>
            _sessionService.CurrentUser?.Role is UserRole.Supervisor
                or UserRole.Admin
                or UserRole.Director;

        // Active tab indicators
        public bool IsNotesActive => CurrentViewModel is MainWindowViewModel;
        public bool IsSupervisorActive => CurrentViewModel is SupervisorDashboardViewModel;
        public bool IsGuidanceActive => CurrentViewModel is GuidanceViewModel;
        public bool IsHelpersActive => CurrentViewModel is HelpersViewModel;

        // User header
        public string UserGreeting => $"Hello, {_sessionService.CurrentUser?.DisplayName ?? "there"}.";

        public string UserInitials
        {
            get
            {
                var name = _sessionService.CurrentUser?.DisplayName ?? "?";
                var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length >= 2
                    ? $"{parts[0][0]}{parts[^1][0]}"
                    : name.Length > 0 ? name[0].ToString() : "?";
            }
        }

        public SolidColorBrush AvatarBrush => new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(
                _sessionService.CurrentUser?.Role switch
                {
                    UserRole.CaseManager => "#5B7FA6",
                    UserRole.Supervisor => "#5A8A5A",
                    UserRole.Director => "#A6607A",
                    UserRole.Admin => "#4A3728",
                    _ => "#9C7A5C"
                }));

        // -------------------------------------------------------------------------
        // Property change callbacks
        // -------------------------------------------------------------------------

        partial void OnCurrentViewModelChanged(object? value)
        {
            OnPropertyChanged(nameof(IsNotesActive));
            OnPropertyChanged(nameof(IsSupervisorActive));
            OnPropertyChanged(nameof(IsGuidanceActive));
            OnPropertyChanged(nameof(IsHelpersActive));
        }

        // -------------------------------------------------------------------------
        // Navigation commands
        // -------------------------------------------------------------------------

        [RelayCommand] private void NavigateToNotes() => CurrentViewModel = _notesViewModel;
        [RelayCommand] private void NavigateToSupervisorDashboard() => CurrentViewModel = _supervisorViewModel;
        [RelayCommand] private void NavigateToGuidance() => CurrentViewModel = _guidanceViewModel;
        [RelayCommand] private void NavigateToHelpers() => CurrentViewModel = _helpersViewModel;
        [RelayCommand] private void RequestSwitchUser() => SwitchUserRequested?.Invoke(this, EventArgs.Empty);

        // -------------------------------------------------------------------------
        // Initialization
        // -------------------------------------------------------------------------

        public async Task InitializeAsync()
        {
            await Scratchpad.InitializeAsync();
            _notesViewModel.Initialize();

            OnPropertyChanged(nameof(UserGreeting));
            OnPropertyChanged(nameof(UserInitials));
            OnPropertyChanged(nameof(AvatarBrush));
            OnPropertyChanged(nameof(IsSupervisionAvailable));

            NavigateByRole();
        }

        public async Task ReinitializeAsync()
        {
            await Scratchpad.SaveScratchpadAsync(Scratchpad.ScratchpadContent);
            await Scratchpad.InitializeAsync();
            _notesViewModel.Reset();
            _notesViewModel.Initialize();
            OnPropertyChanged(nameof(UserGreeting));
            OnPropertyChanged(nameof(UserInitials));
            OnPropertyChanged(nameof(AvatarBrush));
            OnPropertyChanged(nameof(IsSupervisionAvailable));
            NavigateByRole();
        }

        private void NavigateByRole()
        {
            var role = _sessionService.CurrentUser?.Role;
            if (role is UserRole.Supervisor or UserRole.Admin or UserRole.Director)
            {
                _ = InitializeSupervisorAsync();
            }
            else
            {
                NavigateToNotes();
            }
        }

        private async Task InitializeSupervisorAsync()
        {
            await _supervisorViewModel.InitializeAsync();
            CurrentViewModel = _supervisorViewModel;
        }
    }
}