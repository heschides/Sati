using Sati.Data;
using Sati.Models;
using Sati.ViewModels;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

public sealed class ClientNotesWorkspaceTests
{
    private static Note PendingNote(int personId, string narrative) =>
        Note.Create(narrative, DateTime.Today.AddDays(-1), NoteStatus.Pending, 15,
            personId, null, NoteType.Contact);

    [Fact]
    public async Task SelectingAConsumerScopesTheRowsAndEditorToThatConsumer()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var notes = fixture.NotesFromAnotherSession();
        var first = await notes.AddNoteAsync(PendingNote(fixture.PersonOneId, "First consumer note"));
        var second = await notes.AddNoteAsync(PendingNote(fixture.PersonTwoId, "Second consumer note"));
        var editor = fixture.NoteEntry();
        var page = fixture.ClientsPage(clientNoteEntry: editor);
        var personOne = await fixture.PersonOneAsync();
        var personTwo = await fixture.PersonTwoAsync();
        page.ReplacePeople([personOne, personTwo]);

        page.SelectedPerson = personOne;
        await page.RefreshSelectedNotesIfSelectedAsync();

        var firstRow = Assert.Single(page.SelectedPersonNotes);
        Assert.Equal(first.Id, firstRow.Id);
        Assert.Equal(personOne.Id, editor.SelectedPerson?.Id);
        Assert.Equal(personOne.Id, Assert.Single(editor.People).Id);
        page.SelectedClientNote = firstRow;
        Assert.True(editor.IsShowing(firstRow));
        Assert.True(editor.IsLocked);

        page.SelectedPerson = personTwo;
        await page.RefreshSelectedNotesIfSelectedAsync();

        Assert.Null(page.SelectedClientNote);
        Assert.Equal(second.Id, Assert.Single(page.SelectedPersonNotes).Id);
        Assert.Equal(personTwo.Id, editor.SelectedPerson?.Id);
        Assert.Equal(personTwo.Id, Assert.Single(editor.People).Id);
        Assert.False(editor.IsShowing(firstRow));

