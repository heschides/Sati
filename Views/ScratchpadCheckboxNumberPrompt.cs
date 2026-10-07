using Sati.Contracts.V1;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace Sati.Views;

/// <summary>Small local input surface; it never changes a document before acceptance.</summary>
internal sealed class ScratchpadCheckboxNumberPrompt : Border
{
    private readonly TextBox _input;

    internal ScratchpadCheckboxNumberPrompt(decimal? initialNumber, bool editing,
        Action<decimal?> accepted, Action cancelled)
    {
        Padding = new Thickness(12);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(5);
        SetResourceReference(BackgroundProperty, "SurfaceRaisedBrush");
        SetResourceReference(BorderBrushProperty, "BorderBrush");
        _input = new TextBox
        {
            Width = 230, Margin = new Thickness(0, 6, 0, 6),
            Text = initialNumber?.ToString("0.##", CultureInfo.CurrentCulture) ?? string.Empty
        };
        AutomationProperties.SetName(_input, "Optional scratchpad checkbox number");
        AutomationProperties.SetHelpText(_input, "Leave blank, or enter a positive number with up to two decimal places.");
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 250, Visibility = Visibility.Collapsed };
        error.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        var confirm = new Button { Content = editing ? "Save number" : "Add checkbox", Padding = new Thickness(8, 4, 8, 4) };
        AutomationProperties.SetName(confirm, "Confirm scratchpad checkbox number");
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(cancel, "Cancel scratchpad checkbox number");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel); buttons.Children.Add(confirm);
        var panel = new StackPanel();
        panel.SetResourceReference(TextElement.ForegroundProperty, "TextPrimaryBrush");
        panel.Children.Add(new TextBlock { Text = "Checkbox number (optional)", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Leave blank, or enter a positive number.\nNumbered boxes contribute to the checklist total.", FontSize = 11, Margin = new Thickness(0, 4, 0, 0) });
        panel.Children.Add(_input); panel.Children.Add(error); panel.Children.Add(buttons);
        Child = panel;
        void Accept()
        {
            decimal? number = null;
            if (!string.IsNullOrWhiteSpace(_input.Text))
            {
                if (!decimal.TryParse(_input.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed) ||
                    JournalDocument.NormalizeCheckboxNumber(parsed) is null)
                {
                    error.Text = $"Enter a number greater than zero, up to {JournalDocument.MaximumCheckboxNumber:N0}, with at most two decimal places.";
                    error.Visibility = Visibility.Visible;
                    _input.Focus();
                    return;
                }
                number = parsed;
            }
            accepted(number);
        }
        confirm.Click += (_, _) => Accept();
        cancel.Click += (_, _) => cancelled();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
            else if (e.Key == Key.Escape) { cancelled(); e.Handled = true; }
        };
    }

    internal void FocusNumber() { _input.Focus(); _input.SelectAll(); }
}
