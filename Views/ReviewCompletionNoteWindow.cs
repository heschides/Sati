using System.Windows;
using System.Windows.Controls;
using Sati.ViewModels.Children;

namespace Sati.Views;

public sealed class ReviewCompletionNoteWindow : Window
{
    public ReviewCompletionNoteWindow(NoteEntryViewModel editor)
    {
        Title = "Submit review completion note";
        Width = 780;
        Height = 900;
        MinWidth = 540;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
        var layout = new DockPanel { Margin = new Thickness(16) };
        var cancel = new Button { Content = "Cancel completion", IsCancel = true,
            Padding = new Thickness(12, 6, 12, 6), HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(cancel, Dock.Bottom);
        layout.Children.Add(cancel);
        layout.Children.Add(new NoteEntryView { DataContext = editor });
        Content = layout;
        var saved = false;
        EventHandler completed = (_, _) =>
        {
            saved = true;
            if (!string.IsNullOrWhiteSpace(editor.SubmissionFailureMessage))
                MessageBox.Show(this, editor.SubmissionFailureMessage, "Review note saved",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            DialogResult = true;
        };
        editor.NoteSaved += completed;
        Closing += (_, e) =>
        {
            if (!saved && (editor.SubmitNoteCommand.IsRunning || editor.SendToSupervisorCommand.IsRunning ||
                editor.HoldForComplianceCommand.IsRunning))
                e.Cancel = true;
            else if (!saved && !editor.TryReleaseDraft()) e.Cancel = true;
        };
        Closed += (_, _) => editor.NoteSaved -= completed;
    }
}
