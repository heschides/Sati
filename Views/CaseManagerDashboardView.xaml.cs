using Sati.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace Sati.Views
{
    public partial class CaseManagerDashboardView : UserControl
    {
        public CaseManagerDashboardView()
        {
            InitializeComponent();
        }

        private void DataGrid_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (DataContext is CaseManagerDashboardViewModel vm && vm.SelectedNote is not null)
                vm.EnterEditMode();
        }
    }
}