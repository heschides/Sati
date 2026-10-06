using System.Windows;
using System.Windows.Controls;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Views;

public partial class ReviewCompletionPromptWindow : Window
{
    private readonly Form _form;
    private readonly DateTime _cycleStart;
    public DateTime CompletedOn => CompletionDate.SelectedDate!.Value.Date;

    public ReviewCompletionPromptWindow(Person person, Form form)
    {
        _form = form;
        _cycleStart = FormAttestationRules.ResolveCycleForForm(
            person.EffectiveDate ?? throw new InvalidOperationException("The client has no effective date."),
            form.Type.ToString(), form.DueDate,
            form.TargetEffectiveDate == default ? null : form.TargetEffectiveDate)?.CycleStart
            ?? throw new InvalidOperationException("The review has no valid compliance cycle.");
        InitializeComponent();
        ReviewHeading.Text = $"{person.FullName} — {form.Type.ToString()[..2]} 90-day review\nDue {form.DueDate:d}";
        Question.Text = "Enter the actual completion date to continue.";
        Loaded += (_, _) => CompletionDate.Focus();
    }

    private void DateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Question is null || GenerateButton is null) return;
        var error = CompletionDate.SelectedDate is DateTime date
            ? FormAttestationRules.ValidateCompletionDate(date, _cycleStart, DateTime.Today)
            : "Enter the actual completion date to continue.";
        GenerateButton.IsEnabled = error is null;
        Question.Text = error ?? $"Would you like to generate a {(ReviewCompletionNoteRules.IsOverdue(CompletedOn, _form.DueDate) ? "non-billable" : "billable")} case note to record completion of this {_form.Type.ToString()[..2]} 90-day review?";
    }

    private void GenerateClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
