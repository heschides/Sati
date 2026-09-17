using Sati.Services;
using System.Windows;

namespace Sati.Views
{
    /// <summary>
    /// Names the workdays whose documentation window is about to close with nothing written up.
    /// It carries dates only: no client names, no narrative.
    /// </summary>
    public partial class UndocumentedDayPromptWindow : Window
    {
        public UndocumentedDayPromptWindow()
        {
            InitializeComponent();
        }

        /// <summary>True when the case manager chose to write the notes rather than dismiss.</summary>
        public bool WriteThemNowRequested { get; private set; }

        public void Configure(
            IReadOnlyList<DateTime> days,
            int documentationWindowDays,
            UndocumentedDayPromptReason reason)
        {
            ArgumentNullException.ThrowIfNull(days);
            var today = DateTime.Today;
            Heading.Text = days.Count == 1
                ? "One workday has nothing documented yet"
                : $"{days.Count} workdays have nothing documented yet";
            Explanation.Text = reason == UndocumentedDayPromptReason.Shutdown
                ? $"Service has {documentationWindowDays} days to be documented. After that these days cannot be billed, and their units come off this month's total without lowering what the month requires."
                : $"Service has {documentationWindowDays} days to be documented. These days are at the end of that window, so anything not written up is lost from this month's total.";

            DayList.ItemsSource = days
                .OrderBy(day => day)
                .Select(day => new
                {
                    DayLabel = day.ToString("dddd, MMMM d"),
                    DeadlineLabel = DeadlineText(day.AddDays(documentationWindowDays), today)
                })
                .ToList();

            WriteNowButton.Content = reason == UndocumentedDayPromptReason.Shutdown
                ? "Stay open and write them"
                : "Write them now";
        }

        private static string DeadlineText(DateTime lastDay, DateTime today) =>
            (lastDay.Date - today.Date).Days switch
            {
                <= 0 => "Last day to document this is today",
                1 => "Last day to document this is tomorrow",
                var days => $"Last day to document this is in {days} days"
            };

        private void WriteNow_Click(object sender, RoutedEventArgs e)
        {
            WriteThemNowRequested = true;
            DialogResult = true;
        }

        private void Later_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
