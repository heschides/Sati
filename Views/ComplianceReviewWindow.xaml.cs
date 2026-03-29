using Sati.ViewModels;
using System.Windows;

namespace Sati.Views
{
    public partial class ComplianceReviewWindow : Window
    {
        public ComplianceReviewWindow(ComplianceReviewViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
