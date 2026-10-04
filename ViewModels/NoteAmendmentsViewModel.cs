using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Services;
using Sati.ViewModels.Children;
using System.Collections.ObjectModel;

namespace Sati.ViewModels;

public partial class NoteAmendmentsViewModel(INoteAmendmentService service) : ObservableObject
{
    private readonly LatestRequestTracker requests = new();
    private NoteAmendmentWorkspaceDto? workspace;
    private NoteAmendmentDto? current;
    private bool publishing;
    private bool review;
    private int? next;
    private NoteAmendmentRequest? retry;
    public ObservableCollection<NoteAmendmentQueueItem> Queue { get; } = [];
    public ObservableCollection<NoteAmendmentVersionDto> Versions { get; } = [];
    public ObservableCollection<NoteAmendmentEventDto> Decisions { get; } = [];
    public static IReadOnlyList<ServiceStartOption> StartTimeOptions { get; } = ServiceStartOption.BuildDay();
    [ObservableProperty] private ServiceStartOption? selectedStartTime;
    [ObservableProperty] private NoteAmendmentQueueItem? selected;
    [ObservableProperty] private NoteAmendmentVersionDto? selectedVersion;
    [ObservableProperty] private string original = string.Empty;
    [ObservableProperty] private string effective = string.Empty;
    [ObservableProperty] private string narrative = string.Empty;
    [ObservableProperty] private DateTime? serviceDate;
    [ObservableProperty] private int? minutes;
    [ObservableProperty] private int? startTime;
    [ObservableProperty] private bool isUnbilled;
    [ObservableProperty] private string reason = string.Empty;
    [ObservableProperty] private string reviewReason = string.Empty;
    [ObservableProperty] private string statusMessage = "Load approved notes to view or propose a linked amendment.";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isDirty;
    public bool CanEdit => !IsBusy && workspace?.CanAuthor == true && (current is null || current.Status is NoteAmendmentStatus.Draft or NoteAmendmentStatus.Returned);
    public bool CanSave => CanEdit && IsDirty;
    public bool CanSubmit => CanEdit && current is not null && !IsDirty;
    public bool CanReview => !IsBusy && workspace?.CanReview == true && current?.Status == NoteAmendmentStatus.Submitted;
    partial void OnIsBusyChanged(bool value) => NotifyActions();
    partial void OnIsDirtyChanged(bool value) => NotifyActions();
    partial void OnNarrativeChanged(string value) => Dirty();
    partial void OnServiceDateChanged(DateTime? value) => Dirty();
    partial void OnMinutesChanged(int? value) => Dirty();
    partial void OnStartTimeChanged(int? value) => Dirty();
    partial void OnSelectedStartTimeChanged(ServiceStartOption? value) => StartTime = value?.Minutes;
    partial void OnIsUnbilledChanged(bool value) => Dirty();
    partial void OnReasonChanged(string value) => Dirty();
    private void Dirty() { if (!publishing) { IsDirty = true; retry = null; } }
    partial void OnSelectedChanged(NoteAmendmentQueueItem? oldValue, NoteAmendmentQueueItem? newValue)
    {
        if (publishing) return;
        if (IsDirty || IsBusy) { publishing = true; Selected = oldValue; publishing = false; StatusMessage = "Save your proposal before selecting another note."; return; }
        if (newValue is not null) _ = OpenAsync(newValue.NoteId);
    }
    partial void OnSelectedVersionChanged(NoteAmendmentVersionDto? value)
    {
        if (value is not null) StatusMessage = $"Version {value.Number} ({value.Kind}), recorded by user {value.RecordedById} at {value.RecordedAtUtc:u}. Reason: {value.Reason}";
    }
    public void SetReviewMode() => review = true;
    public void ClearForAccountSwitch()
    {
        requests.Invalidate(); publishing = true; workspace = null; current = null; retry = null;
        Queue.Clear(); Versions.Clear(); Decisions.Clear(); Selected = null; SelectedVersion = null; SelectedStartTime = null; Original = Effective = Narrative = Reason = ReviewReason = string.Empty;
        ServiceDate = null; Minutes = StartTime = null; IsUnbilled = false; IsDirty = false; IsBusy = false; next = null;
        publishing = false; NotifyActions();
    }
    [RelayCommand] private async Task LoadAsync() => await LoadQueueAsync(false);
    [RelayCommand] private async Task MoreAsync() => await LoadQueueAsync(true);
    private async Task LoadQueueAsync(bool more)
    {
        if (IsBusy || IsDirty) { StatusMessage = "Save your proposal before refreshing."; return; }
        if (more && next is null) return;
        var request = requests.Begin(); IsBusy = true;
        try { var page = await service.GetQueueAsync(review, more ? next!.Value : 0); if (!requests.IsCurrent(request)) return;
            if (!more) { publishing = true; Queue.Clear(); Selected = null; SelectedVersion = null; workspace = null; current = null; Versions.Clear(); Decisions.Clear(); Original = Effective = Narrative = Reason = ReviewReason = string.Empty; publishing = false; }
            foreach (var item in page.Items) Queue.Add(item); next = page.NextAfterNoteId;
            StatusMessage = Queue.Count == 0 ? "No notes in this queue." : "Select a note to compare its original and amendment history.";
        } catch (Exception e) { if (requests.IsCurrent(request)) StatusMessage = SafeError(e); }
        finally { if (requests.IsCurrent(request)) IsBusy = false; }
    }
    private async Task OpenAsync(int id)
    {
        var request = requests.Begin(); IsBusy = true;
        try { var data = await service.GetAsync(id); if (!requests.IsCurrent(request)) return; Publish(data); }
        catch (Exception e) { if (requests.IsCurrent(request)) { publishing = true; workspace = null; current = null; Original = Effective = Narrative = Reason = ReviewReason = string.Empty; SelectedVersion = null; Versions.Clear(); Decisions.Clear(); publishing = false; StatusMessage = SafeError(e); } }
        finally { if (requests.IsCurrent(request)) IsBusy = false; }
    }
    private void Publish(NoteAmendmentWorkspaceDto data)
    {
        publishing = true; workspace = data; current = data.Amendments.LastOrDefault();
        if (current?.Status is NoteAmendmentStatus.Approved or NoteAmendmentStatus.Rejected) current = null;
        Original = Describe(NoteAmendmentContent.From(data.Original)); Effective = Describe(data.EffectiveContent);
        var version = current?.Versions.Last(); var content = version?.Content ?? data.EffectiveContent;
        Narrative = content.Narrative; ServiceDate = content.EventDate; Minutes = content.Minutes; StartTime = content.StartTime;
        SelectedStartTime = content.StartTime is int start ? StartTimeOptions.FirstOrDefault(s => s.Minutes == start) ?? new(start, ServiceTimeline.Describe(start)) : null; IsUnbilled = content.IsUnbilled;
        Reason = version?.Reason ?? string.Empty; ReviewReason = string.Empty;
        SelectedVersion = null; Versions.Clear(); Decisions.Clear(); foreach (var a in data.Amendments) { foreach(var v in a.Versions) Versions.Add(v); foreach(var e in a.Events) Decisions.Add(e); }
        IsDirty = false; retry = null; publishing = false;
        StatusMessage = $"Original revision {data.Original.Revision}; effective amendment version {data.EffectiveVersionId?.ToString() ?? "none"}. " +
            (data.RequiresFinancialReview ? "Financial review required. Billing is held; existing claims remain frozen." : current?.Status.ToString() ?? "Ready for a new amendment.");
        NotifyActions();
    }
    [RelayCommand(CanExecute = nameof(CanSave))] private Task SaveAsync() => ActAsync(current is null ? NoteAmendmentAction.Create : NoteAmendmentAction.Save);
    [RelayCommand] private void ClearStartTime() { if (CanEdit) { SelectedStartTime = null; StartTime = null; } }
    [RelayCommand(CanExecute = nameof(CanSubmit))] private Task SubmitAsync() => ActAsync(NoteAmendmentAction.Submit);
    [RelayCommand(CanExecute = nameof(CanReview))] private Task ApproveAsync() => ActAsync(NoteAmendmentAction.Approve);
    [RelayCommand(CanExecute = nameof(CanReview))] private Task ReturnAsync() => ActAsync(NoteAmendmentAction.Return);
    [RelayCommand(CanExecute = nameof(CanReview))] private Task RejectAsync() => ActAsync(NoteAmendmentAction.Reject);
    private async Task ActAsync(NoteAmendmentAction action)
    {
        if (workspace is null || IsBusy) return;
        var id = workspace.Original.Id; var identity = requests.Begin();
        var request = new NoteAmendmentRequest(Guid.NewGuid(), action, current?.Id, current?.Revision ?? 0, workspace.Original.Revision, workspace.EffectiveVersionId,
            action is NoteAmendmentAction.Create or NoteAmendmentAction.Save ? new(Narrative, ServiceDate, Minutes, StartTime, IsUnbilled) : null,
            action is NoteAmendmentAction.Create or NoteAmendmentAction.Save ? Reason : null,
            action is NoteAmendmentAction.Return or NoteAmendmentAction.Approve or NoteAmendmentAction.Reject ? ReviewReason : null);
        // Retain the identity on an uncertain transport outcome; never replay changed content under the same key.
        if (retry is not null && retry with { OperationId = request.OperationId } == request) request = retry;
        retry = request; IsBusy = true;
        try { await service.ActAsync(id, request); if (!requests.IsCurrent(identity)) return;
            IsDirty = false; retry = null; var data = await service.GetAsync(id); if (requests.IsCurrent(identity)) Publish(data);
        } catch (Exception e) { if (requests.IsCurrent(identity)) StatusMessage = SafeError(e); }
        finally { if (requests.IsCurrent(identity)) IsBusy = false; }
    }
    private void NotifyActions()
    {
        OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanSave)); OnPropertyChanged(nameof(CanSubmit)); OnPropertyChanged(nameof(CanReview));
        SaveCommand.NotifyCanExecuteChanged(); SubmitCommand.NotifyCanExecuteChanged(); ApproveCommand.NotifyCanExecuteChanged(); ReturnCommand.NotifyCanExecuteChanged(); RejectCommand.NotifyCanExecuteChanged();
    }
    private static string Describe(NoteAmendmentContent content) => $"Service: {content.EventDate:d}; minutes: {content.Minutes}; start minutes after 7 AM: {content.StartTime}; Unbilled: {content.IsUnbilled}\n\n{content.Narrative}";
    private static string SafeError(Exception e) => e is NoteAmendmentWorkflowException or InvalidOperationException ? e.Message : "The request could not be confirmed. Your proposal is retained. Retry the same action or reopen the record after saving.";
}

