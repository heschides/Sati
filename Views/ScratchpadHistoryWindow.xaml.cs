using Sati.ViewModels;
using System.Windows;


namespace Sati
{
    public partial class ScratchpadHistoryWindow : Window
    {
        private readonly ScratchpadHistoryViewModel _viewModel;

        public ScratchpadHistoryWindow(ScratchpadHistoryViewModel vm)
        {
            InitializeComponent();
            _viewModel = vm;
            DataContext = vm;
        }

        public async Task InitializeAsync()
        {
            await _viewModel.InitializeAsync();
        }
    }
}