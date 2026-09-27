using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace Sati.ViewModels.ClientDocuments;

public partial class AnnualDocumentsViewModel(IAnnualDocumentService service, IDocumentTemplateService templates,
    ISettingsService settings, ISessionService session, SignatureRequestsViewModel? signatures = null,
    AnnualFormsViewPreferenceService? viewPreferences = null) : ObservableObject
{
    public SignatureRequestsViewModel? Signatures { get; } = signatures;
    private readonly LatestRequestTracker requests = new();
    private Person? person;
    private AnnualDocumentsStatusDto? status;
    private int activeTicket;
    private bool applyingCycle;
    private bool applyingView;
    private ComplianceScheduleSettings schedule = new();
    private IReadOnlyList<DateTime> planStarts = [];
    private IReadOnlyList<PlanYear> planYears = [];
    [ObservableProperty] private DateTime? cycleStart;
    [ObservableProperty] private DateTime? receivedOn;
    [ObservableProperty] private string goodFaithEffortReason = "";
    [ObservableProperty] private string message = "";
    [ObservableProperty] private string reminder = "";
    [ObservableProperty] private string windowDescription = "";
    [ObservableProperty] private string templateBody = "";
    [ObservableProperty] private string authorizedRepresentativeOnFileNote = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private int verificationArtifactId;
    [ObservableProperty] private int selectedYearIndex = -1;
    [ObservableProperty] private AnnualFormsView view = AnnualFormsView.List;
    public ObservableCollection<DocumentArtifactDto> Artifacts { get; } = [];

    // ---- Plan-year overview -------------------------------------------------------------

    public ObservableCollection<PlanYearSection> ListSections { get; } = [];
    public ObservableCollection<PlanYearSection> PurposeCards { get; } = [];
    public ObservableCollection<PlanYearRow> NeedsYouRows { get; } = [];
    public bool HasNeedsYouRows => NeedsYouRows.Count > 0;
    /// <summary>Items not tied to a plan year, shown apart from the purpose cards.</summary>
    public ObservableCollection<PlanYearRow> OneTimeRows { get; } = [];
    public bool HasOneTimeRows => OneTimeRows.Count > 0;
    [ObservableProperty] private PlanYearTimeline? timeline;

    public PlanYear? SelectedPlanYear =>
        SelectedYearIndex >= 0 && SelectedYearIndex < planYears.Count ? planYears[SelectedYearIndex] : null;
    public bool HasPlanYears => planYears.Count > 0;
    public bool HasNoPlanYear => person is not null && person.EffectiveDate is null;
    public string YearLabel => SelectedPlanYear?.Label ?? "";
    public string YearRange => SelectedPlanYear is { } year
        ? $"{year.Start:MMM d, yyyy} – {year.EndInclusive:MMM d, yyyy}" +
          (SelectedYearIndex == 0 ? " · first plan year" : "")
        : "";
    public string YearBadge => SelectedPlanYear is not { } year ? "" : year.Position switch
    {
        PlanYearPosition.InForce when year.Start > DateTime.Today => $"Begins {year.Start:MMM d, yyyy}",
        PlanYearPosition.InForce => "In force today",
        PlanYearPosition.Past => year.NeedsYouCount == 0 ? "Complete" : $"{year.NeedsYouCount} still open",
        _ => year.WorkOpensOn is DateTime opens && opens > DateTime.Today
            ? $"Work opens {opens:MMM d, yyyy}"
            : "Preparation open"
    };
    /// <summary>Done, Open, Overdue, or ComingUp: selects the badge's colors, which the text also states.</summary>
    public string YearBadgeState => SelectedPlanYear is not { } year ? "" : year.Position switch
    {
        PlanYearPosition.InForce => "Open",
        PlanYearPosition.Past => year.NeedsYouCount == 0 ? "Done" : "Overdue",
        _ => "ComingUp"
    };
    public string SummaryText => SelectedPlanYear is not { } year ? "" :
        year.Items.Count == 0 ? "Nothing is scheduled for this plan year." :
        year.NeedsYouCount == 0 && year.ComingUpCount == 0 ? "Everything for this plan year is done." :
        string.Join(" · ", new[]
        {
            year.NeedsYouCount > 0 ? $"{year.NeedsYouCount} need{(year.NeedsYouCount == 1 ? "s" : "")} you now" : null,
            year.ComingUpCount > 0 ? $"{year.ComingUpCount} coming up" : null,
            year.DoneCount > 0 ? $"{year.DoneCount} done" : null
        }.Where(part => part is not null));
    public GridLength SummaryDoneWidth => Stars(SelectedPlanYear?.DoneCount);
    public GridLength SummaryNeedsYouWidth => Stars(SelectedPlanYear?.NeedsYouCount);
    public GridLength SummaryComingUpWidth => Stars(SelectedPlanYear?.ComingUpCount);
    public bool CanGoToPreviousYear => SelectedYearIndex > 0 && !IsBusy;
    public bool CanGoToNextYear => SelectedYearIndex >= 0 && SelectedYearIndex < planYears.Count - 1 && !IsBusy;
    /// <summary>The next plan year already has work that can be started.</summary>
    public bool NextYearHasOpenWork =>
        SelectedYearIndex >= 0 && SelectedYearIndex < planYears.Count - 1 &&
        planYears[SelectedYearIndex + 1].NeedsYouCount > 0;
    public string NextYearAccessibleName => NextYearHasOpenWork
        ? $"Next plan year, {planYears[SelectedYearIndex + 1].Label}, has work open now"
        : "Next plan year";
    /// <summary>An earlier plan year still has open work; billing checks old years too.</summary>
    public string EarlierYearNotice
    {
        get
        {
            var open = EarliestOpenEarlierYear();
            return open is null ? "" :
                $"{open.Label} still has {open.NeedsYouCount} open item{(open.NeedsYouCount == 1 ? "" : "s")}";
        }
    }
    public bool HasEarlierYearNotice => EarliestOpenEarlierYear() is not null;
    public bool IsListView { get => View == AnnualFormsView.List; set { if (value) View = AnnualFormsView.List; } }
    public bool IsTimelineView { get => View == AnnualFormsView.Timeline; set { if (value) View = AnnualFormsView.Timeline; } }
    public bool IsByPurposeView { get => View == AnnualFormsView.ByPurpose; set { if (value) View = AnnualFormsView.ByPurpose; } }

    // ---- Document workspaces (unchanged behavior) -----------------------------------------

    public bool CanSavePacket => !IsBusy && status?.Window.IsOpen == true && person?.UserId == session.CurrentUser?.Id;
    public bool CanRecordReceipt => !IsBusy && status?.Artifacts.Any(x => x.Kind == "PrivacyPractices" && x.Origin == "GeneratedInSati") == true;
    public bool CanManageTemplates => session.CurrentUser?.HasAdminPermissions == true;
    public bool NeedsAuthorizedRepresentative => status is not null && !status.AuthorizedRepresentativeOnFile;
    public bool CanRecordAuthorizedRepresentativeOnFile => NeedsAuthorizedRepresentative && !IsBusy &&
        person?.UserId == session.CurrentUser?.Id &&
        AnnualDocumentRules.ValidateExternalNote(AuthorizedRepresentativeOnFileNote) is null;
    public string AuthorizedRepresentativeRecordedMessage => status?.AuthorizedRepresentativeOnFile == true
        ? "A signed DHHS Authorized Representative form is already recorded on file. This once-only document is not repeated in each annual period."
        : "";
    public string ReceiptStatus => status?.Artifacts.FirstOrDefault(x => x.Kind == "PrivacyPractices") is { } notice &&
        status.AcknowledgedArtifactIds.Contains(notice.Id) ? "Receipt or good-faith effort is recorded for the current notice." : "Receipt or good-faith effort has not been recorded for the current notice.";
    public string TemplateValidationMessage
    {
        get
        {
            if (string.IsNullOrWhiteSpace(TemplateBody))
                return "Enter template content to validate it before publishing a new version.";
            var errors = DocumentTemplateRules.Validate(AnnualDocumentKind.PrivacyPractices, TemplateBody);
            return errors.Count == 0
                ? "Template is valid and ready to publish as a new version."
                : string.Join(" ", errors.SelectMany(item => item.Value));
        }
    }
    public bool CanPublishTemplate => CanManageTemplates &&
        DocumentTemplateRules.Validate(AnnualDocumentKind.PrivacyPractices, TemplateBody).Count == 0;
    public event Action<AgencyReleaseResult>? FileReady;
    public event Action<IReadOnlyList<DocumentArtifactDto>>? ArtifactsChanged;
    public Func<Task<(string Hash, long Length)?>>? ChooseVerificationFileAsync { get; set; }

    partial void OnIsBusyChanged(bool value)
    {
        NotifyState();
        OnPropertyChanged(nameof(CanGoToPreviousYear)); OnPropertyChanged(nameof(CanGoToNextYear));
        PreviousYearCommand.NotifyCanExecuteChanged(); NextYearCommand.NotifyCanExecuteChanged();
    }

    partial void OnCycleStartChanged(DateTime? value)
    {
        if (applyingCycle) return;
        // A cycle chosen outside the stepper (tests, other workspaces) loads that year.
        requests.Invalidate(); status = null; Artifacts.Clear(); ArtifactsChanged?.Invoke([]); IsBusy = false;
        Signatures?.SetContext(person?.Id ?? 0, []);
        ClearDocumentInputs();
        Reminder = ""; WindowDescription = ""; Message = "";
        var index = value is DateTime start ? IndexOf(start) : -1;
        applyingCycle = true;
        try { SelectedYearIndex = index; }
        finally { applyingCycle = false; }
        RebuildOverview(); NotifyState();
        if (value is DateTime cycle) _ = LoadYearAsync(cycle, requests.Begin());
    }

    partial void OnSelectedYearIndexChanged(int value)
    {
        NotifyYear();
        if (applyingCycle || value < 0 || value >= planStarts.Count) return;
        SelectYear(value);
    }

    partial void OnViewChanged(AnnualFormsView value)
    {
        OnPropertyChanged(nameof(IsListView)); OnPropertyChanged(nameof(IsTimelineView)); OnPropertyChanged(nameof(IsByPurposeView));
        if (applyingView || viewPreferences is null || session.CurrentUser is not { Id: > 0 } user) return;
        _ = viewPreferences.SaveForUserAsync(user.Id, value);
    }

    partial void OnTemplateBodyChanged(string value)
    {
        OnPropertyChanged(nameof(TemplateValidationMessage));
        OnPropertyChanged(nameof(CanPublishTemplate));
        PublishTemplateCommand.NotifyCanExecuteChanged();
    }
    partial void OnAuthorizedRepresentativeOnFileNoteChanged(string value) => NotifyState();

    private void NotifyState()
    {
        OnPropertyChanged(nameof(CanSavePacket)); OnPropertyChanged(nameof(CanRecordReceipt));
        OnPropertyChanged(nameof(CanManageTemplates)); OnPropertyChanged(nameof(ReceiptStatus));
        OnPropertyChanged(nameof(NeedsAuthorizedRepresentative));
        OnPropertyChanged(nameof(CanRecordAuthorizedRepresentativeOnFile));
        OnPropertyChanged(nameof(AuthorizedRepresentativeRecordedMessage));
        RecordAuthorizedRepresentativeOnFileCommand.NotifyCanExecuteChanged();
    }

    private void NotifyYear()
    {
        foreach (var name in new[]
                 {
                     nameof(SelectedPlanYear), nameof(HasPlanYears), nameof(HasNoPlanYear), nameof(YearLabel),
                     nameof(YearRange), nameof(YearBadge), nameof(YearBadgeState), nameof(SummaryText),
                     nameof(SummaryDoneWidth), nameof(SummaryNeedsYouWidth), nameof(SummaryComingUpWidth),
                     nameof(CanGoToPreviousYear), nameof(CanGoToNextYear), nameof(NextYearHasOpenWork),
                     nameof(NextYearAccessibleName), nameof(EarlierYearNotice), nameof(HasEarlierYearNotice)
                 })
            OnPropertyChanged(name);
        PreviousYearCommand.NotifyCanExecuteChanged(); NextYearCommand.NotifyCanExecuteChanged();
        JumpToEarlierOpenYearCommand.NotifyCanExecuteChanged();
    }

    public void SetPerson(Person? selected)
    {
        requests.Invalidate(); person = selected; status = null; Artifacts.Clear();
        ArtifactsChanged?.Invoke([]);
        Signatures?.SetContext(selected?.Id ?? 0, []);
        IsBusy = false; ClearDocumentInputs();
        Message = ""; Reminder = ""; WindowDescription = "";
        planStarts = []; planYears = [];
        applyingCycle = true;
        try { CycleStart = null; SelectedYearIndex = -1; }
        finally { applyingCycle = false; }
        RebuildOverview(); NotifyState(); NotifyYear();
        _ = InitializeAsync();
    }

    /// <summary>
    /// Rebuilds the overview from the client record, which other workspaces update when
    /// they record a completion, and reloads the selected year's documents.
    /// </summary>
    public void RefreshOverview()
    {
        if (person is null || CycleStart is not DateTime cycle || IsBusy) return;
        _ = LoadYearAsync(cycle, requests.Begin());
    }

    private async Task InitializeAsync()
    {
        if (person?.EffectiveDate is not DateTime effective) return;
        var ticket = requests.Begin(); IsBusy = true;
        try
        {
            if (viewPreferences is not null && session.CurrentUser is { Id: > 0 } user)
            {
                var remembered = await viewPreferences.LoadForUserAsync(user.Id);
                if (requests.IsCurrent(ticket))
                {
                    applyingView = true;
                    try { View = remembered; }
                    finally { applyingView = false; }
                }
            }
            var policy = await settings.LoadAsync();
            if (!requests.IsCurrent(ticket)) return;
            schedule = FormDueDateCalculator.ToSchedule(policy);
            planStarts = PlanYearOverview.Starts(effective, DateTime.Today);
            // The overview opens on the plan year in force today.
            var inForce = PlanYearOverview.InForceStart(effective, DateTime.Today);
            await LoadYearCoreAsync(inForce, ticket);
        }
        catch (Exception) { if (requests.IsCurrent(ticket)) Message = "Annual forms could not be loaded. Check the effective date and try again."; }
        finally { if (requests.IsCurrent(ticket)) IsBusy = false; }
    }

    private void SelectYear(int index)
    {
        if (person is null || index < 0 || index >= planStarts.Count) return;
        _ = LoadYearAsync(planStarts[index], requests.Begin());
    }

    private async Task LoadYearAsync(DateTime cycle, int ticket)
    {
        IsBusy = true;
        try { await LoadYearCoreAsync(cycle, ticket); }
        catch (Exception) { if (requests.IsCurrent(ticket)) Message = "This plan year could not be loaded. Try again."; }
        finally { if (requests.IsCurrent(ticket)) IsBusy = false; }
    }

    private async Task LoadYearCoreAsync(DateTime cycle, int ticket)
    {
        if (person is null) return;
        var id = person.Id;
        applyingCycle = true;
        try { CycleStart = cycle; SelectedYearIndex = IndexOf(cycle); }
        finally { applyingCycle = false; }
        status = null; Artifacts.Clear(); ArtifactsChanged?.Invoke([]); ClearDocumentInputs();
        RebuildOverview(); NotifyYear();
        var result = await service.GetStatusAsync(id, cycle);
        if (requests.IsCurrent(ticket)) { Apply(result); Message = ""; }
    }

    private void Apply(AnnualDocumentsStatusDto value)
    {
        status = value; Artifacts.Clear(); foreach (var artifact in value.Artifacts) Artifacts.Add(artifact);
        ArtifactsChanged?.Invoke(value.Artifacts);
        Signatures?.SetContext(person?.Id ?? 0, value.Artifacts);
        WindowDescription = value.Window.IsOpen ? $"Packet available through {value.Window.EndsOn:d}." : $"Packet opens {value.Window.OpensOn:d}.";
        Reminder = value.Reminder; RebuildOverview(); NotifyState(); NotifyYear();
    }

    private void ClearDocumentInputs()
    {
        ReceivedOn = null; GoodFaithEffortReason = ""; VerificationArtifactId = 0;
        AuthorizedRepresentativeOnFileNote = "";
    }

    private int IndexOf(DateTime start)
    {
        for (var i = 0; i < planStarts.Count; i++)
            if (planStarts[i].Date == start.Date) return i;
        return -1;
    }

    private PlanYear? EarliestOpenEarlierYear()
    {
        for (var i = 0; i < SelectedYearIndex && i < planYears.Count; i++)
            if (planYears[i].NeedsYouCount > 0) return planYears[i];
        return null;
    }

    private static GridLength Stars(int? count) => new(Math.Max(0, count ?? 0), GridUnitType.Star);

    private void RebuildOverview()
    {
        planYears = BuildPlanYears();
        ListSections.Clear(); PurposeCards.Clear(); NeedsYouRows.Clear(); OneTimeRows.Clear();
        if (SelectedPlanYear is not { } year)
        {
            Timeline = null;
            OnPropertyChanged(nameof(HasNeedsYouRows)); OnPropertyChanged(nameof(HasOneTimeRows));
            return;
        }

        var rows = year.Items.Select(item => new PlanYearRow(item)).ToList();
        foreach (var row in rows.Where(row => row.Item.NeedsYou)) NeedsYouRows.Add(row);
        foreach (var row in rows.Where(row => row.Item.Group == PlanYearGroup.OneTime)) OneTimeRows.Add(row);

        AddSection(ListSections, "Needs you now", "Start here.", rows.Where(row => row.Item.NeedsYou)
            .OrderBy(row => row.Item.State == PlanYearItemState.Overdue ? 0 : 1)
            .ThenBy(row => row.Item.DueOn ?? DateTime.MaxValue));
        AddSection(ListSections, "Coming up", "Nothing to do yet.", rows.Where(row => row.Item.State == PlanYearItemState.ComingUp)
            .OrderBy(row => row.Item.DueOn));
        AddSection(ListSections, "Done", "Recorded for this plan year.", rows.Where(row => row.Item.State == PlanYearItemState.Done)
            .OrderBy(row => row.Item.CompletedOn));

        foreach (var (group, title, hint) in new[]
                 {
                     (PlanYearGroup.Plan, "The plan", "Written before the year begins."),
                     (PlanYearGroup.Releases, "Permission to share information", "DHHS, plus one release for each provider."),
                     (PlanYearGroup.SafetyAndNotices, "Safety and notices", "Reviewed with the person each year."),
                     (PlanYearGroup.CheckIns, "Check-ins every 90 days", "During the year.")
                 })
        {
            var members = rows.Where(row => row.Item.Group == group).ToList();
            if (members.Count == 0) continue;
            PurposeCards.Add(new PlanYearSection(title, hint, members, PurposeCount(group, members)));
        }

        var next = SelectedYearIndex + 1 < planYears.Count ? planYears[SelectedYearIndex + 1] : null;
        Timeline = PlanYearTimeline.Build(year, next, DateTime.Today);
        OnPropertyChanged(nameof(HasNeedsYouRows)); OnPropertyChanged(nameof(HasOneTimeRows));
    }

    private static void AddSection(ObservableCollection<PlanYearSection> target, string title, string hint, IEnumerable<PlanYearRow> rows)
    {
        var list = rows.ToList();
        if (list.Count > 0) target.Add(new PlanYearSection(title, hint, list, ""));
    }

    private static string PurposeCount(PlanYearGroup group, IReadOnlyList<PlanYearRow> members)
    {
        var done = members.Count(row => row.Item.State == PlanYearItemState.Done);
        if (group == PlanYearGroup.CheckIns && done < members.Count &&
            members.Where(row => row.Item.State != PlanYearItemState.Done).MinBy(row => row.Item.DueOn) is { Item.DueOn: DateTime due })
            return $"Next due {due:MMM d}";
        return group == PlanYearGroup.Releases
            ? $"{done} of {members.Count} signed"
            : $"{done} of {members.Count} done";
    }

    private IReadOnlyList<PlanYear> BuildPlanYears()
    {
        if (person?.EffectiveDate is not DateTime effective || planStarts.Count == 0) return [];
        var forms = person.Forms.Select(form => new ComplianceFormSnapshot(
            form.Type.ToString(),
            form.DueDate,
            form.CompletedDate,
            form.OpenedDate,
            form.Id > 0 ? $"form:{form.Id}" : null,
            TargetEffectiveDate: form.TargetEffectiveDate == default ? null : form.TargetEffectiveDate)).ToList();
        var releases = ((IEventSource)person).ReleaseComplianceFacts.ToList();
        var documents = status is null ? null
            : new PlanYearDocumentFacts(status.Artifacts, status.AcknowledgedArtifactIds, status.AuthorizedRepresentativeOnFile);
        return planStarts
            .Select(start => PlanYearOverview.Build(effective, start, DateTime.Today, forms, releases, schedule,
                CycleStart?.Date == start.Date ? documents : null))
            .ToList();
    }

    [RelayCommand(CanExecute = nameof(CanGoToPreviousYear))]
    private void PreviousYear() { if (SelectedYearIndex > 0) SelectedYearIndex--; }

    [RelayCommand(CanExecute = nameof(CanGoToNextYear))]
    private void NextYear() { if (SelectedYearIndex < planStarts.Count - 1) SelectedYearIndex++; }

    [RelayCommand(CanExecute = nameof(HasEarlierYearNotice))]
    private void JumpToEarlierOpenYear()
    {
        if (EarliestOpenEarlierYear() is { } year) SelectedYearIndex = IndexOf(year.Start);
    }

    // ---- Document commands ----------------------------------------------------------------

    private async Task Run(Func<int, DateTime, Task<string?>> operation)
    {
        if (person is null || CycleStart is null || IsBusy) return;
        var id = person.Id; var cycle = CycleStart.Value.Date; var ticket = requests.Begin(); activeTicket = ticket; IsBusy = true; Message = "";
        try
        {
            var resultMessage = await operation(id, cycle);
            var updated = await service.GetStatusAsync(id, cycle);
            if (requests.IsCurrent(ticket)) { Apply(updated); Message = resultMessage ?? ""; }
        }
        catch (Exception) { if (requests.IsCurrent(ticket)) Message = "The operation could not be completed. Check the dates and required fields, then reload."; }
        finally { if (requests.IsCurrent(ticket)) IsBusy = false; }
    }
    [RelayCommand] private Task ReloadAsync() => Run((_, cycle) =>
        Task.FromResult<string?>(null));
    public Task RefreshCurrentAsync() => Run((_, _) => Task.FromResult<string?>(null));
    [RelayCommand] private Task GenerateNoticeAsync()
    {
        var ticket = 0;
        return Run(async (id, cycle) => { ticket = activeTicket; var result = await templates.GeneratePrivacyPracticesAsync(id, cycle);
            if (requests.IsCurrent(ticket)) FileReady?.Invoke(result); return "Notice generated. Record its receipt separately."; });
    }
    [RelayCommand] private Task SavePacketAsync()
    {
        return Run(async (id, cycle) => { var ticket = activeTicket; var result = await service.SavePacketAsync(id, cycle);
            if (requests.IsCurrent(ticket)) FileReady?.Invoke(result); return "Packet generated. Review MANIFEST.txt for outstanding work."; });
    }
    [RelayCommand] private Task AcknowledgeAsync()
    {
        var notice = status?.Artifacts.FirstOrDefault(x => x.Kind == "PrivacyPractices" && x.Origin == "GeneratedInSati");
        if (notice is null) return Task.CompletedTask;
        var request = new AcknowledgeDocumentRequest(notice.Id, ReceivedOn, GoodFaithEffortReason);
        return Run(async (id, _) => { await service.AcknowledgeAsync(id, request); return "Privacy notice receipt recorded."; });
    }
    [RelayCommand(CanExecute = nameof(CanRecordAuthorizedRepresentativeOnFile))]
    private Task RecordAuthorizedRepresentativeOnFileAsync() =>
        Run(async (id, cycle) =>
        {
            await service.RecordAuthorizedRepresentativeOnFileAsync(
                id, cycle, AuthorizedRepresentativeOnFileNote.Trim());
            AuthorizedRepresentativeOnFileNote = "";
            return "The signed DHHS Authorized Representative form is recorded as already on file.";
        });
    [RelayCommand] private async Task VerifyAsync()
    {
        if (ChooseVerificationFileAsync is null || VerificationArtifactId <= 0) { Message = "Enter the artifact ID from the manifest or list."; return; }
        if (IsBusy) return;
        var artifactId = VerificationArtifactId; var ticket = requests.Begin();
        var result = await ChooseVerificationFileAsync();
        if (result is null || !requests.IsCurrent(ticket)) return;
        await Run(async (id, _) => (await service.VerifyAsync(id, new(artifactId, result.Value.Hash, result.Value.Length))).Message);
    }
    [RelayCommand] private async Task LoadTemplateAsync()
    {
        if (!CanManageTemplates) return;
        try { var versions = await templates.GetVersionsAsync(AnnualDocumentKind.PrivacyPractices);
            TemplateBody = versions.OrderByDescending(x => x.AgencyId is not null).ThenByDescending(x => x.Version).FirstOrDefault()?.Body ?? ""; }
        catch (Exception) { Message = "The privacy template could not be loaded."; }
    }
    [RelayCommand(CanExecute = nameof(CanPublishTemplate))] private async Task PublishTemplateAsync()
    {
        if (!CanManageTemplates) return;
        try { await templates.PublishAsync(AnnualDocumentKind.PrivacyPractices, TemplateBody); Message = "A new agency template version was published."; }
        catch (Exception) { Message = "The template was not published. Check its text and supported tokens."; }
    }
}

