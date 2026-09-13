using Sati.ViewModels.ClientDocuments;
using System.Windows;
using System.Windows.Controls;

namespace Sati.Views.ClientDocuments;

public partial class CwicPacketWorkspace : UserControl
{
    private CwicPacketViewModel? viewModel;

    public CwicPacketWorkspace() => InitializeComponent();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (viewModel is not null)
        {
            viewModel.PdfReady -= SavePdf;
            viewModel.Problem -= ShowProblem;
        }
        viewModel = e.NewValue as CwicPacketViewModel;
        if (viewModel is not null)
        {
            viewModel.PdfReady += SavePdf;
            viewModel.Problem += ShowProblem;
        }
    }

    private async void SavePdf(object? sender, CwicPacketPdfReadyEventArgs args) =>
        await PdfFileSaver.SaveAsync(
            "Save CWIC referral packet draft",
            args.SuggestedFileName,
            args.Content,
            "The CWIC packet draft was saved. Review every page and obtain all required signatures before sending it through an agency-approved protected channel.");

    private void ShowProblem(object? sender, CwicPacketProblemEventArgs args) =>
        MessageBox.Show(Window.GetWindow(this), args.Message, args.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
}
