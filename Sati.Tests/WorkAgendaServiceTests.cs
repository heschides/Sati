using Sati.Data;
using Sati.Models;
using Sati.Services;
using Xunit;

namespace Sati.Tests;

public sealed class WorkAgendaServiceTests
{
    private static readonly DateTime Today = new(2026, 9, 5);

    [Fact]
    public async Task ScheduledNotesAreGroupedByTheirActualType()
    {
        var notes = new RecordingNoteService(
            Scheduled(NoteType.Form, "Complete Q1 review", 11, FormType.Q1R),
            Scheduled(NoteType.Visit, "Home visit", 12),
            Scheduled(NoteType.Phone, "Call guardian", 13),
            Scheduled(NoteType.Email, "Email provider", 14),
            Scheduled(NoteType.Other, "Check portal", 15),
            Note.Create("Already started", Today, NoteStatus.Pending, 15, 11, null, NoteType.Phone));

        var result = await new WorkAgendaService(notes).LoadAsync(41, Today);

        Assert.Equal(5, result.Count);
        Assert.Contains(result, item => item.Section == WorkAgendaSection.Paperwork && item.TypeLabel == "Q1 Review");
        Assert.Contains(result, item => item.Section == WorkAgendaSection.Visits);
        Assert.Contains(result, item => item.Section == WorkAgendaSection.Calls);
        Assert.Contains(result, item => item.Section == WorkAgendaSection.Emails);
        Assert.Contains(result, item => item.Section == WorkAgendaSection.Freeform);
    }

    [Fact]
    public async Task LoginSelectionCreatesAnEditableScheduledFormWithoutTouchingFreeText()
    {
        var notes = new RecordingNoteService();
        var item = AgendaItem();

        var result = await new WorkAgendaService(notes).AddFromDailyAgendaAsync(
            41, Today, [item]);

        var saved = Assert.Single(notes.Notes);
        Assert.Equal(1, result.AddedCount);
        Assert.Equal(NoteStatus.Scheduled, saved.Status);
        Assert.Equal(NoteType.Form, saved.NoteType);
        Assert.Equal(FormType.Q1R, saved.FormType);
        Assert.Equal(item.FormId, saved.FormId);
        Assert.Equal(WorkAgendaService.DefaultMinutes, saved.Minutes);
        Assert.Null(saved.StartTime);
        Assert.Equal(Today, saved.EventDate);
        Assert.Equal(DailyAgendaText.FormatItem(item), saved.Narrative);
    }

    [Fact]
    public async Task RetryingTheSameLoginSelectionDoesNotCreateADuplicate()
    {
        var notes = new RecordingNoteService();
        var service = new WorkAgendaService(notes);
        var item = AgendaItem();

        await service.AddFromDailyAgendaAsync(41, Today, [item]);
        var retry = await service.AddFromDailyAgendaAsync(41, Today, [item]);

        Assert.Single(notes.Notes);
        Assert.Equal(0, retry.AddedCount);
        Assert.Equal(1, retry.ExistingCount);
    }

    [Fact]
    public async Task LegacyUnlinkedScheduledNotePreventsAnUpgradeDuplicate()
    {
        var item = AgendaItem();
        var legacy = Scheduled(
            NoteType.Form,
            DailyAgendaText.FormatItem(item),
            item.PersonId,
            item.FormType);
        var notes = new RecordingNoteService(legacy);

        var result = await new WorkAgendaService(notes).AddFromDailyAgendaAsync(
            41, Today, [item]);

        Assert.Single(notes.Notes);
        Assert.Null(legacy.FormId);
        Assert.Equal(0, result.AddedCount);
        Assert.Equal(1, result.ExistingCount);
    }

    [Fact]
    public async Task ExactFormIdentitySurvivesAnAgendaNarrativeChange()
    {
        var item = AgendaItem();
        var linked = Scheduled(
            NoteType.Form,
            "This display text came from an earlier agenda state",
            item.PersonId,
            item.FormType,
            item.FormId);
        var notes = new RecordingNoteService(linked);

        var result = await new WorkAgendaService(notes).AddFromDailyAgendaAsync(
            41, Today, [item]);

        Assert.Single(notes.Notes);
        Assert.Equal(0, result.AddedCount);
        Assert.Equal(1, result.ExistingCount);
    }

