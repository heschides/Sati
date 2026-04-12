using Sati.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace Sati.Views
{
    public partial class NotesView : UserControl
    {
        public NotesView()
        {
            InitializeComponent();
        }

        private void DataGrid_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && vm.SelectedNote is not null)
                vm.EnterEditMode();
        }
    }
}