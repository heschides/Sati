using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Children;
using Sati.Views;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class ClientNotesViewRenderTests
{
    [Fact]
    public async Task ConsumerNotesTabRendersItsEditorAndKeyboardAccessibleList()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var editor = fixture.NoteEntry();
        var person = await fixture.PersonOneAsync();
        editor.SetPeople([person]);
        editor.SelectedPerson = person;

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<ISessionService, SessionService>();
                services.AddSingleton<IComprehensiveAssessmentService,
                    StabilizationTests.SmokeAssessmentService>();
                services.AddSingleton<IPersonCenteredPlanSourceService,
                    StabilizationTests.SmokePlanSourceService>();
                services.AddSingleton<IConsumerProviderService,
                    StabilizationTests.SmokeConsumerProviderService>();
                services.AddSingleton<IProviderService,
                    StabilizationTests.SmokeProviderService>();
            })
            .Build();

        WpfUiHarness.RunWithHost(host, () =>
        {
            var model = new NotesHost(editor);
            var narrative = "Visit summary.\nFull visit documentation remains available in the editor.";
            var note = Note.Create(narrative, DateTime.Today, NoteStatus.Pending,
                15, person.Id, null, NoteType.Visit);
            model.SelectedPersonNotes.Add(note);
            var view = new ClientsView { DataContext = model };
            WpfUiHarness.RealizePendingContent(view, 1500, 1000);

            var tabs = WpfUiHarness.FindByAutomationName<TabControl>(
                view, "Consumer record sections");
            Assert.Equal("Notes", Assert.IsType<TabItem>(tabs.SelectedItem).Header);

            var editorView = Assert.Single(
                WpfUiHarness.Descendants(view).OfType<NoteEntryView>());
            Assert.Same(editor, editorView.DataContext);

            var grid = WpfUiHarness.FindByAutomationName<DataGrid>(
                view, "Notes for selected consumer");
            var edit = WpfUiHarness.FindByAutomationName<Button>(
                view, "Edit selected consumer note");
            Assert.Equal(Visibility.Visible, grid.Visibility);
            Assert.Equal(Visibility.Visible, edit.Visibility);
            Assert.True(grid.ActualWidth > 0);
            Assert.True(edit.ActualWidth > 0);
            Assert.Equal(model.EditSelectedClientNoteCommand, edit.Command);
            Assert.Contains(grid.InputBindings.OfType<KeyBinding>(),
                binding => binding.Key == Key.Enter &&
                           binding.Command == model.EditSelectedClientNoteCommand);
            Assert.Equal("Consumer notes", AutomationProperties.GetName(
                Assert.IsType<TabItem>(tabs.SelectedItem)));

            var row = Assert.Single(WpfUiHarness.Descendants(grid)
                .OfType<DataGridRow>(),
                candidate => ReferenceEquals(candidate.Item, note));
            var narrativeColumn = Assert.Single(grid.Columns,
                column => Equals(column.Header, "Narrative"));
            var narrativeCell = Assert.Single(WpfUiHarness.Descendants(row)
                .OfType<DataGridCell>(),
                cell => ReferenceEquals(cell.Column, narrativeColumn));
            var preview = Assert.Single(WpfUiHarness.Descendants(narrativeCell).OfType<TextBlock>());
            Assert.Equal(32, row.ActualHeight, precision: 1);
            Assert.Equal(TextWrapping.NoWrap, preview.TextWrapping);
            Assert.Equal(TextTrimming.CharacterEllipsis, preview.TextTrimming);
            Assert.Equal(
                "Visit summary. Full visit documentation remains available in the editor.",
                preview.Text);
            Assert.Equal(narrative, preview.ToolTip);
            Assert.Equal(narrative, AutomationProperties.GetHelpText(preview));

            // The consumer roster and section rail consume most of a narrow window.
            // Both the editor and list must remain reachable instead of clipping.
            WpfUiHarness.RealizePendingContent(view, 900, 1000);
            var workspace = WpfUiHarness.FindByAutomationName<ScrollViewer>(
                view, "Consumer notes workspace");
            Assert.Equal(Visibility.Visible,
                workspace.ComputedHorizontalScrollBarVisibility);
            Assert.True(workspace.ScrollableWidth > 0);
            workspace.ScrollToRightEnd();
            WpfUiHarness.RealizePendingContent(view, 900, 1000);
            Assert.True(workspace.HorizontalOffset > 0);
        });
    }

    private sealed class NotesHost(NoteEntryViewModel editor)
    {
        public bool ShowClientWorkspace => true;
        public int ClientWorkspaceTabIndex { get; set; } = 1;
        public NoteEntryViewModel ClientNoteEntry { get; } = editor;
        public ObservableCollection<Note> SelectedPersonNotes { get; } = [];
        public Note? SelectedClientNote { get; set; }
        public ICommand EditSelectedClientNoteCommand { get; } = new RelayCommand(() => { });
        public string? ClientNoteLoadError => null;
        public bool HasClientNoteLoadError => false;
    }
}
