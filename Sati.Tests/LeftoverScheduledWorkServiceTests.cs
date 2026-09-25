using Sati.Data;
using Sati.Helpers;
using Sati.Models;
using Sati.Services;
using Sati.Views;
using Xunit;

namespace Sati.Tests;

public sealed class LeftoverScheduledWorkServiceTests
{
    // Friday, September 18, 2026.
    private static readonly DateTime Today = new(2026, 9, 18);

    [Fact]
    public async Task OnlyScheduledItemsDatedTodayOrEarlierAreOffered()
    {
        var notes = new RecordingNoteService(
            Note(1, Today.AddDays(-3), NoteStatus.Scheduled),
            Note(2, Today, NoteStatus.Scheduled),
            Note(3, Today.AddDays(1), NoteStatus.Scheduled),
            Note(4, Today.AddDays(-1), NoteStatus.Pending),
            Note(5, Today.AddDays(-1), NoteStatus.Logged),
            Note(6, new DateTime(2025, 12, 30), NoteStatus.Scheduled));

        var result = await Service(notes).LoadAsync(41, Today);

        Assert.Equal([6, 1, 2], result.Select(note => note.Id));
    }

    [Fact]
    public async Task NextWorkdaySkipsTheWeekendHolidaysAndTheCaseManagersTimeOff()
    {
        // Monday the 21st is time off; Tuesday the 22nd is the next day actually worked.
        var service = Service(
            new RecordingNoteService(),
            timeOff: [new DateTime(2026, 9, 21)]);

        Assert.Equal(new DateTime(2026, 9, 22), await service.NextWorkdayAsync(41, Today));
    }

    [Fact]
    public void NextWorkdayHonoursAgencyHolidaysAndExcludedWeekdays()
    {
        // Labor Day 2026 is Monday, September 7; Tuesdays are excluded by the agency.
        var settings = new Settings { ExcludeLaborDay = true, ExcludeTuesday = true };

        Assert.Equal(
            new DateTime(2026, 9, 9),
            WorkdayHelper.NextWorkday(new DateTime(2026, 9, 4), settings, []));
    }

    [Fact]
    public void NextWorkdayIsNullWhenEveryWeekdayIsExcluded()
    {
        var settings = new Settings
        {
            ExcludeMonday = true, ExcludeTuesday = true, ExcludeWednesday = true,
            ExcludeThursday = true, ExcludeFriday = true
        };

        Assert.Null(WorkdayHelper.NextWorkday(Today, settings, []));
    }

    [Fact]
    public async Task ChoicesMoveOrDeleteThroughTheNoteBoundary()
    {
        var moved = Note(1, Today.AddDays(-2), NoteStatus.Scheduled);
        var removed = Note(2, Today, NoteStatus.Scheduled);
        var notes = new RecordingNoteService(moved, removed);
        var target = new DateTime(2026, 9, 21);

        var result = await Service(notes).ApplyAsync(
            [
                new(moved, LeftoverScheduledWorkChoice.MoveToNextWorkday),
                new(removed, LeftoverScheduledWorkChoice.Delete)
            ],
            target,
            Today);

        Assert.Equal(new LeftoverScheduledWorkResult(1, 0, 1, 0), result);
        Assert.Equal(target, Assert.Single(notes.Updated).EventDate);
        Assert.Equal(2, Assert.Single(notes.Deleted).Id);
    }

    [Fact]
    public async Task ANoteThatIsNoLongerScheduledIsNeverDeleted()
    {
        // The row was loaded as Scheduled, then the note was started before the choice
        // was applied. Deleting it would destroy work in progress.
        var started = Note(1, Today, NoteStatus.Scheduled);
        started.Status = NoteStatus.Pending;
        var notes = new RecordingNoteService(started);

        var result = await Service(notes).ApplyAsync(
            [new(started, LeftoverScheduledWorkChoice.Delete)],
            Today.AddDays(3),
            Today);

        Assert.Equal(new LeftoverScheduledWorkResult(0, 0, 0, 1), result);
        Assert.Empty(notes.Deleted);
    }

    [Fact]
    public async Task KeepingTodaysItemForTodayWritesNothing()
    {
        // Closing Sati mid-day to come back later: today's plan stays exactly as it is and is
        // offered again at the next close.
        var morning = Note(1, Today, NoteStatus.Scheduled);
        morning.StartTime = 120;
        var notes = new RecordingNoteService(morning);

        var result = await Service(notes).ApplyAsync(
            [new(morning, LeftoverScheduledWorkChoice.KeepForToday)],
            new DateTime(2026, 9, 21),
            Today);

        Assert.Equal(new LeftoverScheduledWorkResult(0, 1, 0, 0), result);
        Assert.Empty(notes.Updated);
        Assert.Empty(notes.Deleted);
        Assert.Equal(Today, morning.EventDate);
        Assert.Equal(120, morning.StartTime);
    }

    [Fact]
    public async Task KeepingAPastItemBringsItToTodayWithoutItsOldStartTime()
    {
        // Leaving it on its finished day would recreate the backlog this prompt clears.
        var lapsed = Note(1, Today.AddDays(-2), NoteStatus.Scheduled);
        lapsed.StartTime = 90;
        var notes = new RecordingNoteService(lapsed);

        var result = await Service(notes).ApplyAsync(
            [new(lapsed, LeftoverScheduledWorkChoice.KeepForToday)],
            new DateTime(2026, 9, 21),
            Today);

        Assert.Equal(new LeftoverScheduledWorkResult(0, 1, 0, 0), result);
        var written = Assert.Single(notes.Updated);
        Assert.Equal(Today, written.EventDate);
        Assert.Null(written.StartTime);
    }

