using System.Windows.Controls;
using System.Windows;
using Sati.ViewModels;

namespace Sati.Views;

public partial class FormAttestationControl : UserControl
{
    private FormAttestationViewModel? _viewModel;

    public FormAttestationControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => Attach(null);
        Loaded += (_, _) => Attach(DataContext as FormAttestationViewModel);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        Attach(e.NewValue as FormAttestationViewModel);

    private void Attach(FormAttestationViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
            return;
        if (_viewModel is not null)
            _viewModel.AttestationCompleted -= OnAttestationCompleted;
        _viewModel = viewModel;
        if (_viewModel is not null)
            _viewModel.AttestationCompleted += OnAttestationCompleted;
    }

    private void OnAttestationCompleted(object? sender, FormAttestationCompletedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var message = $"{e.ContextLabel} was recorded as complete on {e.CompletedOn:MMM d, yyyy}.";
        if (owner is null)
            MessageBox.Show(message, "Attestation recorded", MessageBoxButton.OK,
                MessageBoxImage.Information);
        else
            MessageBox.Show(owner, message, "Attestation recorded", MessageBoxButton.OK,
                MessageBoxImage.Information);
    }
}
