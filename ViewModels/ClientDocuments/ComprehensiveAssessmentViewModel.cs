using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Contracts.V1;
using Sati.Models;
using Sati.Models.Assessments;
using Sati.Services;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace Sati.ViewModels.ClientDocuments;

public sealed partial class ComprehensiveAssessmentViewModel : ObservableObject
{
    private readonly IComprehensiveAssessmentService _service;
    private readonly ISessionService _session;
    private readonly IConsumerProviderService _consumerProviders;
    private readonly IProviderService _providers;
    private readonly DispatcherTimer _saveTimer;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly LatestRequestTracker _loads = new();
    private (int, int)? _loadedAccount;
    private ComprehensiveAssessment? _record;
    private AssessmentDocument _document = new();
    private bool _loading;
    private int _changeVersion;

    [ObservableProperty] private string personName = "Select a consumer to begin.";
    [ObservableProperty] private bool hasPerson;
    [ObservableProperty] private bool canEdit;
    [ObservableProperty] private string saveStatus = "Not loaded";
    [ObservableProperty] private AssessmentSectionViewModel? selectedSection;
    [ObservableProperty] private string noIdentifiedNeedsReason = string.Empty;
    [ObservableProperty] private Form? selectedAnnualForm;
    [ObservableProperty] private AssessmentReviewEventDto? selectedReviewFlag;
    [ObservableProperty] private string flagResponse = string.Empty;
    [ObservableProperty] private int validationFocusRequest;
    public ObservableCollection<AssessmentSectionViewModel> Sections { get; } = [];
    public ObservableCollection<AssessmentContributorViewModel> Contributors { get; } = [];
    public ObservableCollection<AssessmentNeedViewModel> Needs { get; } = [];
    public ObservableCollection<AssessmentProviderOption> ProviderOptions { get; } = [];
    public ObservableCollection<Form> AnnualForms { get; } = [];
    public ObservableCollection<AssessmentValidationIssue> ValidationIssues { get; } = [];
    public ObservableCollection<AssessmentSubmissionDto> Submissions { get; } = [];
    public ObservableCollection<AssessmentReviewEventDto> ReviewHistory { get; } = [];
    public ObservableCollection<AssessmentReviewEventDto> OpenFlags { get; } = [];
    public event Func<AssessmentPdfDto, Task>? PdfReady;
    public bool CanCreateVersion => _record?.Status == AssessmentStatus.Approved;
    public bool CanRespond => _record?.Status == AssessmentStatus.Returned;
    public bool CanReopenLegacy => _record is not null && AssessmentReviewRules.CanReopenLegacy(_record.Status.ToString(), Submissions.Count > 0);
    public bool HasProviderOptions => ProviderOptions.Count > 0;
    public Array AnswerStatuses => Enum.GetValues<AssessmentAnswerStatus>();
    public Array NeedTypes => Enum.GetValues<AssessmentNeedType>();
    public Array TherapySessionFormats => Enum.GetValues<TherapySessionFormat>().Where(v => v != TherapySessionFormat.NotSelected).ToArray();
    public Array TherapyFrequencyDirections => Enum.GetValues<TherapyFrequencyDirection>().Where(v => v != TherapyFrequencyDirection.NotSelected).ToArray();
    private (int, int)? Account => _session.CurrentUser is User user ? (user.Id, user.AgencyId) : null;

    public ComprehensiveAssessmentViewModel(IComprehensiveAssessmentService service, ISessionService session,
        IConsumerProviderService consumerProviders, IProviderService providers)
    {
        _service = service; _session = session; _consumerProviders = consumerProviders; _providers = providers;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _saveTimer.Tick += async (_, _) => { _saveTimer.Stop(); await SaveAsync(); };
        BuildSections(Sections, ScheduleSave); SelectedSection = Sections.FirstOrDefault();
    }
    partial void OnSelectedSectionChanged(AssessmentSectionViewModel? value) => OnPropertyChanged(nameof(IsSummarySelected));
    partial void OnNoIdentifiedNeedsReasonChanged(string value) => ScheduleSave();
    public bool IsSummarySelected => SelectedSection?.Title == "Summary & needs";

