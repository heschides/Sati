using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Models;
using Sati.Models.Billing;
using Sati.Services;
using Sati.ViewModels.Billing;
using Xunit;

namespace Sati.Tests;

public sealed class BillingStartupPerformanceTests
{
    [Fact]
    public async Task DashboardLoadsOnlyOnFirstExplicitInitializationPerAccount()
    {
        var session = new SessionService();
        session.SetUser(User.Create(
            7001,
            "billing-startup",
            "Billing Startup",
            "hash",
            "salt",
            UserRole.Admin,
            supervisorId: null,
            agencyId: 71));
        var service = new CountingBillingService();
        var overview = new BillingOverviewViewModel(service, session);
        var dashboard = new BillingDashboardViewModel(
            overview,
            new BillingQueueViewModel(service, session),
            new BillingSubmissionsViewModel(service, null!, session),
            new BillingRemittancesViewModel(service, session),
            new BillingAlertsViewModel(service, session));

        Assert.Equal(0, service.ConfigurationLoads);

        await Task.WhenAll(dashboard.InitializeAsync(), dashboard.InitializeAsync());

        Assert.Equal(1, service.ConfigurationLoads);
        Assert.Equal(1, service.OverviewLoads);
        Assert.Same(overview, dashboard.CurrentSubView);

        dashboard.ClearForAccountSwitch();
        await dashboard.InitializeAsync();

        Assert.Equal(2, service.ConfigurationLoads);
        Assert.Equal(2, service.OverviewLoads);
        Assert.Same(overview, dashboard.CurrentSubView);
    }

    private sealed class CountingBillingService : IBillingService
    {
        private int _configurationLoads;
        private int _overviewLoads;
        public int ConfigurationLoads => Volatile.Read(ref _configurationLoads);
        public int OverviewLoads => Volatile.Read(ref _overviewLoads);

        public Task<BillingConfiguration> GetBillingConfigurationAsync(AgencyActor actor)
        {
            Interlocked.Increment(ref _configurationLoads);
            return Task.FromResult(new BillingConfiguration(
                "G9012", null, 25m, "SATI", "MEDICAID", "MCDME", "Billing", "2075550101"));
        }

        public Task<IEnumerable<Note>> GetApprovedUnbilledNotesAsync(AgencyActor actor) =>
            Task.FromResult<IEnumerable<Note>>([]);

        public BillingValidationResult ValidateNoteForBilling(Note note) =>
            new(true, note, []);

        public Task<IEnumerable<BillingPeriod>> GetAllBillingPeriodsAsync(AgencyActor actor) =>
            throw new InvalidOperationException("Billing Overview must not retrieve period history.");

        public Task<BillingPeriodOverviewDto> GetBillingPeriodOverviewAsync(
            AgencyActor actor,
            DateTime asOf)
        {
            Interlocked.Increment(ref _overviewLoads);
            var firstMonth = new DateTime(asOf.Year, asOf.Month, 1).AddMonths(-5);
            return Task.FromResult(new BillingPeriodOverviewDto(
                0m,
                Enumerable.Range(0, 6)
                    .Select(offset => firstMonth.AddMonths(offset))
                    .Select(month => new BillingMonthChargeDto(month.Year, month.Month, 0m))
                    .ToList()));
        }

        public Task<IReadOnlyList<RemittanceClaimOutcomeDto>> GetRemittanceOutcomesAsync(AgencyActor actor) =>
            Task.FromResult<IReadOnlyList<RemittanceClaimOutcomeDto>>([]);

        public Task<IReadOnlyList<RemittanceDepositDto>> GetRemittanceDepositsAsync(AgencyActor actor) =>
            Task.FromResult<IReadOnlyList<RemittanceDepositDto>>([]);

        public Task<BillingPeriod> GetOrCreateBillingPeriodAsync(
            AgencyActor actor, int userId, int month, int year) =>
            throw new NotSupportedException();

        public Task<IEnumerable<BillingPeriod>> GetBillingPeriodsAsync(AgencyActor actor, int userId) =>
            throw new NotSupportedException();

        public Task<ClaimLine> CreateClaimLineAsync(
            AgencyActor actor,
            int noteId,
            bool isComplianceException = false,
            string? complianceExceptionReason = null) =>
            throw new NotSupportedException();

        public Task<IEnumerable<ClaimLine>> GetUnbilledClaimLinesAsync(AgencyActor actor, int userId) =>
            throw new NotSupportedException();

        public Task SubmitBillingPeriodAsync(AgencyActor actor, int billingPeriodId) =>
            throw new NotSupportedException();

        public Task ReturnBillingPeriodToDraftAsync(AgencyActor actor, int billingPeriodId) =>
            throw new NotSupportedException();

        public Task SaveBillingConfigurationAsync(AgencyActor actor, BillingConfiguration configuration) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<BillingSubmissionHistoryDto>> GetSubmissionHistoryAsync(AgencyActor actor) =>
            throw new NotSupportedException();
    }
}
