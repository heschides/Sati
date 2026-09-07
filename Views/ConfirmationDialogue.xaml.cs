using System.Windows;

namespace Sati.Views
{
    // Reusable yes/no confirmation. Returns true ONLY on an explicit click of the
    // action button. Three deliberate safety choices: Cancel is IsCancel, so Esc and
    // the Cancel click both return false; neither button is IsDefault, so pressing
    // Enter does nothing — a destructive action can't be confirmed by reflex; and
    // FocusManager parks initial focus on Cancel. Construct, set Owner, ShowDialog().
    public partial class ConfirmationDialog : Window
    {
        public ConfirmationDialog(string title, string message, string confirmText, bool isDestructive = false)
        {
            InitializeComponent();
            HeadingText.Text = title;
            MessageText.Text = message;
            ConfirmButton.Content = confirmText;

            // One style, two moods: destructive red or the theme's button accent. Set
            // in code because the dialog is built in code anyway, but taken from the
            // theme rather than literal colours — the literals were a light-theme red
            // and amber, and their light ink turned invisible on a dark theme. Fill and
            // ink are always taken as a pair, and as resource references so a theme
            // change while the dialog is open re-resolves both.
            var fill = isDestructive ? "DangerStrongBrush" : "AccentButtonBrush";
            var ink = isDestructive ? "OnDangerStrongBrush" : "OnAccentButtonBrush";
            ConfirmButton.SetResourceReference(BackgroundProperty, fill);
            ConfirmButton.SetResourceReference(ForegroundProperty, ink);
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
