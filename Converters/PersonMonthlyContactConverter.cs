using System.Globalization;
using System.Windows.Data;
using Sati.Helpers;

namespace Sati.Converters;

/// <summary>
/// Client-list last-contact line. Bound with the list's presentation revision so it
/// re-evaluates when the caseload reloads. Pass <c>Overdue</c> as the parameter for the
/// red-text trigger; anything else returns the text.
/// </summary>
public sealed class PersonMonthlyContactConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = values.Length > 0 && values[0] is Person person
            ? person.GetMonthlyContactStatus(DateTime.Today)
            : null;
        return string.Equals(parameter as string, "Overdue", StringComparison.Ordinal)
            ? MonthlyContactPresentation.IsOverdue(status)
            : MonthlyContactPresentation.Describe(status);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();
}
