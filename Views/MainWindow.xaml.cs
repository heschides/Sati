using Sati.Models;
using Sati.ViewModels;
using Sati.Views;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Sati
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly Func<NewClientWindow> _newClientWindow;
        private readonly Func<SettingsWindow> _newSettingsWindow;
        private SchedulerViewModel _schedulerVm;
        private readonly Func<ScratchpadHistoryWindow> _scratchpadHistoryWindowFactory = null!;
        private readonly Func<NotesWindow> _notesWindowFactory;

        private bool _isSavingOnClose = false;


        public MainWindow(MainWindowViewModel vm, Func<NewClientWindow> newClientWindowFactory, Func<SettingsWindow> newSettingsWindowFactory, SchedulerViewModel schedulerVm, Func<ScratchpadHistoryWindow> scratchpadHistoryWindowFactory, Func<NotesWindow> notesWindowFactory)
        {
            InitializeComponent();
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            var screenWidth = SystemParameters.PrimaryScreenWidth;

            DataContext = vm;

            vm.MarkFormCompleteRequested += (s, formType) =>
            {
                var result = MessageBox.Show(
                    $"Would you like to mark the {formType} requirement complete?",
                    "Mark Form Complete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                    _ = vm.MarkFormCompleteAsync(formType);
            };

            _newClientWindow = newClientWindowFactory;

            vm.OpenClientsWindowRequested += (s, success) =>
            {
                var win = _newClientWindow();
                var result = win.ShowDialog();

                _ = vm.LoadPeopleAsync();
            };


            vm.PromptSchedulerRequested += (s, e) =>
            {
                var prompt = new PromptWindow(vm.LoggedInUser?.DisplayName ?? "there");
                var result = prompt.ShowDialog();
                if (result == true)
                    vm.IsSchedulerOpen = true;
            };

            _notesWindowFactory = notesWindowFactory;

            vm.OpenNotesWindowRequested += (s, _) =>
            {
                var win = _notesWindowFactory!();
                win.Owner = this;
                win.Show();
            };

            SchedulerPopup.Closed += async (s, e) =>
            {
                if (DataContext is MainWindowViewModel vm)
                    await vm.RefreshIncentiveAsync();
            };

            _scratchpadHistoryWindowFactory = scratchpadHistoryWindowFactory;
            _newSettingsWindow = newSettingsWindowFactory;
            vm.OpenSettingsWindowRequested += (s, success) =>
            {
                var win = _newSettingsWindow();
                var result = win.ShowDialog();
            };

            Closing += async (s, e) =>
            {
                if (_isSavingOnClose) return;
                e.Cancel = true;
                _isSavingOnClose = true;

                if (DataContext is MainWindowViewModel vm)
                {
                    var content = vm.ScratchpadContent;
                    await vm.SaveScratchpadAsync(content);
                }

                Close();
            };

            _schedulerVm = schedulerVm;
            SchedulerPopup.DataContext = schedulerVm;
            SchedulerPopup.Opened += (s, e) =>
            {
                Debug.WriteLine("Popup opened");
                _schedulerVm.Initialize();
            };

            vm.OpenScratchpadHistoryRequested += (s, e) =>
            {
                var win = _scratchpadHistoryWindowFactory!();
                win.Owner = this;
                win.ShowDialog();
            };

            Mouse.AddPreviewMouseDownOutsideCapturedElementHandler(SchedulerPopup, (s, e) =>
            {
                if (DataContext is MainWindowViewModel vm)
                    vm.IsSchedulerOpen = false;
            });

            Closed += (s, e) => Application.Current.Shutdown();

        }

        private void TodaysWorkBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter &&
                (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                var timestamp = DateTime.Now.ToString("h:mm tt");
                var divider = $"\n\n ─── {timestamp} ───────────────────\n\n";

                var box = (TextBox)sender;
                var caretIndex = box.CaretIndex;
                box.Text = box.Text.Insert(caretIndex, divider);
                box.CaretIndex = caretIndex + divider.Length;

                e.Handled = true;  // prevents the Enter from also adding a newline
            }
        }
        private void DataGrid_MouseDoubleClick(object? sender, MouseEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && vm.SelectedNote is not null)
            {
                vm.EnterEditMode();
            }
        }
  
    }
}
