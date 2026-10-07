using Sati.Contracts.V1;
using Sati.Models;
using Sati.Services;
using Sati.ViewModels.Children;
using Sati.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class ScratchpadFormattingTests
{
    [Theory]
    [InlineData("", null)]
    [InlineData("2.5", "2.5")]
    [InlineData("3", "3")]
    public void NumberPromptDoesNotChangeTheDocumentBeforeConfirmation(string entered, string? expected)
    {
        WpfUiHarness.Run(() =>
        {
            var editor = new ScratchpadEditor { StoredContent = "Keep this task" };
            editor.SelectAll();
            var prompt = new ScratchpadCheckboxNumberPrompt(null, false,
                number => editor.InsertOrToggleCheckbox(number, toggleAdjacent: false), () => { });
            WpfUiHarness.Realize(prompt, 285, 200);
            var input = WpfUiHarness.FindByAutomationName<TextBox>(prompt, "Optional scratchpad checkbox number");
            input.Text = entered;
            Assert.Equal("Keep this task", editor.StoredContent);
            WpfUiHarness.FindByAutomationName<Button>(prompt, "Confirm scratchpad checkbox number")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var checkbox = Assert.Single(JournalDocument.Parse(editor.StoredContent).Pages[0].Paragraphs[0].Inlines,
                inline => inline.IsCheckbox);
            Assert.Equal(expected is null ? (decimal?)null : decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), checkbox.Number);
            Assert.EndsWith("Keep this task", JournalDocument.Parse(editor.StoredContent).ToPlainText());
        });
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("1.234")]
    [InlineData("not a number")]
    [InlineData("1000001")]
    public void InvalidNumberCannotCreateACheckboxAndCancelKeepsTheDraft(string entered)
    {
        WpfUiHarness.Run(() =>
        {
            var accepted = false;
            var cancelled = false;
            var prompt = new ScratchpadCheckboxNumberPrompt(null, false, _ => accepted = true, () => cancelled = true);
            WpfUiHarness.Realize(prompt, 285, 200);
            WpfUiHarness.FindByAutomationName<TextBox>(prompt, "Optional scratchpad checkbox number").Text = entered;
            WpfUiHarness.FindByAutomationName<Button>(prompt, "Confirm scratchpad checkbox number")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(accepted);
            WpfUiHarness.FindByAutomationName<Button>(prompt, "Cancel scratchpad checkbox number")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(cancelled);
            Assert.False(accepted);
        });
    }

    [Fact]
    public void RightClickMenuTargetsTheSelectionAndSuppliesTheFourColors()
    {
        WpfUiHarness.Run(() =>
        {
            var editor = new ScratchpadEditor { StoredContent = "Start selected end" };
            var run = Assert.IsType<Run>(Assert.IsType<Paragraph>(editor.Document.Blocks.FirstBlock).Inlines.FirstInline);
            editor.Selection.Select(run.ContentStart.GetPositionAtOffset(6)!, run.ContentStart.GetPositionAtOffset(14)!);
            var menu = editor.ContextMenu;
            var bold = Assert.Single(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "Bold"));
            Assert.Same(editor, bold.CommandTarget);
            Assert.IsType<RoutedUICommand>(bold.Command).Execute(null, editor);
            var highlight = Assert.Single(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "Highlight"));
            Assert.Equal(["Clear highlight", "Yellow", "Green", "Blue", "Pink"], highlight.Items.OfType<MenuItem>().Select(item => item.Header));
            highlight.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Blue"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var inlines = Assert.Single(JournalFlowDocument.Read(editor.Document)).Inlines;
            Assert.Equal("Start ", inlines[0].Text);
            Assert.False(inlines[0].Bold);
            Assert.Equal("selected", inlines[1].Text);
            Assert.True(inlines[1].Bold);
            Assert.Equal(TextHighlight.Blue, inlines[1].Highlight);
            Assert.Equal(" end", inlines[2].Text);
            Assert.Equal(TextHighlight.None, inlines[2].Highlight);
        });
    }

    [Fact]
    public void TimestampInsertionAndAdjacentCheckboxToggleRetainSurroundingText()
    {
        WpfUiHarness.Run(() =>
        {
            var editor = new ScratchpadEditor { StoredContent = "Before after" };
            var run = Assert.IsType<Run>(Assert.IsType<Paragraph>(editor.Document.Blocks.FirstBlock).Inlines.FirstInline);
            editor.CaretPosition = run.ContentStart.GetPositionAtOffset(7)!;
            editor.InsertOrToggleCheckbox(2);
            editor.InsertOrToggleCheckbox();
            var text = JournalDocument.Parse(editor.StoredContent).ToPlainText();
            Assert.Equal("Before [x] after", text);
            editor.InsertTimestamp(new DateTime(2026, 10, 7, 8, 30, 0));
            text = JournalDocument.Parse(editor.StoredContent).ToPlainText();
            Assert.Contains("8:30 AM", text);
            Assert.StartsWith("Before [x] ", text);
            Assert.EndsWith("after", text);
        });
    }
    [Fact]
    public void NumberedCheckboxProgressUsesCheckedValuesAndIgnoresPlainBoxes()
    {
        WpfUiHarness.Run(() =>
        {
            var stored = new JournalDocument([new JournalPage("Scratchpad", [new JournalParagraph([
                JournalInline.Checkbox(true, 2), JournalInline.Run(" First "),
                JournalInline.Checkbox(false, 3), JournalInline.Run(" Second "),
                JournalInline.Checkbox(false, 5), JournalInline.Run(" Third "),
                JournalInline.Checkbox(true), JournalInline.Run(" Plain")])])]).Serialize();
            var model = new ScratchpadViewModel(null!, null!) { ScratchpadContent = stored };
            var view = new ScratchpadView { DataContext = model };
            WpfUiHarness.Realize(view, 700, 550);
            var editor = WpfUiHarness.FindByAutomationName<ScratchpadEditor>(view, "Today's Work freeform scratchpad");
            var summary = WpfUiHarness.FindByAutomationName<TextBlock>(view, "Today's Work numbered checklist progress");
            Assert.True(editor.HasNumberedCheckboxes);
            Assert.Equal("Checklist  2 / 10", summary.Text);
            var boxes = editor.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines)
                .OfType<InlineUIContainer>().Select(c => Assert.IsType<CheckBox>(c.Child)).ToArray();
            boxes[1].IsChecked = true;
            Assert.Equal("Checklist  5 / 10", summary.Text);
            boxes[3].IsChecked = false;
            Assert.Equal("Checklist  5 / 10", summary.Text);
            editor.SetCheckboxNumber(boxes[0], null);
            Assert.Equal("Checklist  3 / 8", summary.Text);
            editor.SetCheckboxNumber(boxes[1], null);
            editor.SetCheckboxNumber(boxes[2], null);
            Assert.False(editor.HasNumberedCheckboxes);
            Assert.Equal(Visibility.Collapsed, ((Border)summary.Parent).Visibility);
            Assert.Equal(string.Empty, editor.ChecklistProgress);
            var reload = new ScratchpadEditor { StoredContent = model.ScratchpadContent };
            Assert.False(reload.HasNumberedCheckboxes);
        });
    }

    [Fact]
    public void OptionalNumbersSurviveReloadAndCheckboxAtCaretDoesNotDeleteWords()
    {
        WpfUiHarness.Run(() =>
        {
            var editor = new ScratchpadEditor { StoredContent = "First second" };
            var paragraph = Assert.IsType<Paragraph>(editor.Document.Blocks.FirstBlock);
            var run = Assert.IsType<Run>(paragraph.Inlines.FirstInline);
            editor.CaretPosition = run.ContentStart.GetPositionAtOffset(6)!;
            editor.InsertOrToggleCheckbox(2.5m);
            Assert.Equal("First [ ] second", JournalDocument.Parse(editor.StoredContent).ToPlainText());
            Assert.Equal("Checklist  0 / 2.5", editor.ChecklistProgress);
            var reloaded = new ScratchpadEditor { StoredContent = editor.StoredContent };
            var box = Assert.IsType<CheckBox>(Assert.Single(reloaded.Document.Blocks.OfType<Paragraph>())
                .Inlines.OfType<InlineUIContainer>().Single().Child);
            Assert.Equal("2.5", box.Content);
            box.IsChecked = true;
            Assert.Equal("Checklist  2.5 / 2.5", reloaded.ChecklistProgress);
        });
    }

    [Fact]
    public void OldPlainCheckboxesAndInvalidStoredNumbersDoNotShowZeroOverZero()
    {
        var document = JournalDocument.Parse("{\"sati-journal\":1,\"pages\":[{\"name\":\"Scratchpad\",\"p\":[[{\"c\":2},{\"c\":1,\"n\":0},{\"c\":2,\"n\":-3},{\"c\":1,\"n\":1.234},{\"c\":1,\"n\":1000001}]]}]}");
        Assert.Equal(0, document.ChecklistTotals().NumberedCount);
        WpfUiHarness.Run(() =>
        {
            var editor = new ScratchpadEditor { StoredContent = document.Serialize() };
            Assert.False(editor.HasNumberedCheckboxes);
            Assert.Equal(string.Empty, editor.ChecklistProgress);
        });
    }

    [Theory]
    [InlineData(TextHighlight.Yellow)]
    [InlineData(TextHighlight.Green)]
    [InlineData(TextHighlight.Blue)]
    [InlineData(TextHighlight.Pink)]
    public void SelectedMarksAndClickedCheckboxSurviveSavingAndReloading(TextHighlight color)
    {
        WpfUiHarness.Run(() =>
        {
            var model = new ScratchpadViewModel(null!, null!) { ScratchpadContent = "Call the office" };
            var view = new ScratchpadView { DataContext = model };
            WpfUiHarness.Realize(view, 700, 550);
            var editor = WpfUiHarness.FindByAutomationName<ScratchpadEditor>(view, "Today's Work freeform scratchpad");
            editor.SelectAll();
            EditingCommands.ToggleBold.Execute(null, editor);
            EditingCommands.ToggleItalic.Execute(null, editor);
            editor.ToggleDecoration(TextDecorationLocation.Underline);
            editor.ToggleDecoration(TextDecorationLocation.Strikethrough);
            editor.ApplyHighlight(color);
            editor.InsertOrToggleCheckbox();
            var check = Assert.IsType<CheckBox>(Assert.Single(editor.Document.Blocks.OfType<Paragraph>())
                .Inlines.OfType<InlineUIContainer>().Single().Child);
            check.IsChecked = true;

            var stored = model.ScratchpadContent;
            var paragraph = Assert.Single(Assert.Single(JournalDocument.Parse(stored).Pages).Paragraphs);
            Assert.True(paragraph.Inlines[0].IsChecked);
            var text = Assert.Single(paragraph.Inlines, inline => !inline.IsCheckbox);
            Assert.Equal("Call the office", text.Text);
            Assert.True(text.Bold && text.Italic && text.Underline && text.Strikethrough);
            Assert.Equal(color, text.Highlight);
            var reloaded = new ScratchpadEditor { StoredContent = stored };
            Assert.Equal(paragraph, Assert.Single(JournalFlowDocument.Read(reloaded.Document)));
            Assert.Equal("[x] Call the office", new Scratchpad { Content = stored }.DisplayContent);
            Assert.DoesNotContain("sati-journal", new Scratchpad { Content = stored }.ContentPreview);
        });
    }

    [Fact]
    public void RemovingUnderlinePreservesStrikethroughAndClearHighlightPreservesEmphasis()
    {
        WpfUiHarness.Run(() =>
        {
            var editor = new ScratchpadEditor { StoredContent = "Keep words" };
            editor.SelectAll();
            editor.ToggleDecoration(TextDecorationLocation.Strikethrough);
            editor.ToggleDecoration(TextDecorationLocation.Underline);
            editor.ToggleDecoration(TextDecorationLocation.Underline);
            editor.ApplyHighlight(TextHighlight.Yellow);
            editor.ApplyHighlight(TextHighlight.None);
            var text = Assert.Single(Assert.Single(JournalFlowDocument.Read(editor.Document)).Inlines);
            Assert.False(text.Underline);
            Assert.True(text.Strikethrough);
            Assert.Equal(TextHighlight.None, text.Highlight);
        });
    }

    [Fact]
    public void UnderliningAMixedSelectionPreservesEachRunsStrikethrough()
    {
        WpfUiHarness.Run(() =>
        {
            var stored = new JournalDocument([new JournalPage("Scratchpad", [new JournalParagraph([
                JournalInline.Run("Done ", strikethrough: true), JournalInline.Run("Next")])])]).Serialize();
            var editor = new ScratchpadEditor { StoredContent = stored };
            editor.SelectAll();
            editor.ToggleDecoration(TextDecorationLocation.Underline);
            var runs = Assert.Single(JournalFlowDocument.Read(editor.Document)).Inlines;
            Assert.All(runs, run => Assert.True(run.Underline));
            Assert.True(runs[0].Strikethrough);
            Assert.False(runs[1].Strikethrough);
        });
    }

    [Theory]
    [InlineData(360)]
    [InlineData(760)]
    public void FormattedChecklistCornerFitsBothAgendaLayouts(double width)
    {
        WpfUiHarness.Run(() =>
        {
            var content = new JournalDocument([new JournalPage("Scratchpad", [
                new JournalParagraph([JournalInline.Checkbox(true, 2), JournalInline.Run("First task", bold: true)]),
                new JournalParagraph([JournalInline.Checkbox(false, 3), JournalInline.Run("Second task", italic: true, highlight: TextHighlight.Yellow)]),
                new JournalParagraph([JournalInline.Checkbox(false, 5), JournalInline.Run("Third task", underline: true)]),
                new JournalParagraph([JournalInline.Checkbox(true), JournalInline.Run("Plain checkbox", strikethrough: true)])])]).Serialize();
            var model = new ScratchpadViewModel(null!, null!) { ScratchpadContent = content, TomorrowAgendaContent = content, ScratchpadFontSize = 18 };
            var view = new ScratchpadView { DataContext = model };
            WpfUiHarness.Realize(view, width, 550);
            var editor = WpfUiHarness.FindByAutomationName<ScratchpadEditor>(view, "Today's Work freeform scratchpad");
            var summary = WpfUiHarness.FindByAutomationName<TextBlock>(view, "Today's Work numbered checklist progress");
            Assert.True(editor.ActualHeight > 200);
            Assert.True(summary.ActualWidth <= editor.ActualWidth);
            Assert.Equal("Checklist  2 / 10", summary.Text);
            if (Environment.GetEnvironmentVariable("SATI_SCRATCHPAD_QA_OUTPUT") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                var bitmap = new RenderTargetBitmap((int)width, 550, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(view);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(directory, $"scratchpad-{width:0}.png")); encoder.Save(file);
            }
        });
    }

    [Fact]
    public void TextSizingChangesDisplayWithoutChangingSavedFormatting()
    {
        WpfUiHarness.Run(() =>
        {
            var model = new ScratchpadViewModel(null!, null!) { ScratchpadContent = "Formatted text" };
            var view = new ScratchpadView { DataContext = model };
            WpfUiHarness.Realize(view, 700, 550);
            var editor = WpfUiHarness.FindByAutomationName<ScratchpadEditor>(view, "Today's Work freeform scratchpad");
            editor.SelectAll();
            editor.ApplyHighlight(TextHighlight.Green);
            var saved = model.ScratchpadContent;
            model.IncreaseScratchpadFontCommand.Execute(null);
            Assert.Equal(16, editor.FontSize);
            Assert.Equal(16d, editor.Selection.GetPropertyValue(TextElement.FontSizeProperty));
            Assert.Equal(saved, model.ScratchpadContent);
            Assert.Equal(Brushes.Black, editor.Selection.GetPropertyValue(TextElement.ForegroundProperty));
        });
    }

    [Fact]
    public void LockedHistoryRendersMarksAndDisablesCheckboxesAndFormatting()
    {
        WpfUiHarness.Run(() =>
        {
            var stored = new JournalDocument([new JournalPage("Scratchpad", [new JournalParagraph([
                JournalInline.Checkbox(true), JournalInline.Run("Original", bold: true,
                    strikethrough: true, highlight: TextHighlight.Pink)])])]).Serialize();
            var editor = new ScratchpadEditor { IsReadOnly = true, StoredContent = stored };
            var check = Assert.IsType<CheckBox>(Assert.Single(editor.Document.Blocks.OfType<Paragraph>())
                .Inlines.OfType<InlineUIContainer>().Single().Child);
            Assert.False(check.IsEnabled);
            editor.SelectAll();
            editor.ApplyHighlight(TextHighlight.Blue);
            editor.InsertOrToggleCheckbox();
            editor.ToggleDecoration(TextDecorationLocation.Strikethrough);
            Assert.Equal(stored, editor.StoredContent);
            Assert.Equal(2, Assert.Single(JournalFlowDocument.Read(editor.Document)).Inlines.Count);
        });
    }

    [Fact]
    public void ExternalContentClearsOldUndoAndSnippetsRemainPlainText()
    {
        WpfUiHarness.Run(() =>
        {
            var editor = new ScratchpadEditor { StoredContent = "Old draft" };
            editor.SelectAll();
            editor.Selection.Text = "Edited old draft";
            editor.StoredContent = "New account";
            Assert.False(editor.CanUndo);
            TextShortcutTarget.SetIsEnabled(editor, true);
            editor.SelectAll();
            Assert.True(TextShortcutTarget.TryInsert(editor, "<b>literal snippet</b>"));
            Assert.True(editor.Selection.IsEmpty);
            Assert.Equal("<b>literal snippet</b>", JournalDocument.Parse(editor.StoredContent).ToPlainText());
            editor.IsReadOnly = true;
            Assert.False(TextShortcutTarget.TryInsert(editor, "must not replace history"));
        });
    }

    [Fact]
    public void PastedMarkupIsReducedToPlainTextBeforeWpfCanLoadIt()
    {
        WpfUiHarness.Run(() =>
        {
            var editor = new ScratchpadEditor();
            var data = new DataObject();
            data.SetData(DataFormats.Xaml, "<InlineUIContainer><Button>Do not load</Button></InlineUIContainer>");
            data.SetData(DataFormats.UnicodeText, "Literal words");
            var paste = new DataObjectPastingEventArgs(data, false, DataFormats.Xaml);
            editor.RaiseEvent(paste);
            Assert.False(paste.CommandCancelled);
            Assert.Equal("Literal words", paste.DataObject.GetData(DataFormats.UnicodeText));
            Assert.Equal(DataFormats.UnicodeText, paste.FormatToApply);
            Assert.False(paste.DataObject.GetDataPresent(DataFormats.Xaml));
            var unsupported = new DataObjectPastingEventArgs(new DataObject(DataFormats.Xaml, "<Button/>"),
                false, DataFormats.Xaml);
            editor.RaiseEvent(unsupported);
            Assert.True(unsupported.CommandCancelled);
        });
    }

    [Fact]
    public void UndoingTheLastNumberedCheckboxHidesItsProgressWithoutRemovingTheTask()
    {
        WpfUiHarness.Run(() =>
        {
            var editor = new ScratchpadEditor { StoredContent = "Keep the task" };
            WpfUiHarness.Realize(editor, 400, 240);
            editor.SelectAll();
            editor.InsertOrToggleCheckbox(3);
            Assert.True(editor.HasNumberedCheckboxes);
            Assert.True(editor.CanUndo);
            editor.Undo();
            Assert.False(editor.HasNumberedCheckboxes);
            Assert.Equal("Keep the task", JournalDocument.Parse(editor.StoredContent).ToPlainText());
            editor.Redo();
            Assert.True(editor.HasNumberedCheckboxes);
            Assert.Equal("Checklist  0 / 3", editor.ChecklistProgress);
            var checkbox = Assert.IsType<CheckBox>(Assert.Single(editor.Document.Blocks.OfType<Paragraph>())
                .Inlines.OfType<InlineUIContainer>().Single().Child);
            checkbox.IsChecked = true;
            Assert.Equal("Checklist  3 / 3", editor.ChecklistProgress);
            Assert.NotNull(checkbox.ContextMenu);
        });
    }
}