    public async Task LoadPersonAsync(Person? person)
    {
        var request = _loads.Begin(); var account = Account;
        await _loadGate.WaitAsync();
        try
        {
            if (!_loads.IsCurrent(request) || Account != account) return;
            _saveTimer.Stop();
            if (_record is not null && _loadedAccount == account && CanEdit && !await SaveAsync())
            { if (_loads.IsCurrent(request)) SaveStatus = "The outgoing draft could not be saved. Retry before switching assessments."; return; }
            if (!_loads.IsCurrent(request) || Account != account) return;
            _loading = true;
            _record = null; _document = new(); _loadedAccount = account;
            PersonName = person?.FullName ?? "Select a consumer to begin."; HasPerson = person is not null;
            foreach (var q in Sections.SelectMany(s => s.Questions)) { q.SetPerson(person); q.Load(new()); }
            Contributors.Clear(); Needs.Clear(); ProviderOptions.Clear(); AnnualForms.Clear(); Submissions.Clear();
            ReviewHistory.Clear(); OpenFlags.Clear(); ValidationIssues.Clear(); NoIdentifiedNeedsReason = ""; SelectedAnnualForm = null;
            OnPropertyChanged(nameof(HasProviderOptions));
            var user = _session.CurrentUser;
            CanEdit = person is not null && user is { HasCaseManagerPermissions: true } && person.UserId == user.Id;
            if (person is null || user is null) { SaveStatus = "Not loaded"; return; }
            if (!CanEdit) { SaveStatus = "Read only — this consumer is not on your caseload"; return; }
            foreach (var form in person.Forms.Where(f => f.Type == FormType.ComprehensiveAssessment && f.TargetEffectiveDate != default).OrderBy(f => f.TargetEffectiveDate)) AnnualForms.Add(form);
            SelectedAnnualForm = AnnualForms.Count == 1 ? AnnualForms[0] : null;
            await LoadProviderOptionsAsync(person.Id, request);
            if (!_loads.IsCurrent(request) || Account != account) return;
            var latest = await _service.GetLatestForAgendaAsync(person.Id);
            if (!_loads.IsCurrent(request) || Account != account) return;
            var record = latest is { Status: AssessmentStatus.ReadyForReview or AssessmentStatus.Approved }
                ? latest : await _service.GetOrCreateDraftAsync(person.Id, user.Id);
            if (!_loads.IsCurrent(request) || Account != account) return;
            _record = record; _document = AssessmentReviewRules.Parse(record.DocumentJson); ApplyDocument();
            CanEdit = AssessmentReviewRules.CanEdit(record.Status.ToString());
            SaveStatus = $"{record.Status} v{record.Version} · All changes saved";
            await LoadReviewHistoryAsync(record, request);
        }
        catch (Exception ex)
        {
            if (_loads.IsCurrent(request) && Account == account)
            { CanEdit = false; var reference = AppErrorLog.Record(ex, "client-documents.assessment.load"); SaveStatus = $"The assessment could not be loaded. Reference {reference}."; }
        }
        finally
        {
            _loading = false; RefreshProgress(); OnPropertyChanged(nameof(CanCreateVersion)); OnPropertyChanged(nameof(CanRespond)); OnPropertyChanged(nameof(CanReopenLegacy));
            _loadGate.Release();
        }
    }
    private async Task LoadProviderOptionsAsync(int personId, int request)
    {
        var account = Account;
        try
        {
            var directory = (await _providers.GetAllAsync()).ToAffiliationNodes();
            var links = await _consumerProviders.GetByPersonAsync(personId);
            if (!_loads.IsCurrent(request) || Account != account) return;
            foreach (var option in links.Where(link => link.IsActive).Select(link => new AssessmentProviderOption(ProviderAffiliation.Snapshot(link.ProviderId, directory)))
                .Where(option => option.ProviderId != 0).OrderBy(option => option.Display, StringComparer.CurrentCultureIgnoreCase)) ProviderOptions.Add(option);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        { if (_loads.IsCurrent(request)) SaveStatus = "Provider choices could not be loaded."; }
        OnPropertyChanged(nameof(HasProviderOptions));
    }
    [RelayCommand] public void AddContributor()
    { if (!CanEdit) return; Contributors.Add(new(new(), ScheduleSave, RemoveContributor)); ScheduleSave(); }
    private void RemoveContributor(AssessmentContributorViewModel row) { if (!CanEdit) return; Contributors.Remove(row); ScheduleSave(); }
    [RelayCommand] public void AddNeed()
    { if (!CanEdit) return; Needs.Add(new(new(), ScheduleSave, RemoveNeed, ProviderOptions)); ScheduleSave(); }
    private void RemoveNeed(AssessmentNeedViewModel row) { if (!CanEdit) return; Needs.Remove(row); ScheduleSave(); }
    private void ApplyDocument()
    {
        NoIdentifiedNeedsReason = _document.NoIdentifiedNeedsReason;
        foreach (var q in Sections.SelectMany(s => s.Questions)) q.Load(_document.Answers.GetValueOrDefault(q.Key) ?? new());
        foreach (var c in _document.Contributors) Contributors.Add(new(c, ScheduleSave, RemoveContributor));
        foreach (var n in _document.Needs) Needs.Add(new(n, ScheduleSave, RemoveNeed, ProviderOptions));
    }
    private AssessmentDocument CaptureDocument() => new()
    {
        NoIdentifiedNeedsReason = NoIdentifiedNeedsReason,
        Contributors = Contributors.Select(c => c.ToModel()).ToList(), Needs = Needs.Select(n => n.ToModel()).ToList(),
        Answers = Sections.SelectMany(s => s.Questions).ToDictionary(q => q.Key, q => q.ToModel())
    };
    private void ScheduleSave()
    {
        if (_loading || !CanEdit || _record is null) return;
        _changeVersion++; SaveStatus = "Saving…"; _saveTimer.Stop(); _saveTimer.Start(); RefreshProgress();
    }
    private async Task<bool> SaveAsync(bool submitting = false)
    {
        var record = _record; var account = Account;
        if (_loading || (!CanEdit && !submitting) || record is null) return false;
        await _saveGate.WaitAsync();
        try
        {
            if (!ReferenceEquals(_record, record) || account != _loadedAccount) return false;
            var document = CaptureDocument(); var changes = _changeVersion;
            await _service.SaveDocumentAsync(record, document);
            if (ReferenceEquals(_record, record) && Account == account)
            { _document = document; SaveStatus = $"Draft v{record.Version} · All changes saved"; }
            return ReferenceEquals(_record, record) && Account == account && changes == _changeVersion;
        }
        catch (Exception ex)
        { if (ReferenceEquals(_record, record) && Account == account) SaveStatus = $"Could not save: {ex.Message}"; return false; }
        finally { _saveGate.Release(); }
    }
    [RelayCommand] private async Task SubmitForReviewAsync()
    {
        var record = _record; var account = Account; var form = SelectedAnnualForm;
        if (record is null || account is null || !CanEdit || _loading) return;
        ValidationIssues.Clear(); foreach (var issue in AssessmentReviewRules.Validate(CaptureDocument())) ValidationIssues.Add(issue);
        if (form is null) ValidationIssues.Add(new("form", "Choose the annual assessment Form this version documents."));
        if (ValidationIssues.Count > 0) { SaveStatus = "Resolve the validation summary before submission."; ValidationFocusRequest++; return; }
        _saveTimer.Stop(); CanEdit = false;
        if (!await SaveAsync(submitting: true)) { if (ReferenceEquals(_record, record) && Account == account) CanEdit = true; return; }
        if (!ReferenceEquals(_record, record) || Account != account) return;
        try
        {
            record.SubmissionRequest = new(record.Revision, AssessmentReviewRules.Hash(record.DocumentJson), form!.Id, form.TargetEffectiveDate, form.DueDate);
            await _service.SubmitForReviewAsync(record);
            if (!ReferenceEquals(_record, record) || Account != account) return;
            SaveStatus = "Submitted for supervisor review"; await LoadReviewHistoryAsync(record, _loads.Begin());
        }
        catch (AssessmentValidationException ex)
        { if (ReferenceEquals(_record, record) && Account == account) { foreach (var issue in ex.Issues) ValidationIssues.Add(issue); ValidationFocusRequest++; CanEdit = true; SaveStatus = ex.Message; } }
        catch (Exception ex)
        { if (ReferenceEquals(_record, record) && Account == account) { CanEdit = true; SaveStatus = $"Could not submit: {ex.Message}"; } }
    }
    private async Task LoadReviewHistoryAsync(ComprehensiveAssessment record, int request)
    {
        var account = Account;
        try
        {
            var details = await _service.GetReviewAsync(record.Id);
            if (!_loads.IsCurrent(request) || !ReferenceEquals(_record, record) || Account != account) return;
            Submissions.Clear(); ReviewHistory.Clear(); OpenFlags.Clear();
            foreach (var s in details.Submissions) Submissions.Add(s);
            foreach (var e in details.Events) ReviewHistory.Add(e);
            foreach (var flag in details.Events.Where(e => e.Action == "Flag" && !details.Events.Any(r => r.Action == "Resolve" && r.FlagId == e.Id))) OpenFlags.Add(flag);
            var latest = details.Submissions.LastOrDefault(); if (latest is not null) SelectedAnnualForm = AnnualForms.FirstOrDefault(f => f.Id == latest.FormId);
            OnPropertyChanged(nameof(CanReopenLegacy));
        }
        catch (NotSupportedException) { }
        catch (Exception ex) { if (_loads.IsCurrent(request) && Account == account) SaveStatus = $"Review history could not be read: {ex.Message}"; }
    }
    [RelayCommand] private async Task ReopenLegacyAsync()
    {
        var record = _record; var account = Account;
        if (!CanReopenLegacy || record is null || _loading) return;
        var request = _loads.Begin();
        try
        {
            var updated = await _service.ReopenLegacyAsync(record.Id, record.Revision);
            if (!_loads.IsCurrent(request) || !ReferenceEquals(_record, record) || Account != account) return;
            record.Status = Enum.Parse<AssessmentStatus>(updated.Status); record.Revision = updated.Revision;
            record.UpdatedAt = updated.UpdatedAt; CanEdit = true;
            SaveStatus = "Legacy submission reopened. Complete the document and select its annual Form before resubmitting.";
            OnPropertyChanged(nameof(CanReopenLegacy)); OnPropertyChanged(nameof(CanRespond));
        }
        catch (Exception ex) { if (_loads.IsCurrent(request) && Account == account) SaveStatus = $"Could not reopen the legacy submission: {ex.Message}"; }
    }
    [RelayCommand] private async Task RespondToFlagAsync()
    {
        var record = _record; var snapshot = Submissions.LastOrDefault(); var flag = SelectedReviewFlag; var account = Account;
        if (!CanRespond || record is null || snapshot is null || flag is null || !await SaveAsync()) return;
        try
        {
            var details = await _service.ReviewAsync(record.Id, new(snapshot.Id, record.Revision, snapshot.ContentSha256, "Respond", flag.Location, FlagResponse, FlagId: flag.Id));
            if (!ReferenceEquals(_record, record) || Account != account) return;
            record.Revision = details.Assessment.Revision; FlagResponse = ""; await LoadReviewHistoryAsync(record, _loads.Begin());
        }
        catch (Exception ex) { if (ReferenceEquals(_record, record) && Account == account) SaveStatus = $"Could not record response: {ex.Message}"; }
    }
    [RelayCommand] private async Task CreateNewVersionAsync()
    {
        if (!CanCreateVersion || _record is null || _session.CurrentUser is not User user) return;
        var current = _record; var request = _loads.Begin(); var account = Account;
        try
        {
            var draft = await _service.GetOrCreateDraftAsync(current.PersonId, user.Id);
            if (!_loads.IsCurrent(request) || Account != account) return;
            _loading = true; _record = draft; _document = AssessmentReviewRules.Parse(draft.DocumentJson);
            Contributors.Clear(); Needs.Clear(); ApplyDocument(); CanEdit = true;
            Submissions.Clear(); ReviewHistory.Clear(); OpenFlags.Clear(); SaveStatus = $"Draft v{draft.Version} · All changes saved";
        }
        catch (Exception ex) { if (_loads.IsCurrent(request) && Account == account) SaveStatus = $"Could not create a new version: {ex.Message}"; }
        finally { _loading = false; RefreshProgress(); OnPropertyChanged(nameof(CanCreateVersion)); OnPropertyChanged(nameof(CanRespond)); }
    }
    [RelayCommand] private async Task GeneratePdfAsync(AssessmentSubmissionDto? snapshot)
    {
        if (_record is null || snapshot is null) return; var record = _record; var account = Account;
        try { var pdf = await _service.GeneratePdfAsync(record.Id, snapshot.Id); if (ReferenceEquals(_record, record) && Account == account && PdfReady is not null) await PdfReady(pdf); }
        catch (Exception ex) { if (ReferenceEquals(_record, record) && Account == account) SaveStatus = $"Could not generate PDF: {ex.Message}"; }
    }
    private void RefreshProgress()
    {
        foreach (var section in Sections) section.RefreshProgress();
        OnPropertyChanged(nameof(AnsweredCount)); OnPropertyChanged(nameof(TotalCount)); OnPropertyChanged(nameof(IsComplete)); OnPropertyChanged(nameof(CompletionText));
    }
    public int AnsweredCount => Sections.Sum(s => s.Questions.Count(q => q.IsAddressed));
    public int TotalCount => AssessmentCatalog.Questions.Count;
    public bool IsComplete => HasPerson && AssessmentReviewRules.Validate(CaptureDocument()).Count == 0;
    public string CompletionText => $"{AnsweredCount} of {TotalCount} questions addressed";
    internal static AssessmentProgress CalculateProgress(AssessmentDocument document) => new(
        AssessmentCatalog.Questions.Count(q => document.Answers.TryGetValue(q.Key, out var a) && a is not null && AssessmentReviewRules.IsAddressed(q, a)), AssessmentCatalog.Questions.Count);
    private static void BuildSections(ICollection<AssessmentSectionViewModel> target, Action changed)
    {
        foreach (var section in AssessmentCatalog.Sections) target.Add(new(section.Title, section.Subtitle,
            section.Questions.Select(q => new AssessmentQuestionViewModel(q.Key, q.Prompt, q.WhyAsked, q.CompleteAnswerIncludes, q.Avoid, q.UsesSupports, q.Kind, q.Activities, changed))));
    }
}
internal sealed record AssessmentProgress(int AnsweredCount, int TotalCount)
{
    public string Text => $"{AnsweredCount} of {TotalCount} questions addressed";
}

public sealed partial class AssessmentSectionViewModel(string title, string subtitle, IEnumerable<AssessmentQuestionViewModel> questions) : ObservableObject
{
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
    public ObservableCollection<AssessmentQuestionViewModel> Questions { get; } = new(questions);
    public string ProgressText => $"{Questions.Count(q => q.IsAddressed)}/{Questions.Count}";
    public void RefreshProgress() => OnPropertyChanged(nameof(ProgressText));
}

public sealed partial class AssessmentQuestionViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _loading;
    private readonly string _promptTemplate;
    private string _subjectPronoun = "they";
    private bool _usesPluralAgreement = true;
    public string Key { get; }
    public string Prompt { get; private set; }
    public string WhyAsked { get; }
    public string CompleteAnswerIncludes { get; }
    public string Avoid { get; }
    public bool UsesSupports { get; }
    public AssessmentQuestionKind Kind { get; }
    public bool IsNarrative => Kind == AssessmentQuestionKind.Narrative;
    public bool HasGuidance => !string.IsNullOrWhiteSpace(WhyAsked);
    public bool UsesYesNo => Kind is AssessmentQuestionKind.YesNo or AssessmentQuestionKind.HealthConcern or AssessmentQuestionKind.Therapy;
    public bool IsHealthConcern => Kind == AssessmentQuestionKind.HealthConcern;
    public bool IsTherapy => Kind == AssessmentQuestionKind.Therapy;
    public bool UsesActivitySupport => Kind == AssessmentQuestionKind.ActivitySupport;
    public string WouldLikeTherapistPrompt => $"Would {_subjectPronoun} like to see a therapist?";
    public string TherapistSatisfactionPrompt => $"{(_usesPluralAgreement ? "Are" : "Is")} {_subjectPronoun} satisfied with the therapist?";
    public string SessionFormatPrompt => "Are therapy sessions attended in person or via Telehealth?";
    public string OtherSessionFormatPrompt => $"Would {_subjectPronoun} like to switch to {(TherapySessionFormat == TherapySessionFormat.InPerson ? "Telehealth" : "in-person")} sessions?";
    public string FrequencyChangePrompt => $"Would {_subjectPronoun} like to change the frequency of their therapy sessions?";
    public ObservableCollection<ActivitySupportViewModel> Activities { get; } = [];

