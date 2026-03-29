using Sati.Models;

namespace Sati.ViewModels
{
    public class ComplianceReviewViewModel
    {
        public string ClientName { get; init; } = string.Empty;
        public List<Form> Forms { get; init; } = [];
    }
}
