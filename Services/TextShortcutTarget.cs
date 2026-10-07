using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Sati.Services;

public static class TextShortcutTarget
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(TextShortcutTarget),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    internal static bool TryInsert(TextBoxBase textBox, string text)
    {
        if (!GetIsEnabled(textBox) || !textBox.IsEnabled || textBox.IsReadOnly || string.IsNullOrEmpty(text))
            return false;

        if (textBox is TextBox plain)
        {
            var insertionPoint = plain.SelectionStart;
            plain.SelectedText = text;
            plain.CaretIndex = insertionPoint + text.Length;
            plain.SelectionLength = 0;
        }
        else if (textBox is RichTextBox rich)
        {
            rich.Selection.Text = text;
            rich.CaretPosition = rich.Selection.End;
            rich.Selection.Select(rich.CaretPosition, rich.CaretPosition);
        }
        else return false;
        return true;
    }
}
