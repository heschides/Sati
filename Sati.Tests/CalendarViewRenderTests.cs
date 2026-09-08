using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Children;
using Sati.Views;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class CalendarViewRenderTests
{
    [Fact]
    public void YearOverviewUsesReadableMonthColumnsAtDifferentWidths()
    {
        WpfUiHarness.Run(() =>
        {
            var view = new CalendarView();

            WpfUiHarness.Realize(view, 1400, 900);
            Assert.Equal(3, view.MonthColumnCount);

            WpfUiHarness.Realize(view, 930, 700);
            Assert.Equal(2, view.MonthColumnCount);

            WpfUiHarness.Realize(view, 650, 700);
            Assert.Equal(1, view.MonthColumnCount);
        });
    }

    [Fact]
    public void CalendarStartsInMonthView()
    {
        var viewModel = new CalendarViewModel(
            new EmptyExemptDateService(),
            new StaticYearNoteService(Note.Create(
                "Synthetic note.",
                DateTime.Today,
                NoteStatus.Logged,
                15,
                1,
                noteType: NoteType.Contact)),
            new SessionService());

        Assert.False(viewModel.IsYearOverview);
    }

    [Fact]
    public async Task ACalendarDayIsKeyboardReachableAndOpensTheFocusedNoteView()
    {
        var date = new DateTime(2026, 8, 12);
        var person = Person.Rehydrate(101, 7);
        person.FirstName = "Synthetic";
        person.LastName = "Client";
        var note = Note.Create(
            "Reviewed the synthetic service plan.",
            date,
            NoteStatus.Logged,
            30,
            person.Id,
            noteType: NoteType.Contact);
        note.StartTime = 60;
        note.Person = person;

        var session = new SessionService();
        session.SetUser(User.Create(
            7, "calendar-user", "Calendar User", "hash", "salt",
            UserRole.CaseManager, null, 1));
        var viewModel = new CalendarViewModel(
            new EmptyExemptDateService(),
            new StaticYearNoteService(note),
            session)
        {
            CurrentYear = date.Year,
            SelectedMonth = date.Month
        };
        await viewModel.InitializeAsync();
        var day = viewModel.Months
            .Single(month => month.Month == date.Month)
            .Cells.OfType<CalendarDay>()
            .Single(candidate => candidate.Date == date);

        WpfUiHarness.Run(() =>
        {
            var view = new CalendarView { DataContext = viewModel };
            WpfUiHarness.Realize(view);

            var dayButton = Assert.Single(
                WpfUiHarness.Descendants(view).OfType<Button>(),
                candidate => ReferenceEquals(candidate.CommandParameter, day) &&
                             candidate.MinHeight == 96);
            Assert.Equal(day.AccessibleLabel, AutomationProperties.GetName(dayButton));
            Assert.True(dayButton.IsTabStop);
            Assert.Equal(Visibility.Visible, dayButton.Visibility);
            Assert.Equal(VerticalAlignment.Stretch, dayButton.VerticalAlignment);
            Assert.True(double.IsNaN(dayButton.Height));

            dayButton.Command.Execute(dayButton.CommandParameter);
            var focusButton = WpfUiHarness.FindByAutomationName<Button>(
                view, "Focus on selected calendar day");
            focusButton.Command.Execute(focusButton.CommandParameter);
            WpfUiHarness.Realize(view);

            var backButton = WpfUiHarness.FindByAutomationName<Button>(
                view, "Return to calendar month");
            Assert.Equal(Visibility.Visible, backButton.Visibility);
            Assert.Equal(
                Visibility.Visible,
                Assert.IsType<Grid>(view.FindName("FocusedDayPanel")).Visibility);
            Assert.Equal(
                Visibility.Collapsed,
                Assert.IsType<Grid>(view.FindName("YearOverviewPanel")).Visibility);
            Assert.Contains(
                WpfUiHarness.Descendants(view).OfType<TextBlock>(),
                text => text.Text == "Reviewed the synthetic service plan.");
            Assert.Contains(
                WpfUiHarness.Descendants(view).OfType<TextBlock>(),
                text => text.Text == "Synthetic Client");
            Assert.Contains(
                WpfUiHarness.Descendants(view).OfType<TextBlock>(),
                text => text.Inlines.OfType<Run>()
                    .Any(run => run.Text == "8:00 AM–8:30 AM"));
        });
    }

    private sealed class StaticYearNoteService(Note note) : INoteService
    {
        public Task<List<Note>> GetByYearAsync(int userId, int year) =>
            Task.FromResult(note.EventDate?.Year == year
                ? new List<Note> { note }
                : []);

        public Task<Note> AddNoteAsync(Note candidate) => throw new NotSupportedException();
        public Task DeleteNoteAsync(Note candidate) => throw new NotSupportedException();
        public Task UpdateNoteAsync(Note candidate) => throw new NotSupportedException();
        public Task<List<Note>> GetAllByPersonAsync(int personId) => throw new NotSupportedException();
        public Task UpdateAbandonedNotesAsync(int abandonedAfterDays) => throw new NotSupportedException();
        public Task<List<Note>> GetMonthlyNotesAsync(int userId) => throw new NotSupportedException();
        public Task<List<Note>> GetDayScheduleAsync(int userId, DateTime date) => throw new NotSupportedException();
    }

    private sealed class EmptyExemptDateService : IExemptDateService
    {
        public Task<List<ExemptDate>> GetByYearAsync(int userId, int year) =>
            Task.FromResult(new List<ExemptDate>());
        public Task<ExemptDate> AddAsync(int userId, DateTime date, string? reason = null) =>
            throw new NotSupportedException();
        public Task RemoveAsync(int id) => throw new NotSupportedException();
    }
}
