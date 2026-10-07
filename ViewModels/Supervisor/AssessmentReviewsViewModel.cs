using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models.Assessments;
using Sati.Services;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.Supervisor;

public sealed record AssessmentReadOnlyAnswer(string Location, string Prompt, string Answer);
public sealed record AssessmentReviewLocation(string Key, string Label);

public sealed partial class AssessmentReviewsViewModel(IComprehensiveAssessmentService service, ISessionService session) : ObservableObject
{
    private readonly LatestRequestTracker _queueLoads = new();
    private readonly LatestRequestTracker _selectionLoads = new();
    private (int, int)? Account => session.CurrentUser is { } user ? (user.Id, user.AgencyId) : null;
    private bool _restoringSelection;
    [ObservableProperty] private AssessmentQueueItemDto? selected;
    [ObservableProperty] private AssessmentReviewDetailsDto? details;
    [ObservableProperty] private AssessmentSubmissionDto? selectedSnapshot;
    [ObservableProperty] private AssessmentReviewEventDto? selectedFlag;
    [ObservableProperty] private string location = "document";
    [ObservableProperty] private string reviewText = string.Empty;
    [ObservableProperty] private bool blocking;
    [ObservableProperty] private bool completionAttested;
    [ObservableProperty] private DateTime? completedOn;
    [ObservableProperty] private string statusMessage = "Load submitted assessments to review.";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private int validationFocusRequest;
    public ObservableCollection<AssessmentQueueItemDto> Queue { get; } = [];
    public ObservableCollection<AssessmentSubmissionDto> Submissions { get; } = [];
    public ObservableCollection<AssessmentReadOnlyAnswer> Answers { get; } = [];
    public ObservableCollection<AssessmentReviewEventDto> History { get; } = [];
    public ObservableCollection<AssessmentReviewEventDto> OpenFlags { get; } = [];
    public ObservableCollection<AssessmentValidationIssue> ValidationIssues { get; } = [];
    public IReadOnlyList<AssessmentReviewLocation> Locations { get; } = new AssessmentReviewLocation[]
        { new("document", "Entire assessment"), new("contributors", "Contributors"), new("needs", "Identified needs") }
        .Concat(AssessmentCatalog.Sections.Select(s => new AssessmentReviewLocation($"section:{s.Title}", s.Title)))
        .Concat(AssessmentCatalog.Questions.Select(q => new AssessmentReviewLocation(q.Key, AssessmentCatalog.DisplayPrompt(q.Prompt)))).ToArray();
    public bool CanReview => !IsBusy && Details?.Assessment.Status == "ReadyForReview" &&
        SelectedSnapshot?.Id == Submissions.LastOrDefault()?.Id && session.CurrentUser is { HasSupervisorPermissions: true } actor &&
        Details.Assessment.AuthorUserId != actor.Id;
    public string VersionLabel => SelectedSnapshot is { } s
        ? $"Assessment {s.AssessmentId} · v{s.AssessmentVersion} · review {s.CycleNumber} · submission {s.Id} · SHA256 {s.ContentSha256}" : "Select a submission";
    public event Func<Task>? RecordChanged;
    public event Func<AssessmentPdfDto, Task>? PdfReady;
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanReview));
    partial void OnSelectedChanged(AssessmentQueueItemDto? oldValue, AssessmentQueueItemDto? newValue)
    {
        if (_restoringSelection) return;
        if (IsBusy || !string.IsNullOrWhiteSpace(ReviewText))
        { _restoringSelection = true; Selected = oldValue; _restoringSelection = false; StatusMessage = "Save or clear the current review text before changing assessments."; return; }
        _ = LoadSelectedAsync(newValue);
    }
    partial void OnSelectedSnapshotChanged(AssessmentSubmissionDto? value)
    {
        Answers.Clear(); OnPropertyChanged(nameof(VersionLabel)); OnPropertyChanged(nameof(CanReview));
        if (value is null) return;
        var doc = AssessmentReviewRules.Parse(value.DocumentJson);
        Answers.Add(new("contributors", "Contributors", string.Join("\n", doc.Contributors.Select(c => $"{c.Name} — {c.Relationship}"))));
        foreach (var q in AssessmentCatalog.Questions)
        {
            var a = doc.Answers[q.Key];
            var text = $"{a.Status}\n{a.Narrative}\n{a.ExceptionReason}";
            if (q.UsesSupports) text += $"\nSupports: {a.Supports}\n{a.SupportDetails}";
            if (q.Kind is AssessmentQuestionKind.YesNo or AssessmentQuestionKind.HealthConcern or AssessmentQuestionKind.Therapy)
                text += $"\nResponse: {a.YesNoResponse}; follow-up: {a.FollowUpYesNoResponse}\n{a.Details}";
            if (q.Kind == AssessmentQuestionKind.Therapy) text += $"\nFormat: {a.TherapySessionFormat}; change format: {a.WantsOtherSessionFormat}; change frequency: {a.WantsFrequencyChange}; direction: {a.TherapyFrequencyDirection}";
            foreach (var activity in a.ActivitySupportLevels) text += $"\n{activity.Key}: {activity.Value}; skills training: {a.ActivitySkillsTraining.GetValueOrDefault(activity.Key)}";
            if (!string.IsNullOrWhiteSpace(a.DissentingOpinion)) text += $"\nDiffering perspective ({a.DissentContributor}): {a.DissentingOpinion}\nDiscussion: {a.DissentDiscussion}\nUnresolved: {a.DissentUnresolved}";
            Answers.Add(new(q.Key, AssessmentCatalog.DisplayPrompt(q.Prompt, value.ConsumerName), text.Trim()));
        }
        Answers.Add(new("needs", "Identified needs", doc.Needs.Count == 0 ? doc.NoIdentifiedNeedsReason :
            string.Join("\n\n", doc.Needs.Select(n => $"{n.Type}: {n.Description}\nDesired result: {n.DesiredResult}\n{n.DescribeProvider()}"))));
    }
    [RelayCommand] private async Task LoadAsync()
    {
        if (IsBusy || !string.IsNullOrWhiteSpace(ReviewText)) { StatusMessage = "Save or clear review text before reloading."; return; }
        var request = _queueLoads.Begin(); var account = Account;
        ClearDetails(); Queue.Clear();
        try
        {
            var rows = await service.GetReviewQueueAsync();
            if (!_queueLoads.IsCurrent(request) || Account != account) return;
            foreach (var row in rows) Queue.Add(row);
            StatusMessage = $"{rows.Count} submitted assessments. The queue displays up to 200 at a time.";
        }
        catch (Exception ex) { if (_queueLoads.IsCurrent(request) && Account == account) StatusMessage = $"Could not load assessments: {ex.Message}"; }
    }
    internal async Task LoadSelectedAsync(AssessmentQueueItemDto? item)
    {
        var request = _selectionLoads.Begin(); var account = Account;
        ClearDetails();
        if (item is null) return;
        try
        {
            var details = await service.GetReviewAsync(item.AssessmentId);
            if (!_selectionLoads.IsCurrent(request) || Account != account) return;
            Apply(details); StatusMessage = "Review the exact submitted answers. Record comments, flags or a documented decision.";
        }
        catch (Exception ex) { if (_selectionLoads.IsCurrent(request) && Account == account) StatusMessage = $"Could not load this assessment: {ex.Message}"; }
    }
    private void Apply(AssessmentReviewDetailsDto details)
    {
        Details = details; Submissions.Clear(); History.Clear(); OpenFlags.Clear();
        foreach (var s in details.Submissions) Submissions.Add(s);
        foreach (var e in details.Events) History.Add(e);
        foreach (var flag in details.Events.Where(e => e.Action == "Flag" && !details.Events.Any(r => r.Action == "Resolve" && r.FlagId == e.Id))) OpenFlags.Add(flag);
        SelectedSnapshot = Submissions.LastOrDefault(); OnPropertyChanged(nameof(CanReview));
    }
    [RelayCommand] private Task CommentAsync() => ActAsync("Comment");
    [RelayCommand] private Task FlagAsync() => ActAsync("Flag");
    [RelayCommand] private Task ResolveAsync() => ActAsync("Resolve");
    [RelayCommand] private Task ReturnAsync() => ActAsync("Return");
    [RelayCommand] private Task ApproveAsync() => ActAsync("Approve");
    private async Task ActAsync(string action)
    {
        if (!CanReview || Details is null || SelectedSnapshot is null) return;
        var account = Account; var details = Details; var snapshot = SelectedSnapshot;
        IsBusy = true; ValidationIssues.Clear();
        try
        {
            var updated = await service.ReviewAsync(details.Assessment.Id, new(snapshot.Id, details.Assessment.Revision,
                snapshot.ContentSha256, action, action == "Resolve" ? SelectedFlag?.Location ?? Location : Location,
                ReviewText, action == "Flag" && Blocking, action == "Resolve" ? SelectedFlag?.Id : null,
                action == "Approve" && CompletionAttested ? CompletedOn : null, action == "Approve" && CompletionAttested));
            if (Account != account || !ReferenceEquals(Details, details)) return;
            ReviewText = ""; Blocking = false; CompletionAttested = false; CompletedOn = null;
            Apply(updated); StatusMessage = action == "Approve" ? "Approved. Form completion was recorded only if explicitly attested." : $"{action} recorded.";
            if (action is "Approve" or "Return")
            {
                var queued = Queue.FirstOrDefault(x => x.AssessmentId == details.Assessment.Id); if (queued is not null) Queue.Remove(queued);
                if (RecordChanged is not null)
                    try { await RecordChanged(); }
                    catch (Exception ex) { StatusMessage += $" The record was saved, but views could not refresh: {ex.Message}"; }
            }
        }
        catch (AssessmentValidationException ex)
        { if (Account == account) { foreach (var issue in ex.Issues) ValidationIssues.Add(issue); ValidationFocusRequest++; StatusMessage = ex.Message; } }
        catch (Exception ex) { if (Account == account) StatusMessage = $"The decision was not saved: {ex.Message}"; }
        finally { if (Account == account) IsBusy = false; }
    }
    [RelayCommand] private async Task GeneratePdfAsync()
    {
        var details = Details; var snapshot = SelectedSnapshot; var account = Account;
        if (details is null || snapshot is null || IsBusy) return;
        IsBusy = true;
        try { var pdf = await service.GeneratePdfAsync(details.Assessment.Id, snapshot.Id); if (Account == account && ReferenceEquals(Details, details) && PdfReady is not null) await PdfReady(pdf); }
        catch (Exception ex) { if (Account == account) StatusMessage = $"Could not generate PDF: {ex.Message}"; }
        finally { if (Account == account) IsBusy = false; }
    }
    private void ClearDetails()
    { Details = null; SelectedSnapshot = null; SelectedFlag = null; Submissions.Clear(); History.Clear(); OpenFlags.Clear(); Answers.Clear(); ValidationIssues.Clear(); ReviewText = ""; CompletionAttested = false; CompletedOn = null; Blocking = false; OnPropertyChanged(nameof(CanReview)); }
    public void ClearForAccountSwitch()
    { _queueLoads.Invalidate(); _selectionLoads.Invalidate(); _restoringSelection = true; Selected = null; _restoringSelection = false; Queue.Clear(); ClearDetails(); IsBusy = false; StatusMessage = "Load submitted assessments to review."; }
}