    [ObservableProperty] private AssessmentAnswerStatus status = AssessmentAnswerStatus.NotYetAnswered;
    [ObservableProperty] private string narrative = string.Empty;
    [ObservableProperty] private string supportDetails = string.Empty;
    [ObservableProperty] private string exceptionReason = string.Empty;
    [ObservableProperty] private string dissentingOpinion = string.Empty;
    [ObservableProperty] private string dissentContributor = string.Empty;
    [ObservableProperty] private string dissentDiscussion = string.Empty;
    [ObservableProperty] private bool? dissentUnresolved;
    [ObservableProperty] private bool setupOrEnvironmental;
    [ObservableProperty] private bool promptingOrCoaching;
    [ObservableProperty] private bool handsOnAssistance;
    [ObservableProperty] private bool anotherPersonCompletes;
    [ObservableProperty] private bool varies;
    [ObservableProperty] private bool noSupportCurrentlyNeeded;
    [ObservableProperty] private bool? yesNoResponse;
    [ObservableProperty] private bool? followUpYesNoResponse;
    [ObservableProperty] private string details = string.Empty;
    [ObservableProperty] private TherapySessionFormat therapySessionFormat;
    [ObservableProperty] private bool? wantsOtherSessionFormat;
    [ObservableProperty] private bool? wantsFrequencyChange;
    [ObservableProperty] private TherapyFrequencyDirection therapyFrequencyDirection;

