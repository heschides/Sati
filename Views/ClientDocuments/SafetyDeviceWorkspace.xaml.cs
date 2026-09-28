using System.Windows;
using System.Windows.Controls;
using Sati.ViewModels.ClientDocuments;

namespace Sati.Views.ClientDocuments;

public partial class SafetyDeviceWorkspace : UserControl
{
    private SafetyDeviceViewModel? viewModel;

    public SafetyDeviceWorkspace() => InitializeComponent();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (viewModel is not null)
        {
            viewModel.PdfReady -= SavePdf;
            viewModel.Problem -= ShowProblem;
        }
        viewModel = e.NewValue as SafetyDeviceViewModel;
        if (viewModel is not null)
        {
            viewModel.PdfReady += SavePdf;
            viewModel.Problem += ShowProblem;
        }
    }

    private async void SavePdf(object? sender, SafetyDevicePdfReadyEventArgs args)
    {
        if (!IsVisible) return;
        await PdfFileSaver.SaveAsync(
            "Save Safety Device Request draft", args.SuggestedFileName, args.Content,
            "The editable OADS draft was saved. Review all six pages, obtain the medical provider and member or guardian signatures, and complete the planning team and HCBS steps before submission.");
    }

    private void ShowProblem(object? sender, SafetyDeviceProblemEventArgs args)
    {
        if (!IsVisible) return;
        MessageBox.Show(Window.GetWindow(this), args.Message, args.Title,
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
