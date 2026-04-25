using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Sati.Models;

namespace Sati.Converters
{
    /// <summary>
    /// Maps FormCellStatus enum values to the SolidColorBrush used as the
    /// matrix cell's background. Lives on the XAML side of the boundary so
    /// the view-model stays UI-framework-agnostic — FormCellViewModel exposes
    /// a Status enum, the converter chooses the brush.
    ///
    /// Color choices match the warm earth-tone palette:
    ///   Complete      — soft green, "done, no action needed"
    ///   DueThisMonth  — orange, attention-grabbing without being alarming
    ///   DueNextMonth  — yellow, "on the horizon"
    ///   NotYetOpen    — neutral parchment, blends with background
    ///   Overdue       — red, "this needs action now"
    /// </summary>
    public class FormCellStatusToBrushConverter : IValueConverter
    {
        // Brushes are constructed once and frozen so WPF can share them across
        // the visual tree without per-cell allocation. Frozen Freezables are
        // thread-safe and bypass change-tracking overhead.
        private static readonly SolidColorBrush CompleteBrush = Freeze(0xC2, 0xDF, 0xB3);
        private static readonly SolidColorBrush DueThisMonthBrush = Freeze(0xF2, 0xB1, 0x7A);
        private static readonly SolidColorBrush DueNextMonthBrush = Freeze(0xF5, 0xDF, 0xB0);
        private static readonly SolidColorBrush NotYetOpenBrush = Freeze(0xF5, 0xEC, 0xDC);
        private static readonly SolidColorBrush OverdueBrush = Freeze(0xEE, 0xB0, 0x9A);

        public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not FormCellStatus status)
                return NotYetOpenBrush;

            return status switch
            {
                FormCellStatus.Complete => CompleteBrush,
                FormCellStatus.DueThisMonth => DueThisMonthBrush,
                FormCellStatus.DueNextMonth => DueNextMonthBrush,
                FormCellStatus.NotYetOpen => NotYetOpenBrush,
                FormCellStatus.Overdue => OverdueBrush,
                _ => NotYetOpenBrush
            };
        }

        public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException("FormCellStatusToBrushConverter is one-way.");

        private static SolidColorBrush Freeze(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}