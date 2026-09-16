using Sati.ViewModels.ClientDocuments;
using System.Windows;
using System.Windows.Controls;

namespace Sati.Views.ClientDocuments;

public partial class HousingSupportFundsWorkspace : UserControl
{
    private HousingSupportFundsViewModel? viewModel;

    public HousingSupportFundsWorkspace() => InitializeComponent();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (viewModel is not null)
        {
            viewModel.PdfReady -= SavePdf;
            viewModel.Problem -= ShowProblem;
        }
        viewModel = e.NewValue as HousingSupportFundsViewModel;
        if (viewModel is not null)
        {
            viewModel.PdfReady += SavePdf;
            viewModel.Problem += ShowProblem;
        }
    }

    private async void SavePdf(object? sender, HousingSupportFundsPdfReadyEventArgs args) =>
        await PdfFileSaver.SaveAsync(
            "Save Housing Support Funds application draft",
            args.SuggestedFileName,
            args.Content,
            "The editable application draft was saved. Review pages 1-2, attach the required housing proof, and obtain the required consumer or guardian signature. Leave page 3 for DHHS staff.");

    private void ShowProblem(object? sender, HousingSupportFundsProblemEventArgs args) =>
        MessageBox.Show(Window.GetWindow(this), args.Message, args.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
}
