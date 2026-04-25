using Sati.Data;
using Sati.ViewModels;
using System.Windows;

namespace Sati.Views
{
    public partial class ShellWindow : Window
    {
        private readonly ShellViewModel _shellViewModel;
        private readonly CaseManagerDashboardViewModel _caseManagerDashboardViewModel;
        private readonly Func<SwitchUserWindow> _switchUserWindowFactory;
        private readonly ISessionService _sessionService;
        private bool _isSavingOnClose = false;

        public ShellWindow(ShellViewModel shellViewModel,
            CaseManagerDashboardViewModel caseManagerDashboardViewModel,
            ISessionService sessionService,
            Func<SettingsWindow> settingsWindowFactory,
            Func<ScratchpadHistoryWindow> scratchpadHistoryWindowFactory,
            Func<SwitchUserWindow> switchUserWindowFactory)
        {
            InitializeComponent();
            _shellViewModel = shellViewModel;
            _caseManagerDashboardViewModel = caseManagerDashboardViewModel;
            _sessionService = sessionService;
            _switchUserWindowFactory = switchUserWindowFactory;
            DataContext = shellViewModel;

            _shellViewModel.OpenSettingsWindowRequested += (s, e) =>
            {
                var win = settingsWindowFactory();
                win.Owner = this;
                win.ShowDialog();
            };

            //_caseManagerDashboardViewModel.OpenClientsWindowRequested += async (s, e) =>
            //{
            //    var win = newClientWindowFactory();
            //    win.Owner = this;
            //    win.ShowDialog();
            //    await _caseManagerDashboardViewModel.LoadPeopleAsync();
            //};

            //_caseManagerDashboardViewModel.OpenNotesWindowRequested += (s, e) =>
            //{
            //    var win = notesWindowFactory();
            //    win.Owner = this;
            //    win.Show();
            //};

            _caseManagerDashboardViewModel.MarkFormCompleteRequested += (s, formType) =>
            {
                var result = MessageBox.Show(
                    $"Did you complete the {formType} today?",
                    "Form Status",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                // Yes: form is done — mark compliant, set CompletedDate.
                // No: form was worked on but not finished — set OpenedDate so
                //     the matrix can show the open-form indicator.
                // Anything else (X-button → MessageBoxResult.None): no change.
                if (result == MessageBoxResult.Yes)
                    _ = _caseManagerDashboardViewModel.MarkFormCompleteAsync(formType);
                else if (result == MessageBoxResult.No)
                    _ = _caseManagerDashboardViewModel.OpenFormAsync(formType);
            };

            _caseManagerDashboardViewModel.PromptSchedulerRequested += (s, e) =>
            {
                var prompt = new PromptWindow(_caseManagerDashboardViewModel.LoggedInUser?.DisplayName ?? "there");
                var result = prompt.ShowDialog();
                if (result == true)
                    _caseManagerDashboardViewModel.IsSchedulerOpen = true;
            };

            shellViewModel.Scratchpad.OpenScratchpadHistoryRequested += async (s, e) =>
            {
                var win = scratchpadHistoryWindowFactory();
                win.Owner = this;
                await win.InitializeAsync();
                win.Show();
            };

            shellViewModel.SwitchUserRequested += async (s, e) =>
            {
                // Save scratchpad before switching users
                var content = _shellViewModel.Scratchpad.ScratchpadContent;
                await _shellViewModel.Scratchpad.SaveScratchpadAsync(content);

                var win = _switchUserWindowFactory();
                win.Owner = this;
                bool? result = win.ShowDialog();

                if (result == true && win.NewUser is not null)
                {
                    _sessionService.SetUser(win.NewUser);
                    await _shellViewModel.ReinitializeAsync();
                }
            };

            Closing += async (s, e) =>
            {
                if (!IsVisible) return;
                if (_isSavingOnClose) return;
                e.Cancel = true;
                _isSavingOnClose = true;

                var content = _shellViewModel.Scratchpad.ScratchpadContent;
                await _shellViewModel.Scratchpad.SaveScratchpadAsync(content);

                Close();
            };

            Closed += (s, e) => Application.Current.Shutdown();
        }
    }
}