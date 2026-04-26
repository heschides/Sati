using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Models.Billing;

namespace Sati.ViewModels.Billing
{
    public partial class BillingQueueItemViewModel : ObservableObject
    {
        public BillingValidationResult Result { get; }

        [ObservableProperty] private bool isSelected;

        public bool IsValid => Result.IsValid;
        public bool IsComplianceOverride => Result.Note.ComplianceOverride;

        public BillingQueueItemViewModel(BillingValidationResult result)
        {
            Result = result;
        }
    }
}