using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Sati.Models;
using Sati.ViewModels;
using Sati.ViewModels.ClientDocuments;
using System.ComponentModel;

namespace Sati.Views.ClientDocuments
{
    public partial class ComprehensiveAssessmentWorkspace : UserControl
    {
        private NewClientViewModel? _parent;
        public ComprehensiveAssessmentViewModel Workspace { get; }

        public ComprehensiveAssessmentWorkspace()
        {
            var app = (App)System.Windows.Application.Current;
            Workspace = new ComprehensiveAssessmentViewModel(
                app.Services.GetRequiredService<Data.IComprehensiveAssessmentService>(),
                app.Services.GetRequiredService<Data.ISessionService>(),
                app.Services.GetRequiredService<Data.IConsumerProviderService>(),
                app.Services.GetRequiredService<Data.IProviderService>());
            InitializeComponent();
            DataContextChanged += OnParentDataContextChanged;
            Unloaded += async (_, _) => await Workspace.LoadPersonAsync(null);
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
            if (_parent is not null) _parent.PropertyChanged -= OnParentPropertyChanged;
            _parent = e.NewValue as NewClientViewModel;
            if (_parent is not null) _parent.PropertyChanged += OnParentPropertyChanged;
            _ = Workspace.LoadPersonAsync(EligiblePerson);
        }

        private void OnParentPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(NewClientViewModel.SelectedPerson)
                or nameof(NewClientViewModel.IsComprehensiveAssessmentAuthoringEnabled))
                _ = Workspace.LoadPersonAsync(EligiblePerson);
        }
    }
}
