using Sati.Models;

namespace Sati.ViewModels
{
    public partial class ComplianceReviewViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        public string ClientName { get; init; } = string.Empty;
        [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
        private string? validationMessage;

        // The forms under review, each wrapped in a row that holds the in-flight
        // checkbox/date state without touching the entity until Commit. Built from
        // the raw Form list the handler still passes in, so the call site in
        // OnDataContextChanged barely changes — it sets Forms from person.Forms,
        // and the wrapping happens here.
        public List<ComplianceFormRow> Rows { get; }

        public ComplianceReviewViewModel(IEnumerable<Form> forms)
        {
            Rows = forms.Select(f => new ComplianceFormRow(f)).ToList();
        }

        public bool Validate(DateTime today)
        {
            ValidationMessage = Rows
                .Select(row => row.ValidationError(today))
                .FirstOrDefault(error => error is not null);
            return ValidationMessage is null;
        }

        // Writes every row's reconciled state back onto its Form through the
        // sanctioned door (MarkComplete/Reset). Called once, on Confirm. After this
        // returns, the entities carry the user's choices and the parent's
        // AddPersonAsync persists them.
        public void Commit()
        {
            if (!Validate(DateTime.Today))
                throw new InvalidOperationException(ValidationMessage);

            foreach (var row in Rows)
                row.Commit();
        }
    }
}
