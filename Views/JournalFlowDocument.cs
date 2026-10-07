using Sati.Contracts.V1;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Globalization;

namespace Sati.Views;

/// <summary>
/// Translates between a journal page as <see cref="JournalDocument"/> stores it and the
/// FlowDocument the editor shows. Presentation only: the stored shape is the contract's, and
/// anything the editor holds that the contract cannot express (a pasted table, an image) is
/// reduced to its text rather than carried silently.
/// </summary>
internal static class JournalFlowDocument
{
    public static FlowDocument Build(
        IReadOnlyList<JournalParagraph> paragraphs,
        Func<bool, CheckBox> createCheckBox,
        Func<bool, decimal?, CheckBox>? createNumberedCheckBox = null)
    {
        var document = new FlowDocument { PagePadding = new Thickness(2) };
        foreach (var paragraph in paragraphs)
        {
            var block = new Paragraph { Margin = new Thickness(0) };
            foreach (var inline in paragraph.Inlines)
            {
                if (inline.IsCheckbox)
                {
                    var checkbox = createNumberedCheckBox?.Invoke(inline.IsChecked, inline.Number)
                        ?? createCheckBox(inline.IsChecked);
                    checkbox.Tag = inline.Number;
                    block.Inlines.Add(new InlineUIContainer(checkbox)
                    {
                        BaselineAlignment = BaselineAlignment.Center
                    });
                    continue;
                }

                var run = new Run(inline.Text);
                if (inline.Bold)
                    run.FontWeight = FontWeights.Bold;
                if (inline.Italic)
                    run.FontStyle = FontStyles.Italic;
                var decorations = new TextDecorationCollection();
                if (inline.Underline) decorations.Add(TextDecorations.Underline);
                if (inline.Strikethrough) decorations.Add(TextDecorations.Strikethrough);
                if (decorations.Count > 0) run.TextDecorations = decorations;
                if (inline.Highlight != TextHighlight.None)
                {
                    run.Background = HighlightBrush(inline.Highlight);
                    // The pale palette needs dark ink in both light and dark themes.
                    run.Foreground = Brushes.Black;
                }
                block.Inlines.Add(run);
            }
            document.Blocks.Add(block);
        }

        if (document.Blocks.Count == 0)
            document.Blocks.Add(new Paragraph { Margin = new Thickness(0) });
        return document;
    }

    /// <summary>
    /// The page's paragraphs as the contract stores them. A line break inside a paragraph
    /// (Shift+Enter) is stored as a paragraph break; trailing blank paragraphs are dropped,
    /// because the editor always keeps one to type into.
    /// </summary>
    public static IReadOnlyList<JournalParagraph> Read(FlowDocument document)
    {
        var result = new List<JournalParagraph>();
        foreach (var block in document.Blocks)
            ReadBlock(block, result);

        while (result.Count > 0 && result[^1].Inlines.Count == 0)
            result.RemoveAt(result.Count - 1);
        return result;
    }

    private static void ReadBlock(Block block, List<JournalParagraph> result)
    {
        switch (block)
        {
            case Paragraph paragraph:
                var builder = new ParagraphBuilder(result);
                foreach (var inline in paragraph.Inlines)
                    ReadInline(inline, builder);
                builder.Finish();
                break;
            case Section section:
                foreach (var child in section.Blocks)
                    ReadBlock(child, result);
                break;
            case List list:
                foreach (var item in list.ListItems)
                    foreach (var child in item.Blocks)
                        ReadBlock(child, result);
                break;
            default:
                // Tables and anything else: keep the words, not the structure.
                var text = new TextRange(block.ContentStart, block.ContentEnd).Text;
                foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
                    result.Add(JournalParagraph.FromText(line));
                break;
        }
    }

