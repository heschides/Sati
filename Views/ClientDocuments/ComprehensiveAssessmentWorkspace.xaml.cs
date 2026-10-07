using System.Windows.Controls;
using System.Windows;
using Sati.Models;
using Sati.ViewModels;
using Sati.ViewModels.ClientDocuments;
using System.ComponentModel;

namespace Sati.Views.ClientDocuments
{
    public partial class ComprehensiveAssessmentWorkspace : UserControl
    {
        private NewClientViewModel? _parent;
        public static readonly DependencyProperty WorkspaceProperty = DependencyProperty.Register(
            nameof(Workspace), typeof(ComprehensiveAssessmentViewModel), typeof(ComprehensiveAssessmentWorkspace),
            new PropertyMetadata(null, OnWorkspaceChanged));
        public ComprehensiveAssessmentViewModel? Workspace
        {
            get => (ComprehensiveAssessmentViewModel?)GetValue(WorkspaceProperty);
            set => SetValue(WorkspaceProperty, value);
        }

        public ComprehensiveAssessmentWorkspace()
        {
            InitializeComponent();
            DataContextChanged += OnParentDataContextChanged;
            Loaded += (_, _) =>
            {
                AttachParent(DataContext as NewClientViewModel);
                if (Workspace is { } workspace)
                { AttachWorkspace(workspace); _ = workspace.LoadPersonAsync(EligiblePerson); }
            };
            Unloaded += async (_, _) =>
            {
                AttachParent(null);
                if (Workspace is { } workspace)
                { DetachWorkspace(workspace); await workspace.LoadPersonAsync(null); }
            };
        }

        private static void OnWorkspaceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (ComprehensiveAssessmentWorkspace)d;
            if (e.OldValue is ComprehensiveAssessmentViewModel old) view.DetachWorkspace(old);
            if (e.NewValue is ComprehensiveAssessmentViewModel current && view.IsLoaded)
            { view.AttachWorkspace(current); _ = current.LoadPersonAsync(view.EligiblePerson); }
        }
        private void AttachWorkspace(ComprehensiveAssessmentViewModel workspace)
        {
            DetachWorkspace(workspace);
            workspace.PdfReady += SavePdfAsync; workspace.PropertyChanged += OnWorkspacePropertyChanged;
        }
        private void DetachWorkspace(ComprehensiveAssessmentViewModel workspace)
        { workspace.PdfReady -= SavePdfAsync; workspace.PropertyChanged -= OnWorkspacePropertyChanged; }
        private static Task SavePdfAsync(Sati.Contracts.V1.AssessmentPdfDto pdf) =>
            PdfFileSaver.SaveAsync("Save assessment version", pdf.FileName, pdf.Content,
                $"Saved assessment submission {pdf.SubmissionId}. Signing and external acceptance are separate.");
        private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ComprehensiveAssessmentViewModel.ValidationFocusRequest))
            { ValidationSummary.Focus(); if (ValidationSummary.Items.Count > 0) ValidationSummary.SelectedIndex = 0; }
        }
        private void AttachParent(NewClientViewModel? parent)
        {
            if (_parent is not null) _parent.PropertyChanged -= OnParentPropertyChanged;
            _parent = parent;
            if (_parent is not null) _parent.PropertyChanged += OnParentPropertyChanged;
        }

        /// <summary>
        /// The tab is only hidden when the agency has the workflow off; the control still
        /// exists and still hears every selection. The server refuses the draft in that case,
        /// so there is nothing to load.
        /// </summary>
        private Person? EligiblePerson =>
            _parent is { IsComprehensiveAssessmentAuthoringEnabled: true } ? _parent.SelectedPerson : null;

        private void OnParentDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            AttachParent(e.NewValue as NewClientViewModel);
            if (IsLoaded && Workspace is { } workspace) _ = workspace.LoadPersonAsync(EligiblePerson);
        }

        private void OnParentPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(NewClientViewModel.SelectedPerson)
                or nameof(NewClientViewModel.IsComprehensiveAssessmentAuthoringEnabled))
                if (Workspace is { } workspace) _ = workspace.LoadPersonAsync(EligiblePerson);
        }
    }
}
