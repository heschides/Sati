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
using System.Windows.Shapes;

namespace Sati.Views
{
    /// <summary>
    /// Interaction logic for UserMessageDialog.xaml
    /// </summary>
    public partial class UserMessageDialog : Window
    {
        public string ErrorMessage { get; }

        public UserMessageDialog(string errorMessage)
        {
            InitializeComponent();
            ErrorMessage = errorMessage;
            DataContext = this;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
