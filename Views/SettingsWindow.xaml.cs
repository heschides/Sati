using Sati.ViewModels;
using System.Windows;

namespace Sati.Views
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml. This is the only window behind the
    /// shell's greeting badge: the signed-in user's own profile and password, then
    /// the agency and application settings.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly SettingsViewModel _viewModel;

        public SettingsWindow(SettingsViewModel vm)
        {
            InitializeComponent();
            _viewModel = vm;
            DataContext = vm;

            _viewModel.Account.PasswordChanged += OnPasswordChanged;

            // Switching accounts replaces the session user and rebuilds every view
            // model this window is bound to, so the window closes before the shell
            // starts the flow. The shell reads DialogResult to tell a switch apart
            // from an ordinary close.
            _viewModel.Account.SwitchUserRequested += OnSwitchUserRequested;

            Closed += (_, _) =>
            {
                _viewModel.Account.PasswordChanged -= OnPasswordChanged;
                _viewModel.Account.SwitchUserRequested -= OnSwitchUserRequested;
            };
        }

        /// <summary>Raised after the window has closed because the user asked to switch accounts.</summary>
        public event EventHandler? SwitchUserRequested;

        private void OnSwitchUserRequested(object? sender, EventArgs e)
        {
            // Whatever is unsaved on the account tabs is discarded by the switch, so
            // the view model decides whether that is worth stopping for.
            if (_viewModel.Account.UnsavedWorkWarning() is { } warning)
            {
                var confirmation = new ConfirmationDialog(
                    "Switch user?", warning, "Switch User", isDestructive: true) { Owner = this };
                if (confirmation.ShowDialog() != true)
                    return;
            }

            Close();
            SwitchUserRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnPasswordChanged(object? sender, EventArgs e)
        {
            CurrentPasswordBox.Clear();
            NewPasswordBox.Clear();
            ConfirmPasswordBox.Clear();
        }

        // PasswordBox.Password isn't a DependencyProperty (WPF won't let the
        // plaintext sit in a binding), so each box hands its SecurePassword — a
        // SecureString WPF maintains for us — to the view model on change.
        private void CurrentPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) =>
            _viewModel.Account.CurrentPassword = CurrentPasswordBox.SecurePassword;

        private void NewPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) =>
            _viewModel.Account.NewPassword = NewPasswordBox.SecurePassword;

        private void ConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) =>
            _viewModel.Account.ConfirmPassword = ConfirmPasswordBox.SecurePassword;
    }
}
