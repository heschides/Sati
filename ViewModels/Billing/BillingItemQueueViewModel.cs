using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Models.Billing;
using Sati.Contracts.V1;

namespace Sati.ViewModels.Billing
{
    public partial class BillingQueueItemViewModel : ObservableObject
    {
        public BillingValidationResult Result { get; }

        [ObservableProperty] private bool isSelected;

        public bool IsValid => Result.IsValid;
        public bool IsComplianceOverride => Result.Note.ComplianceOverride;
        public decimal BillingUnits => BillingRules.CalculateSection13Units(Result.Note.Minutes);
        public string ProcedureDisplay { get; }
        public string ClientDisplayName { get; }

        public BillingQueueItemViewModel(
            BillingValidationResult result,
            BillingConfiguration configuration,
            bool showClientIdentity = true)
        {
            Result = result;
            ClientDisplayName = showClientIdentity
                ? result.Note.Person.FullName
                : $"Consumer record {result.Note.Person.Id}";
            ProcedureDisplay = string.IsNullOrWhiteSpace(configuration.Modifier)
                ? configuration.ProcedureCode
                : $"{configuration.ProcedureCode} {configuration.Modifier}";
        }
    }
}
