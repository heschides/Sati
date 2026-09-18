using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Models;
using Sati.Services;
using System.Windows;
using System.Windows.Automation;

namespace Sati.Views
{
    /// <summary>
    /// Asks, at shutdown, what to do with each Scheduled item whose day has passed or is ending
    /// undone: move it to the next workday or delete it. Every item defaults to moving, so
    /// pressing Enter never loses planned work.
    /// </summary>
    public partial class LeftoverScheduledWorkWindow : Window
    {
        private List<LeftoverScheduledWorkRow> _rows = [];

        public LeftoverScheduledWorkWindow()
        {
            InitializeComponent();
        }

        /// <summary>The choices made, or empty when the case manager chose to keep Sati open.</summary>
        public IReadOnlyList<LeftoverScheduledWorkDecision> Decisions { get; private set; } = [];

        public void Configure(IReadOnlyList<Note> items, DateTime nextWorkday, DateTime today)
        {
            ArgumentNullException.ThrowIfNull(items);
            var target = DayName(nextWorkday, today);
            _rows = items.Select(note => new LeftoverScheduledWorkRow(note, target, today)).ToList();
            ItemList.ItemsSource = _rows;

            Heading.Text = items.Count == 1
                ? "One scheduled item did not get done"
                : $"{items.Count} scheduled items did not get done";
            Explanation.Text =
                $"Planned work left on a finished day clutters the calendar. Move each item to {target}, " +
                "or delete it if it no longer needs doing. Paperwork that is still due will come back on " +
                "the daily agenda either way.";
            MoveAllButton.Content = $"Move all to {target}";
            AutomationProperties.SetName(MoveAllButton, $"Choose move to {target} for every item");
        }

        internal static string DayName(DateTime date, DateTime today) =>
            (date.Date - today.Date).Days == 1
                ? $"tomorrow ({date:dddd, MMMM d})"
                : date.ToString("dddd, MMMM d");

        private void MoveAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows) row.IsMove = true;
        }

        private void DeleteAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows) row.IsDelete = true;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            Decisions = _rows
                .Select(row => new LeftoverScheduledWorkDecision(
                    row.Note,
                    row.IsDelete ? LeftoverScheduledWorkChoice.Delete : LeftoverScheduledWorkChoice.MoveToNextWorkday))
                .ToList();
            DialogResult = true;
        }

        private void StayOpen_Click(object sender, RoutedEventArgs e)
        {
            Decisions = [];
            DialogResult = false;
        }
    }

    public sealed partial class LeftoverScheduledWorkRow : ObservableObject
    {
        public LeftoverScheduledWorkRow(Note note, string targetDay, DateTime today)
        {
            Note = note;
            var item = new WorkAgendaItem(note);
            Summary = item.Summary;
            var when = note.EventDate?.Date == today.Date
                ? "Today"
                : note.EventDate?.ToString("ddd, MMM d") ?? "No date";
            Details = $"{when} · {item.ClientName} · {item.TypeLabel}";
            MoveLabel = $"Move to {targetDay}";
            MoveAutomationName = $"Move {Summary} for {item.ClientName} to {targetDay}";
            DeleteAutomationName = $"Delete {Summary} for {item.ClientName}";
            GroupName = $"leftover-{note.Id}";
        }

        public Note Note { get; }
        public string Summary { get; }
        public string Details { get; }
        public string MoveLabel { get; }
        public string MoveAutomationName { get; }
        public string DeleteAutomationName { get; }
        public string GroupName { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsDelete))]
        private bool isMove = true;

        public bool IsDelete
        {
            get => !IsMove;
            set => IsMove = !value;
        }
    }
}
