using Sati.ViewModels.ClientDocuments;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Sati.Views.ClientDocuments;

public partial class BenefitsApplicationWorkspace : UserControl
{
    private BenefitsApplicationViewModel? viewModel;

    public BenefitsApplicationWorkspace() => InitializeComponent();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (viewModel is not null)
        {
            viewModel.PdfReady -= SavePdf;
            viewModel.Problem -= ShowProblem;
            viewModel.PropertyChanged -= ViewModelPropertyChanged;
        }
        viewModel = e.NewValue as BenefitsApplicationViewModel;
        if (viewModel is not null)
        {
            viewModel.PdfReady += SavePdf;
            viewModel.Problem += ShowProblem;
            viewModel.PropertyChanged += ViewModelPropertyChanged;
        }
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(BenefitsApplicationViewModel.CurrentFields))
            AnswerScroll.ScrollToTop();
    }

    private void SensitiveLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is PasswordBox box && box.DataContext is BenefitsAnswerViewModel field)
            box.Password = field.Value;
    }

    private void SensitiveChanged(object sender, RoutedEventArgs args)
    {
        if (sender is PasswordBox box && box.DataContext is BenefitsAnswerViewModel field)
            field.Value = box.Password;
    }

    private async void SavePdf(object? sender, BenefitsApplicationPdfReadyEventArgs args) =>
        await PdfFileSaver.SaveAsync("Save OFI application draft", args.FileName, args.Pdf,
            "The 20-page application draft was saved. Review every page, complete unanswered questions and signatures, and submit it through an agency-approved protected channel.");

    private void ShowProblem(object? sender, BenefitsApplicationProblemEventArgs args) =>
        MessageBox.Show(Window.GetWindow(this), args.Message, args.Title,
            MessageBoxButton.OK, MessageBoxImage.Warning);
}
