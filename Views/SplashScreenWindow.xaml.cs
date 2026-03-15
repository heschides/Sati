using System;
using System.Collections.Generic;
using System.Text;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Sati.Views
{
    /// <summary>
    /// Interaction logic for SplashScreenWindow.xaml
    /// </summary>
    public partial class SplashScreenWindow : Window
    {
        private readonly DispatcherTimer _timer;
        private int _activeDot = 0;
        private readonly Ellipse[] _dots;
        public SplashScreenWindow()
        {
            InitializeComponent();

            _dots = new[] { Dot1, Dot2, Dot3, Dot4, Dot5 };

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _timer.Tick += (s, e) =>
            {
                foreach (var dot in _dots)
                    dot.Fill = new SolidColorBrush(Color.FromRgb(0xD4, 0xA8, 0x82));

                _dots[_activeDot].Fill = new SolidColorBrush(Color.FromRgb(0xC8, 0x79, 0x41));

                _activeDot = (_activeDot + 1) % _dots.Length;
            };
            _timer.Start();

            Closing += (s, e) => _timer.Stop();

        }
    }
}
