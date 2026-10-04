using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Services;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.Billing;

public partial class NoteAmendmentFinancialReviewsViewModel(INoteAmendmentService service) : ObservableObject
{
    private readonly LatestRequestTracker requests = new();
    private NoteAmendmentFinancialReviewRequest? pending;
    private bool publishing;
    public ObservableCollection<NoteAmendmentFinancialItem> Queue { get; } = [];
    [ObservableProperty] private NoteAmendmentFinancialItem? selected;
    [ObservableProperty] private string reason = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "Review exact approved service facts. Existing claims need an explicit linked correction; no file is sent by this action.";
    partial void OnSelectedChanged(NoteAmendmentFinancialItem? oldValue, NoteAmendmentFinancialItem? newValue)
    {
        if (publishing) return;
        if (IsBusy || Reason.Length > 0) { publishing = true; Selected = oldValue; publishing = false; StatusMessage = "Finish the financial review before changing the selected note."; }
        else { requests.Begin(); pending = null; }
    }
    public void ClearForAccountSwitch() { requests.Invalidate(); publishing = true; Queue.Clear(); Selected = null; Reason = string.Empty; pending = null; IsBusy = false; publishing = false; }
    [RelayCommand] private Task LoadAsync() => LoadPageAsync(false);
    [RelayCommand] private Task MoreAsync() => LoadPageAsync(true);
    private async Task LoadPageAsync(bool more)
    {
        if (IsBusy || Reason.Length > 0) return; var identity = requests.Begin(); IsBusy = true;
        try { var rows = await service.GetFinancialQueueAsync(more ? Queue.LastOrDefault()?.NoteId ?? 0 : 0);
            if (!requests.IsCurrent(identity)) return;
            if (!more) { publishing = true; Queue.Clear(); Selected = null; publishing = false; }
            foreach(var row in rows) Queue.Add(row); StatusMessage = rows.Count == 0 ? "No further financial amendments." : "Select the exact approved service version to review.";
        } catch { if (requests.IsCurrent(identity)) StatusMessage = "The financial queue could not be loaded. Retry."; }
        finally { if (requests.IsCurrent(identity)) IsBusy = false; }
    }
    [RelayCommand] private async Task ReviewAsync()
    {
        if (IsBusy || Selected is not { Reviewed: false } row) return;
        var identity = requests.Begin(); IsBusy = true;
        var request = new NoteAmendmentFinancialReviewRequest(Guid.NewGuid(), row.ApprovedVersionId, row.NoteRevision, Reason);
        if (pending is not null && pending with { OperationId = request.OperationId } == request) request = pending;
        pending = request;
        try { await service.ReviewFinancialAsync(row.NoteId, request); if (!requests.IsCurrent(identity)) return;
            var index = Queue.IndexOf(row); var reviewed = row with { Reviewed = true }; Queue[index] = reviewed; publishing = true; Selected = reviewed; publishing = false;
            Reason = string.Empty; pending = null; StatusMessage = row.HasClaimLine ? $"Version {row.ApprovedVersionId} reviewed. Use that version in the existing claim correction screen; original claim and files remain frozen." : "Financial version reviewed. The billing queue will validate corrected service facts when creating the claim.";
        } catch (Exception e) { if (requests.IsCurrent(identity)) StatusMessage = e is InvalidOperationException ? e.Message : "Review could not be confirmed. Retry the same review; your explanation is retained."; }
        finally { if (requests.IsCurrent(identity)) IsBusy = false; }
    }
}
