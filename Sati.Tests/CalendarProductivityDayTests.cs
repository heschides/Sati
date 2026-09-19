using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Children;
using Sati.Views;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The calendar's own reading of the productivity rules: which day squares are tinted, what
/// each square says about its units, and that the tint really reaches the rendered view.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class CalendarProductivityDayTests
{
    private static readonly DateTime Today = new(2026, 9, 17);

    [Fact]
    public async Task TheMonthTintsCountedDaysAndLeavesFutureAndEmptyOnesAlone()
    {
        var logged = Note.Create("Logged.", Today.AddDays(-2), NoteStatus.Logged, 60, 1,
            noteType: NoteType.Contact);
        var pending = Note.Create("Pending.", Today.AddDays(-1), NoteStatus.Pending, 30, 1,
            noteType: NoteType.Contact);
        var scheduled = Note.Create("Next week.", Today.AddDays(4), NoteStatus.Pending, 60, 1,
            noteType: NoteType.Visit);
        var viewModel = await LoadAsync(logged, pending, scheduled);

        Assert.Equal(ProductivityDayKind.CountedWithSecuredUnits, Day(viewModel, Today.AddDays(-2)).ProductivityKind);
        Assert.Equal(ProductivityDayKind.CountedWithoutSecuredUnits, Day(viewModel, Today.AddDays(-1)).ProductivityKind);
        Assert.Equal(ProductivityDayKind.NotCounted, Day(viewModel, Today.AddDays(4)).ProductivityKind);
        Assert.Equal(ProductivityDayKind.NotCounted, Day(viewModel, Today).ProductivityKind);
        Assert.Equal(ProductivityDayKind.NotCounted, Day(viewModel, Today.AddDays(-3)).ProductivityKind);
    }

    [Fact]
    public async Task ASquareReportsItsUnitsByStatusAndSaysWhyItIsTinted()
    {
        var logged = Note.Create("Logged.", Today.AddDays(-2), NoteStatus.Logged, 60, 1,
            noteType: NoteType.Contact);
        var alsoPending = Note.Create("Pending.", Today.AddDays(-2), NoteStatus.Pending, 20, 1,
            noteType: NoteType.Contact);
        var viewModel = await LoadAsync(logged, alsoPending);

        var day = Day(viewModel, Today.AddDays(-2));
        Assert.Equal(6, day.TotalUnits);
        Assert.Equal("6 units", day.UnitsLabel);
        Assert.Equal("Pending 2 · Logged 4", day.UnitsByStatusLabel);
        Assert.Equal("In average", day.ProductivityLabel);
        Assert.Contains("6 units (Pending 2 · Logged 4)", day.AccessibleLabel);
        Assert.Contains("counts toward this month's daily average", day.AccessibleLabel);

        var pendingOnly = Day(await LoadAsync(alsoPending), Today.AddDays(-2));
        Assert.Equal("In average · none logged", pendingOnly.ProductivityLabel);
        Assert.Contains("nothing logged or approved yet", pendingOnly.AccessibleLabel);
    }

    [Fact]
    public async Task TheRenderedMonthPaintsTheCountedSquaresAndOnlyThose()
    {
        var logged = Note.Create("Logged.", Today.AddDays(-2), NoteStatus.Logged, 60, 1,
            noteType: NoteType.Contact);
        var pending = Note.Create("Pending.", Today.AddDays(-1), NoteStatus.Pending, 30, 1,
            noteType: NoteType.Contact);
        var viewModel = await LoadAsync(logged, pending);

        WpfUiHarness.Run(() =>
        {
            var view = new CalendarView { DataContext = viewModel };
            WpfUiHarness.Realize(view, 1400, 900);

            var secured = SquareFor(view, Today.AddDays(-2));
            var pendingOnly = SquareFor(view, Today.AddDays(-1));
            var untouched = SquareFor(view, Today.AddDays(-3));

            Assert.Equal(Resource(view, "ProductivityDayFillBrush"), secured.Background);
            Assert.Equal(Resource(view, "ProductivityPendingDayFillBrush"), pendingOnly.Background);
            Assert.Equal(Resource(view, "ProductivityPendingDayBorderBrush"), pendingOnly.BorderBrush);
            Assert.Equal(Resource(view, "SurfaceRaisedBrush"), untouched.Background);

            // The words carry it too, for anyone who cannot use the colour.
            var lines = WpfUiHarness.Descendants(secured).OfType<TextBlock>()
                .Select(text => text.Text).ToList();
            Assert.Contains("4 units", lines);
            Assert.Contains("In average", lines);
        });
    }

    [Fact]
    public async Task OnlyAnOpenDayOffersTheCountedTickAndEverySquareTakesRightClickTimeOff()
    {
        var open = Note.Create("Review.", Today.AddDays(-2), NoteStatus.Logged, 15, 1,
            noteType: NoteType.Form);
        var stillScheduled = Note.Create("Visit to write up.", Today.AddDays(-2), NoteStatus.Scheduled, 60, 1,
            noteType: NoteType.Visit);
        var settled = Note.Create("Settled.", Today.AddDays(-9), NoteStatus.Logged, 60, 1,
            noteType: NoteType.Contact);
        var viewModel = await LoadAsync(open, stillScheduled, settled);

        WpfUiHarness.Run(() =>
        {
            var view = new CalendarView { DataContext = viewModel };
            WpfUiHarness.Realize(view, 1400, 900);

            var openSquare = SquareFor(view, Today.AddDays(-2));
            var tick = WpfUiHarness.Descendants(openSquare).OfType<CheckBox>().Single();
            Assert.Equal(Visibility.Visible, tick.Visibility);
            // The visit left Scheduled on a past day has lapsed, so the day counts by
            // default; the tick is still offered so the case manager can hold it out.
            Assert.True(tick.IsChecked);
            Assert.Equal(
                viewModel.ToggleCountedDayCommand,
                ((System.Windows.Controls.Primitives.ButtonBase)tick).Command);

            // A settled day is no longer the case manager's to hold, so it offers no tick.
            var settledSquare = SquareFor(view, Today.AddDays(-9));
            Assert.Equal(
                Visibility.Collapsed,
                WpfUiHarness.Descendants(settledSquare).OfType<CheckBox>().Single().Visibility);

            // Right-click schedules time off from the month view, as it already did in the year view.
            var binding = Assert.Single(openSquare.InputBindings.OfType<MouseBinding>()
                .Where(item => item.MouseAction == MouseAction.RightClick));
            Assert.Equal(viewModel.ToggleExemptCommand, binding.Command);
        });
    }

    private static async Task<CalendarViewModel> LoadAsync(params Note[] notes)
    {
        var session = new SessionService();
        session.SetUser(User.Create(7, "calendar-user", "Calendar User", "hash", "salt",
            UserRole.CaseManager, null, 1));
        var viewModel = new CalendarViewModel(
            new NoExemptDates(),
            new FixedNotes(notes),
            session,
            today: () => Today)
        {
            CurrentYear = Today.Year,
            SelectedMonth = Today.Month
        };
        await viewModel.InitializeAsync();
        return viewModel;
    }

    private static CalendarDay Day(CalendarViewModel viewModel, DateTime date) =>
        viewModel.Months.Single(month => month.Month == date.Month).Cells
            .OfType<CalendarDay>()
            .Single(day => day.Date.Date == date.Date);

    /// <summary>The month view's square. The year overview draws the same day in its own grid.</summary>
    private static Button SquareFor(CalendarView view, DateTime date) =>
        WpfUiHarness.Descendants(view).OfType<Button>()
            .Single(button => button.DataContext is CalendarDay day &&
                              day.Date.Date == date.Date &&
                              ReferenceEquals(button.Style, view.FindResource("CalendarMonthDayButtonStyle")));

    private static Brush Resource(CalendarView view, string key) => (Brush)view.FindResource(key);

    private sealed class FixedNotes(IReadOnlyList<Note> notes) : INoteService
    {
        public Task<List<Note>> GetByYearAsync(int userId, int year) =>
            Task.FromResult(notes.Where(note => note.EventDate?.Year == year).ToList());

        public Task<Note> AddNoteAsync(Note candidate) => throw new NotSupportedException();
        public Task DeleteNoteAsync(Note candidate) => throw new NotSupportedException();
        public Task UpdateNoteAsync(Note candidate) => throw new NotSupportedException();
        public Task<List<Note>> GetAllByPersonAsync(int personId) => throw new NotSupportedException();
        public Task UpdateAbandonedNotesAsync(int abandonedAfterDays) => throw new NotSupportedException();
        public Task<List<Note>> GetMonthlyNotesAsync(int userId) => throw new NotSupportedException();
        public Task<List<Note>> GetDayScheduleAsync(int userId, DateTime date) => throw new NotSupportedException();
    }

    private sealed class NoExemptDates : IExemptDateService
    {
        public Task<List<ExemptDate>> GetByYearAsync(int userId, int year) =>
            Task.FromResult(new List<ExemptDate>());
        public Task<ExemptDate> AddAsync(int userId, DateTime date, string? reason = null) =>
            throw new NotSupportedException();
        public Task RemoveAsync(int exemptDateId) => throw new NotSupportedException();
    }
}
