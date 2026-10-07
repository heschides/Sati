using Sati.Contracts.V1;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Globalization;
using System.Windows.Threading;
using System.Runtime.CompilerServices;

namespace Sati.Views;

/// <summary>
/// Personal agenda editor. Only the portable journal contract crosses the binding;
/// stored values and pasted markup are never loaded as XAML/RTF or executable objects.
/// </summary>
public sealed class ScratchpadEditor : RichTextBox
{
    public static readonly DependencyProperty StoredContentProperty = DependencyProperty.Register(
        nameof(StoredContent), typeof(string), typeof(ScratchpadEditor),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (sender, _) => ((ScratchpadEditor)sender).Rebuild()));

    private bool _rebuilding;
    private bool _publishing;
    private Popup? _numberPrompt;
    private readonly ConditionalWeakTable<CheckBox, object> _wiredCheckboxes = new();
    private static readonly DependencyPropertyKey ChecklistProgressPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ChecklistProgress), typeof(string), typeof(ScratchpadEditor), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ChecklistProgressProperty = ChecklistProgressPropertyKey.DependencyProperty;
    public string ChecklistProgress => (string)GetValue(ChecklistProgressProperty);
    private static readonly DependencyPropertyKey HasNumberedCheckboxesPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasNumberedCheckboxes), typeof(bool), typeof(ScratchpadEditor), new PropertyMetadata(false));
    public static readonly DependencyProperty HasNumberedCheckboxesProperty = HasNumberedCheckboxesPropertyKey.DependencyProperty;
    public bool HasNumberedCheckboxes => (bool)GetValue(HasNumberedCheckboxesProperty);

    public string StoredContent
    {
        get => (string)GetValue(StoredContentProperty);
        set => SetValue(StoredContentProperty, value);
    }

    public ScratchpadEditor()
    {
        IsDocumentEnabled = true;
        AcceptsReturn = true;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        SpellCheck.SetIsEnabled(this, true);
        TextChanged += (_, _) => Publish();
        DataObject.AddPastingHandler(this, (_, e) =>
        {
            if (e.SourceDataObject.GetData(DataFormats.UnicodeText, true) is string text)
            {
                var plain = new DataObject();
                plain.SetData(DataFormats.UnicodeText, text);
                e.DataObject = plain;
                e.FormatToApply = DataFormats.UnicodeText;
            }
            else e.CancelCommand();
        });
        // Unsupported editing commands must not produce styles that disappear on reload.
        RoutedUICommand[] unstored =
        [
            EditingCommands.AlignCenter, EditingCommands.AlignJustify, EditingCommands.AlignLeft,
            EditingCommands.AlignRight, EditingCommands.IncreaseFontSize, EditingCommands.DecreaseFontSize,
            EditingCommands.ToggleBullets, EditingCommands.ToggleNumbering,
            EditingCommands.IncreaseIndentation, EditingCommands.DecreaseIndentation,
            EditingCommands.ToggleSubscript, EditingCommands.ToggleSuperscript
        ];
        foreach (var command in unstored)
            CommandBindings.Add(new CommandBinding(command, (_, e) => e.Handled = true,
                (_, e) => { e.CanExecute = false; e.Handled = true; }));
        ContextMenu = CreateMenu();
        Unloaded += (_, _) => { if (_numberPrompt is not null) _numberPrompt.IsOpen = false; };
        Rebuild();
    }

    private void Rebuild()
    {
        if (_publishing) return;
        if (_numberPrompt is not null) _numberPrompt.IsOpen = false;
        _rebuilding = true;
        try
        {
            var content = JournalDocument.Parse(StoredContent);
            var paragraphs = content.Pages.SelectMany(page => page.Paragraphs).ToArray();
            if (IsReadOnly && string.IsNullOrWhiteSpace(content.ToPlainText()))
                paragraphs = [JournalParagraph.FromText("No text was entered on this day.")];
            // Replacing a draft/account also replaces its undo stack.
            Document = JournalFlowDocument.Build(paragraphs, value => CreateCheckBox(value, null), CreateCheckBox);
            UpdateChecklist(content);
        }
        finally { _rebuilding = false; }
    }

    private void Publish()
    {
        if (_rebuilding || IsReadOnly || !IsEnabled) return;
        // Undo/redo recreates embedded WPF controls without their event handlers.
        foreach (var checkbox in CheckBoxes()) WireCheckbox(checkbox);
        var content = new JournalDocument([new JournalPage("Scratchpad", JournalFlowDocument.Read(Document))]);
        UpdateChecklist(content);
        var stored = content.Serialize();
        if (stored == StoredContent) return;
        _publishing = true;
        try { SetCurrentValue(StoredContentProperty, stored); }
        finally { _publishing = false; }
    }

    private void UpdateChecklist(JournalDocument document)
    {
        var totals = document.ChecklistTotals();
        SetValue(HasNumberedCheckboxesPropertyKey, totals.NumberedCount > 0);
        SetValue(ChecklistProgressPropertyKey, totals.NumberedCount == 0 ? string.Empty
            : $"Checklist  {totals.CheckedNumberTotal:0.##} / {totals.NumberTotal:0.##}");
    }

    private CheckBox CreateCheckBox(bool isChecked, decimal? number)
    {
        var checkbox = new CheckBox
        {
            IsChecked = isChecked, IsEnabled = !IsReadOnly && IsEnabled,
            Focusable = false, Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Click, or press Ctrl+Shift+C beside it, to check or uncheck"
        };
        SetCheckboxNumber(checkbox, number, publish: false);
        WireCheckbox(checkbox);
        return checkbox;
    }

    private void WireCheckbox(CheckBox checkbox)
    {
        if (_wiredCheckboxes.TryGetValue(checkbox, out _)) return;
        _wiredCheckboxes.Add(checkbox, new object());
        checkbox.IsEnabled = !IsReadOnly && IsEnabled;
        checkbox.Focusable = false;
        checkbox.Cursor = Cursors.Hand;
        SetCheckboxNumber(checkbox, JournalFlowDocument.CheckboxNumber(checkbox), publish: false);
        var menu = new ContextMenu();
        var edit = new MenuItem { Header = "Edit checkbox number…" };
        edit.Click += (_, _) => OpenCheckboxNumberPrompt(checkbox);
        menu.Items.Add(edit);
        checkbox.ContextMenu = menu;
        checkbox.Checked += (_, _) => Publish();
        checkbox.Unchecked += (_, _) => Publish();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsReadOnlyProperty || e.Property == IsEnabledProperty)
        {
            foreach (var checkbox in CheckBoxes()) checkbox.IsEnabled = !IsReadOnly && IsEnabled;
            if (e.Property == IsReadOnlyProperty) Rebuild();
            if (e.NewValue is false && e.Property == IsEnabledProperty && _numberPrompt is not null)
                _numberPrompt.IsOpen = false;
        }
    }

    private IEnumerable<CheckBox> CheckBoxes() => Document.Blocks.OfType<Paragraph>()
        .SelectMany(paragraph => paragraph.Inlines.OfType<InlineUIContainer>())
        .Select(container => container.Child).OfType<CheckBox>();

    protected override void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
    {
        // WPF otherwise moves the caret and collapses selected text before opening a menu.
        if (!Selection.IsEmpty) e.Handled = true;
        base.OnPreviewMouseRightButtonDown(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!IsReadOnly && IsEnabled)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                if (Selection.IsEmpty && AdjacentCheckbox() is { } checkbox)
                    checkbox.IsChecked = checkbox.IsChecked != true;
                else OpenCheckboxNumberPrompt();
                e.Handled = true;
            }
            else if (e.Key == Key.U && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ToggleDecoration(TextDecorationLocation.Underline);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                InsertTimestamp(DateTime.Now);
                e.Handled = true;
            }
        }
        base.OnPreviewKeyDown(e);
    }

    internal void InsertTimestamp(DateTime time)
    {
        if (IsReadOnly || !IsEnabled) return;
        var range = new TextRange(CaretPosition, CaretPosition);
        range.Text = $"\r\n\r\n ─── {time:h:mm tt} ───────────────────\r\n\r\n";
        CaretPosition = range.End;
    }

    private CheckBox? AdjacentCheckbox()
    {
        foreach (var direction in new[] { LogicalDirection.Backward, LogicalDirection.Forward })
        {
            var position = CaretPosition;
            while (position is not null)
            {
                var adjacent = position.GetAdjacentElement(direction);
                if (adjacent is InlineUIContainer { Child: CheckBox checkbox }) return checkbox;
                if (adjacent is CheckBox embedded) return embedded;
                if (adjacent is not (Run or Span)) break;
                position = position.GetNextContextPosition(direction);
            }
        }
        return null;
    }

    internal void InsertOrToggleCheckbox(decimal? number = null, bool toggleAdjacent = true)
    {
        if (IsReadOnly || !IsEnabled) return;
        if (toggleAdjacent && Selection.IsEmpty)
        {
            if (AdjacentCheckbox() is { } checkbox)
            {
                checkbox.IsChecked = checkbox.IsChecked != true;
                Focus();
                return;
            }
        }
        // Insertion and baseline alignment are one undo action.
        BeginChange();
        try
        {
            // Prepend to a selection without replacing its words; otherwise insert at the caret.
            var inline = new InlineUIContainer(CreateCheckBox(false, number),
                Selection.Start.GetInsertionPosition(LogicalDirection.Forward))
            { BaselineAlignment = BaselineAlignment.Center };
            CaretPosition = inline.ElementEnd.GetInsertionPosition(LogicalDirection.Forward);
        }
        finally { EndChange(); }
        Publish();
        Focus();
    }

    internal void SetCheckboxNumber(CheckBox checkbox, decimal? number, bool publish = true)
    {
        checkbox.Tag = JournalDocument.NormalizeCheckboxNumber(number);
        checkbox.Content = checkbox.Tag is decimal value ? value.ToString("0.##", CultureInfo.CurrentCulture) : null;
        AutomationProperties.SetName(checkbox, checkbox.Content is string label
            ? $"Scratchpad checkbox, value {label}" : "Scratchpad checkbox");
        checkbox.ToolTip = "Click to check or uncheck; right-click to edit its optional number. Ctrl+Shift+C beside it also toggles it.";
        if (publish && !_rebuilding) Publish();
    }

    internal void OpenCheckboxNumberPrompt(CheckBox? existing = null)
    {
        if (IsReadOnly || !IsEnabled) return;
        if (_numberPrompt is not null) _numberPrompt.IsOpen = false;
        var start = Selection.Start;
        var end = Selection.End;
        var rectangle = start.GetCharacterRect(LogicalDirection.Forward);
        var popup = new Popup { PlacementTarget = this, Placement = PlacementMode.Relative,
            HorizontalOffset = rectangle.IsEmpty ? 0 : Math.Clamp(rectangle.Left, 0, Math.Max(0, ActualWidth - 280)),
            VerticalOffset = rectangle.IsEmpty ? 0 : rectangle.Bottom, StaysOpen = false, AllowsTransparency = true };
        _numberPrompt = popup;
        var prompt = new ScratchpadCheckboxNumberPrompt(existing?.Tag as decimal?, existing is not null, number =>
        {
            // Account/draft replacement and unloading close the popup before this can run.
            if (!popup.IsOpen || IsReadOnly || !IsEnabled) return;
            popup.IsOpen = false;
            if (existing is not null) SetCheckboxNumber(existing, number);
            else
            {
                Selection.Select(start, end);
                InsertOrToggleCheckbox(number, toggleAdjacent: false);
            }
            Focus();
        }, () => { popup.IsOpen = false; Focus(); });
        popup.Child = prompt;
        popup.IsOpen = true;
        Dispatcher.BeginInvoke(() => { if (popup.IsOpen) prompt.FocusNumber(); }, DispatcherPriority.Input);
    }

    internal void ToggleDecoration(TextDecorationLocation location)
    {
        if (IsReadOnly || !IsEnabled || Selection.IsEmpty) return;
        var ranges = Document.Blocks.OfType<Paragraph>().SelectMany(paragraph => TextRuns(paragraph.Inlines))
            .Where(run => Selection.Start.CompareTo(run.ContentEnd) < 0 && Selection.End.CompareTo(run.ContentStart) > 0)
            .Select(run => (Range: new TextRange(
                    Selection.Start.CompareTo(run.ContentStart) > 0 ? Selection.Start : run.ContentStart,
                    Selection.End.CompareTo(run.ContentEnd) < 0 ? Selection.End : run.ContentEnd),
                Decorations: run.TextDecorations?.Clone() ?? new TextDecorationCollection())).ToArray();
        var active = ranges.Length > 0 && ranges.All(range => range.Decorations.Any(d => d.Location == location));
        BeginChange();
        try
        {
            foreach (var (range, decorations) in ranges)
            {
                foreach (var decoration in decorations.Where(d => d.Location == location).ToArray()) decorations.Remove(decoration);
                if (!active) decorations.Add(location == TextDecorationLocation.Underline
                    ? TextDecorations.Underline : TextDecorations.Strikethrough);
                range.ApplyPropertyValue(Inline.TextDecorationsProperty, decorations);
            }
        }
        finally { EndChange(); }
        Publish();
        Focus();
    }

    private static IEnumerable<Run> TextRuns(InlineCollection inlines)
    {
        foreach (var inline in inlines)
            if (inline is Run run) yield return run;
            else if (inline is Span span)
                foreach (var child in TextRuns(span.Inlines)) yield return child;
    }

    internal void ApplyHighlight(TextHighlight highlight)
    {
        if (IsReadOnly || !IsEnabled || Selection.IsEmpty) return;
        BeginChange();
        try
        {
            Selection.ApplyPropertyValue(TextElement.BackgroundProperty, JournalFlowDocument.HighlightBrush(highlight));
            Selection.ApplyPropertyValue(TextElement.ForegroundProperty,
                highlight == TextHighlight.None ? Foreground : Brushes.Black);
        }
        finally { EndChange(); }
        Publish();
        Focus();
    }

    private ContextMenu CreateMenu()
    {
        var menu = new ContextMenu();
        MenuItem Command(string label, ICommand command, string? gesture = null)
        {
            var item = new MenuItem { Header = label, Command = command, CommandTarget = this, InputGestureText = gesture };
            menu.Items.Add(item);
            return item;
        }
        Command("Undo", ApplicationCommands.Undo, "Ctrl+Z");
        Command("Redo", ApplicationCommands.Redo, "Ctrl+Y");
        menu.Items.Add(new Separator());
        Command("Cut", ApplicationCommands.Cut, "Ctrl+X");
        Command("Copy", ApplicationCommands.Copy, "Ctrl+C");
        Command("Paste", ApplicationCommands.Paste, "Ctrl+V");
        Command("Select all", ApplicationCommands.SelectAll, "Ctrl+A");
        menu.Items.Add(new Separator());
        var bold = Command("Bold", EditingCommands.ToggleBold, "Ctrl+B");
        var italic = Command("Italic", EditingCommands.ToggleItalic, "Ctrl+I");
        var underline = new MenuItem { Header = "Underline", InputGestureText = "Ctrl+U" };
        underline.Click += (_, _) => ToggleDecoration(TextDecorationLocation.Underline);
        menu.Items.Add(underline);
        var strike = new MenuItem { Header = "Strikethrough" };
        strike.Click += (_, _) => ToggleDecoration(TextDecorationLocation.Strikethrough);
        menu.Items.Add(strike);
        var highlight = new MenuItem { Header = "Highlight" };
        foreach (var color in Enum.GetValues<TextHighlight>())
        {
            var item = new MenuItem { Header = color == TextHighlight.None ? "Clear highlight" : color.ToString() };
            if (color != TextHighlight.None) item.Icon = new Border
            { Background = JournalFlowDocument.HighlightBrush(color), Width = 14, Height = 14 };
            item.Click += (_, _) => ApplyHighlight(color);
            highlight.Items.Add(item);
        }
        menu.Items.Add(highlight);
        var check = new MenuItem { Header = "Insert checkbox…", InputGestureText = "Ctrl+Shift+C" };
        check.Click += (_, _) => OpenCheckboxNumberPrompt();
        menu.Items.Add(check);
        menu.Opened += (_, _) =>
        {
            var canFormat = !IsReadOnly && IsEnabled && !Selection.IsEmpty;
            foreach (var item in new[] { bold, italic, underline, strike, highlight }) item.IsEnabled = canFormat;
            bold.IsChecked = Equals(Selection.GetPropertyValue(TextElement.FontWeightProperty), FontWeights.Bold);
            italic.IsChecked = Equals(Selection.GetPropertyValue(TextElement.FontStyleProperty), FontStyles.Italic);
            var decorations = Selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
            underline.IsChecked = decorations?.Any(value => value.Location == TextDecorationLocation.Underline) == true;
            strike.IsChecked = decorations?.Any(value => value.Location == TextDecorationLocation.Strikethrough) == true;
            check.IsEnabled = !IsReadOnly && IsEnabled;
        };
        return menu;
    }
}
