using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Models;

namespace Sati.ViewModels.Supervisor
{
    public record OverdueRow(
        string CaseManagerName,
        string ClientName,
        string Title,
        DateTime Date,
        int DaysOverdue);

    public record UpcomingRow(
        string CaseManagerName,
        string ClientName,
        string Title,
        DateTime Date,
        UpcomingEventKind Kind);

    public partial class OverdueItemsViewModel : ObservableObject
    {
        private IReadOnlyList<CaseManagerSummaryViewModel> _allManagers = [];

        [ObservableProperty] private IReadOnlyList<OverdueRow> overdueRows = [];
        [ObservableProperty] private IReadOnlyList<UpcomingRow> upcomingRows = [];
        [ObservableProperty] private List<string> caseManagerNames = [];
        [ObservableProperty] private string selectedCaseManagerName = "All";
        [ObservableProperty] private int upcomingWindowDays = 30;

        public void Refresh(IReadOnlyList<CaseManagerSummaryViewModel> managers)
        {
            _allManagers = managers;
            CaseManagerNames = ["All", .. managers.Select(cm => cm.DisplayName)];
            ApplyFilter();
        }

        partial void OnSelectedCaseManagerNameChanged(string value) => ApplyFilter();
        partial void OnUpcomingWindowDaysChanged(int value) => ApplyFilter();

        private void ApplyFilter()
        {
            var source = SelectedCaseManagerName == "All"
                ? _allManagers
                : _allManagers.Where(cm => cm.DisplayName == SelectedCaseManagerName).ToList();

            var today = DateTime.Today;
            var windowEnd = today.AddDays(UpcomingWindowDays);

            OverdueRows = source
                .SelectMany(cm => cm.UpcomingEvents
                    .Where(e => e.Kind == UpcomingEventKind.LateReview)
                    .Select(e => new OverdueRow(
                        cm.DisplayName,
                        e.ClientName,
                        e.Title,
                        e.Date,
                        (today - e.Date).Days)))
                .OrderByDescending(r => r.DaysOverdue)
                .ToList();

            UpcomingRows = source
                .SelectMany(cm => cm.UpcomingEvents
                    .Where(e => e.Kind != UpcomingEventKind.LateReview
                             && e.Date >= today
                             && e.Date <= windowEnd)
                    .Select(e => new UpcomingRow(
                        cm.DisplayName,
                        e.ClientName,
                        e.Title,
                        e.Date,
                        e.Kind)))
                .OrderBy(r => r.Date)
                .ToList();
        }
    }
}