    [Fact]
    public async Task ASeparateExactFormRemainsSeparateEvenWhenItsTextMatches()
    {
        var item = AgendaItem();
        var otherForm = Scheduled(
            NoteType.Form,
            DailyAgendaText.FormatItem(item),
            item.PersonId,
            item.FormType,
            formId: item.FormId!.Value + 1);
        var notes = new RecordingNoteService(otherForm);

        var result = await new WorkAgendaService(notes).AddFromDailyAgendaAsync(
            41, Today, [item]);

        Assert.Equal(2, notes.Notes.Count);
        Assert.Equal(1, result.AddedCount);
        Assert.Equal(0, result.ExistingCount);
    }

    [Fact]
    public async Task LegacyUnlinkedNoteWithDifferentTextDoesNotClaimTheExactForm()
    {
        var item = AgendaItem();
        var unrelatedLegacy = Scheduled(
            NoteType.Form,
            "Different planned work for the same form category",
            item.PersonId,
            item.FormType);
        var notes = new RecordingNoteService(unrelatedLegacy);

        var result = await new WorkAgendaService(notes).AddFromDailyAgendaAsync(
            41, Today, [item]);

        Assert.Equal(2, notes.Notes.Count);
        Assert.Equal(1, result.AddedCount);
        Assert.Equal(0, result.ExistingCount);
        Assert.Equal(item.FormId, notes.Notes[^1].FormId);
    }

    [Fact]
    public async Task ReleaseLinkedLegacyNoteDoesNotClaimAnExactFormWithTheSameText()
    {
        var item = AgendaItem();
        var releaseNote = Scheduled(
            NoteType.Form,
            DailyAgendaText.FormatItem(item),
            item.PersonId,
            item.FormType);
        releaseNote.ReleaseObligationId = 91;
        var notes = new RecordingNoteService(releaseNote);

        var result = await new WorkAgendaService(notes).AddFromDailyAgendaAsync(
            41, Today, [item]);

        Assert.Equal(2, notes.Notes.Count);
        Assert.Equal(1, result.AddedCount);
        Assert.Equal(0, result.ExistingCount);
        Assert.Equal(item.FormId, notes.Notes[^1].FormId);
    }

    [Fact]
    public async Task MissingClientOrFormTypeIsRejectedBeforeAnyItemIsWritten()
    {
        var notes = new RecordingNoteService();
        var valid = AgendaItem();
        var invalid = valid with { PersonId = 0, FormType = null };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WorkAgendaService(notes).AddFromDailyAgendaAsync(
                41, Today, [valid, invalid]));

        Assert.Empty(notes.Notes);
    }

    [Fact]
    public async Task MixedFormAndReleaseIdentityIsRejectedBeforeAnyItemIsWritten()
    {
        var notes = new RecordingNoteService();
        var invalid = AgendaItem() with { ReleaseObligationId = Guid.NewGuid() };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WorkAgendaService(notes).AddFromDailyAgendaAsync(
                41, Today, [invalid]));

        Assert.Contains("both a form and a release obligation", error.Message);
        Assert.Empty(notes.Notes);
    }

    private static DailyAgendaItem AgendaItem() => new(
        "form:11:7",
        11,
        "Alex Person",
        "Q1 Review",
        Today.AddDays(-3),
        DailyAgendaItemKind.OverdueForm,
        true,
        FormType.Q1R,
        FormId: 7,
        TargetEffectiveDate: new DateTime(2026, 5, 30));

    private static Note Scheduled(
        NoteType type,
        string narrative,
        int personId,
        FormType? formType = null,
        int? formId = null)
    {
        var note = Note.Create(
            narrative, Today, NoteStatus.Scheduled, 15, personId, formType, type, formId);
        var person = Person.Rehydrate(personId, 41);
        person.FirstName = $"Client {personId}";
        note.Person = person;
        return note;
    }

    private sealed class RecordingNoteService(params Note[] seed) : INoteService
    {
        public List<Note> Notes { get; } = [.. seed];

        public Task<Note> AddNoteAsync(Note note)
        {
            Notes.Add(note);
            return Task.FromResult(note);
        }

        public Task<List<Note>> GetDayScheduleAsync(int userId, DateTime date) =>
            Task.FromResult(Notes.Where(note => note.EventDate?.Date == date.Date).ToList());

        public Task DeleteNoteAsync(Note note) => throw new NotSupportedException();
        public Task UpdateNoteAsync(Note note) => throw new NotSupportedException();
        public Task<List<Note>> GetAllByPersonAsync(int personId) => throw new NotSupportedException();
        public Task UpdateAbandonedNotesAsync(int abandonedAfterDays) => throw new NotSupportedException();
        public Task<List<Note>> GetMonthlyNotesAsync(int userId) => throw new NotSupportedException();
        public Task<List<Note>> GetByYearAsync(int userId, int year) => throw new NotSupportedException();
    }
}
