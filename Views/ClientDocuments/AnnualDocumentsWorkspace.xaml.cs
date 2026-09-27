using System.IO;
using System.Security.Cryptography;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using Sati.Contracts.V1;
using Sati.ViewModels;
using Sati.ViewModels.ClientDocuments;

namespace Sati.Views.ClientDocuments;
public partial class AnnualDocumentsWorkspace : UserControl
{
    private AgencyReleaseViewModel? _releaseEditor;
    private bool _showingReleaseEditor;

    public AnnualDocumentsWorkspace() => InitializeComponent();
    private void OnContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_releaseEditor is not null)
            _releaseEditor.PropertyChanged -= OnReleaseEditorPropertyChanged;
        if (e.OldValue is NewClientViewModel { AnnualDocuments: { } old })
        {
            old.FileReady -= Save;
            old.ChooseVerificationFileAsync = null;
        }
        if (e.NewValue is NewClientViewModel { AnnualDocuments: { } current })
        {
            current.FileReady += Save;
            current.ChooseVerificationFileAsync = Choose;
        }
        _releaseEditor = (e.NewValue as NewClientViewModel)?.AgencyRelease;
        if (_releaseEditor is not null)
            _releaseEditor.PropertyChanged += OnReleaseEditorPropertyChanged;
        ShowReleasePanelImmediately(_releaseEditor?.IsEditorOpen == true);
    }

    private void OnReleaseEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AgencyReleaseViewModel.IsEditorOpen) ||
            sender is not AgencyReleaseViewModel editor)
            return;
        Dispatcher.BeginInvoke(() => AnimateReleasePanel(editor.IsEditorOpen));
    }

    private void ShowReleasePanelImmediately(bool showEditor)
    {
        _showingReleaseEditor = showEditor;
        ReleaseOverviewTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
        ReleaseEditorTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
        ReleaseOverviewTransform.X = 0;
        ReleaseEditorTransform.X = 0;
        ReleaseOverviewPanel.Visibility = showEditor ? Visibility.Collapsed : Visibility.Visible;
        ReleaseEditorPanel.Visibility = showEditor ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AnimateReleasePanel(bool showEditor)
    {
        if (_showingReleaseEditor == showEditor &&
            (showEditor ? ReleaseEditorPanel.Visibility : ReleaseOverviewPanel.Visibility) == Visibility.Visible)
            return;
        if (!SystemParameters.ClientAreaAnimation)
        {
            ShowReleasePanelImmediately(showEditor);
            FocusReleasePanel(showEditor);
            return;
        }

        _showingReleaseEditor = showEditor;
        var width = Math.Max(ReleaseTransitionHost.ActualWidth, 640);
        ReleaseOverviewPanel.Visibility = Visibility.Visible;
        ReleaseEditorPanel.Visibility = Visibility.Visible;
        ReleaseOverviewTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
        ReleaseEditorTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);

        var duration = new Duration(TimeSpan.FromMilliseconds(280));
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
        var overview = new DoubleAnimation(
            showEditor ? 0 : width,
            showEditor ? -width : 0,
            duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
        var editor = new DoubleAnimation(
            showEditor ? width : 0,
            showEditor ? 0 : -width,
            duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
        editor.Completed += (_, _) =>
        {
            ReleaseOverviewTransform.X = 0;
            ReleaseEditorTransform.X = 0;
            ReleaseOverviewPanel.Visibility = showEditor ? Visibility.Collapsed : Visibility.Visible;
            ReleaseEditorPanel.Visibility = showEditor ? Visibility.Visible : Visibility.Collapsed;
            FocusReleasePanel(showEditor);
        };
        ReleaseOverviewTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, overview);
        ReleaseEditorTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, editor);
    }

    private void FocusReleasePanel(bool showEditor) =>
        (showEditor ? ReleaseEditorPanel : ReleaseOverviewPanel)
        .MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    private async void Save(AgencyReleaseResult result)
    {
        if (!result.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        { await PdfFileSaver.SaveAsync("Save privacy notice", result.FileName, result.Pdf, "Privacy notice saved."); return; }
        var dialog = new SaveFileDialog { Title = "Save annual forms package", FileName = result.FileName, DefaultExt = ".zip",
            Filter = "ZIP archives (*.zip)|*.zip", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog() != true) return;
        try { await File.WriteAllBytesAsync(dialog.FileName, result.Pdf); }
        catch (Exception) { MessageBox.Show("The packet could not be saved. Choose a writable location and try again."); }
    }
    private async Task<(string Hash, long Length)?> Choose()
    {
        var dialog = new OpenFileDialog { Title = "Verify a saved document", Filter = "PDF documents (*.pdf)|*.pdf|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return null;
        try
        {
            await using var stream = File.OpenRead(dialog.FileName);
            var size = stream.Length; var hash = await SHA256.HashDataAsync(stream);
            return (Convert.ToHexString(hash), size);
        }
        catch (Exception) { MessageBox.Show("The file could not be read."); return null; }
    }
}
