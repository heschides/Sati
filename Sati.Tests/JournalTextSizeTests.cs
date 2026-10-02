using Sati.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class JournalTextSizeTests
{
    [Fact]
    public void TextSizeButtonsZoomTheEditorWithoutChangingJournalContent()
    {
        WpfUiHarness.Run(() =>
        {
            var view = new JournalEditor { DataContext = new { CanEditJournal = true } };
            WpfUiHarness.Realize(view, 700, 450);
            var editor = WpfUiHarness.FindByAutomationName<RichTextBox>(view, "Journal");
            new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text = "Keep this text";
            var before = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text;
            var larger = WpfUiHarness.FindByAutomationName<Button>(view, "Increase journal text size");
            var smaller = WpfUiHarness.FindByAutomationName<Button>(view, "Decrease journal text size");

            larger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(16, editor.FontSize);
            Assert.Equal(16d, new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd)
                .GetPropertyValue(TextElement.FontSizeProperty));
            smaller.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(14, editor.FontSize);
            Assert.Equal(before, new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text);
        });
    }
}
