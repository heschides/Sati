using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Services;
namespace Sati.ViewModels.Admin;

public partial class RecordsGovernanceViewModel(IRecordsGovernanceService service, ISessionService session) : ObservableObject
{
    private readonly LatestRequestTracker requests = new();
    private GovernanceHoldRequest? holdRetry;
    private RetentionPolicyRequest? policyRetry;
    private bool publishing;
    public ObservableCollection<GovernanceHoldDto> Holds { get; } = [];
    public ObservableCollection<RetentionPolicyDto> Policies { get; } = [];
    public IReadOnlyList<RetentionRecordClass> RecordClasses { get; } = Enum.GetValues<RetentionRecordClass>();
    public IReadOnlyList<PreservationScope> Scopes { get; } = Enum.GetValues<PreservationScope>();
    [ObservableProperty] private GovernanceHoldDto? selectedHold;
    [ObservableProperty] private RetentionPolicyDto? selectedPolicy;
    [ObservableProperty] private PreservationScope scope;
    [ObservableProperty] private RetentionRecordClass recordClass;
    [ObservableProperty] private RetentionRecordClass policyClass;
    [ObservableProperty] private bool allRecordClasses = true;
    [ObservableProperty] private string personIdText = "";
    [ObservableProperty] private string? recordId;
    [ObservableProperty] private string reason = "";
    [ObservableProperty] private string? caseReference;
    [ObservableProperty] private string? issuedBy;
    [ObservableProperty] private string retentionDaysText = "";
    [ObservableProperty] private string policyReason = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "Retention is PolicyOnly. Load governance records to review holds and proposed policies.";
    [ObservableProperty] private RetentionPreviewDto? preview;
    public string PreviewSummary => Preview is not { } result ? "No preview prepared." :
        $"Policy version {result.PolicyVersion}; {result.RecordClass}; prepared {result.PreparedAtUtc:u}.\n" +
        $"Candidates: {Count(result.CandidateCount)}; preserved by holds: {Count(result.HeldCount)}; clear: {Count(result.ClearCount)}; unavailable dependencies: {Count(result.UnavailableCount)}; dependency links: {Count(result.DependencyCount)}.\n" +
        $"Date range: {result.OldestUtc?.ToString("u") ?? "unknown"} to {result.NewestUtc?.ToString("u") ?? "unknown"}.\n" +
        "Deletion disabled. " + string.Join("; ", result.Blockers);
    private static string Count(int? value) => value?.ToString() ?? "unknown";
    partial void OnPreviewChanged(RetentionPreviewDto? value) => OnPropertyChanged(nameof(PreviewSummary));
    public bool CanWork => !IsBusy && session.CurrentUser?.HasAdminPermissions == true;
    public bool CanDecideRelease => CanWork && SelectedHold is { IsReleased: false, ReleaseRequestedById: not null } hold &&
        hold.ReleaseRequestedById != session.CurrentUser?.Id && hold.PlacedById != session.CurrentUser?.Id;
    partial void OnIsBusyChanged(bool value) => Notify();
    partial void OnSelectedHoldChanged(GovernanceHoldDto? value)
    {
        if (value is not null) { Scope = value.Scope; RecordClass = value.RecordClass ?? RecordClasses[0]; AllRecordClasses = value.RecordClass is null;
            PersonIdText = value.PersonId?.ToString() ?? ""; RecordId = value.RecordId; Reason = ""; CaseReference = value.History.LastOrDefault()?.CaseReference; IssuedBy = value.History.LastOrDefault()?.IssuedBy; }
        Notify();
    }
    partial void OnSelectedPolicyChanged(RetentionPolicyDto? value)
    {
        if (!publishing) { requests.Invalidate(); IsBusy = false; }
        Preview = null;
        if (value is not null) { PolicyClass = value.RecordClass; RetentionDaysText = value.RetentionDays?.ToString() ?? ""; }
    }
    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task LoadAsync()
    {
        var identity = requests.Begin(); IsBusy = true;
        try { var holds = await service.GetHoldsAsync(); var policies = await service.GetPoliciesAsync();
            if (!requests.IsCurrent(identity)) return;
            publishing = true;
            try { SelectedHold = null; SelectedPolicy = null; }
            finally { publishing = false; }
            Holds.Clear(); Policies.Clear();
            foreach (var hold in holds) Holds.Add(hold); foreach (var policy in policies) Policies.Add(policy);
            StatusMessage = "Records loaded. A release request keeps its hold active until an independent Admin approves it.";
        } catch { if (requests.IsCurrent(identity)) StatusMessage = "Governance records could not be loaded. Preservation remains enforced."; }
        finally { if (requests.IsCurrent(identity)) IsBusy = false; }
    }
    [RelayCommand(CanExecute = nameof(CanWork))] private Task PlaceAsync() => ChangeAsync(GovernanceHoldAction.Place);
    [RelayCommand(CanExecute = nameof(CanWork))] private Task AmendAsync() => ChangeAsync(GovernanceHoldAction.Amend);
    [RelayCommand(CanExecute = nameof(CanWork))] private Task RequestReleaseAsync() => ChangeAsync(GovernanceHoldAction.RequestRelease);
    [RelayCommand(CanExecute = nameof(CanDecideRelease))] private Task ApproveReleaseAsync() => ChangeAsync(GovernanceHoldAction.ApproveRelease);
    [RelayCommand(CanExecute = nameof(CanDecideRelease))] private Task RejectReleaseAsync() => ChangeAsync(GovernanceHoldAction.RejectRelease);
    private async Task ChangeAsync(GovernanceHoldAction action)
    {
        if (action != GovernanceHoldAction.Place && SelectedHold is null) { StatusMessage = "Select a hold first."; return; }
        int? person = null;
        if (Scope != PreservationScope.Agency && !string.IsNullOrWhiteSpace(PersonIdText))
        {
            if (!int.TryParse(PersonIdText, out var parsed) || parsed < 1) { StatusMessage = "Enter a valid positive person record ID."; return; }
            person = parsed;
        }
        var request = new GovernanceHoldRequest(Guid.NewGuid(), action, action == GovernanceHoldAction.Place ? null : SelectedHold!.Id,
            action == GovernanceHoldAction.Place ? 0 : SelectedHold!.Revision, Scope, AllRecordClasses ? null : RecordClass,
            person, Scope == PreservationScope.Record ? RecordId : null, Reason, CaseReference, IssuedBy);
        if (holdRetry is not null && holdRetry with { OperationId = request.OperationId } == request) request = holdRetry;
        holdRetry = request; var identity = requests.Begin(); IsBusy = true;
        try { var hold = await service.ChangeHoldAsync(request); if (!requests.IsCurrent(identity)) return;
            holdRetry = null; var old = Holds.FirstOrDefault(x => x.Id == hold.Id); if (old is not null) Holds.Remove(old); Holds.Add(hold); SelectedHold = hold;
            StatusMessage = hold.IsReleased ? "Release independently approved; history retained." : "Hold saved and remains active.";
        } catch { if (requests.IsCurrent(identity)) StatusMessage = "The hold action was refused or could not be confirmed. Refresh, or retry the unchanged action. Preservation remains enforced."; }
        finally { if (requests.IsCurrent(identity)) IsBusy = false; }
    }
    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task SavePolicyAsync()
    {
        int? days = null;
        if (!string.IsNullOrWhiteSpace(RetentionDaysText))
        {
            if (!int.TryParse(RetentionDaysText, out var parsed) || parsed is < 1 or > 36500) { StatusMessage = "Enter a valid retention period in days, or leave it blank for indefinite preservation."; return; }
            days = parsed;
        }
        var latest = Policies.Where(x => x.RecordClass == PolicyClass).Max(x => (int?)x.Version) ?? 0;
        var request = new RetentionPolicyRequest(Guid.NewGuid(), PolicyClass, latest, days, PolicyReason);
        if (policyRetry is not null && policyRetry with { OperationId = request.OperationId } == request) request = policyRetry;
        policyRetry = request; var identity = requests.Begin(); IsBusy = true;
        try { var policy = await service.SavePolicyAsync(request); if (!requests.IsCurrent(identity)) return; policyRetry = null; Policies.Add(policy); StatusMessage = "New policy version saved. Runtime retention remains disabled."; }
        catch { if (requests.IsCurrent(identity)) StatusMessage = "Policy save was refused or could not be confirmed. Refresh or retry unchanged fields."; }
        finally { if (requests.IsCurrent(identity)) IsBusy = false; }
    }
    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task PreviewAsync()
    {
        if (SelectedPolicy is null) { StatusMessage = "Select a policy version first."; return; }
        var identity = requests.Begin(); IsBusy = true;
        try { var result = await service.PreviewAsync(SelectedPolicy.Id); if (requests.IsCurrent(identity)) { Preview = result; StatusMessage = string.Join("; ", result.Blockers); } }
        catch { if (requests.IsCurrent(identity)) StatusMessage = "Preview unavailable. No destructive operation is authorized."; }
        finally { if (requests.IsCurrent(identity)) IsBusy = false; }
    }
    public void ClearForAccountSwitch() { requests.Invalidate(); SelectedHold = null; SelectedPolicy = null; Holds.Clear(); Policies.Clear(); Preview = null;
        holdRetry = null; policyRetry = null; Reason = PolicyReason = PersonIdText = RetentionDaysText = ""; RecordId = CaseReference = IssuedBy = null; IsBusy = false; }
    private void Notify() { OnPropertyChanged(nameof(CanWork)); OnPropertyChanged(nameof(CanDecideRelease));
        LoadCommand.NotifyCanExecuteChanged(); PlaceCommand.NotifyCanExecuteChanged(); AmendCommand.NotifyCanExecuteChanged(); RequestReleaseCommand.NotifyCanExecuteChanged();
        ApproveReleaseCommand.NotifyCanExecuteChanged(); RejectReleaseCommand.NotifyCanExecuteChanged(); SavePolicyCommand.NotifyCanExecuteChanged(); PreviewCommand.NotifyCanExecuteChanged(); }
}
