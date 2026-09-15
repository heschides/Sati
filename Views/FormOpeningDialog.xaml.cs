using Sati.Contracts.V1;
using System.Windows;
using System.Windows.Controls;

namespace Sati.Views;

public partial class FormOpeningDialog : Window
{
    private readonly DateTime _availableOn;
    private readonly DateTime _latestAllowedDate;

    public FormOpeningDialog(
        string formLabel,
        DateTime availableOn,
        DateTime latestAllowedDate)
    {
        InitializeComponent();
        _availableOn = availableOn.Date;
        _latestAllowedDate = latestAllowedDate.Date;
        PromptText.Text = $"When was {formLabel} actually opened?";
        WindowText.Text =
            $"Choose the occurrence date, from {_availableOn:MMM d, yyyy} through {_latestAllowedDate:MMM d, yyyy}. " +
            "Sati records the current timestamp separately.";
        OpeningDatePicker.DisplayDateStart = _availableOn;
        OpeningDatePicker.DisplayDateEnd = _latestAllowedDate;
        OpeningDatePicker.DisplayDate = _latestAllowedDate;
        OpeningDatePicker.SelectedDate = null;
        Loaded += (_, _) => OpeningDatePicker.Focus();
    }

    public DateTime? OpenedOn { get; private set; }

    private void OpeningDatePicker_SelectedDateChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        var selected = OpeningDatePicker.SelectedDate;
        ErrorText.Text = selected is DateTime openedOn
            ? FormOpeningRules.Validate(openedOn, _availableOn, _latestAllowedDate) ?? string.Empty
            : string.Empty;
        SaveButton.IsEnabled = selected is not null && string.IsNullOrEmpty(ErrorText.Text);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (OpeningDatePicker.SelectedDate is not DateTime openedOn)
            return;
        var error = FormOpeningRules.Validate(openedOn, _availableOn, _latestAllowedDate);
        if (error is not null)
        {
            ErrorText.Text = error;
            return;
        }

        OpenedOn = openedOn.Date;
        DialogResult = true;
    }
}
