using Sati.Models.Billing;

namespace Sati.Data.Billing
{
    public interface IBillingService
    {
        Task<BillingPeriod> GetOrCreateBillingPeriodAsync(int userId, int month, int year);
        Task<IEnumerable<BillingPeriod>> GetBillingPeriodsAsync(int userId);
        Task<ClaimLine> CreateClaimLineAsync(int noteId);
        Task<IEnumerable<ClaimLine>> GetUnbilledClaimLinesAsync(int userId);
        Task SubmitBillingPeriodAsync(int billingPeriodId);
    }
}
