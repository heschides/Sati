using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.ClientDocuments;

public partial class AnnualDocumentsViewModel(IAnnualDocumentService service, IDocumentTemplateService templates,
    ISettingsService settings, ISessionService session, SignatureRequestsViewModel? signatures = null) : ObservableObject
{
    public SignatureRequestsViewModel? Signatures { get; } = signatures;
    private readonly LatestRequestTracker requests = new();
    private Person? person;
    private AnnualDocumentsStatusDto? status;
    private int activeTicket;
    private bool applyingCycle;
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
    public ObservableCollection<DocumentArtifactDto> Artifacts { get; } = [];
    public ObservableCollection<AnnualDocumentWorkflowItem> DocumentWorkflow { get; } = [];
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
    public string PeriodTitle
    {
        get
        {
            if (CycleStart is not DateTime start)
                return "Select a service year to review its annual forms.";

            var end = person?.EffectiveDate is DateTime effective
                ? AnnualDocumentCycle.EndInclusive(effective, start.Date)
                : start.Date.AddYears(1).AddDays(-1);
            return $"Service year: {start:MMMM d, yyyy} - {end:MMMM d, yyyy}";
        }
    }
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
    public Func<Task<(string Hash, long Length)?>>? ChooseVerificationFileAsync { get; set; }
    partial void OnIsBusyChanged(bool value) => NotifyState();
    partial void OnCycleStartChanged(DateTime? value)
    {
        if (applyingCycle) return;
        requests.Invalidate(); status = null; Artifacts.Clear(); IsBusy = false;
        Signatures?.SetContext(person?.Id ?? 0, []);
        ReceivedOn = null; GoodFaithEffortReason = ""; VerificationArtifactId = 0;
        AuthorizedRepresentativeOnFileNote = "";
        Reminder = ""; WindowDescription = ""; Message = "Select View selected year to load its form and signature status.";
        RebuildDocumentWorkflow(); OnPropertyChanged(nameof(PeriodTitle)); NotifyState();
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
    public void SetPerson(Person? selected)
    {
        requests.Invalidate(); person = selected; status = null; Artifacts.Clear();
        Signatures?.SetContext(selected?.Id ?? 0, []);
        IsBusy = false; ReceivedOn = null; GoodFaithEffortReason = ""; VerificationArtifactId = 0;
        AuthorizedRepresentativeOnFileNote = "";
        Message = ""; Reminder = ""; WindowDescription = "";
        // No suggestion until the agency's packet window loads; InitializeAsync selects it.
        CycleStart = null;
        RebuildDocumentWorkflow(); NotifyState(); _ = InitializeAsync();
    }
    private async Task InitializeAsync()
    {
        if (person?.EffectiveDate is not DateTime effective) return;
        var ticket = requests.Begin(); var id = person.Id; IsBusy = true;
        try
        {
            var policy = await settings.LoadAsync();
            var cycle = AnnualPacketWindow.SuggestedCycle(effective, DateTime.Today, policy.AnnualPacketOpenDaysBefore);
            var result = await service.GetStatusAsync(id, cycle);
            if (requests.IsCurrent(ticket))
            {
                // This response already owns the request ticket; the user-change callback must not invalidate it.
                applyingCycle = true;
                try { CycleStart = cycle; }
                finally { applyingCycle = false; }
                Apply(result);
                Message = $"Annual-form status loaded for the service year beginning {cycle:MMMM d, yyyy}.";
            }
        }
        catch (Exception) { if (requests.IsCurrent(ticket)) Message = "Annual forms could not be loaded. Check the effective date and try again."; }
        finally { if (requests.IsCurrent(ticket)) IsBusy = false; }
    }
    private void Apply(AnnualDocumentsStatusDto value)
    {
        status = value; Artifacts.Clear(); foreach (var artifact in value.Artifacts) Artifacts.Add(artifact);
        Signatures?.SetContext(person?.Id ?? 0, value.Artifacts);
        WindowDescription = value.Window.IsOpen ? $"Packet available through {value.Window.EndsOn:d}." : $"Packet opens {value.Window.OpensOn:d}.";
        Reminder = value.Reminder; RebuildDocumentWorkflow(); OnPropertyChanged(nameof(PeriodTitle)); NotifyState();
    }
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
        Task.FromResult<string?>($"Annual-form status refreshed for the service year beginning {cycle:MMMM d, yyyy}."));
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

    private void RebuildDocumentWorkflow()
    {
        DocumentWorkflow.Clear();
        foreach (var definition in AnnualWorkflowDefinitions)
        {
            var artifact = status?.Artifacts
                .Where(item => item.Kind == definition.Kind.ToString())
                .OrderByDescending(item => item.GeneratedAtUtc)
                .FirstOrDefault();
            var preparationStatus = status is null ? "Select View selected year" : DescribeArtifact(artifact);
            if (definition.Kind == AnnualDocumentKind.PrivacyPractices && artifact is not null &&
                status!.AcknowledgedArtifactIds.Contains(artifact.Id))
                preparationStatus = "Receipt recorded";
            DocumentWorkflow.Add(new(
                definition.DisplayName,
                preparationStatus,
                definition.Instructions,
                definition.Section,
                definition.ActionLabel));
        }

        if (NeedsAuthorizedRepresentative)
        {
            var artifact = status!.Artifacts
                .Where(item => item.Kind == AnnualDocumentKind.DhhsAuthorizedRepresentative.ToString())
                .OrderByDescending(item => item.GeneratedAtUtc)
                .FirstOrDefault();
            DocumentWorkflow.Add(new(
                "DHHS Authorized Representative",
                DescribeArtifact(artifact),
                "This appointment is required only once. Prepare the official form in DHHS Documents; after a signed physical copy is retained through the agency's approved process, record that fact there.",
                AnnualFormsSection.DhhsDocuments,
                "Open DHHS Documents"));
        }
    }

    private static string DescribeArtifact(DocumentArtifactDto? artifact) => artifact?.Origin switch
    {
        nameof(DocumentArtifactOrigin.Draft) => "Draft",
        nameof(DocumentArtifactOrigin.GeneratedInSati) => artifact.BlankFields.Count == 0
            ? "Ready for review"
            : "Needs completion",
        nameof(DocumentArtifactOrigin.RecordedAsExternal) => "Recorded on file",
        _ => "Not started"
    };

    private static readonly (AnnualDocumentKind Kind, string DisplayName, string Instructions,
        AnnualFormsSection Section, string ActionLabel)[] AnnualWorkflowDefinitions =
    [
        (AnnualDocumentKind.ReleaseDhhs, "DHHS release", "Complete the official state authorization, save the actual state PDF, and then handle review and signature separately.", AnnualFormsSection.DhhsDocuments, "Open DHHS Documents"),
        (AnnualDocumentKind.ReleaseMedical, "Medical Provider Release", "Choose the exact provider obligation, prepare the release, and save the generated PDF for review and signature.", AnnualFormsSection.Releases, "Open Releases"),
        (AnnualDocumentKind.ReleaseAgency, "Agency Release", "Choose the exact agency obligation, prepare the release, and save the generated PDF for review and signature.", AnnualFormsSection.Releases, "Open Releases"),
        (AnnualDocumentKind.SafetyPlan, "Safety Plan", "Complete the saved plan and obtain supervisor approval before requesting the consumer or guardian's review.", AnnualFormsSection.SafetyPlan, "Open Safety Plan"),
        (AnnualDocumentKind.PrivacyPractices, "Privacy Practices", "Generate the current approved notice, then record receipt or a good-faith delivery effort. Electronic acknowledgment remains separate.", AnnualFormsSection.PrivacyPractices, "Open Privacy Practices")
    ];
}

public enum AnnualFormsSection
{
    Overview,
    Releases,
    DhhsDocuments,
    SafetyPlan,
    PrivacyPractices
}

public sealed record AnnualDocumentWorkflowItem(
    string DisplayName,
    string Status,
    string Instructions,
    AnnualFormsSection Section,
    string ActionLabel);