    public AssessmentQuestionViewModel(string key, string prompt, string why, string include, string avoid,
        bool usesSupports, AssessmentQuestionKind kind, IEnumerable<string> activities, Action changed)
    {
        Key = key; _promptTemplate = prompt; Prompt = prompt; WhyAsked = why;
        CompleteAnswerIncludes = include; Avoid = avoid; UsesSupports = usesSupports; Kind = kind; _changed = changed;
        foreach (var activity in activities)
            Activities.Add(new ActivitySupportViewModel(activity, ActivitySupportLevel.Independent, Changed));
        SetPerson(null);
    }

    public void SetPerson(Person? person)
    {
        var name = person?.FirstName ?? "the person";
        _subjectPronoun = person?.SubjectPronoun ?? "they";
        _usesPluralAgreement = string.Equals(_subjectPronoun, "they", StringComparison.OrdinalIgnoreCase);
        Prompt = _promptTemplate
            .Replace("{Name}", name)
            .Replace("{subject}", person?.SubjectPronoun ?? "they")
            .Replace("{object}", person?.ObjectPronoun ?? "them")
            .Replace("{possessive}", person?.PossessivePronoun ?? "their")
            .Replace("{reflexive}", person?.ReflexivePronoun ?? "themselves")
            .Replace("{does}", _usesPluralAgreement ? "do" : "does")
            .Replace("{Has}", _usesPluralAgreement ? "Have" : "Has")
            .Replace("{Is}", _usesPluralAgreement ? "Are" : "Is")
            .Replace("{wants}", _usesPluralAgreement ? "want" : "wants");
        OnPropertyChanged(nameof(Prompt));
        OnPropertyChanged(nameof(WouldLikeTherapistPrompt));
        OnPropertyChanged(nameof(TherapistSatisfactionPrompt));
        OnPropertyChanged(nameof(OtherSessionFormatPrompt));
        OnPropertyChanged(nameof(FrequencyChangePrompt));
    }