    private static void ReadInline(Inline inline, ParagraphBuilder builder)
    {
        switch (inline)
        {
            case Run run:
                builder.AddText(run.Text, IsBold(run), IsItalic(run),
                    HasDecoration(run, TextDecorationLocation.Underline),
                    HasDecoration(run, TextDecorationLocation.Strikethrough), ReadHighlight(run));
                break;
            case LineBreak:
                builder.Break();
                break;
            case InlineUIContainer { Child: CheckBox checkBox }:
                builder.AddCheckbox(checkBox.IsChecked == true, CheckboxNumber(checkBox));
                break;
            case Span span:
                foreach (var child in span.Inlines)
                    ReadInline(child, builder);
                break;
        }
    }

    private static bool IsBold(TextElement element) => element.FontWeight.ToOpenTypeWeight() >= 600;

    // WPF's restrictive undo reader restores the visible string content but can
    // drop the object-typed Tag. Accept only our numeric label as its fallback.
    internal static decimal? CheckboxNumber(CheckBox checkbox) =>
        JournalDocument.NormalizeCheckboxNumber(checkbox.Tag as decimal?) ??
        (checkbox.Content is string label && decimal.TryParse(label, NumberStyles.Number,
            CultureInfo.CurrentCulture, out var number) ? JournalDocument.NormalizeCheckboxNumber(number) : null);

    private static bool IsItalic(TextElement element) =>
        element.FontStyle == FontStyles.Italic || element.FontStyle == FontStyles.Oblique;

    // TextDecorations is not inherited, and the editor may put an underline on the run or on
    // any span around it, so every ancestor up to the paragraph is asked.
    private static bool HasDecoration(Inline inline, TextDecorationLocation location)
    {
        for (DependencyObject? current = inline; current is Inline element; current = element.Parent)
        {
            if (element.TextDecorations?.Any(decoration =>
                    decoration.Location == location) == true)
                return true;
        }
        return false;
    }

    internal static Brush HighlightBrush(TextHighlight highlight) => highlight switch
    {
        TextHighlight.Yellow => Brushes.PaleGoldenrod,
        TextHighlight.Green => Brushes.PaleGreen,
        TextHighlight.Blue => Brushes.LightBlue,
        TextHighlight.Pink => Brushes.LightPink,
        _ => Brushes.Transparent
    };

    private static TextHighlight ReadHighlight(Inline inline)
    {
        for (DependencyObject? current = inline; current is Inline element; current = element.Parent)
        {
            if (element.Background is not SolidColorBrush brush || brush.Color.A == 0) continue;
            foreach (var highlight in Enum.GetValues<TextHighlight>().Where(value => value != TextHighlight.None))
                if (((SolidColorBrush)HighlightBrush(highlight)).Color == brush.Color) return highlight;
            return TextHighlight.None;
        }
        return TextHighlight.None;
    }

    private sealed class ParagraphBuilder(List<JournalParagraph> result)
    {
        private readonly List<JournalInline> _inlines = [];
        private readonly StringBuilder _text = new();
        private (bool Bold, bool Italic, bool Underline, bool Strikethrough, TextHighlight Highlight)? _marks;

        public void AddText(string text, bool bold, bool italic, bool underline,
            bool strikethrough, TextHighlight highlight)
        {
            if (text.Length == 0)
                return;
            // Adjacent runs with the same marks are one run in storage; the editor splits
            // runs freely and the stored form should not grow with every keystroke.
            if (_marks != (bold, italic, underline, strikethrough, highlight))
                FlushText();
            _marks = (bold, italic, underline, strikethrough, highlight);
            _text.Append(text);
        }

        public void AddCheckbox(bool isChecked, decimal? number)
        {
            FlushText();
            _inlines.Add(JournalInline.Checkbox(isChecked, number));
        }

        public void Break()
        {
            Finish();
        }

        public void Finish()
        {
            FlushText();
            result.Add(new JournalParagraph(_inlines.ToList()));
            _inlines.Clear();
        }

        private void FlushText()
        {
            if (_text.Length > 0 && _marks is var (bold, italic, underline, strikethrough, highlight))
                _inlines.Add(JournalInline.Run(_text.ToString(), bold, italic, underline, strikethrough, highlight));
            _text.Clear();
            _marks = null;
        }
    }
}
