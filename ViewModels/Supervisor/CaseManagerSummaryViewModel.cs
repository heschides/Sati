using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Models;

namespace Sati.ViewModels.Supervisor
{
    public partial class CaseManagerSummaryViewModel : ObservableObject
    {
        private readonly User _user;
        public int UserId => _user.Id;

        public CaseManagerSummaryViewModel(User user, List<PersonSummary> people,
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

            OverdueCount = upcomingEvents.Count(e => e.Kind == UpcomingEventKind.LateReview);
            ClientsWithOverdueItems = upcomingEvents
                .Where(e => e.Kind == UpcomingEventKind.LateReview)
                .Select(e => e.PersonId)
                .Distinct()
                .Count();
            ClientsClearOfOverdueItems = Math.Max(0, ClientCount - ClientsWithOverdueItems);
            CompliancePercent = ClientCount == 0
                ? 0
                : 100m * ClientsClearOfOverdueItems / ClientCount;
            ComplianceStatusLevel = ClientsWithOverdueItems == 0 ? "Ok" : "Danger";
            ComplianceStatusLabel = ClientCount == 0
                ? "No clients"
                : ClientsWithOverdueItems == 0 ? "Clear" : "Review";
            HasOverdue = OverdueCount > 0;
            DetailHeading = $"{DisplayName} — upcoming items";

            LoggedCount = monthlyNotes.Count(n => n.Status == NoteStatus.Logged);
            PendingCount = monthlyNotes.Count(n => n.Status == NoteStatus.Pending);
            AbandonedCount = monthlyNotes.Count(n => n.Status == NoteStatus.Abandoned);
            ScheduledCount = monthlyNotes.Count(n => n.Status == NoteStatus.Scheduled);
            CancelledCount = monthlyNotes.Count(n => n.Status == NoteStatus.Cancelled);
            DelayedCount = monthlyNotes.Count(n => n.Status == NoteStatus.Delayed);
        }

        /// <summary>
        /// Days this case manager marked as having produced nothing billable, once their
        /// documentation window has closed. Before that the mark is a working annotation they can
        /// still change by writing the day up, so a supervisor would be asking too early.
        /// </summary>
        public IReadOnlyList<DateTime> SettledDaysWithoutBillableWork { get; private set; } = [];

        public bool HasSettledDaysWithoutBillableWork => SettledDaysWithoutBillableWork.Count > 0;

        public string SettledDaysWithoutBillableWorkLabel =>
            SettledDaysWithoutBillableWork.Count == 1
                ? "1 day with no billable work"
                : $"{SettledDaysWithoutBillableWork.Count} days with no billable work";

        public string SettledDaysWithoutBillableWorkDetail =>
            string.Join(" · ", SettledDaysWithoutBillableWork.Select(day => day.ToString("MMM d")));

        public void SetSettledDaysWithoutBillableWork(IReadOnlyList<DateTime> days)
        {
            SettledDaysWithoutBillableWork = days;
            OnPropertyChanged(nameof(SettledDaysWithoutBillableWork));
            OnPropertyChanged(nameof(HasSettledDaysWithoutBillableWork));
            OnPropertyChanged(nameof(SettledDaysWithoutBillableWorkLabel));
            OnPropertyChanged(nameof(SettledDaysWithoutBillableWorkDetail));
        }

        public string DisplayName { get; }
        public string Initials { get; }
        public int ClientCount { get; }
        public string ClientCountLabel { get; }
        public int NotesThisMonth { get; }
        public decimal UnitsThisMonth { get; }
        public int OverdueCount { get; }
        public int ClientsWithOverdueItems { get; }
        public int ClientsClearOfOverdueItems { get; }
        public decimal CompliancePercent { get; }
        public string ComplianceStatusLevel { get; }
        public string ComplianceStatusLabel { get; }
        public bool HasOverdue { get; }
        public string DetailHeading { get; }
        public List<UpcomingEvent> UpcomingEvents { get; }

        public int LoggedCount { get; }
        public int PendingCount { get; }
        public int AbandonedCount { get; }
        public int ScheduledCount { get; }
        public int CancelledCount { get; }
        public int DelayedCount { get; }

        [ObservableProperty] private bool isSelected;

        public decimal ProgressPercent { get; private set; }
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

            ProgressPercent = Math.Min(100.0m * UnitsThisMonth / threshold, 100);

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

        private static string GetInitials(string displayName)
        {
            var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2
                ? $"{parts[0][0]}{parts[^1][0]}"
                : displayName.Length > 0 ? displayName[0].ToString() : "?";
        }
    }
}