public enum AnnualFormsSection
{
    Overview,
    Releases,
    DhhsDocuments,
    SafetyPlan,
    PrivacyPractices
}

/// <summary>One item as the overview shows it. <see cref="StateKey"/> selects colors; the label always says the same.</summary>
public sealed class PlanYearRow(PlanYearItem item)
{
    public PlanYearItem Item { get; } = item;
    public string Title => Item.Title;
    public string WhenText => Item.WhenText;
    public string Detail => Item.Detail;
    public bool HasDetail => Item.Detail.Length > 0;
    public string StateLabel => Item.StateLabel;
    public string StateKey => Item.State.ToString();
    public string ActionLabel => Item.ActionLabel;
    public bool HasAction => Item.ActionLabel.Length > 0;
    public bool IsPrimaryAction => Item.NeedsYou;
    public string Marker => Item.State switch
    {
        PlanYearItemState.Done => "✓",
        PlanYearItemState.Overdue or PlanYearItemState.Open => "!",
        _ => "•"
    };
    public string QuickActionLabel => $"{Item.ActionLabel}: {Item.Title}";
    public string AccessibleName => $"{Item.Title}. {Item.StateLabel}. {Item.WhenText}." +
                                    (Item.Detail.Length > 0 ? $" {Item.Detail}." : "");
}

public sealed record PlanYearSection(string Title, string Hint, IReadOnlyList<PlanYearRow> Rows, string CountText)
{
    public bool HasOpenWork => Rows.Any(row => row.Item.NeedsYou);
    public bool IsAllDone => Rows.All(row => row.Item.State == PlanYearItemState.Done);
}
