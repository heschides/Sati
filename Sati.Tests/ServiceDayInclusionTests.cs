using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The case manager's decision about whether an open day counts toward the documented daily
/// average: stored on their own calendar only, and read back by the calendar and the average.
/// </summary>
public sealed class ServiceDayInclusionTests
{
    private static readonly DateTime Today = new(2026, 9, 17);

    [Fact]
    public async Task ADecisionIsStoredOncePerDayAndCanBeChangedOrCleared()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var service = Service(fixture, fixture.CaseManagerOne);
        var day = new DateTime(2026, 9, 14, 11, 30, 0);

        var first = await service.SetAsync(fixture.CaseManagerOne.Id, day, true);
        Assert.Equal(day.Date, first.Date);
        Assert.True(first.IsIncluded);

        var second = await service.SetAsync(fixture.CaseManagerOne.Id, day.Date, false);
        Assert.Equal(first.Id, second.Id);
        var stored = await service.GetByYearAsync(fixture.CaseManagerOne.Id, 2026);
        Assert.False(Assert.Single(stored).IsIncluded);

        await service.ClearAsync(fixture.CaseManagerOne.Id, day.Date);
        Assert.Empty(await service.GetByYearAsync(fixture.CaseManagerOne.Id, 2026));
    }

    [Fact]
    public async Task OneCaseManagerCannotReadOrChangeAnothersDays()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var mine = Service(fixture, fixture.CaseManagerOne);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            mine.SetAsync(fixture.CaseManagerTwo.Id, new DateTime(2026, 9, 14), true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            mine.GetByYearAsync(fixture.CaseManagerTwo.Id, 2026));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            mine.ClearAsync(fixture.CaseManagerTwo.Id, new DateTime(2026, 9, 14)));

        await using var db = fixture.Factory.CreateDbContext();
        Assert.Empty(await db.ServiceDayInclusions.ToListAsync());
    }

    [Fact]
    public async Task TheCalendarCountsAnOpenDayWhenTheCaseManagerTicksIt()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var monday = Today.AddDays(-3);
        var notes = new FixedNotes(
            Note.Create("Review.", monday, NoteStatus.Logged, 15, fixture.PersonOneId,
                noteType: NoteType.Form),
            Note.Create("Visit to write up.", monday, NoteStatus.Scheduled, 60, fixture.PersonOneId,
                noteType: NoteType.Visit));
        var inclusions = Service(fixture, fixture.CaseManagerOne);
        var calendar = Calendar(fixture, notes, inclusions);
        await calendar.InitializeAsync();

        var day = Day(calendar, monday);
        Assert.Equal(ProductivityDayKind.OpenUntilDocumented, day.ProductivityKind);
        Assert.True(day.CanChooseCounted);
        Assert.False(day.HasCaseManagerChoice);

        await calendar.ToggleCountedDayCommand.ExecuteAsync(day);

        var counted = Day(calendar, monday);
        Assert.Equal(ProductivityDayKind.CountedWithSecuredUnits, counted.ProductivityKind);
        Assert.True(counted.HasCaseManagerChoice);
        Assert.True(Assert.Single(await inclusions.GetByYearAsync(fixture.CaseManagerOne.Id, 2026)).IsIncluded);
    }

    [Fact]
    public async Task ReturningADayToSatisOwnReadingClearsTheStoredDecision()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var tuesday = Today.AddDays(-2);
        // Finished: documented work, nothing left scheduled, so Sati counts it by itself.
        var notes = new FixedNotes(
            Note.Create("Visit.", tuesday, NoteStatus.Logged, 60, fixture.PersonOneId,
                noteType: NoteType.Visit));
        var inclusions = Service(fixture, fixture.CaseManagerOne);
        var calendar = Calendar(fixture, notes, inclusions);
        await calendar.InitializeAsync();

        // Held back by hand, then handed back to Sati.
        await calendar.ToggleCountedDayCommand.ExecuteAsync(Day(calendar, tuesday));
        Assert.False(Assert.Single(await inclusions.GetByYearAsync(fixture.CaseManagerOne.Id, 2026)).IsIncluded);
        Assert.Equal(ProductivityDayKind.OpenUntilDocumented, Day(calendar, tuesday).ProductivityKind);

        await calendar.ToggleCountedDayCommand.ExecuteAsync(Day(calendar, tuesday));

        // No row is left behind saying what Sati already reads, so a later change of
        // schedule is still followed.
        Assert.Empty(await inclusions.GetByYearAsync(fixture.CaseManagerOne.Id, 2026));
        Assert.Equal(ProductivityDayKind.CountedWithSecuredUnits, Day(calendar, tuesday).ProductivityKind);
    }

    [Fact]
    public async Task ASettledDayOffersNoChoiceAndCountsAnyway()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var settled = Today.AddDays(-9);
        var notes = new FixedNotes(
            Note.Create("Written up late.", settled, NoteStatus.Logged, 60, fixture.PersonOneId,
                noteType: NoteType.Visit),
            Note.Create("Never documented.", settled, NoteStatus.Scheduled, 60, fixture.PersonOneId,
                noteType: NoteType.Visit));
        var calendar = Calendar(fixture, notes, Service(fixture, fixture.CaseManagerOne));
        await calendar.InitializeAsync();

        var day = Day(calendar, settled);
        Assert.False(day.CanChooseCounted);
        Assert.Equal(ProductivityDayKind.CountedWithSecuredUnits, day.ProductivityKind);
    }

    /// <summary>
    /// A workday that produced nothing billable is the case the pace figures used to miss: it
    /// sat in capacity pretending to be worth a day's work until its window closed a week later.
    /// </summary>
    [Fact]
    public async Task AnEmptyWorkdayCanBeMarkedAsHavingNoBillableWork()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var empty = Today.AddDays(-6);  // Friday September 11, a workday with nothing on it
        var notes = new FixedNotes(
            Note.Create("Elsewhere.", Today.AddDays(-1), NoteStatus.Logged, 60, fixture.PersonOneId,
                noteType: NoteType.Visit));
        var inclusions = Service(fixture, fixture.CaseManagerOne);
        var calendar = Calendar(fixture, notes, inclusions);
        await calendar.InitializeAsync();

        var day = Day(calendar, empty);
        Assert.Equal(ProductivityDayKind.NotCounted, day.ProductivityKind);
        Assert.True(day.CanChooseCounted);
        Assert.Contains("no billable work", day.CountedToggleLabel);

        await calendar.ToggleCountedDayCommand.ExecuteAsync(day);

        var marked = Day(calendar, empty);
        Assert.Equal(ProductivityDayKind.CountedWithoutBillableWork, marked.ProductivityKind);
        Assert.Equal("In average · no billable work", marked.ProductivityLabel);
        Assert.True(Assert.Single(await inclusions.GetByYearAsync(fixture.CaseManagerOne.Id, 2026)).IsIncluded);

        // And it stops being work waiting to be written up.
        Assert.DoesNotContain(empty, ProductivityForecast.PastWorkdaysStillToDocument(
            [empty], notes.Facts(), Today, 7,
            CalendarViewModel.ChoicesByDate(await inclusions.GetByYearAsync(fixture.CaseManagerOne.Id, 2026))));
    }

    private static ServiceDayInclusionService Service(NoteEntryFixture fixture, User actor)
    {
        var session = new SessionService();
        session.SetUser(actor);
        return new ServiceDayInclusionService(fixture.Factory, session);
    }

    private static CalendarViewModel Calendar(
        NoteEntryFixture fixture, INoteService notes, IServiceDayInclusionService inclusions)
    {
        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        return new CalendarViewModel(
            new NoExemptDates(),
            notes,
            session,
            today: () => Today,
            serviceDayInclusionService: inclusions)
        {
            CurrentYear = Today.Year,
            SelectedMonth = Today.Month
        };
    }

    private static CalendarDay Day(CalendarViewModel calendar, DateTime date) =>
        calendar.Months.Single(month => month.Month == date.Month).Cells
            .OfType<CalendarDay>()
            .Single(day => day.Date.Date == date.Date);

    private sealed class FixedNotes(params Note[] notes) : INoteService
    {
        public IEnumerable<ProductivityNoteFact> Facts() => notes.Select(note =>
            new ProductivityNoteFact(note.EventDate, note.Status?.ToString(), note.Minutes));

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
