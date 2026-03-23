using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Sati.Converters
{
    public class BoolToTileColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isExcluded && isExcluded)
                return new SolidColorBrush(Color.FromRgb(0xD4, 0xA8, 0x82)); // muted warm
            return new SolidColorBrush(Color.FromRgb(0xED, 0xD9, 0xC0)); // active cream
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}