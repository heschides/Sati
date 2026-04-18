using Sati.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Sati.Views
{
    public partial class ClientsView : UserControl
    {
        public ClientsView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is NewClientViewModel vm)
            {
                vm.ComplianceReviewRequested += (forms) =>
                {
                    var reviewVm = new ComplianceReviewViewModel
                    {
                        ClientName = $"{vm.FirstName} {vm.LastName}",
                        Forms = forms
                    };

                    var dialog = new ComplianceReviewWindow(reviewVm)
                    {
                        Owner = Application.Current.MainWindow
                    };
                    return dialog.ShowDialog() == true;
                };
            }
        }

        private void DataGrid_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (DataContext is NewClientViewModel vm && vm.SelectedPerson is Person person)
            {
                vm.LoadPersonForEdit(person);
                vm.IsEditMode = true;
            }
        }
    }
}