        // A stale grid row must not load a note from the previous consumer.
        page.SelectedClientNote = firstRow;
        Assert.Null(page.SelectedClientNote);
        Assert.False(editor.IsShowing(firstRow));
        Assert.False(page.EditSelectedClientNoteCommand.CanExecute(null));
    }

    [Fact]
    public async Task DecliningToDiscardADraftKeepsTheConsumerAndGridSelection()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var notes = fixture.NotesFromAnotherSession();
        await notes.AddNoteAsync(PendingNote(fixture.PersonOneId, "Saved note"));
        var editor = fixture.NoteEntry(discardAnswer: false);
        var page = fixture.ClientsPage(clientNoteEntry: editor);
        var personOne = await fixture.PersonOneAsync();
        var personTwo = await fixture.PersonTwoAsync();
        page.ReplacePeople([personOne, personTwo]);
        page.SelectedPerson = personOne;
        await page.RefreshSelectedNotesIfSelectedAsync();
        editor.Narrative = "An unfinished visit narrative";

        page.SelectedClientNote = Assert.Single(page.SelectedPersonNotes);
        Assert.Null(page.SelectedClientNote);
        Assert.Equal("An unfinished visit narrative", editor.Narrative);

        page.SelectedPerson = personTwo;
        Assert.Equal(personOne.Id, page.SelectedPerson?.Id);
        Assert.Equal(personOne.Id, editor.SelectedPerson?.Id);
        Assert.Equal("An unfinished visit narrative", editor.Narrative);
        Assert.True(editor.HasUnsavedChanges);
    }

    [Fact]
    public async Task SavingAndEditingAClientNoteRefreshesItsRowsWithoutCreatingADuplicate()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var editor = fixture.NoteEntry();
        var page = fixture.ClientsPage(clientNoteEntry: editor);
        var person = await fixture.PersonOneAsync();
        page.ReplacePeople([person, await fixture.PersonTwoAsync()]);
        page.SelectedPerson = person;
        await page.RefreshSelectedNotesIfSelectedAsync();

        editor.SelectedNoteType = NoteType.Contact;
        editor.Status = NoteStatus.Pending;
        editor.EventDate = DateTime.Today;
        editor.Minutes = 15;
        editor.Narrative = "Initial note for this consumer.";
        await SaveAndWaitForRefreshAsync(page, editor);

        var savedRow = Assert.Single(page.SelectedPersonNotes);
        Assert.Equal(person.Id, savedRow.PersonId);
        Assert.Equal("Initial note for this consumer.", savedRow.Narrative);
        Assert.Null(page.SelectedClientNote);
        Assert.Equal(person.Id, editor.SelectedPerson?.Id);

        page.SelectedClientNote = savedRow;
        Assert.True(editor.IsLocked);
        page.EditSelectedClientNoteCommand.Execute(null);
        Assert.False(editor.IsLocked);
        editor.Narrative = "Corrected note for this consumer.";
        await SaveAndWaitForRefreshAsync(page, editor);

        var updatedRow = Assert.Single(page.SelectedPersonNotes);
        Assert.Equal(savedRow.Id, updatedRow.Id);
        Assert.Equal("Corrected note for this consumer.", updatedRow.Narrative);
        Assert.Single(await fixture.NotesFromAnotherSession().GetAllByPersonAsync(person.Id));
        Assert.Empty(await fixture.NotesFromAnotherSession()
            .GetAllByPersonAsync(fixture.PersonTwoId));
    }

    [Fact]
    public async Task AccountSwitchClearsTheDraftAndRejectsAnOldDelayedNotesRead()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var realNotes = fixture.NotesFromAnotherSession();
        await realNotes.AddNoteAsync(PendingNote(fixture.PersonOneId, "Old account note"));
        var delayedNotes = new DelayedSnapshotNoteService(realNotes);
        var editor = fixture.NoteEntry(notes: realNotes, session: session, discardAnswer: false);
        var page = fixture.ClientsPage(
            clientNoteEntry: editor, notes: delayedNotes, session: session);
        var person = await fixture.PersonOneAsync();
        page.ReplacePeople([person]);
        page.SelectedPerson = person;
        await page.RefreshSelectedNotesIfSelectedAsync();
        Assert.Single(page.SelectedPersonNotes);
        editor.Narrative = "Private unfinished draft";

        delayedNotes.BlockNextRead();
        var oldRead = page.RefreshSelectedNotesIfSelectedAsync();
        await delayedNotes.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        page.ClearForAccountSwitch();
        session.SetUser(fixture.CaseManagerTwo);
        delayedNotes.ReleaseRead();
        await oldRead;

        Assert.Null(page.SelectedPerson);
        Assert.Empty(page.People);
        Assert.Empty(page.SelectedPersonNotes);
        Assert.Null(page.SelectedClientNote);
        Assert.Empty(editor.People);
        Assert.Null(editor.SelectedPerson);
        Assert.True(string.IsNullOrEmpty(editor.Narrative));
        Assert.False(editor.HasUnsavedChanges);
    }

    private static async Task SaveAndWaitForRefreshAsync(
        NewClientViewModel page, NoteEntryViewModel editor)
    {
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnSaved(object? _, EventArgs __) => refreshed.TrySetResult();
        page.ClientNoteSaved += OnSaved;
        try
        {
            await editor.SubmitNoteCommand.ExecuteAsync(null);
            await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            page.ClientNoteSaved -= OnSaved;
        }
    }

    private sealed class DelayedSnapshotNoteService(INoteService inner) : INoteService
    {
        private int _blockNext;
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void BlockNextRead() => Interlocked.Exchange(ref _blockNext, 1);
        public void ReleaseRead() => _release.TrySetResult();

        public async Task<List<Note>> GetAllByPersonAsync(int personId)
        {
            var snapshot = await inner.GetAllByPersonAsync(personId);
            if (Interlocked.Exchange(ref _blockNext, 0) != 1)
                return snapshot;

            ReadStarted.TrySetResult();
            await _release.Task;
            return snapshot;
        }

        public Task<Note> AddNoteAsync(Note note) => inner.AddNoteAsync(note);
        public Task DeleteNoteAsync(Note note) => inner.DeleteNoteAsync(note);
        public Task UpdateNoteAsync(Note note) => inner.UpdateNoteAsync(note);
        public Task UpdateAbandonedNotesAsync(int abandonedAfterDays) =>
            inner.UpdateAbandonedNotesAsync(abandonedAfterDays);
        public Task<List<Note>> GetMonthlyNotesAsync(int userId) => inner.GetMonthlyNotesAsync(userId);
        public Task<List<Note>> GetByYearAsync(int userId, int year) => inner.GetByYearAsync(userId, year);
        public Task<List<Note>> GetDayScheduleAsync(int userId, DateTime date) =>
            inner.GetDayScheduleAsync(userId, date);
    }
}
