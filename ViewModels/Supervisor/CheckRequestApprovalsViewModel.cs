using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.Supervisor;

public partial class CheckRequestApprovalsViewModel(
    IRepresentativePayeeService service) : ObservableObject
{
    public ObservableCollection<CheckRequestWorkflowQueueItemDto> Requests { get; } = [];
    [ObservableProperty] private CheckRequestWorkflowQueueItemDto? selectedRequest;
    [ObservableProperty] private string reviewNote = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;

    public bool CanReview => SelectedRequest is not null && !IsBusy;
    partial void OnSelectedRequestChanged(CheckRequestWorkflowQueueItemDto? value) => NotifyActions();
    partial void OnIsBusyChanged(bool value) => NotifyActions();

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Requests.Clear();
            foreach (var request in await service.GetSupervisorQueueAsync()) Requests.Add(request);
            SelectedRequest = Requests.FirstOrDefault();
            StatusMessage = Requests.Count == 0
                ? "No check requests are waiting for your review."
                : $"{Requests.Count} check request(s) are waiting for review.";
        }
        catch
        {
            StatusMessage = "The check-request review queue could not be loaded. Try Refresh.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand] private Task RefreshAsync() => LoadAsync();
    [RelayCommand] private Task ApproveAsync() => ReviewAsync(CheckRequestWorkflowAction.Approved);
    [RelayCommand] private Task ReturnAsync() => ReviewAsync(CheckRequestWorkflowAction.Returned);

    private async Task ReviewAsync(CheckRequestWorkflowAction action)
    {
        var request = SelectedRequest;
        if (!CanReview || request is null) return;
        var errors = CheckRequestWorkflowRules.ValidateNote(action, ReviewNote);
        if (errors.Count > 0) { StatusMessage = string.Join(" ", errors); return; }
        IsBusy = true;
        try
        {
            await service.ApplyActionAsync(request.CheckRequestId, action, ReviewNote);
            Requests.Remove(request);
            SelectedRequest = Requests.FirstOrDefault();
            ReviewNote = string.Empty;
            StatusMessage = action == CheckRequestWorkflowAction.Approved
                ? "Check request approved and sent to the Finance queue."
                : "Check request returned to the case manager with the recorded reason.";
        }
        catch (Exception error)
        {
            StatusMessage = error is InvalidOperationException or UnauthorizedAccessException
                ? error.Message : "The decision could not be confirmed. Refresh before retrying.";
        }
        finally { IsBusy = false; }
    }

    public void ClearForAccountSwitch()
    {
        Requests.Clear();
        SelectedRequest = null;
        ReviewNote = string.Empty;
        StatusMessage = string.Empty;
        IsBusy = false;
    }

    private void NotifyActions()
    {
        OnPropertyChanged(nameof(CanReview));
        ApproveCommand.NotifyCanExecuteChanged();
        ReturnCommand.NotifyCanExecuteChanged();
    }
}