    [Fact]
    public async Task ARefusedKeepLeavesThePastItemAndItsStartTimeWhereTheyWere()
    {
        var lapsed = Note(1, Today.AddDays(-1), NoteStatus.Scheduled);
        lapsed.StartTime = 60;
        var notes = new RecordingNoteService(lapsed) { RefuseId = 1 };

        var result = await Service(notes).ApplyAsync(
            [new(lapsed, LeftoverScheduledWorkChoice.KeepForToday)],
            new DateTime(2026, 9, 21),
            Today);

        Assert.Equal(new LeftoverScheduledWorkResult(0, 0, 0, 1), result);
        Assert.Equal(Today.AddDays(-1), lapsed.EventDate);
        Assert.Equal(60, lapsed.StartTime);
    }

    [Fact]
    public async Task OneRefusedItemDoesNotStopTheRestAndKeepsItsDate()
    {
        var refused = Note(1, Today.AddDays(-1), NoteStatus.Scheduled);
        var fine = Note(2, Today, NoteStatus.Scheduled);
        var notes = new RecordingNoteService(refused, fine) { RefuseId = 1 };
        var target = new DateTime(2026, 9, 21);

        var result = await Service(notes).ApplyAsync(
            [
                new(refused, LeftoverScheduledWorkChoice.MoveToNextWorkday),
                new(fine, LeftoverScheduledWorkChoice.MoveToNextWorkday)
            ],
            target,
            Today);

        Assert.Equal(new LeftoverScheduledWorkResult(1, 0, 0, 1), result);
        Assert.Equal(Today.AddDays(-1), refused.EventDate);
        Assert.Equal(target, fine.EventDate);
    }

    [Fact]
    public void EachRowOffersThreeChoicesAndDefaultsToMoving()
    {
        var today = new LeftoverScheduledWorkRow(
            Note(1, Today, NoteStatus.Scheduled), "Monday, September 21", Today);
        var lapsed = new LeftoverScheduledWorkRow(
            Note(2, Today.AddDays(-1), NoteStatus.Scheduled), "Monday, September 21", Today);

        Assert.Equal(LeftoverScheduledWorkChoice.MoveToNextWorkday, today.Choice);
        Assert.Equal("Keep for today", today.KeepLabel);
        Assert.Equal("Move to today", lapsed.KeepLabel);

        // A radio group unchecks the old option by pushing false into it; that must never
        // change the choice, only the newly checked option's true does.
        today.IsKeep = true;
        today.IsMove = false;
        Assert.Equal(LeftoverScheduledWorkChoice.KeepForToday, today.Choice);
        Assert.True(today.IsKeep);
        Assert.False(today.IsMove);
        Assert.False(today.IsDelete);

        today.IsDelete = true;
        today.IsKeep = false;
        Assert.Equal(LeftoverScheduledWorkChoice.Delete, today.Choice);
    }

    private static LeftoverScheduledWorkService Service(
        RecordingNoteService notes,
        IReadOnlyList<DateTime>? timeOff = null) =>
        new(notes, new SettingsStub(), new ExemptDatesStub(timeOff ?? []));

    private static Note Note(int id, DateTime date, NoteStatus status)
    {
        var note = Models.Note.Rehydrate(id);
        note.Narrative = "Planned work";
        note.EventDate = date;
        note.Status = status;
        note.Minutes = 15;
        note.PersonId = 11;
        note.NoteType = NoteType.Phone;
        var person = Person.Rehydrate(11, 41);
        person.FirstName = "Client";
        note.Person = person;
        return note;
    }

    private sealed class RecordingNoteService(params Note[] seed) : INoteService
    {
        public List<Note> Notes { get; } = [.. seed];
        public List<Note> Updated { get; } = [];
        public List<Note> Deleted { get; } = [];
        public int? RefuseId { get; init; }

        public Task<List<Note>> GetByYearAsync(int userId, int year) =>
            Task.FromResult(Notes.Where(note => note.EventDate?.Year == year).ToList());

        public Task UpdateNoteAsync(Note note)
        {
            if (note.Id == RefuseId) throw new NoteConcurrencyException();
            Updated.Add(note);
            return Task.CompletedTask;
        }

        public Task DeleteNoteAsync(Note note)
        {
            if (note.Id == RefuseId) throw new NoteConcurrencyException();
            Deleted.Add(note);
            return Task.CompletedTask;
        }

        public Task<Note> AddNoteAsync(Note note) => throw new NotSupportedException();
        public Task<List<Note>> GetAllByPersonAsync(int personId) => throw new NotSupportedException();
        public Task UpdateAbandonedNotesAsync(int abandonedAfterDays) => throw new NotSupportedException();
        public Task<List<Note>> GetMonthlyNotesAsync(int userId) => throw new NotSupportedException();
        public Task<List<Note>> GetDayScheduleAsync(int userId, DateTime date) => throw new NotSupportedException();
    }

    private sealed class SettingsStub : ISettingsService
    {
        public Task<Settings> LoadAsync() => Task.FromResult(new Settings());
        public Task SaveAsync(Settings settings) => throw new NotSupportedException();
    }

    private sealed class ExemptDatesStub(IReadOnlyList<DateTime> dates) : IExemptDateService
    {
        public Task<List<ExemptDate>> GetByYearAsync(int userId, int year) =>
            Task.FromResult(dates.Where(date => date.Year == year)
                .Select(date => new ExemptDate { UserId = userId, Date = date })
                .ToList());

        public Task<ExemptDate> AddAsync(int userId, DateTime date, string? reason = null) =>
            throw new NotSupportedException();

        public Task RemoveAsync(int id) => throw new NotSupportedException();
    }
}
