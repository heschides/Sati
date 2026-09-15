using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Models;

namespace Sati.ViewModels
{
    // One row in the compliance-review dialog. Wraps a real Form and holds the
    // in-flight UI state — checkbox + chosen completion date — without mutating the
    // entity until Confirm. The entity stays untouched so a cancelled dialog leaves
    // no trace; Commit() is the single write-back point.
    //
    // Completion is evidence, not an assumption. The worker must explicitly choose
    // the actual occurrence date before a new obligation can be completed.
    public partial class ComplianceFormRow : ObservableObject
    {
        private readonly Form _form;

        public ComplianceFormRow(Form form)
        {
            _form = form;
            isCompliant = form.IsCompliant;
            completedDate = form.CompletedDate;
        }

        public FormType Type => _form.Type;
        public DateTime DueDate => _form.DueDate;
        public bool IsEditable => _form.Id == 0;

        [ObservableProperty] private bool isCompliant;

        // Past dates are normal. Future dates are invalid. DueDate is deliberately
        // never copied into this property.
        [ObservableProperty] private DateTime? completedDate;

        partial void OnIsCompliantChanged(bool value)
        {
            if (!value)
                CompletedDate = null;
        }

        public string? ValidationError(DateTime today)
        {
            if (!IsEditable)
                return null;
            if (IsCompliant && CompletedDate is null)
                return $"Enter the actual completion date for {Type}.";
            if (CompletedDate is DateTime completed && completed.Date > today.Date)
                return $"The completion date for {Type} cannot be in the future.";
            return null;
        }

        // Writes the reconciled row state back onto the entity through the only
        // sanctioned door. Called once, on Confirm, for every row.
        public void Commit()
        {
            // Persisted obligations change through their individual append-only
            // attestation controls, never through this onboarding/bulk dialog.
            if (!IsEditable)
                return;

            var validationError = ValidationError(DateTime.Today);
            if (validationError is not null)
                throw new InvalidOperationException(validationError);

            if (IsCompliant && CompletedDate is DateTime date)
                _form.SetInitialCompletion(date);
            else
                _form.SetInitialCompletion(null);
        }
    }
}
