using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Sati.Views
{
    /// <summary>
    /// Interaction logic for MonthlyProductivityView.xaml
    /// </summary>
    public partial class MonthlyProductivityView : UserControl
    {
        public MonthlyProductivityView()
        {
            InitializeComponent();
            Unloaded += (_, _) => StatusChart.Model = null;

        }
    }
}
