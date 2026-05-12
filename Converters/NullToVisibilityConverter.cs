using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Sati.Converters
{
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isNull = value is null;
            bool inverse = parameter is string s && s == "inverse";
            return (isNull == inverse) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
