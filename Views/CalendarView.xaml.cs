using System;
using System.Windows;
using System.Windows.Controls;

namespace Sati.Views
{
    /// <summary>
    /// Interaction logic for CalendarView.xaml
    /// </summary>
    public partial class CalendarView : UserControl
    {
        public static readonly DependencyProperty MonthColumnCountProperty =
            DependencyProperty.Register(
                nameof(MonthColumnCount),
                typeof(int),
                typeof(CalendarView),
                new PropertyMetadata(4));

        public int MonthColumnCount
        {
            get => (int)GetValue(MonthColumnCountProperty);
            private set => SetValue(MonthColumnCountProperty, value);
        }

        public CalendarView()
        {
            InitializeComponent();
            SizeChanged += CalendarView_SizeChanged;
        }

        private void CalendarView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // The detail rail always uses 220 px. The year view intentionally
            // stops at three columns so each mini-calendar keeps some breathing room.
            var yearOverviewWidth = Math.Max(0, e.NewSize.Width - 220);
            MonthColumnCount = yearOverviewWidth switch
            {
                >= 900 => 3,
                >= 500 => 2,
                _ => 1
            };
        }
    }
}
