using Sati.Models;
using Sati.ViewModels;
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
    /// Interaction logic for NewUserWindow.xaml
    /// </summary>
    public partial class NewUserWindow : Window
    {    
        public User? CreatedUser => (DataContext as NewUserViewModel)?.CreatedUser;

        public NewUserWindow(NewUserViewModel vm)

        {
            InitializeComponent();
            DataContext = vm;
            vm.CloseWindowRequested += (s, success) =>
            {
                DialogResult = success;
                Close();
            };
        }     

        private void PasswordInit_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is NewUserViewModel vm && sender is PasswordBox box)
            {
                vm.PasswordInit = box.SecurePassword;
            }
        }

        private void PasswordConfirm_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is NewUserViewModel vm && sender is PasswordBox box)
            {
                vm.PasswordConfirm = box.SecurePassword;
            }
        }
    }
}
