using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Services;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.Finance;

public partial class RepresentativePayeeDashboardViewModel(
    IRepresentativePayeeService service) : ObservableObject
{
    private readonly LatestRequestTracker loads = new();

    public ObservableCollection<RepresentativePayeeConsumerDto> Consumers { get; } = [];
    public ObservableCollection<CheckRequestWorkflowQueueItemDto> CheckRequests { get; } = [];
    public ObservableCollection<RepresentativePayeeLedgerEntryDto> LedgerEntries { get; } = [];

    [ObservableProperty] private RepresentativePayeeConsumerDto? selectedConsumer;
    [ObservableProperty] private CheckRequestWorkflowQueueItemDto? selectedCheckRequest;
    [ObservableProperty] private DateTime? ledgerEntryDate = DateTime.Today;
    [ObservableProperty] private decimal ledgerAmount;
    [ObservableProperty] private string ledgerDescription = string.Empty;
    [ObservableProperty] private decimal balance;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;

    public bool HasConsumers => Consumers.Count > 0;
    public bool CanRelease => !IsBusy && SelectedCheckRequest?.Status == CheckRequestWorkflowStatus.Approved;
    public bool CanAcknowledgeReceipt => !IsBusy &&
        SelectedCheckRequest?.Status == CheckRequestWorkflowStatus.Released;
    public bool CanAddLedgerEntry => !IsBusy && SelectedConsumer is not null;
    public string SelectedRequestStatus => SelectedCheckRequest is null
        ? "Choose a check request."
        : CheckRequestWorkflowRules.Describe(SelectedCheckRequest.Status);

    partial void OnSelectedConsumerChanged(RepresentativePayeeConsumerDto? value)
    {
        OnPropertyChanged(nameof(CanAddLedgerEntry));
        _ = LoadLedgerAsync(value);
    }

    partial void OnSelectedCheckRequestChanged(CheckRequestWorkflowQueueItemDto? value) => NotifyActions();
    partial void OnIsBusyChanged(bool value) => NotifyActions();

    public async Task InitializeAsync()
    {
        var ticket = loads.Begin();
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var consumersTask = service.GetConsumersAsync();
            var queueTask = service.GetFinanceQueueAsync();
            await Task.WhenAll(consumersTask, queueTask);
            if (!loads.IsCurrent(ticket)) return;
            Consumers.Clear();
            foreach (var consumer in await consumersTask) Consumers.Add(consumer);
            CheckRequests.Clear();
            foreach (var request in await queueTask) CheckRequests.Add(request);
            OnPropertyChanged(nameof(HasConsumers));
            SelectedConsumer = Consumers.FirstOrDefault();
            StatusMessage = CheckRequests.Count == 0
                ? "No approved check requests are waiting for Finance."
                : $"{CheckRequests.Count} approved, released, or completed check request(s).";
        }
        catch
        {
            if (loads.IsCurrent(ticket)) StatusMessage = "The representative-payee workspace could not be loaded. Try Refresh.";
        }
        finally { if (loads.IsCurrent(ticket)) IsBusy = false; }
    }

    [RelayCommand] private Task RefreshAsync() => InitializeAsync();

    [RelayCommand]
    private async Task ReleaseAsync()
    {
        if (!CanRelease || SelectedCheckRequest is null) return;
        await ApplyActionAsync(SelectedCheckRequest, CheckRequestWorkflowAction.Released,
            "Check released by Finance.");
    }

    [RelayCommand]
    private async Task AcknowledgeReceiptAsync()
    {
        if (!CanAcknowledgeReceipt || SelectedCheckRequest is null) return;
        await ApplyActionAsync(SelectedCheckRequest, CheckRequestWorkflowAction.ReceiptAcknowledged,
            "Receipt acknowledged by Finance.");
    }

    private async Task ApplyActionAsync(
        CheckRequestWorkflowQueueItemDto request,
        CheckRequestWorkflowAction action,
        string note)
    {
        IsBusy = true;
        try
        {
            var updated = await service.ApplyActionAsync(request.CheckRequestId, action, note);
            var index = CheckRequests.IndexOf(request);
            if (index >= 0) CheckRequests[index] = updated;
            SelectedCheckRequest = updated;
            if (SelectedConsumer?.PersonId == updated.PersonId)
                await LoadLedgerAsync(SelectedConsumer);
            StatusMessage = action == CheckRequestWorkflowAction.Released
                ? "Check release recorded and the ledger debit was added."
                : "Receipt acknowledgement recorded.";
        }
        catch (Exception error)
        {
            StatusMessage = error is InvalidOperationException or UnauthorizedAccessException
                ? error.Message : "The workflow action could not be confirmed. Refresh before retrying.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task AddLedgerEntryAsync()
    {
        if (!CanAddLedgerEntry || SelectedConsumer is null) return;
        var errors = RepresentativePayeeLedgerRules.ValidateManualEntry(
            LedgerEntryDate, LedgerAmount, LedgerDescription);
        if (errors.Count > 0) { StatusMessage = string.Join(" ", errors); return; }
        IsBusy = true;
        try
        {
            await service.AddLedgerEntryAsync(SelectedConsumer.PersonId,
                LedgerEntryDate, LedgerAmount, LedgerDescription);
            LedgerAmount = 0;
            LedgerDescription = string.Empty;
            await LoadLedgerAsync(SelectedConsumer);
            StatusMessage = "Ledger entry recorded. Earlier entries remain unchanged.";
        }
        catch (Exception error)
        {
            StatusMessage = error is InvalidOperationException or UnauthorizedAccessException
                ? error.Message : "The ledger entry could not be confirmed. Refresh before retrying.";
        }
        finally { IsBusy = false; }
    }

    private async Task LoadLedgerAsync(RepresentativePayeeConsumerDto? consumer)
    {
        LedgerEntries.Clear();
        Balance = 0;
        if (consumer is null) return;
        try
        {
            var workspace = await service.GetWorkspaceAsync(consumer.PersonId);
            if (SelectedConsumer?.PersonId != consumer.PersonId) return;
            foreach (var entry in workspace.Entries) LedgerEntries.Add(entry);
            Balance = workspace.Balance;
        }
        catch
        {
            if (SelectedConsumer?.PersonId == consumer.PersonId)
                StatusMessage = "This consumer's ledger could not be loaded.";
        }
    }

    public void ClearForAccountSwitch()
    {
        loads.Invalidate();
        SelectedConsumer = null;
        SelectedCheckRequest = null;
        Consumers.Clear();
        CheckRequests.Clear();
        LedgerEntries.Clear();
        Balance = 0;
        StatusMessage = string.Empty;
        IsBusy = false;
        OnPropertyChanged(nameof(HasConsumers));
    }

    private void NotifyActions()
    {
        OnPropertyChanged(nameof(CanRelease));
        OnPropertyChanged(nameof(CanAcknowledgeReceipt));
        OnPropertyChanged(nameof(CanAddLedgerEntry));
        OnPropertyChanged(nameof(SelectedRequestStatus));
    }
}
