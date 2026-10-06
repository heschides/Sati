using System.Windows;
using System.Windows.Controls;
using Sati.Contracts.V1;
using Sati.ViewModels.Children;

namespace Sati.Views;

public partial class FormProgressWindow : Window
{
    private readonly FormProgressConfirmationEventArgs _request;
    private readonly List<AnnualPcpProgressAction> _actions = [AnnualPcpProgressAction.None];

    public FormProgressWindow(FormProgressConfirmationEventArgs request)
    {
        _request = request;
        InitializeComponent();
        DocumentLabel.Text = request.FormType == "PCP" ? "Person-Centered Plan" : "Comprehensive Assessment";
        var state = request.CompletedOn is DateTime completed
            ? $"Completed {completed:d}"
            : request.OpenedOn is DateTime opened ? $"Open — opened {opened:d}" : "Not opened";
        StateLabel.Text = $"Plan effective {request.TargetEffectiveDate:d}\nComplete by {request.DueOn:d}\nCurrent state: {state}";
        if (request.CompletedOn is not null && request.OpenedOn is DateTime recordedOpening)
            StateLabel.Text += $"\nOpened {recordedOpening:d}";
        if (request.FormType == "PCP" && request.ActivityOn.Date > request.DueOn.Date)
            StateLabel.Text += "\nThis PCP activity occurred after its deadline and will be marked Unbilled.";
        ActionChoice.Items.Add("Leave unchanged");
        if (request.CompletedOn is null)
        {
            if (request.OpenedOn is null)
            {
                _actions.Add(AnnualPcpProgressAction.Open);
                ActionChoice.Items.Add("Mark opened");
            }
            _actions.Add(AnnualPcpProgressAction.Complete);
            ActionChoice.Items.Add("Mark completed");
        }
        OpeningDate.SelectedDate = request.OpenedOn;
        OpeningDate.IsEnabled = request.OpenedOn is null;
        CompletionDate.SelectedDate = request.ActivityOn;
        ActionChoice.SelectedIndex = 0;
        Loaded += (_, _) => ActionChoice.Focus();
    }

    private void ActionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OpeningPanel is null || CompletionPanel is null || ActionChoice.SelectedIndex < 0) return;
        var action = _actions[ActionChoice.SelectedIndex];
        OpeningPanel.Visibility = action == AnnualPcpProgressAction.None ? Visibility.Collapsed : Visibility.Visible;
        CompletionPanel.Visibility = action == AnnualPcpProgressAction.Complete ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ContinueSave(object sender, RoutedEventArgs e)
    {
        var action = _actions[ActionChoice.SelectedIndex];
        var progress = new FormProgressRequest(action,
            action == AnnualPcpProgressAction.None ? null : OpeningDate.SelectedDate,
            action == AnnualPcpProgressAction.Complete ? CompletionDate.SelectedDate : null,
            _request.OpenedOn, _request.CompletedOn, _request.DueOn, _request.TargetEffectiveDate);
        var error = FormProgressRules.Validate(progress, _request.FormType, _request.Status,
            _request.AvailableOn, _request.Today, _request.DueOn, _request.TargetEffectiveDate,
            _request.OpenedOn, _request.CompletedOn);
        if (error is not null)
        {
            ValidationMessage.Text = error;
            return;
        }
        _request.Progress = progress;
        DialogResult = true;
    }
}
