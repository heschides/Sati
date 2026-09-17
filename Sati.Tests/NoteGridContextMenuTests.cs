using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Sati.Models;
using Sati.Views;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The note grids' context menus act on the selected note. A stock data grid selects the
/// row under the pointer as its menu opens, but still opens the menu over the column header
/// and when the view model keeps the old selection for an unsaved draft; Mark Note Logged
/// and Delete Note then act on a note the case manager did not point at.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class NoteGridContextMenuTests
{
    [Fact]
    public async Task TheNotesLogMenuOpensOnlyOverARow()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var log = await LogWithTwoNotesAsync(fixture);

        WpfUiHarness.Run(() =>
        {
            var view = new NotesLogView { DataContext = log };
            WpfUiHarness.Realize(view, 1400, 900);
            var grid = WpfUiHarness.FindByAutomationName<DataGrid>(view, "Notes");
            var (first, second) = Rows(log);

            log.SelectedNote = first;
            Assert.False(OpenMenuOver(CellFor(grid, second)));
            Assert.Same(second, log.SelectedNote);

            // Over the column header there is no note to act on.
            var header = WpfUiHarness.Descendants(grid)
                .OfType<System.Windows.Controls.Primitives.DataGridColumnHeader>().First();
            Assert.True(OpenMenuOver(header));
            Assert.Same(second, log.SelectedNote);
        });
    }

    [Fact]
    public async Task KeepingAnUnsavedEditKeepsTheMenuClosed()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var log = await LogWithTwoNotesAsync(fixture, discardAnswer: false);

        WpfUiHarness.Run(() =>
        {
            var view = new NotesLogView { DataContext = log };
            WpfUiHarness.Realize(view, 1400, 900);
            var grid = WpfUiHarness.FindByAutomationName<DataGrid>(view, "Notes");
            var (first, second) = Rows(log);
            log.SelectedNote = first;
            log.OpenSelectedNoteForEdit();
            log.NoteEntry.Narrative = "A correction the case manager has not saved.";

            Assert.True(OpenMenuOver(CellFor(grid, second)));

            Assert.Same(first, log.SelectedNote);
            Assert.Same(first, grid.SelectedItem);
            Assert.Equal("A correction the case manager has not saved.", log.NoteEntry.Narrative);
        });
    }

    [Fact]
    public async Task TheDashboardDeleteMenuOpensOnlyOverARow()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var log = await LogWithTwoNotesAsync(fixture);
        var (first, second) = Rows(log);
        var host = new DashboardNotesHost([first, second]) { SelectedNote = first };

        WpfUiHarness.Run(() =>
        {
            var view = new NotesPanelView { DataContext = host };
            WpfUiHarness.Realize(view, 600, 600);
            var grid = WpfUiHarness.FindByAutomationName<DataGrid>(view, "Notes");

            // Delete Note would remove the selected first note.
            var header = WpfUiHarness.Descendants(grid)
                .OfType<System.Windows.Controls.Primitives.DataGridColumnHeader>().First();
            Assert.True(OpenMenuOver(header));
            Assert.Same(first, host.SelectedNote);

            Assert.False(OpenMenuOver(CellFor(grid, second)));
            Assert.Same(second, host.SelectedNote);
        });
    }

    private static async Task<ViewModels.NotesWindowViewModel> LogWithTwoNotesAsync(
        NoteEntryFixture fixture, bool? discardAnswer = null)
    {
        var notes = fixture.NotesFromAnotherSession();
        foreach (var (narrative, day) in new[] { ("First note.", 10), ("Second note.", 11) })
        {
            await notes.AddNoteAsync(Note.Create(
                narrative, new DateTime(2026, 8, day), NoteStatus.Pending, 15,
                fixture.PersonOneId, noteType: NoteType.Contact));
        }

        var log = fixture.NotesWindow(discardAnswer: discardAnswer);
        await log.NoteEntry.InitializeAsync();
        await log.ReloadAsync();
        return log;
    }

    private static (Note First, Note Second) Rows(ViewModels.NotesWindowViewModel log)
    {
        var rows = log.NotesView.Cast<Note>().OrderBy(note => note.EventDate).ToArray();
        return (rows[0], rows[1]);
    }

    private static DataGridCell CellFor(DataGrid grid, Note note) =>
        WpfUiHarness.Descendants(grid).OfType<DataGridCell>()
            .First(cell => ReferenceEquals(cell.DataContext, note));

    /// <summary>Raises the event WPF raises before showing a context menu; true when it was refused.</summary>
    private static bool OpenMenuOver(UIElement element)
    {
        var args = (ContextMenuEventArgs)Activator.CreateInstance(
            typeof(ContextMenuEventArgs),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [element, true],
            culture: null)!;
        args.RoutedEvent = FrameworkElement.ContextMenuOpeningEvent;
        args.Source = element;
        element.RaiseEvent(args);
        return args.Handled;
    }

    /// <summary>Only what the dashboard notes grid binds to.</summary>
    public sealed class DashboardNotesHost(IReadOnlyList<Note> notes)
    {
        public IReadOnlyList<Note> NotesView { get; } = notes;
        public Note? SelectedNote { get; set; }
        public string SearchText { get; set; } = string.Empty;
        public IReadOnlyList<string> NoteStatusOptions { get; } = [];
        public string? FilterStatus { get; set; }
    }
}
