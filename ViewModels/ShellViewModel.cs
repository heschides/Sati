using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using Sati.ViewModels.Billing;
using Sati.ViewModels.Children;
using Sati.ViewModels.Supervisor;
using Sati.ViewModels.Admin;
using Sati.ViewModels.Finance;
using System.Windows;
using System.Windows.Media;

namespace Sati.ViewModels
{
    public partial class ShellViewModel : ObservableObject
    {
        // -------------------------------------------------------------------------
        // Services & private state
        // -------------------------------------------------------------------------

        // Case Management owns the workspace with its feature tabs and sidebars.
        private readonly CaseManagementViewModel _caseManagementViewModel;
        private readonly SupervisorDashboardViewModel _supervisorDashboardViewModel;
        private readonly ISessionService _sessionService;
        private readonly ISessionLifetime _sessionLifetime;
        private readonly BillingDashboardViewModel _billingDashboardViewModel;
        private readonly AdminDashboardViewModel _adminDashboardViewModel;
        private readonly RepresentativePayeeDashboardViewModel _representativePayeeDashboardViewModel;
        private readonly PlatformHealthViewModel _platformHealthViewModel;
        private readonly DataEnvironmentInfo _dataEnvironment;
        private readonly IApiCompatibilityService _apiCompatibility;
        private readonly EasyEyesPreferenceService _easyEyesPreferences;
        private readonly IdleLockPreferenceService _idlePreferences;
        private bool _isTogglingEasyEyes;


        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public ShellViewModel(
            CaseManagementViewModel caseManagementViewModel,
            ScratchpadViewModel scratchpadViewModel,
            SupervisorDashboardViewModel supervisorViewModel,
            ISessionService sessionService,
            BillingDashboardViewModel billingDashboardViewModel,
            AdminDashboardViewModel adminDashboardViewModel,
            RepresentativePayeeDashboardViewModel representativePayeeDashboardViewModel,
            PlatformHealthViewModel platformHealthViewModel,
            DataEnvironmentInfo dataEnvironment,
            IApiCompatibilityService apiCompatibility,
            DatabaseActivityViewModel databaseActivity,
            EasyEyesPreferenceService easyEyesPreferences,
            IdleLockPreferenceService idlePreferences,
            ChatViewModel chatViewModel,
            ISessionLifetime sessionLifetime)
        {
            _apiCompatibility = apiCompatibility;
            _caseManagementViewModel = caseManagementViewModel;
            _supervisorDashboardViewModel = supervisorViewModel;
            _sessionService = sessionService;
            _sessionLifetime = sessionLifetime;
            Scratchpad = scratchpadViewModel;
            Chat = chatViewModel;
            _billingDashboardViewModel = billingDashboardViewModel;
            _adminDashboardViewModel = adminDashboardViewModel;
            _representativePayeeDashboardViewModel = representativePayeeDashboardViewModel;
            _platformHealthViewModel = platformHealthViewModel;
            _dataEnvironment = dataEnvironment;
            _easyEyesPreferences = easyEyesPreferences;
            DatabaseActivity = databaseActivity;
            _idlePreferences = idlePreferences;
            _easyEyesPreferences.PreferenceChanged += (_, enabled) => ApplyEasyEyesMode(enabled);
            _idlePreferences.PreferenceChanged += (_, minutes) => Idle.ApplyTimeout(minutes);
            Idle.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(IdleSessionState.IsOverlayVisible)) UpdateChatVisibility();
            };

            // One scratchpad, two possible homes. The Overview renders this same
            // instance when it is centered; it is never given one of its own.
            NotesViewModel.AttachScratchpad(Scratchpad);
            Scratchpad.ScheduledWorkOpeningAsync = OpenScheduledWorkItemAsync;

