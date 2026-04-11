using Sati.Models;
using Sati.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace Sati.Views
{
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
            Loaded += async (s, e) => await vm.InitializeAsync();
        }

        private void PasswordInit_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is NewUserViewModel vm && sender is PasswordBox box)
                vm.PasswordInit = box.SecurePassword;
        }

        private void PasswordConfirm_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is NewUserViewModel vm && sender is PasswordBox box)
                vm.PasswordConfirm = box.SecurePassword;
        }
    }
}