    partial void OnStatusChanged(AssessmentAnswerStatus value)
    {
        OnPropertyChanged(nameof(ExceptionReasonPrompt));
        Changed();
    }
    partial void OnNarrativeChanged(string value) => Changed();
    partial void OnSupportDetailsChanged(string value) => Changed();
    partial void OnExceptionReasonChanged(string value) => Changed();
    partial void OnDissentingOpinionChanged(string value) => Changed();
    partial void OnDissentContributorChanged(string value) => Changed();
    partial void OnDissentDiscussionChanged(string value) => Changed();
    partial void OnDissentUnresolvedChanged(bool? value) => Changed();
    partial void OnSetupOrEnvironmentalChanged(bool value) { ClearNoSupport(value); Changed(); }
    partial void OnPromptingOrCoachingChanged(bool value) { ClearNoSupport(value); Changed(); }
    partial void OnHandsOnAssistanceChanged(bool value) { ClearNoSupport(value); Changed(); }
    partial void OnAnotherPersonCompletesChanged(bool value) { ClearNoSupport(value); Changed(); }
    partial void OnVariesChanged(bool value) { ClearNoSupport(value); Changed(); }
    partial void OnNoSupportCurrentlyNeededChanged(bool value)
    {
        if (value)
        {
            SetupOrEnvironmental = PromptingOrCoaching = HandsOnAssistance = AnotherPersonCompletes = Varies = false;
            SupportDetails = string.Empty;
        }
        Changed();
    }
    partial void OnYesNoResponseChanged(bool? value)
    {
        OnPropertyChanged(nameof(ShowHealthConcernDetails));
        OnPropertyChanged(nameof(ShowTherapyCurrentFollowUps));
        OnPropertyChanged(nameof(ShowTherapyRequestedFollowUp));
        Changed();
    }
    partial void OnFollowUpYesNoResponseChanged(bool? value) => Changed();
    partial void OnDetailsChanged(string value) => Changed();
    partial void OnTherapySessionFormatChanged(TherapySessionFormat value)
    {
        OnPropertyChanged(nameof(ShowOtherSessionFormatQuestion));
        OnPropertyChanged(nameof(OtherSessionFormatPrompt));
        Changed();
    }
    partial void OnWantsOtherSessionFormatChanged(bool? value) => Changed();
    partial void OnWantsFrequencyChangeChanged(bool? value)
    {
        OnPropertyChanged(nameof(ShowFrequencyDirection));
        Changed();
    }
    partial void OnTherapyFrequencyDirectionChanged(TherapyFrequencyDirection value) => Changed();
    private void ClearNoSupport(bool selected) { if (selected) NoSupportCurrentlyNeeded = false; }
    private void Changed() { if (!_loading) { OnPropertyChanged(nameof(IsAddressed)); OnPropertyChanged(nameof(ShowExceptionReason)); _changed(); } }