            // Moving between Overview and the other Case Management sub-tabs moves
            // the one live Work Agenda view between its center and side hosts.
            NotesViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(CaseManagerDashboardViewModel.IsDashboardSubActive))
                    NotifyOverviewActivityChanged();
            };
        }

        private void NotifyOverviewActivityChanged()
        {
            OnPropertyChanged(nameof(IsOverviewActive));
        }

        // -------------------------------------------------------------------------
        // Events
        // -------------------------------------------------------------------------

        // Settings is the one window behind the greeting badge. Switching accounts is
        // asked for from inside it, so the shell no longer raises a request of its own.
        public event EventHandler<bool>? OpenSettingsWindowRequested;
        public event EventHandler? ReauthenticationRequested;
        [RelayCommand] private void Reauthenticate() => ReauthenticationRequested?.Invoke(this, EventArgs.Empty);

        // -------------------------------------------------------------------------
        // Observable properties
        // -------------------------------------------------------------------------

        [ObservableProperty] private object? currentViewModel;

        // Opaque shell-wide privacy boundary used while account credentials and
        // user-scoped workspaces are changing. This is independent of the idle screen.
        [ObservableProperty] private bool isAccountTransitionActive;
        [ObservableProperty] private bool isSessionReauthenticationRequired;
        public string AccountTransitionTitle => IsSessionReauthenticationRequired ? "Sign-in required" : "Switching account";
        public string AccountTransitionMessage => IsSessionReauthenticationRequired
            ? "Your session ended. Your unsaved work is retained behind this screen. Sign in again to continue; closing Sati may discard unsaved changes."
            : "The previous account's information is hidden while Sati prepares the next workspace.";
        partial void OnIsSessionReauthenticationRequiredChanged(bool value)
        {
            OnPropertyChanged(nameof(AccountTransitionTitle));
            OnPropertyChanged(nameof(AccountTransitionMessage));
        }

        // Open/closed state of the scratchpad panel. The actual column collapse and
        // width-restore lives in ShellWindow.xaml.cs, which reacts to this changing —
        // remembering a user-dragged GridSplitter width is pure view layout, not a
        // view-model concern. Defaults open.
        [ObservableProperty] private bool isScratchpadVisible = true;

        [ObservableProperty] private bool isCompactDisplayMode;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EasyEyesScale))]
        private bool isEasyEyesMode;
        // -------------------------------------------------------------------------
        // Child ViewModels
        // -------------------------------------------------------------------------

        public ScratchpadViewModel Scratchpad { get; }
        public ChatViewModel Chat { get; }
        public bool IsChatAvailable => Chat.IsAvailableHere;
        public bool IsChatActive => ReferenceEquals(CurrentViewModel, Chat);
        private bool _chatWindowVisible = true;
        public void SetChatWindowVisible(bool visible) { _chatWindowVisible = visible; UpdateChatVisibility(); }
        private void UpdateChatVisibility() => Chat.SetSurfaceState(IsChatActive && _chatWindowVisible, Idle.IsOverlayVisible);
        public void ResumeChatAccount() { Chat.ResumeAccount(); UpdateChatVisibility(); }
        public DatabaseActivityViewModel DatabaseActivity { get; }

        // Forwarded so window-close journal flushing still reaches the dashboard now
        // that Case Management owns it.
        public CaseManagerDashboardViewModel NotesViewModel => _caseManagementViewModel.Dashboard;

        // -------------------------------------------------------------------------
        // Computed properties
        // -------------------------------------------------------------------------
        public bool IsCaseManagementAvailable =>
            _sessionService.CurrentUser?.HasCaseManagerPermissions == true;
        public bool IsBillingAvailable =>
            _sessionService.CurrentUser?.HasBillingPermissions == true;
        public bool IsRepresentativePayeeAvailable =>
            _sessionService.CurrentUser?.HasRepresentativePayeePermissions == true;
        public bool IsAdminAvailable =>
            _sessionService.CurrentUser?.HasAdminPermissions == true;
        public bool IsPlatformHealthAvailable => _sessionService.CurrentUser?.Role is UserRole.PlatformOperator;
        public bool IsDemoEnvironment => _dataEnvironment.IsDemo;
        public string DataEnvironmentLabel => _dataEnvironment.DisplayName;
        public double EasyEyesScale => IsEasyEyesMode ? 1.3 : 1.0;

        public bool IsOverviewActive =>
            IsCaseManagementActive && NotesViewModel.IsDashboardSubActive;

        public string ScratchpadToggleAutomationName => "Show or hide Work Agenda";

        public bool IsBillingActive => CurrentViewModel is BillingDashboardViewModel;
        public bool IsRepresentativePayeeActive => CurrentViewModel is RepresentativePayeeDashboardViewModel;
        public bool IsAdminActive => CurrentViewModel is AdminDashboardViewModel;
        public bool IsPlatformHealthActive => CurrentViewModel is PlatformHealthViewModel;

        public bool IsSupervisionAvailable =>
            _sessionService.CurrentUser?.HasSupervisorPermissions == true;
        public bool IsUserManagementAvailable => IsSupervisionAvailable || IsAdminAvailable;
        public bool IsUserManagementActive => CurrentViewModel is UserManagementViewModel;

        // Active tab indicators
        public bool IsCaseManagementActive => CurrentViewModel is CaseManagementViewModel;
        public bool IsSupervisorActive => CurrentViewModel is SupervisorDashboardViewModel;

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
                _sessionService.CurrentUser switch
                {
                    { Role: UserRole.PlatformOperator } => "#7A2E8E",
                    { HasAdminPermissions: true } => "#4A3728",
                    { HasBillingPermissions: true } => "#A6607A",
                    { HasSupervisorPermissions: true } => "#5A8A5A",
                    { HasCaseManagerPermissions: true } => "#5B7FA6",
                    _ => "#9C7A5C"
                }));

        // -------------------------------------------------------------------------
        // Property change callbacks
        // -------------------------------------------------------------------------

        partial void OnCurrentViewModelChanged(object? value)
        {
            OnPropertyChanged(nameof(IsCaseManagementActive));
            OnPropertyChanged(nameof(IsSupervisorActive));
            OnPropertyChanged(nameof(IsUserManagementActive));
            OnPropertyChanged(nameof(IsBillingActive));
            OnPropertyChanged(nameof(IsRepresentativePayeeActive));
            OnPropertyChanged(nameof(IsAdminActive));
            OnPropertyChanged(nameof(IsPlatformHealthActive));
            OnPropertyChanged(nameof(IsChatActive));
            UpdateChatVisibility();
            // Navigating away from Case Management returns the side panel to the
            // Scratchpad, since the notes panel it was hosting belongs to the Overview.
            NotifyOverviewActivityChanged();
            if (value is not SupervisorDashboardViewModel)
                _supervisorDashboardViewModel?.ClearCharts();
        }

        // -------------------------------------------------------------------------
        // Navigation commands
        // -------------------------------------------------------------------------

        [RelayCommand]
        private void NavigateToCaseManagement()
        {
            if (IsCaseManagementAvailable) CurrentViewModel = _caseManagementViewModel;
        }
        [RelayCommand]
        private async Task NavigateToSupervisorDashboard()
        {
            if (!IsSupervisionAvailable) return;
            CurrentViewModel = _supervisorDashboardViewModel;
            await _supervisorDashboardViewModel.InitializeAsync();
        }
        [RelayCommand]
        private async Task NavigateToUserManagement()
        {
            if (!IsUserManagementAvailable) return;
            CurrentViewModel = _supervisorDashboardViewModel.UserManagement;
            await _supervisorDashboardViewModel.UserManagement.InitializeAsync();
        }
        [RelayCommand] public void OpenSettingsWindow() => OpenSettingsWindowRequested?.Invoke(this, true);
        [RelayCommand]
        private async Task NavigateToBilling()
        {
            if (!IsBillingAvailable) return;
            CurrentViewModel = _billingDashboardViewModel;
            await _billingDashboardViewModel.InitializeAsync();
        }
        [RelayCommand]
        private async Task NavigateToRepresentativePayee()
        {
            if (!IsRepresentativePayeeAvailable) return;
            CurrentViewModel = _representativePayeeDashboardViewModel;
            await _representativePayeeDashboardViewModel.InitializeAsync();
        }
        [RelayCommand]
        private async Task NavigateToAdmin()
        {
            if (!IsAdminAvailable) return;
            CurrentViewModel = _adminDashboardViewModel;
            await _adminDashboardViewModel.InitializeAsync();
        }
        [RelayCommand]
        private async Task NavigateToPlatformHealth()
        {
            CurrentViewModel = _platformHealthViewModel;
            await _platformHealthViewModel.RefreshAsync();
        }
        [RelayCommand] private void ToggleScratchpad() => IsScratchpadVisible = !IsScratchpadVisible;

        [RelayCommand]
        private void NavigateToChat()
        {
            if (IsChatAvailable) CurrentViewModel = Chat;
        }

        public void SetCompactDisplayMode(bool enabled) => IsCompactDisplayMode = enabled;

        /// <summary>
        /// Covers the entire shell before another account can authenticate. The outgoing
        /// content stays intact underneath until its drafts have been saved or the user
        /// cancels, but none of it remains visible around the account dialog.
        /// </summary>
        public void BeginAccountTransition()
        {
            IsAccountTransitionActive = true;
            Chat.SuspendAndClear();
        }

        /// <summary>
        /// Called only after the switch dialog has authenticated a replacement account,
        /// and before ISessionService is changed. From this point the old account's
        /// workspace is both hidden and synchronously cleared.
        /// </summary>
        public void ClearOutgoingAccountContent()
        {
            if (!IsAccountTransitionActive)
                throw new InvalidOperationException("The account privacy shield must be active before clearing account content.");

            CurrentViewModel = null;
            IsScratchpadVisible = false;
            Scratchpad.ClearForAccountSwitch();
            NotesViewModel.Reset();
            _supervisorDashboardViewModel.ClearForAccountSwitch();
            _billingDashboardViewModel.ClearForAccountSwitch();
            _representativePayeeDashboardViewModel.ClearForAccountSwitch();
            _adminDashboardViewModel.ClearForAccountSwitch();
        }

        public void CompleteAccountTransition()
        {
            IsSessionReauthenticationRequired = false;
            IsScratchpadVisible = true;
            IsAccountTransitionActive = false;
            ResumeChatAccount();
        }

        public void CancelAccountTransition(bool resumeChat = true)
        {
            IsAccountTransitionActive = false;
            if (resumeChat)
                ResumeChatAccount();
        }

        public void RequireReauthentication()
        {
            _sessionLifetime.SuspendAccess();
            IsSessionReauthenticationRequired = true;
            BeginAccountTransition();
            Scratchpad.SuspendForReauthentication();
        }

        public bool CanResumeReauthenticatedSession(User user) =>
            user.IsEnabled && user.SecurityVersion > 0 && _sessionService.CurrentUser is { } previous &&
            user.Id == previous.Id && user.AgencyId == previous.AgencyId &&
            user.Role == previous.Role && user.Permissions == previous.Permissions &&
            user.SupervisorId == previous.SupervisorId;

        public async Task ResumeReauthenticatedSessionAsync(User user)
        {
            if (!CanResumeReauthenticatedSession(user))
                throw new InvalidOperationException("Changed account access requires reloading the workspace.");
            // Replace the identity, never mutate the object captured by older work.
            // Editors keep their unsaved content; future commands use the new stamp.
            _sessionService.SetUser(user);
            _sessionLifetime.ResumeAccess();
            NotesViewModel.LoggedInUser = _sessionService.CurrentUser;
            NotifyRoleDependentProperties();
            try
            {
                await Scratchpad.ResumeAfterReauthenticationAsync();
            }
            finally
            {
                // Authentication succeeded even if restoring an unrelated workspace
                // later reports its own recoverable failure. Do not leave the privacy
                // shield claiming that credentials are still required.
                CompleteAccountTransition();
            }
        }

        private void ApplyEasyEyesMode(bool enabled)
        {
            IsEasyEyesMode = enabled;
            NotesViewModel.Clients.IsEasyEyesMode = enabled;
            NotesViewModel.NotesLog.IsEasyEyesMode = enabled;
        }

        [RelayCommand]
        private async Task ToggleEasyEyes()
        {
            var userId = _sessionService.CurrentUser?.Id;
            if (userId is null || _isTogglingEasyEyes || IsAccountTransitionActive)
                return;

            _isTogglingEasyEyes = true;
            try
            {
                await _easyEyesPreferences.SetEnabledAsync(userId.Value, !IsEasyEyesMode);
            }
            catch (EasyEyesPreferenceSaveException exception)
            {
                AppErrorLog.Record(exception, "easy-eyes.keyboard-shortcut");
            }
            finally
            {
                _isTogglingEasyEyes = false;
            }
        }

        /// <summary>
        /// The inactivity privacy screen. The window drives it: input calls
        /// RegisterActivity, a one-second timer calls Evaluate.
        /// </summary>
        public IdleSessionState Idle { get; } = new();

        private async Task LoadEasyEyesPreferenceAsync()
        {
            var userId = _sessionService.CurrentUser?.Id;
            ApplyEasyEyesMode(userId is not null &&
                await _easyEyesPreferences.LoadForUserAsync(userId.Value));
        }

        private async Task LoadIdlePreferenceAsync()
        {
            var userId = _sessionService.CurrentUser?.Id;
            Idle.Reset();
            Idle.ApplyTimeout(userId is null
                ? IdleLockPreferenceService.DefaultMinutes
                : await _idlePreferences.LoadForUserAsync(userId.Value));
        }

        public async Task OpenAgendaItemAsync(DailyAgendaItem item)
        {
            if (!IsCaseManagementAvailable)
                return;

            CurrentViewModel = _caseManagementViewModel;
            _caseManagementViewModel.ResetToDashboard();

            var person = NotesViewModel.People.FirstOrDefault(candidate =>
                candidate.Id == item.PersonId);
            if (person is null)
                return;

            NotesViewModel.NoteEntry.SelectedPerson = person;
            if (item.ReleaseObligationId is Guid obligationId)
            {
                NotesViewModel.SelectedPerson = person;
                NotesViewModel.Clients.SelectedPerson =
                    NotesViewModel.Clients.People.FirstOrDefault(candidate =>
                        candidate.Id == person.Id) ?? person;
                NotesViewModel.NavigateToClientsCommand.Execute(null);
                await NotesViewModel.Clients.OpenReleaseObligationAsync(
                    obligationId,
                    item.TargetEffectiveDate);
            }
            else if (item.FormType is FormType formType)
            {
                await NotesViewModel.OpenFormAsync(
                    formType,
                    item.FormId,
                    item.TargetEffectiveDate);
            }
        }

        public async Task OpenGeneratedCheckRequestDraftAsync(
            Sati.Contracts.V1.GeneratedCheckRequestDraftDto draft)
        {
            if (!IsCaseManagementAvailable)
                return;

            CurrentViewModel = _caseManagementViewModel;
            NotesViewModel.NavigateToClientsCommand.Execute(null);
            var person = NotesViewModel.People.FirstOrDefault(candidate =>
                candidate.Id == draft.PersonId);
            if (person is null)
                return;

            NotesViewModel.NoteEntry.SelectedPerson = person;
            NotesViewModel.Clients.SelectedPerson = person;
            NotesViewModel.Clients.ClientWorkspaceTabIndex =
                NewClientViewModel.CheckRequestsTabIndex;
            if (NotesViewModel.Clients.CheckRequests is not null)
                await NotesViewModel.Clients.CheckRequests.OpenRequestAsync(draft.CheckRequestId);
        }

        private async Task OpenScheduledWorkItemAsync(WorkAgendaItem item)
        {
            if (!IsCaseManagementAvailable)
                return;

            CurrentViewModel = _caseManagementViewModel;
            _caseManagementViewModel.ResetToDashboard();
            await NotesViewModel.NoteEntry.PrepareScheduledWorkAsync(item.Note);
        }
        // -------------------------------------------------------------------------
        // Initialization
        // -------------------------------------------------------------------------

        // -------------------------------------------------------------------------
        // API compatibility
        // -------------------------------------------------------------------------

        /// <summary>
        /// Set when the server serves a different route surface than this build
        /// expects. Drives a banner rather than blocking anything: most of the
        /// application still works, and the point is to name the cause before a
        /// missing route surfaces somewhere else as a missing record.
        /// </summary>
        [ObservableProperty]
        private bool _serverSurfaceDisagrees;

        [ObservableProperty]
        private string _serverSurfaceWarning = string.Empty;

        public async Task InitializeAsync()
        {
            Chat.ResumeAccount();
            NotifyRoleDependentProperties();
            await LoadEasyEyesPreferenceAsync();
            await LoadIdlePreferenceAsync();
            await CheckApiCompatibilityAsync();
            if (_sessionService.CurrentUser?.Role == UserRole.PlatformOperator)
            {
                await NavigateToPlatformHealth();
                return;
            }

            await Scratchpad.InitializeAsync();
            if (IsCaseManagementAvailable)
            {
                // Own casework is not a prerequisite for billing, supervision or
                // administration. Their service permissions remain independent.
                // Keep these loads sequential to avoid overlapping LocalDB sort grants.
                await NotesViewModel.InitializeAsync();
            }

            await NavigateByRoleAsync();
        }

        /// <summary>
        /// Never allowed to break sign-in. A compatibility check that could stop a
        /// case manager working would be a worse failure than the one it detects.
        /// </summary>
        private async Task CheckApiCompatibilityAsync()
        {
            try
            {
                var compatibility = await _apiCompatibility.CheckAsync();
                ServerSurfaceDisagrees = compatibility.Disagrees;
                ServerSurfaceWarning = compatibility.Detail ?? string.Empty;
            }
            catch (Exception)
            {
                ServerSurfaceDisagrees = false;
                ServerSurfaceWarning = string.Empty;
            }
        }

        public async Task ReinitializeAsync()
        {
            Chat.ResumeAccount();
            // The switch flow saves the outgoing user's scratchpad and journal before
            // authentication replaces the cloud API token. From this point onward every
            // request must belong to the newly selected user.
            NotesViewModel.Reset();
            _caseManagementViewModel.ResetToDashboard();
            NotifyRoleDependentProperties();
            await LoadEasyEyesPreferenceAsync();
            await LoadIdlePreferenceAsync();
            if (_sessionService.CurrentUser?.Role == UserRole.PlatformOperator)
            {
                await NavigateToPlatformHealth();
                return;
            }

            await Scratchpad.InitializeAsync();
            if (IsCaseManagementAvailable)
            {
                await NotesViewModel.InitializeAsync();
            }

            await NavigateByRoleAsync();
        }

        private void NotifyRoleDependentProperties()
        {
            OnPropertyChanged(nameof(UserGreeting));
            OnPropertyChanged(nameof(UserInitials));
            OnPropertyChanged(nameof(AvatarBrush));
            OnPropertyChanged(nameof(IsSupervisionAvailable));
            OnPropertyChanged(nameof(IsUserManagementAvailable));
            OnPropertyChanged(nameof(IsCaseManagementAvailable));
            OnPropertyChanged(nameof(IsBillingAvailable));
            OnPropertyChanged(nameof(IsRepresentativePayeeAvailable));
            OnPropertyChanged(nameof(IsAdminAvailable));
            OnPropertyChanged(nameof(IsPlatformHealthAvailable));
            OnPropertyChanged(nameof(IsChatAvailable));
        }

        private async Task NavigateByRoleAsync()
        {
            if (_sessionService.CurrentUser?.Role == UserRole.PlatformOperator)
            {
                await NavigateToPlatformHealth();
                return;
            }
            if (IsCaseManagementAvailable) NavigateToCaseManagement();
            else if (IsSupervisionAvailable) await NavigateToSupervisorDashboard();
            else if (IsBillingAvailable)
            {
                await NavigateToBilling();
            }
            else if (IsRepresentativePayeeAvailable) await NavigateToRepresentativePayee();
            else if (IsAdminAvailable) await NavigateToAdmin();
        }

    }
}
