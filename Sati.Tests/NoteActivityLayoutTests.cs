using System.Windows;
using System.Windows.Controls;
using Sati.Models;
using Sati.Views;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class NoteActivityLayoutTests
{
    [Fact]
    public async Task NarrowEditorShowsFullHeadingAndWorkflowChoicesBeforeActivities()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var editor = fixture.NoteEntry();
        WpfUiHarness.Run(() =>
        {
            var view = new NoteEntryView { DataContext = editor };
            WpfUiHarness.Realize(view, 480, 1100);
            var heading = WpfUiHarness.FindByAutomationName<TextBlock>(view, "Activities section");
            var reminder = WpfUiHarness.FindByAutomationName<CheckBox>(view, "Reminder note");
            var unbilled = WpfUiHarness.FindByAutomationName<CheckBox>(view, "Unbilled note");
            var telehealth = WpfUiHarness.FindByAutomationName<CheckBox>(view, "Telehealth activity");
            Assert.Equal(4, Grid.GetColumnSpan(heading));
            Assert.Equal(TextWrapping.Wrap, heading.TextWrapping);
            Assert.True(Grid.GetRow(reminder) < Grid.GetRow(heading));
            Assert.True(Grid.GetRow(unbilled) < Grid.GetRow(heading));
            telehealth.IsChecked = true;
            Assert.True(editor.IsTelehealthSelected);
            Assert.Equal(NoteType.Phone, editor.SelectedNoteType);
            Assert.False(editor.IsVisitNote);
            reminder.IsChecked = true;
            Assert.True(editor.IsReminderSelected);
            Assert.False(editor.IsTelehealthSelected);
        });
    }
}
