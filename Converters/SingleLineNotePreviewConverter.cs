using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;

namespace Sati.Converters;

/// <summary>Flattens whitespace for a compact grid preview; the saved narrative is unchanged.</summary>
public sealed class SingleLineNotePreviewConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string narrative
            ? Regex.Replace(narrative, @"\s+", " ").Trim()
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
