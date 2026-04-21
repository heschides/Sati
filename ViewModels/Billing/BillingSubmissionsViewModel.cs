using CommunityToolkit.Mvvm.ComponentModel;

namespace Sati.ViewModels.Billing
{
    public class BillingSubmissionsViewModel : ObservableObject
    {
        public string StubMessage => "Submissions: 837P claim files pending submission, recently sent, and filterable by date range.";
    }
}