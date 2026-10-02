using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
        viewModel = e.NewValue as SafetyDeviceViewModel;
        if (viewModel is not null)
        {
            viewModel.PdfReady += SavePdf;
            viewModel.Problem += ShowProblem;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(SafetyDeviceViewModel.StepTitle) || !IsVisible) return;
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (!IsVisible) return;
            AnswerScroll.ScrollToTop();
            StepHeading.Focus();
        }, DispatcherPriority.Loaded);
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