    public bool ShowExceptionReason => Status is not AssessmentAnswerStatus.Answered
        and not AssessmentAnswerStatus.NotYetAnswered;
    public bool ShowHealthConcernDetails => IsHealthConcern && YesNoResponse == true;
    public bool ShowTherapyCurrentFollowUps => IsTherapy && YesNoResponse == true;
    public bool ShowTherapyRequestedFollowUp => IsTherapy && YesNoResponse == false;
    public bool ShowOtherSessionFormatQuestion => ShowTherapyCurrentFollowUps && TherapySessionFormat != TherapySessionFormat.NotSelected;
    public bool ShowFrequencyDirection => ShowTherapyCurrentFollowUps && WantsFrequencyChange == true;
    public string ExceptionReasonPrompt => Status switch
    {
        AssessmentAnswerStatus.FollowUpRequired => "Explain why follow-up is required.",
        AssessmentAnswerStatus.NotYetAnswered => string.Empty,
        AssessmentAnswerStatus.UnableToAssess => "Explain why this item could not be assessed.",
        AssessmentAnswerStatus.Declined => "Explain why an answer was declined.",
        AssessmentAnswerStatus.NotApplicable => "Explain why this item is not applicable.",
        _ => "Additional explanation"
    };
    public bool HasConcreteSupport => SetupOrEnvironmental || PromptingOrCoaching || HandsOnAssistance || AnotherPersonCompletes;
    public bool IsAddressed => AssessmentReviewRules.IsAddressed(new AssessmentQuestionDefinition(Key, Prompt, WhyAsked, CompleteAnswerIncludes, Avoid, UsesSupports, Kind, Activities.Select(a => a.Name).ToArray()), ToModel());

    public void Load(AssessmentAnswer answer)
    {
        _loading = true;
        Status = answer.Status; Narrative = answer.Narrative; SupportDetails = answer.SupportDetails;
        ExceptionReason = answer.ExceptionReason; DissentingOpinion = answer.DissentingOpinion;
        DissentContributor = answer.DissentContributor; DissentDiscussion = answer.DissentDiscussion; DissentUnresolved = answer.DissentUnresolved;
        SetupOrEnvironmental = answer.Supports.HasFlag(SupportMethod.SetupOrEnvironmental);
        PromptingOrCoaching = answer.Supports.HasFlag(SupportMethod.PromptingOrCoaching);
        HandsOnAssistance = answer.Supports.HasFlag(SupportMethod.HandsOnAssistance);
        AnotherPersonCompletes = answer.Supports.HasFlag(SupportMethod.AnotherPersonCompletes);
        Varies = answer.Supports.HasFlag(SupportMethod.Varies);
        NoSupportCurrentlyNeeded = answer.Supports.HasFlag(SupportMethod.NoSupportCurrentlyNeeded);
        YesNoResponse = answer.YesNoResponse;
        FollowUpYesNoResponse = answer.FollowUpYesNoResponse;
        Details = answer.Details;
        TherapySessionFormat = answer.TherapySessionFormat;
        WantsOtherSessionFormat = answer.WantsOtherSessionFormat;
        WantsFrequencyChange = answer.WantsFrequencyChange;
        TherapyFrequencyDirection = answer.TherapyFrequencyDirection;
        var savedActivityLevels = answer.ActivitySupportLevels ?? [];
        var savedSkillsTraining = answer.ActivitySkillsTraining ?? [];
        foreach (var activity in Activities)
        {
            var level = savedActivityLevels.TryGetValue(activity.Name, out var savedLevel)
                ? savedLevel : ActivitySupportLevel.Independent;
            var skillsTraining = savedSkillsTraining.TryGetValue(activity.Name, out var savedTraining) && savedTraining;
            activity.Load(level, skillsTraining);
        }
        _loading = false; OnPropertyChanged(string.Empty);
    }

