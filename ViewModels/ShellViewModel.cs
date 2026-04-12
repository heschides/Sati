using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.ViewModels.Children;
using Sati.ViewModels.Supervisor;
using System.Diagnostics;

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
            ISessionService sessionService,
            GuidanceViewModel guidanceViewModel,
            HelpersViewModel helpersViewModel)
        {
            _notesViewModel = notesViewModel;
            _supervisorViewModel = supervisorViewModel;
            _sessionService = sessionService;
            Scratchpad = scratchpadViewModel;
            _guidanceViewModel = guidanceViewModel;
            _helpersViewModel = helpersViewModel;
        }

        // -------------------------------------------------------------------------
        // Observable properties
        // -------------------------------------------------------------------------

        [ObservableProperty] private object? currentViewModel;
        partial void OnCurrentViewModelChanged(object? value)
        {
            OnPropertyChanged(nameof(IsNotesActive));
            OnPropertyChanged(nameof(IsSupervisorActive));
            OnPropertyChanged(nameof(IsGuidanceActive));
            OnPropertyChanged(nameof(IsHelpersActive));
        }

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
            _sessionService.CurrentUser?.Role is UserRole.Supervisor or UserRole.Admin;
        public bool IsNotesActive => CurrentViewModel is MainWindowViewModel;
        public bool IsSupervisorActive => CurrentViewModel is SupervisorDashboardViewModel;
        public bool IsGuidanceActive => CurrentViewModel is GuidanceViewModel;
        public bool IsHelpersActive => CurrentViewModel is HelpersViewModel;


        // -------------------------------------------------------------------------
        // Navigation commands
        // -------------------------------------------------------------------------

        [RelayCommand] private void NavigateToNotes() => CurrentViewModel = _notesViewModel;

        [RelayCommand]
        private void NavigateToSupervisorDashboard() =>
            CurrentViewModel = _supervisorViewModel;

        [RelayCommand]
        private void NavigateToGuidance()
        {
            Debug.WriteLine("NavigateToGuidance called");
            CurrentViewModel = _guidanceViewModel;
        }

        [RelayCommand]
        private void NavigateToHelpers()
        {
            Debug.WriteLine("NavigateToHelpers called");
            CurrentViewModel = _helpersViewModel;
        }


        // -------------------------------------------------------------------------
        // Initialization
        // -------------------------------------------------------------------------

        public async Task InitializeAsync()
        {
            await Scratchpad.InitializeAsync();
            _notesViewModel.Initialize();

            var role = _sessionService.CurrentUser?.Role;

            if (role is UserRole.Supervisor or UserRole.Admin)
            {
                await _supervisorViewModel.InitializeAsync();
                CurrentViewModel = _supervisorViewModel;
            }
            else
            {
                NavigateToNotes();
            }
        }
    }
}