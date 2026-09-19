using Sati.ViewModels;
using Sati.ViewModels.Children;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Sati.Views;

/// <summary>
/// The client page's journal: named pages, bold/italic/underline, and checkboxes. The rich
/// text lives here; what is stored is <see cref="Sati.Contracts.V1.JournalDocument"/>, reached
/// through <see cref="JournalPageViewModel.ApplyEditorContent"/> on every change so the view
/// model's existing debounced save and flush paths see each edit as they always did.
/// </summary>
public partial class JournalEditor : UserControl
{
    private static readonly Geometry SparkleGeometry = Geometry.Parse(
        "M 0,-6 C 0.6,-1.6 1.6,-0.6 6,0 C 1.6,0.6 0.6,1.6 0,6 C -0.6,1.6 -1.6,0.6 -6,0 C -1.6,-0.6 -0.6,-1.6 0,-6 Z");

    private readonly Random _random = new();
    private NewClientViewModel? _viewModel;
    private JournalPageViewModel? _shownPage;
    private bool _rebuilding;

    public JournalEditor()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as NewClientViewModel);
        DataObject.AddPastingHandler(Editor, OnPasting);
        DisableUnstoredFormatting();
    }

    // -------------------------------------------------------------------------
    // Keeping the editor and the page in step
    // -------------------------------------------------------------------------

    private void Attach(NewClientViewModel? viewModel)
    {
        if (_viewModel is not null)
            _viewModel.JournalPages.PropertyChanged -= OnPagesPropertyChanged;
        _viewModel = viewModel;
        if (_viewModel is not null)
            _viewModel.JournalPages.PropertyChanged += OnPagesPropertyChanged;
        Show(_viewModel?.JournalPages.SelectedPage);
    }

    private void OnPagesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(JournalPagesViewModel.SelectedPage))
            Show(_viewModel?.JournalPages.SelectedPage);
    }

    private void Show(JournalPageViewModel? page)
    {
        if (_shownPage is not null)
            _shownPage.PropertyChanged -= OnShownPagePropertyChanged;
        _shownPage = page;
        if (_shownPage is not null)
            _shownPage.PropertyChanged += OnShownPagePropertyChanged;
        Rebuild();
    }

    // Paragraphs is raised only when the content was replaced from outside — a
    // client switch, or a reminder the server wrote at the top of the page.
    private void OnShownPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(JournalPageViewModel.Paragraphs))
            Rebuild();
    }

    // A new document also starts a new undo history, so Ctrl+Z cannot reach
    // back into another page or another client's journal.
    private void Rebuild()
    {
        _rebuilding = true;
        try
        {
            Editor.Document = JournalFlowDocument.Build(_shownPage?.Paragraphs ?? [], CreateCheckBox);
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e) => SyncToPage();

    private void SyncToPage()
    {
        if (_rebuilding || _shownPage is null)
            return;
        _shownPage.ApplyEditorContent(JournalFlowDocument.Read(Editor.Document));
    }

    private CheckBox CreateCheckBox(bool isChecked)
    {
        var checkBox = new CheckBox
        {
            IsChecked = isChecked,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            // Not a tab stop inside the text; Ctrl+Shift+C beside it toggles it.
            Focusable = false,
            ToolTip = "Click, or press Ctrl+Shift+C beside it, to check or uncheck"
        };
        AutomationProperties.SetName(checkBox, "Journal checkbox");
        checkBox.Checked += (_, _) => SyncToPage();
        checkBox.Unchecked += (_, _) => SyncToPage();
        return checkBox;
    }

    // The stored journal keeps plain text and its own marks. Rich content pasted
    // from Word or a browser would show until the next load and then silently
    // lose its tables and colours, so it arrives as the text it contains.
    private static void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.SourceDataObject.GetData(DataFormats.UnicodeText, true) is string text)
        {
            var plain = new DataObject();
            plain.SetData(DataFormats.UnicodeText, text);
            e.DataObject = plain;
            return;
        }

        e.CancelCommand();
    }

    // RichTextBox ships shortcuts for alignment, font size, lists, indentation, and
    // sub/superscript. None of them can be stored, so they are switched off rather
    // than allowed to show formatting that disappears on the next load.
    private void DisableUnstoredFormatting()
    {
        RoutedUICommand[] unstored =
        [
            EditingCommands.AlignCenter, EditingCommands.AlignJustify, EditingCommands.AlignLeft,
            EditingCommands.AlignRight, EditingCommands.IncreaseFontSize, EditingCommands.DecreaseFontSize,
            EditingCommands.ToggleBullets, EditingCommands.ToggleNumbering,
            EditingCommands.IncreaseIndentation, EditingCommands.DecreaseIndentation,
            EditingCommands.ToggleSubscript, EditingCommands.ToggleSuperscript
        ];
        foreach (var command in unstored)
        {
            Editor.CommandBindings.Add(new CommandBinding(command,
                (_, e) => e.Handled = true,
                (_, e) =>
                {
                    e.CanExecute = false;
                    e.Handled = true;
                }));
        }
    }

    // -------------------------------------------------------------------------
    // Checking text
    // -------------------------------------------------------------------------

    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            CheckSelection();
            e.Handled = true;
        }
    }

    private void CheckButton_Click(object sender, RoutedEventArgs e)
    {
        CheckSelection();
        Editor.Focus();
    }

    /// <summary>
    /// With text selected: puts a checkbox in front of it, sparkles over it, and asks whether
    /// it should go on the calendar. With nothing selected: toggles a checkbox beside the caret,
    /// or puts a new one there — a to-do line with nothing to remind about asks nothing.
    /// </summary>
    private void CheckSelection()
    {
        if (_viewModel is null || !_viewModel.CanEditJournal || _shownPage is null)
            return;

        var selection = Editor.Selection;
        if (selection.IsEmpty || string.IsNullOrWhiteSpace(selection.Text))
        {
            if (AdjacentCheckBox(Editor.CaretPosition) is CheckBox existing)
            {
                existing.IsChecked = existing.IsChecked != true;
                return;
            }

            _ = new InlineUIContainer(CreateCheckBox(false),
                Editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward))
            {
                BaselineAlignment = BaselineAlignment.Center
            };
            return;
        }

        var text = selection.Text;
        var bounds = SelectionBounds(selection);
        _ = new InlineUIContainer(CreateCheckBox(false),
            selection.Start.GetInsertionPosition(LogicalDirection.Forward))
        {
            BaselineAlignment = BaselineAlignment.Center
        };

        if (bounds is Rect rect)
            Sparkle(rect);
        _viewModel.OpenJournalCheckPrompt(text);
    }

    private static CheckBox? AdjacentCheckBox(TextPointer caret)
    {
        foreach (var direction in new[] { LogicalDirection.Backward, LogicalDirection.Forward })
        {
            if (caret.GetAdjacentElement(direction) is InlineUIContainer { Child: CheckBox checkBox })
                return checkBox;
        }
        return null;
    }

    // Where the selected text is drawn, in the sparkle layer's coordinates. A selection
    // on one line is its own width; across lines it is the text column between them.
    private Rect? SelectionBounds(TextSelection selection)
    {
        var start = selection.Start.GetCharacterRect(LogicalDirection.Forward);
        var end = selection.End.GetCharacterRect(LogicalDirection.Backward);
        if (start.IsEmpty || end.IsEmpty)
            return null;

        Rect rect;
        if (Math.Abs(start.Top - end.Top) < 2 && end.Right > start.Left)
        {
            rect = new Rect(start.Left, start.Top, end.Right - start.Left, Math.Max(start.Height, end.Height));
        }
        else
        {
            var left = Editor.Padding.Left;
            rect = new Rect(left, start.Top,
                Math.Max(20, Editor.ActualWidth - left - Editor.Padding.Right),
                Math.Max(start.Height, end.Bottom - start.Top));
        }

        var origin = Editor.TranslatePoint(rect.TopLeft, SparkleLayer);
        var visible = new Rect(origin, rect.Size);
        visible.Intersect(new Rect(0, 0, SparkleLayer.ActualWidth, SparkleLayer.ActualHeight));
        return visible.IsEmpty || visible.Width < 1 ? null : visible;
    }

    // A gold shimmer sweeps across the checked text while small four-point stars
    // burst out of it and fade. Skipped when Windows has client-area animation
    // turned off — the check and the question do not depend on it.
    private void Sparkle(Rect rect)
    {
        if (!SystemParameters.ClientAreaAnimation)
            return;

        var gold = TryFindResource("ChartGoldBrush") as SolidColorBrush ?? new SolidColorBrush(Color.FromRgb(0xE3, 0xAD, 0x3F));
        var accent = TryFindResource("AccentBrush") as Brush ?? gold;
        var goldColor = gold.Color;

        var sweep = new TranslateTransform(-1, 0);
        var shimmer = new Rectangle
        {
            Width = rect.Width + 8,
            Height = rect.Height + 4,
            RadiusX = 3,
            RadiusY = 3,
            Opacity = 0,
            Fill = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb(0, goldColor.R, goldColor.G, goldColor.B), 0.0),
                    new(Color.FromArgb(150, goldColor.R, goldColor.G, goldColor.B), 0.5),
                    new(Color.FromArgb(0, goldColor.R, goldColor.G, goldColor.B), 1.0)
                },
                new Point(0, 0.5), new Point(1, 0.5))
            {
                RelativeTransform = sweep
            }
        };
        Canvas.SetLeft(shimmer, rect.Left - 4);
        Canvas.SetTop(shimmer, rect.Top - 2);
        SparkleLayer.Children.Add(shimmer);

        var shimmerFade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(900) };
        shimmerFade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))));
        shimmerFade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900))));
        shimmerFade.Completed += (_, _) => SparkleLayer.Children.Remove(shimmer);
        sweep.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-1, 1, TimeSpan.FromMilliseconds(800))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
        shimmer.BeginAnimation(OpacityProperty, shimmerFade);

        var count = Math.Clamp((int)(rect.Width / 9), 14, 28);
        for (var index = 0; index < count; index++)
            AddStar(rect, index % 3 == 0 ? accent : gold);
    }

    private void AddStar(Rect rect, Brush fill)
    {
        var scale = new ScaleTransform(0, 0);
        var rotate = new RotateTransform(_random.Next(0, 90));
        var move = new TranslateTransform();
        var star = new Path
        {
            Data = SparkleGeometry,
            Fill = fill,
            Opacity = 0,
            RenderTransform = new TransformGroup { Children = { scale, rotate, move } }
        };
        Canvas.SetLeft(star, rect.Left + _random.NextDouble() * rect.Width);
        Canvas.SetTop(star, rect.Top + _random.NextDouble() * rect.Height);
        SparkleLayer.Children.Add(star);

        var angle = _random.NextDouble() * Math.PI * 2;
        var distance = 16 + _random.NextDouble() * 30;
        var size = 0.5 + _random.NextDouble() * 0.9;
        var begin = TimeSpan.FromMilliseconds(_random.Next(0, 200));
        var duration = TimeSpan.FromMilliseconds(650 + _random.Next(0, 400));
        var easeOut = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Math.Cos(angle) * distance, duration)
        {
            BeginTime = begin,
            EasingFunction = easeOut
        });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Math.Sin(angle) * distance - 6, duration)
        {
            BeginTime = begin,
            EasingFunction = easeOut
        });
        rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(
            rotate.Angle, rotate.Angle + (_random.Next(2) == 0 ? -180 : 180), duration)
        {
            BeginTime = begin
        });

        var grow = new DoubleAnimationUsingKeyFrames { BeginTime = begin, Duration = duration };
        grow.KeyFrames.Add(new EasingDoubleKeyFrame(size * 1.3, KeyTime.FromPercent(0.3)));
        grow.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1.0)));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);

        var fade = new DoubleAnimationUsingKeyFrames { BeginTime = begin, Duration = duration };
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(0)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.55)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1.0)));
        fade.Completed += (_, _) => SparkleLayer.Children.Remove(star);
        star.BeginAnimation(OpacityProperty, fade);
    }

    // -------------------------------------------------------------------------
    // The reminder question
    // -------------------------------------------------------------------------

    private void CheckPrompt_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            if (SystemParameters.ClientAreaAnimation)
            {
                var pop = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 }
                };
                PromptScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                PromptScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
                CheckPrompt.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            }
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => PromptDate.Focus());
            return;
        }

        // Answered or withdrawn: writing continues where it left off.
        if (CheckPrompt.IsKeyboardFocusWithin || Keyboard.FocusedElement is null)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Editor.Focus());
    }

    private void CheckPrompt_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _viewModel?.JournalCheckPrompt.DeclineCommand.CanExecute(null) == true)
        {
            _viewModel.JournalCheckPrompt.DeclineCommand.Execute(null);
            Editor.Focus();
            e.Handled = true;
        }
    }

    // -------------------------------------------------------------------------
    // Page tabs
    // -------------------------------------------------------------------------

    private void PageTab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement { DataContext: JournalPageViewModel page })
        {
            _viewModel?.JournalPages.BeginRenameCommand.Execute(page);
            e.Handled = true;
        }
    }

    private void PageTabs_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && e.OriginalSource is not TextBox &&
            _viewModel?.JournalPages.SelectedPage is JournalPageViewModel page)
        {
            _viewModel.JournalPages.BeginRenameCommand.Execute(page);
            e.Handled = true;
        }
    }

    private void RenameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is TextBox box)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                box.Focus();
                box.SelectAll();
            });
        }
    }

    private void RenameBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: JournalPageViewModel page } || _viewModel is null)
            return;

        if (e.Key == Key.Enter)
        {
            _viewModel.JournalPages.CommitRenameCommand.Execute(page);
            Editor.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.JournalPages.CancelRenameCommand.Execute(page);
            Editor.Focus();
            e.Handled = true;
        }
    }

    private void RenameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: JournalPageViewModel page })
            _viewModel?.JournalPages.CommitRenameCommand.Execute(page);
    }
}