    public AssessmentAnswer ToModel()
    {
        var supports = SupportMethod.None;
        if (SetupOrEnvironmental) supports |= SupportMethod.SetupOrEnvironmental;
        if (PromptingOrCoaching) supports |= SupportMethod.PromptingOrCoaching;
        if (HandsOnAssistance) supports |= SupportMethod.HandsOnAssistance;
        if (AnotherPersonCompletes) supports |= SupportMethod.AnotherPersonCompletes;
        if (Varies) supports |= SupportMethod.Varies;
        if (NoSupportCurrentlyNeeded) supports |= SupportMethod.NoSupportCurrentlyNeeded;
        return new()
        {
            Status = Status, Narrative = Narrative, Supports = supports, SupportDetails = SupportDetails,
            ExceptionReason = ExceptionReason, DissentingOpinion = DissentingOpinion,
            DissentContributor = DissentContributor, DissentDiscussion = DissentDiscussion, DissentUnresolved = DissentUnresolved,
            YesNoResponse = YesNoResponse, FollowUpYesNoResponse = FollowUpYesNoResponse, Details = Details,
            TherapySessionFormat = TherapySessionFormat, WantsOtherSessionFormat = WantsOtherSessionFormat,
            WantsFrequencyChange = WantsFrequencyChange, TherapyFrequencyDirection = TherapyFrequencyDirection,
            ActivitySupportLevels = Activities.ToDictionary(activity => activity.Name, activity => activity.Level),
            ActivitySkillsTraining = Activities.ToDictionary(activity => activity.Name, activity => activity.SkillsTraining)
        };
    }
}

public sealed partial class ActivitySupportViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _loading;
    public string Name { get; }
    [ObservableProperty] private int levelValue;
    [ObservableProperty] private bool skillsTraining;
    public ActivitySupportLevel Level => LevelValue switch
    {
        0 => ActivitySupportLevel.Independent,
        1 => ActivitySupportLevel.Prompting,
        2 => ActivitySupportLevel.Monitoring,
        3 => ActivitySupportLevel.PhysicalAssistance,
        _ => ActivitySupportLevel.TotalCare
    };

    public ActivitySupportViewModel(string name, ActivitySupportLevel level, Action changed)
    { Name = name; levelValue = (int)level; _changed = changed; }

    partial void OnLevelValueChanged(int value)
    {
        OnPropertyChanged(nameof(Level));
        if (!_loading) _changed();
    }
    partial void OnSkillsTrainingChanged(bool value)
    {
        if (!_loading) _changed();
    }

    public void Load(ActivitySupportLevel level, bool savedSkillsTraining)
    {
        _loading = true;
        SkillsTraining = savedSkillsTraining || level == ActivitySupportLevel.SkillsTraining;
        LevelValue = level switch
        {
            ActivitySupportLevel.Prompting => 1,
            ActivitySupportLevel.Monitoring => 2,
            ActivitySupportLevel.PhysicalAssistance => 3,
            ActivitySupportLevel.TotalCare => 4,
            _ => 0
        };
        _loading = false;
    }
}

public sealed partial class AssessmentContributorViewModel : ObservableObject
{
    private readonly Action _changed;
    private readonly Action<AssessmentContributorViewModel> _remove;
    public Guid Id { get; }
    [ObservableProperty] private string name;
    [ObservableProperty] private string relationship;
    public AssessmentContributorViewModel(AssessmentContributor model, Action changed, Action<AssessmentContributorViewModel> remove)
    { Id = model.Id; name = model.Name; relationship = model.Relationship; _changed = changed; _remove = remove; }
    partial void OnNameChanged(string value) => _changed();
    partial void OnRelationshipChanged(string value) => _changed();
    [RelayCommand]
    public void Remove() => _remove(this);
    public AssessmentContributor ToModel() => new() { Id = Id, Name = Name, Relationship = Relationship };
}

