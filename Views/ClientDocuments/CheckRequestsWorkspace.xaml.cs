using System.Windows;
using System.Windows.Controls;
using Sati.ViewModels.ClientDocuments;

namespace Sati.Views.ClientDocuments;

public partial class CheckRequestsWorkspace : UserControl
{
    public CheckRequestsWorkspace() => InitializeComponent();

    private void OnContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is CheckRequestsViewModel old) old.PdfReady -= Save;
        if (e.NewValue is CheckRequestsViewModel current) current.PdfReady += Save;
    }

    private async void Save(CheckRequestPdfReadyEventArgs result) =>
        await PdfFileSaver.SaveAsync("Save check request", result.SuggestedFileName, result.Content, "Check request PDF saved.");
}
