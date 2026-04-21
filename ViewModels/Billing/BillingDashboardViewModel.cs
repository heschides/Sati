using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Threading;

namespace Sati.ViewModels.Billing
{
    public partial class BillingDashboardViewModel : ObservableObject
    {
        private readonly BillingOverviewViewModel _overviewViewModel;
        private readonly BillingSubmissionsViewModel _submissionsViewModel;
        private readonly BillingRemittancesViewModel _remittancesViewModel;
        private readonly BillingAlertsViewModel _alertsViewModel;

        public BillingDashboardViewModel(
            BillingOverviewViewModel overviewViewModel,
            BillingSubmissionsViewModel submissionsViewModel,
            BillingRemittancesViewModel remittancesViewModel,
            BillingAlertsViewModel alertsViewModel)
        {
            _overviewViewModel = overviewViewModel;
            _submissionsViewModel = submissionsViewModel;
            _remittancesViewModel = remittancesViewModel;
            _alertsViewModel = alertsViewModel;

            CurrentSubView = _overviewViewModel;
        }

        [ObservableProperty] private object? currentSubView;

        public bool IsOverviewActive => CurrentSubView is BillingOverviewViewModel;
        public bool IsSubmissionsActive => CurrentSubView is BillingSubmissionsViewModel;
        public bool IsRemittancesActive => CurrentSubView is BillingRemittancesViewModel;
        public bool IsAlertsActive => CurrentSubView is BillingAlertsViewModel;

        partial void OnCurrentSubViewChanged(object? value)
        {
            OnPropertyChanged(nameof(IsOverviewActive));
            OnPropertyChanged(nameof(IsSubmissionsActive));
            OnPropertyChanged(nameof(IsRemittancesActive));
            OnPropertyChanged(nameof(IsAlertsActive));
        }

        [RelayCommand]
        private async Task NavigateToOverview()
        {
            CurrentSubView = null;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            CurrentSubView = _overviewViewModel;
        }

        [RelayCommand]
        private async Task NavigateToSubmissions()
        {
            CurrentSubView = null;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            CurrentSubView = _submissionsViewModel;
        }

        [RelayCommand]
        private async Task NavigateToRemittances()
        {
            CurrentSubView = null;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            CurrentSubView = _remittancesViewModel;
        }

        [RelayCommand]
        private async Task NavigateToAlerts()
        {
            CurrentSubView = null;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            CurrentSubView = _alertsViewModel;
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }
}