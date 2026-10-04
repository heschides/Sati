using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;

namespace Sati.ViewModels.Admin;

public partial class PlatformHealthViewModel(
    IPlatformHealthService service, IIncidentReporter incidentReporter) : ObservableObject
{
    public ObservableCollection<PlatformAgencyHealthDto> Agencies { get; } = [];
    public ObservableCollection<IncidentGroupDto> Incidents { get; } = [];

    [ObservableProperty] private IncidentHealthScoreDto? overallHealth;
    [ObservableProperty] private DateTime? lastRefreshedAt;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string localIncidentOutboxStatus = string.Empty;

    public string LastRefreshedLabel => LastRefreshedAt is null
        ? "Not loaded"
        : $"Updated {LastRefreshedAt:MMM d, h:mm tt}";
    public bool HasError => !string.IsNullOrWhiteSpace(StatusMessage);

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        StatusMessage = string.Empty;
        LocalIncidentOutboxStatus = DescribeLocalOutbox(incidentReporter.GetOutboxStatus());
        try
        {
            var dashboard = await service.GetDashboardAsync();
            OverallHealth = dashboard.OverallHealth;
            Replace(Agencies, dashboard.Agencies);
            Replace(Incidents, dashboard.Incidents);
            LastRefreshedAt = DateTime.Now;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Platform health could not be loaded. {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnLastRefreshedAtChanged(DateTime? value) => OnPropertyChanged(nameof(LastRefreshedLabel));
    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

    private static string DescribeLocalOutbox(IncidentOutboxSupportStatus? status)
    {
        if (status is null)
            return "Local incident delivery status is unavailable.";
        var oldest = status.OldestPendingAge is TimeSpan age
            ? $"; oldest waiting {Math.Max(0, (int)age.TotalMinutes)} minutes"
            : string.Empty;
        return $"Local incident delivery: {status.PendingForCurrentAccount} pending for this account" +
               oldest + $"; {status.HeldForOtherAccounts} held for other accounts on this " +
               $"Windows profile; {status.Quarantined} quarantined for support review.";
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
            target.Add(value);
    }
}
