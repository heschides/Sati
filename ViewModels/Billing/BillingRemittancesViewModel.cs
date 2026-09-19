using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Services;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.Billing;

public partial class BillingRemittancesViewModel(
    IBillingService billingService,
    ISessionService sessionService) : ObservableObject
{
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly LatestRequestTracker _accountLoads = new();
    public ObservableCollection<RemittanceClaimOutcomeDto> Outcomes { get; } = [];
    public ObservableCollection<RemittanceDepositDto> Deposits { get; } = [];
    public ObservableCollection<EftDepositRecordDto> SelectedDepositHistory { get; } = [];
    [ObservableProperty] private string? statusMessage;
    public bool HasLoaded { get; private set; }

    // -------------------------------------------------------------------------
    // Recording the bank deposit
    // -------------------------------------------------------------------------

    public bool CanRecordDeposits => billingService.SupportsClaimCorrections;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDepositSelected))]
    [NotifyPropertyChangedFor(nameof(DepositEntryHeading))]
    [NotifyPropertyChangedFor(nameof(IsCorrectingDeposit))]
    [NotifyCanExecuteChangedFor(nameof(RecordDepositCommand))]
    private RemittanceDepositDto? selectedDeposit;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecordDepositCommand))]
    private decimal? depositAmount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecordDepositCommand))]
    private DateTime? depositDate;

    [ObservableProperty] private string? depositBankTrace;
    [ObservableProperty] private string? depositNote;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecordDepositCommand))]
    private bool isRecordingDeposit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDepositProblem))]
    private string? depositProblem;

    public bool IsDepositSelected => SelectedDeposit is not null;
    public bool HasDepositProblem => !string.IsNullOrWhiteSpace(DepositProblem);
    public bool IsCorrectingDeposit => SelectedDeposit?.CurrentEftRecordId is not null;

    public string DepositEntryHeading => SelectedDeposit is null
        ? "Select a deposit row to record what the bank received."
        : IsCorrectingDeposit
            ? $"Correcting the recorded deposit for {SelectedDeposit.PaymentReference}. Say why; the earlier entry is kept."
            : $"Recording the bank deposit for {SelectedDeposit.PaymentReference}.";

    partial void OnSelectedDepositChanged(RemittanceDepositDto? value)
    {
        DepositProblem = null;
        DepositBankTrace = null;
        DepositNote = null;
        // The 835's own total is the figure a matching deposit would carry; the case for
        // typing anything else is exactly the case worth seeing on screen.
        DepositAmount = value?.EftDepositAmount ?? value?.RemittancePaymentAmount;
        DepositDate = value?.EftDepositDate ?? value?.PaymentDate ?? DateTime.Today;
        SelectedDepositHistory.Clear();
        if (value is not null && CanRecordDeposits)
            _ = LoadDepositHistoryAsync(value.Id);
    }

    private async Task LoadDepositHistoryAsync(long depositId)
    {
        try
        {
            if (sessionService.CurrentUser?.ToAgencyActor() is not AgencyActor actor)
                return;
            var history = await billingService.GetEftDepositRecordsAsync(actor, depositId);
            if (SelectedDeposit?.Id != depositId)
                return;
            SelectedDepositHistory.Clear();
            foreach (var record in history)
                SelectedDepositHistory.Add(record);
        }
        catch (Exception ex)
        {
            if (SelectedDeposit?.Id == depositId)
                DepositProblem = $"The deposit history could not be loaded: {ex.Message}";
        }
    }

    private bool CanRecordDeposit() =>
        CanRecordDeposits && !IsRecordingDeposit && SelectedDeposit is not null &&
        DepositAmount.HasValue && DepositDate.HasValue;

    [RelayCommand(CanExecute = nameof(CanRecordDeposit))]
    private async Task RecordDeposit()
    {
        if (SelectedDeposit is not { } deposit || DepositAmount is not decimal amount ||
            DepositDate is not DateTime date)
            return;

        var problems = EftDepositRules.Validate(amount, date, DateTime.Today,
            DepositBankTrace, DepositNote, isCorrection: IsCorrectingDeposit);
        if (problems.Count > 0)
        {
            DepositProblem = string.Join(" ", problems.SelectMany(problem => problem.Value));
            return;
        }

        IsRecordingDeposit = true;
        DepositProblem = null;
        try
        {
            var actor = sessionService.CurrentUser?.ToAgencyActor()
                ?? throw new UnauthorizedAccessException("A signed-in user is required.");
            var updated = await billingService.RecordEftDepositAsync(actor, deposit.Id,
                new RecordEftDepositRequest(amount, date.Date, DepositBankTrace, DepositNote,
                    deposit.CurrentEftRecordId));
            var index = Deposits.IndexOf(deposit);
            if (index >= 0)
                Deposits[index] = updated;
            SelectedDeposit = updated;
            StatusMessage = $"Recorded {updated.EftDepositAmount:C} against {updated.PaymentReference}. " +
                DepositReconciliationRules.Explain(
                    Enum.TryParse<DepositReconciliationStatus>(updated.Status, out var status)
                        ? status : DepositReconciliationStatus.AwaitingEft);
        }
        catch (Exception ex)
        {
            DepositProblem = $"The deposit was not recorded: {ex.Message}";
        }
        finally
        {
            IsRecordingDeposit = false;
        }
    }

    public async Task LoadAsync(bool waitForExisting = false)
    {
        if (waitForExisting)
            await _loadGate.WaitAsync();
        else if (!await _loadGate.WaitAsync(0))
            return;
        var account = sessionService.CurrentUser;
        var request = _accountLoads.Begin();
        try
        {
            var actor = account?.ToAgencyActor()
                ?? throw new UnauthorizedAccessException("A signed-in user is required.");
            var outcomes = await billingService.GetRemittanceOutcomesAsync(actor);
            var deposits = await billingService.GetRemittanceDepositsAsync(actor);
            if (!_accountLoads.IsCurrent(request) || !ReferenceEquals(sessionService.CurrentUser, account))
                return;
            Outcomes.Clear();
            Deposits.Clear();
            foreach (var outcome in outcomes)
                Outcomes.Add(outcome);
            foreach (var deposit in deposits)
                Deposits.Add(deposit);
            HasLoaded = true;
            StatusMessage = Outcomes.Count == 0
                ? "No remittance claim outcomes have been received."
                : $"Showing {Deposits.Count} deposit reconciliation(s) and {Outcomes.Count} claim outcome(s).";
        }
        catch (Exception ex)
        {
            if (_accountLoads.IsCurrent(request) && ReferenceEquals(sessionService.CurrentUser, account))
                StatusMessage = $"Unable to load remittance history: {ex.Message}";
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public void ClearForAccountSwitch()
    {
        _accountLoads.Invalidate();
        Outcomes.Clear();
        Deposits.Clear();
        SelectedDeposit = null;
        SelectedDepositHistory.Clear();
        DepositProblem = null;
        StatusMessage = null;
        HasLoaded = false;
    }

}
