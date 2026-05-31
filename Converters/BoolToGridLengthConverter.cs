using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Sati.Converters
{
    public class BoolToGridLengthConverter : IValueConverter
    {
        public double TrueLength { get; set; } = 260;
        public double FalseLength { get; set; } = 0;

        /// <summary>
        /// Use "Star" to get proportional (*) sizing; "Pixel" for fixed; "Auto" is ignored here
        /// since we're driving layout programmatically.
        /// </summary>
        public GridUnitType TrueUnit { get; set; } = GridUnitType.Pixel;
        public GridUnitType FalseUnit { get; set; } = GridUnitType.Star;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b
                ? new GridLength(TrueLength, TrueUnit)
                : new GridLength(FalseLength, FalseUnit);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}