/// <summary>
/// One provider a need may be associated with, already resolved. The snapshot travels with the
/// option so choosing it is what freezes the practice and network onto the document — there is
/// no second lookup that could disagree with what the picker showed.
/// </summary>
public sealed class AssessmentProviderOption(ProviderAffiliation.ProviderSnapshot snapshot)
{
    public ProviderAffiliation.ProviderSnapshot Snapshot { get; } = snapshot;
    public int ProviderId => Snapshot.ProviderId;
    public string Display => Snapshot.Describe();
}

public sealed partial class AssessmentNeedViewModel : ObservableObject
{
    private readonly Action _changed;
    private readonly Action<AssessmentNeedViewModel> _remove;
    public Guid Id { get; }
    [ObservableProperty] private AssessmentNeedType type;
    [ObservableProperty] private string description;
    [ObservableProperty] private string desiredResult;
    [ObservableProperty] private bool associateProvider;
    [ObservableProperty] private string providerNameSnapshot;
    [ObservableProperty] private string providerPracticeSnapshot;
    [ObservableProperty] private string providerNetworkSnapshot;
    [ObservableProperty] private int? providerId;

    public AssessmentNeedViewModel(
        AssessmentNeed model,
        Action changed,
        Action<AssessmentNeedViewModel> remove,
        IReadOnlyList<AssessmentProviderOption>? providerOptions = null)
    {
        Id = model.Id; type = model.Type; description = model.Description; desiredResult = model.DesiredResult;
        associateProvider = model.AssociateProvider; providerNameSnapshot = model.ProviderNameSnapshot;
        providerPracticeSnapshot = model.ProviderPracticeSnapshot;
        providerNetworkSnapshot = model.ProviderNetworkSnapshot;
        providerId = model.ProviderId;
        ProviderOptions = providerOptions ?? [];
        _changed = changed; _remove = remove;
    }

    public IReadOnlyList<AssessmentProviderOption> ProviderOptions { get; }

    public bool HasProviderOptions => ProviderOptions.Count > 0;

    /// <summary>
    /// What this need currently records, exactly as it will read on the document. Shown even
    /// when the provider is no longer in the directory, or was typed before the directory
    /// existed — the document keeps what it froze.
    /// </summary>
    public string RecordedProvider => string.Join(" — ", new[]
    {
        ProviderNameSnapshot,
        string.Join(" · ", new[] { ProviderPracticeSnapshot, ProviderNetworkSnapshot }
            .Where(part => !string.IsNullOrWhiteSpace(part)))
    }.Where(part => !string.IsNullOrWhiteSpace(part)));

    public bool HasRecordedProvider => !string.IsNullOrWhiteSpace(ProviderNameSnapshot);

    /// <summary>
    /// Shown when a need already names a provider that is not among the current choices —
    /// typed before the directory, or somebody the consumer no longer sees. The document is not
    /// rewritten to match; it says what it said.
    /// </summary>
    public bool RecordedProviderIsOutsideCurrentChoices =>
        HasRecordedProvider && ProviderOptions.All(option => option.ProviderId != ProviderId);

    partial void OnTypeChanged(AssessmentNeedType value) => _changed();
    partial void OnDescriptionChanged(string value) => _changed();
    partial void OnDesiredResultChanged(string value) => _changed();
    partial void OnAssociateProviderChanged(bool value) => _changed();

    /// <summary>
    /// Choosing a provider freezes its resolved chain onto the need. This is the one place in
    /// Sati where the practice and network are copied rather than derived, and it is deliberate:
    /// an assessment approved in March has to keep saying what it said in March.
    /// </summary>
    partial void OnProviderIdChanged(int? value)
    {
        var chosen = ProviderOptions.FirstOrDefault(option => option.ProviderId == value);
        if (chosen is not null)
        {
            ProviderNameSnapshot = chosen.Snapshot.ProviderName;
            ProviderPracticeSnapshot = chosen.Snapshot.PracticeName;
            ProviderNetworkSnapshot = chosen.Snapshot.NetworkName;
        }

        RaiseRecordedProviderChanged();
        _changed();
    }

    partial void OnProviderNameSnapshotChanged(string value) => RaiseRecordedProviderChanged();
    partial void OnProviderPracticeSnapshotChanged(string value) => RaiseRecordedProviderChanged();
    partial void OnProviderNetworkSnapshotChanged(string value) => RaiseRecordedProviderChanged();

    private void RaiseRecordedProviderChanged()
    {
        OnPropertyChanged(nameof(RecordedProvider));
        OnPropertyChanged(nameof(HasRecordedProvider));
        OnPropertyChanged(nameof(RecordedProviderIsOutsideCurrentChoices));
    }

    [RelayCommand] public void Remove() => _remove(this);

    public AssessmentNeed ToModel() => new()
    {
        Id = Id, Type = Type, Description = Description, DesiredResult = DesiredResult,
        AssociateProvider = AssociateProvider, ProviderId = AssociateProvider ? ProviderId : null,
        ProviderNameSnapshot = AssociateProvider ? ProviderNameSnapshot : string.Empty,
        ProviderPracticeSnapshot = AssociateProvider ? ProviderPracticeSnapshot : string.Empty,
        ProviderNetworkSnapshot = AssociateProvider ? ProviderNetworkSnapshot : string.Empty
    };
}
