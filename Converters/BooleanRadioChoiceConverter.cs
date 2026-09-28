using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Sati.Converters;

/// <summary>
/// Binds a group of radio buttons to one boolean without allowing an automatically
/// unchecked button to write a competing value back to the source.
/// </summary>
public sealed class BooleanRadioChoiceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool current && TryGetChoice(parameter, out var choice)
            ? current == choice
            : DependencyProperty.UnsetValue;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true && TryGetChoice(parameter, out var choice)
            ? choice
            : Binding.DoNothing;

    private static bool TryGetChoice(object? parameter, out bool choice)
    {
        if (parameter is bool boolean)
        {
            choice = boolean;
            return true;
        }

        return bool.TryParse(parameter?.ToString(), out choice);
    }
}
