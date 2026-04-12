using Sati.ViewModels;
using System.Windows;

namespace Sati.Views
{
    public partial class ShellWindow : Window
    {
        private readonly ShellViewModel _viewModel;
        private bool _isSavingOnClose = false;

        public ShellWindow(ShellViewModel viewModel,
            Func<SettingsWindow> settingsWindowFactory,
            Func<ScratchpadHistoryWindow> scratchpadHistoryWindowFactory,
            Func<NotesWindow> notesWindowFactory,
            Func<NewClientWindow> newClientWindowFactory)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;

            var notesVm = viewModel.NotesViewModel;

            notesVm.OpenSettingsWindowRequested += (s, e) =>
            {
                var win = settingsWindowFactory();
                win.Owner = this;
                win.ShowDialog();
            };

            notesVm.OpenClientsWindowRequested += async (s, e) =>
            {
                var win = newClientWindowFactory();
                win.Owner = this;
                win.ShowDialog();
                await notesVm.LoadPeopleAsync();
            };

            notesVm.OpenNotesWindowRequested += (s, e) =>
            {
                var win = notesWindowFactory();
                win.Owner = this;
                win.Show();
            };

            notesVm.MarkFormCompleteRequested += (s, formType) =>
            {
                var result = MessageBox.Show(
                    $"Would you like to mark the {formType} requirement complete?",
                    "Mark Form Complete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                    _ = notesVm.MarkFormCompleteAsync(formType);
            };

            notesVm.PromptSchedulerRequested += (s, e) =>
            {
                var prompt = new PromptWindow(notesVm.LoggedInUser?.DisplayName ?? "there");
                var result = prompt.ShowDialog();
                if (result == true)
                    notesVm.IsSchedulerOpen = true;
            };

            viewModel.Scratchpad.OpenScratchpadHistoryRequested += async (s, e) =>
            {
                var win = scratchpadHistoryWindowFactory();
                win.Owner = this;
                await win.InitializeAsync();
                win.ShowDialog();
            };

            Closing += async (s, e) =>
            {
                if (!IsVisible) return;
                if (_isSavingOnClose) return;
                e.Cancel = true;
                _isSavingOnClose = true;

                var content = _viewModel.Scratchpad.ScratchpadContent;
                await _viewModel.Scratchpad.SaveScratchpadAsync(content);

                Close();
            };

            Closed += (s, e) => Application.Current.Shutdown();
        }
    }
}