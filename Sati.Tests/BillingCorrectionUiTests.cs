using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Edi;
using Sati.Models;
using Sati.Models.Billing;
using Sati.Services;
using Sati.ViewModels.Billing;
using System.Windows.Controls;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The two screens that record a bank deposit and correct a sent claim. The API enforces both
/// rules; these check that the desktop offers exactly what they allow and says what went wrong.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class BillingCorrectionUiTests
{
    private static User Biller(int id = 7) =>
        User.Create(id, "biller", "Synthetic Biller", "hash", "salt", UserRole.Admin, null, 1);

    private static RemittanceDepositDto Deposit(decimal payment = 25.60m, long? currentRecord = null) =>
        new(1702, "SYN-EFT", "Synthetic Payer", new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 1), payment, 0m, null, payment, null,
            nameof(DepositReconciliationStatus.AwaitingEft), null, "Awaiting the deposit.", true)
        {
            CurrentEftRecordId = currentRecord
        };

    private static BillingClaimStatusDto Claim(
        string state = nameof(ClaimLifecycleState.Denied),
        string[]? actions = null,
        string? payerClaimNumber = "ICN-1") =>
        new(1402, 603, "Person Two", new DateTime(2026, 8, 12), 33.25m, state,
            ClaimCorrectionRules.Describe(Enum.Parse<ClaimLifecycleState>(state)),
            "The payer denied the claim.", actions ?? [nameof(ClaimCorrectionAction.Replace)],
            payerClaimNumber, 1);

    // -------------------------------------------------------------------------
    // Recording the deposit
    // -------------------------------------------------------------------------

    [Fact]
    public void TheDepositPanelRendersAndOffersTheRemittanceTotalToConfirm()
    {
        var session = new SessionService();
        session.SetUser(Biller());
        var billing = new BillingStub { Deposits = [Deposit()] };
        var viewModel = new BillingRemittancesViewModel(billing, session);

        WpfUiHarness.Run(() =>
        {
            var view = new Sati.Views.Billing.BillingRemittancesView { DataContext = viewModel };
            WpfUiHarness.Realize(view, 1200, 900);
            var button = WpfUiHarness.FindByAutomationName<Button>(view, "Record bank deposit");
            Assert.Same(viewModel.RecordDepositCommand, button.Command);
            Assert.False(viewModel.RecordDepositCommand.CanExecute(null));

            viewModel.SelectedDeposit = viewModel.Deposits.Count > 0 ? viewModel.Deposits[0] : Deposit();
            // The 835's own total is proposed; typing a different figure is the interesting case.
            Assert.Equal(25.60m, viewModel.DepositAmount);
            Assert.True(viewModel.RecordDepositCommand.CanExecute(null));
        });
    }

    [Fact]
    public async Task CorrectingARecordedDepositWithoutSayingWhyIsRefusedBeforeItIsSent()
    {
        var session = new SessionService();
        session.SetUser(Biller());
        var billing = new BillingStub { Deposits = [Deposit(currentRecord: 9)] };
        var viewModel = new BillingRemittancesViewModel(billing, session);
        await viewModel.LoadAsync(waitForExisting: true);
        viewModel.SelectedDeposit = viewModel.Deposits[0];
        viewModel.DepositAmount = 25.00m;

        await viewModel.RecordDepositCommand.ExecuteAsync(null);

        Assert.Equal(0, billing.RecordedDeposits);
        Assert.True(viewModel.HasDepositProblem);

        viewModel.DepositNote = "Bank statement says $25.00.";
        await viewModel.RecordDepositCommand.ExecuteAsync(null);

        Assert.Equal(1, billing.RecordedDeposits);
        // The entry names the one it supersedes, so a concurrent correction is refused server-side.
        Assert.Equal(9, billing.LastDepositRequest!.PreviousRecordId);
        Assert.False(viewModel.HasDepositProblem);
    }

    [Fact]
    public async Task ARefusedDepositEntryIsReportedRatherThanSwallowed()
    {
        var session = new SessionService();
        session.SetUser(Biller());
        var billing = new BillingStub
        {
            Deposits = [Deposit()],
            RecordFailure = () => throw new InvalidOperationException("eft_changed")
        };
        var viewModel = new BillingRemittancesViewModel(billing, session);
        await viewModel.LoadAsync(waitForExisting: true);
        viewModel.SelectedDeposit = viewModel.Deposits[0];

        await viewModel.RecordDepositCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasDepositProblem);
        Assert.Contains("eft_changed", viewModel.DepositProblem);
    }

    // -------------------------------------------------------------------------
    // Correcting a claim
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnlyTheCorrectionsThePayersAnswerAllowsAreOffered()
    {
        var session = new SessionService();
        session.SetUser(Biller());
        var billing = new BillingStub { Claims = [Claim()] };
        var viewModel = new BillingSubmissionsViewModel(billing, new EdiStub(), session);
        var period = BillingPeriodFor(77);

        viewModel.SelectedPeriod = period;
        await billing.ClaimsLoaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Yield();

        viewModel.SelectedClaim = viewModel.PeriodClaims.Single();
        Assert.Equal([ClaimCorrectionAction.Replace], viewModel.AvailableCorrections);
        // A single option is preselected; the reason is still required.
        Assert.Equal(ClaimCorrectionAction.Replace, viewModel.SelectedCorrection);
        Assert.False(viewModel.CreateCorrectionCommand.CanExecute(null));

        viewModel.CorrectionReason = "Diagnosis corrected on the profile.";
        Assert.True(viewModel.CreateCorrectionCommand.CanExecute(null));

        await viewModel.CreateCorrectionCommand.ExecuteAsync(null);
        Assert.Equal(ClaimCorrectionAction.Replace, billing.LastCorrection!.Action);
        Assert.Equal(1402, billing.LastCorrection.ClaimLineId);
    }

    [Fact]
    public async Task TheCorrectionFileIsOfferedOnlyWhileOneIsWaitingToBeSent()
    {
        var session = new SessionService();
        session.SetUser(Biller());
        var billing = new BillingStub { Claims = [Claim()] };
        var viewModel = new BillingSubmissionsViewModel(billing, new EdiStub(), session);

        viewModel.SelectedPeriod = BillingPeriodFor(77);
        await billing.ClaimsLoaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Yield();
        Assert.False(viewModel.GenerateCorrectionFileCommand.CanExecute(null));

        billing.Claims = [Claim(state: nameof(ClaimLifecycleState.CorrectionWaitingToSend), actions: [])];
        billing.ClaimsLoaded = new TaskCompletionSource();
        viewModel.SelectedPeriod = BillingPeriodFor(77);
        await billing.ClaimsLoaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Yield();

        Assert.True(viewModel.HasCorrectionsWaiting);
        Assert.True(viewModel.GenerateCorrectionFileCommand.CanExecute(null));
        await viewModel.GenerateCorrectionFileCommand.ExecuteAsync(null);
        Assert.Equal(1, billing.CorrectionFiles);
    }

    [Fact]
    public void ALocalProductionSessionIsNotOfferedCorrectionsAtAll()
    {
        var session = new SessionService();
        session.SetUser(Biller());
        var local = new BillingStub { Supports = false };
        var submissions = new BillingSubmissionsViewModel(local, new EdiStub(), session);
        var remittances = new BillingRemittancesViewModel(local, session);

        Assert.False(submissions.ShowsCorrections);
        Assert.False(remittances.CanRecordDeposits);
        Assert.False(remittances.RecordDepositCommand.CanExecute(null));
    }

    private static BillingPeriod BillingPeriodFor(int id)
    {
        var period = new BillingPeriod { UserId = 7, Month = 8, Year = 2026, Status = BillingStatus.Submitted };
        typeof(BillingPeriod).GetProperty(nameof(BillingPeriod.Id))!.SetValue(period, id);
        return period;
    }

    private sealed class EdiStub : IEdiService
    {
        public Task<string> GenerateAndSaveAsync(int billingPeriodId, bool isTest, string idempotencyKey) =>
            Task.FromResult("C:\\Demo\\837P.txt");
    }

    private sealed class BillingStub : IBillingService
    {
        public bool Supports { get; init; } = true;
        public IReadOnlyList<RemittanceDepositDto> Deposits { get; init; } = [];
        public IReadOnlyList<BillingClaimStatusDto> Claims { get; set; } = [];
        public TaskCompletionSource ClaimsLoaded { get; set; } = new();
        public Action? RecordFailure { get; init; }
        public int RecordedDeposits { get; private set; }
        public int CorrectionFiles { get; private set; }
        public RecordEftDepositRequest? LastDepositRequest { get; private set; }
        public CreateClaimCorrectionRequest? LastCorrection { get; private set; }

        public bool SupportsClaimCorrections => Supports;

        public Task<IReadOnlyList<RemittanceDepositDto>> GetRemittanceDepositsAsync(AgencyActor actor) =>
            Task.FromResult(Deposits);
        public Task<IReadOnlyList<RemittanceClaimOutcomeDto>> GetRemittanceOutcomesAsync(AgencyActor actor) =>
            Task.FromResult<IReadOnlyList<RemittanceClaimOutcomeDto>>([]);
        public Task<IReadOnlyList<EftDepositRecordDto>> GetEftDepositRecordsAsync(AgencyActor actor, long depositId) =>
            Task.FromResult<IReadOnlyList<EftDepositRecordDto>>([]);

        public Task<RemittanceDepositDto> RecordEftDepositAsync(
            AgencyActor actor, long depositId, RecordEftDepositRequest request)
        {
            RecordFailure?.Invoke();
            RecordedDeposits++;
            LastDepositRequest = request;
            return Task.FromResult(Deposits[0] with { EftDepositAmount = request.Amount });
        }

        public Task<IReadOnlyList<BillingClaimStatusDto>> GetBillingPeriodClaimsAsync(AgencyActor actor, int billingPeriodId)
        {
            ClaimsLoaded.TrySetResult();
            return Task.FromResult(Claims);
        }

        public Task<ClaimCorrectionDto> CreateClaimCorrectionAsync(
            AgencyActor actor, int billingPeriodId, CreateClaimCorrectionRequest request)
        {
            LastCorrection = request;
            return Task.FromResult(new ClaimCorrectionDto(
                1, request.ClaimLineId, request.Action.ToString(), "ICN-1", request.Reason, DateTime.UtcNow));
        }

        public Task<string> GenerateCorrectionEdiAsync(
            AgencyActor actor, int billingPeriodId, bool isTest, string idempotencyKey)
        {
            CorrectionFiles++;
            return Task.FromResult("C:\\Demo\\837P_CORRECTION.txt");
        }

        public Task<IReadOnlyList<BillingSubmissionHistoryDto>> GetSubmissionHistoryAsync(AgencyActor actor) =>
            Task.FromResult<IReadOnlyList<BillingSubmissionHistoryDto>>([]);
        public Task<IEnumerable<BillingPeriod>> GetAllBillingPeriodsAsync(AgencyActor actor) =>
            Task.FromResult<IEnumerable<BillingPeriod>>([]);
        public Task<BillingPeriod> GetOrCreateBillingPeriodAsync(AgencyActor actor, int userId, int month, int year) => throw new NotSupportedException();
        public Task<IEnumerable<BillingPeriod>> GetBillingPeriodsAsync(AgencyActor actor, int userId) => throw new NotSupportedException();
        public Task<ClaimLine> CreateClaimLineAsync(AgencyActor actor, int noteId, bool isComplianceException = false, string? complianceExceptionReason = null) => throw new NotSupportedException();
        public Task<IEnumerable<ClaimLine>> GetUnbilledClaimLinesAsync(AgencyActor actor, int userId) => throw new NotSupportedException();
        public Task SubmitBillingPeriodAsync(AgencyActor actor, int billingPeriodId) => throw new NotSupportedException();
        public Task ReturnBillingPeriodToDraftAsync(AgencyActor actor, int billingPeriodId) => throw new NotSupportedException();
        public Task<IEnumerable<Note>> GetApprovedUnbilledNotesAsync(AgencyActor actor) => throw new NotSupportedException();
        public BillingValidationResult ValidateNoteForBilling(Note note) => throw new NotSupportedException();
        public Task<BillingConfiguration> GetBillingConfigurationAsync(AgencyActor actor) => throw new NotSupportedException();
        public Task SaveBillingConfigurationAsync(AgencyActor actor, BillingConfiguration configuration) => throw new NotSupportedException();
    }
}
