using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Models;

namespace Sati.ViewModels.Supervisor
{
    public partial class CaseManagerSummaryViewModel : ObservableObject
    {
        // -------------------------------------------------------------------------
        // Private state
        // -------------------------------------------------------------------------

        private readonly User _user;

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public CaseManagerSummaryViewModel(User user, List<Person> people,
            List<Note> monthlyNotes, List<UpcomingEvent> upcomingEvents)
        {
            _user = user;

            DisplayName = user.DisplayName;
            Initials = GetInitials(user.DisplayName);
            ClientCount = people.Count;
            ClientCountLabel = $"{ClientCount} client{(ClientCount == 1 ? "" : "s")}";
            NotesThisMonth = monthlyNotes.Count;
            UnitsThisMonth = monthlyNotes.Sum(n => n.Units ?? 0);
            UpcomingEvents = upcomingEvents;

            OverdueCount = upcomingEvents.Count(e =>
                e.Kind == UpcomingEventKind.LateReview);
            HasOverdue = OverdueCount > 0;

            DetailHeading = $"{DisplayName} — upcoming items";
        }

        // -------------------------------------------------------------------------
        // Properties
        // -------------------------------------------------------------------------

        public string DisplayName { get; }
        public string Initials { get; }
        public int ClientCount { get; }
        public string ClientCountLabel { get; }
        public int NotesThisMonth { get; }
        public int UnitsThisMonth { get; }
        public int OverdueCount { get; }
        public bool HasOverdue { get; }
        public string DetailHeading { get; }
        public List<UpcomingEvent> UpcomingEvents { get; }

        // -------------------------------------------------------------------------
        // Selection state — mutable, bound by the DataGrid
        // -------------------------------------------------------------------------

        [ObservableProperty] private bool isSelected;

        // -------------------------------------------------------------------------
        // Progress and status
        // -------------------------------------------------------------------------

        // ProgressPercent and StatusLevel require the incentive threshold, which
        // varies per user. SupervisorDashboardViewModel passes it in via
        // SetThreshold() after construction once incentive data is loaded.

        public double ProgressPercent { get; private set; }
        public string StatusLevel { get; private set; } = "Warning";
        public string StatusLabel { get; private set; } = "No data";

        public void SetThreshold(int threshold)
        {
            if (threshold <= 0)
            {
                ProgressPercent = 0;
                StatusLevel = "Warning";
                StatusLabel = "No threshold";
                return;
            }

            ProgressPercent = Math.Min(100.0 * UnitsThisMonth / threshold, 100);

            (StatusLevel, StatusLabel) = ProgressPercent switch
            {
                >= 100 => ("Ok", "On track"),
                >= 50 => ("Warning", "In progress"),
                _ => ("Danger", "Behind")
            };

            OnPropertyChanged(nameof(ProgressPercent));
            OnPropertyChanged(nameof(StatusLevel));
            OnPropertyChanged(nameof(StatusLabel));
        }

        // -------------------------------------------------------------------------
        // Private helpers
        // -------------------------------------------------------------------------

        private static string GetInitials(string displayName)
        {
            var parts = displayName.Split(' ',
                StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2
                ? $"{parts[0][0]}{parts[^1][0]}"
                : displayName.Length > 0 ? displayName[0].ToString() : "?";
        }
    }
}