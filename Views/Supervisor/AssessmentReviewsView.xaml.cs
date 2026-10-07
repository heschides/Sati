using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Sati.Contracts.V1;
using Sati.ViewModels.Supervisor;

namespace Sati.Views.Supervisor;

public partial class AssessmentReviewsView : UserControl
{
    private AssessmentReviewsViewModel? _workspace;
    public AssessmentReviewsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BindWorkspace();
        Loaded += (_, _) => BindWorkspace();
        Unloaded += (_, _) => Detach();
    }
    private void BindWorkspace()
    {
        Detach(); _workspace = DataContext as AssessmentReviewsViewModel;
        if (_workspace is null) return;
        _workspace.PdfReady += SavePdfAsync; _workspace.PropertyChanged += OnPropertyChanged;
    }
    private void Detach()
    { if (_workspace is null) return; _workspace.PdfReady -= SavePdfAsync; _workspace.PropertyChanged -= OnPropertyChanged; _workspace = null; }
    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(AssessmentReviewsViewModel.ValidationFocusRequest)) { ValidationSummary.Focus(); if (ValidationSummary.Items.Count > 0) ValidationSummary.SelectedIndex = 0; } }
    private static Task SavePdfAsync(AssessmentPdfDto pdf) => PdfFileSaver.SaveAsync("Save assessment version", pdf.FileName, pdf.Content,
        $"Saved submission {pdf.SubmissionId}. Approval, signing and external acceptance are separate.");
}
