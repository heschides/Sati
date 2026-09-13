using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;

namespace Sati.ViewModels;

/// <summary>
/// Reusable, presentation-only capture for the one authoritative form-attestation
/// service boundary. It deliberately starts with a blank date.
/// </summary>
public partial class FormAttestationViewModel(IFormService formService) : ObservableObject
{
    private Form? _form;
    private DateTime _cycleStart;
    private int? _evidenceNoteId;
    private int _loadVersion;
    private FormPrerequisiteStatusDto? _prerequisiteStatus;

    [ObservableProperty]
    private bool isVisible;

    [ObservableProperty]
    private string contextLabel = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompleteAttestationCommand))]
    private DateTime? completionDate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompleteAttestationCommand))]
    private bool hasConfirmedEvergreenCompletion;

    [ObservableProperty]
    private string completionDateError = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevokeAttestationCommand))]
    private string revocationReason = string.Empty;

    [ObservableProperty]
    private string revocationReasonError = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompleteAttestationCommand))]
    private string supervisorOverrideReason = string.Empty;

    [ObservableProperty]
    private string externalDocumentNote = string.Empty;

    [ObservableProperty]
    private string prerequisiteError = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<FormAttestationHistoryItemViewModel> attestationHistory = [];

    [ObservableProperty]
    private bool isHistoryLoading;

    [ObservableProperty]
    private string historyError = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompleteAttestationCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevokeAttestationCommand))]
    private bool isSaving;

    public Func<Task>? AttestationChangedAsync { get; set; }

    public bool IsComplete => _form?.CompletedDate is not null;
    public bool IsIncomplete => !IsComplete;
    public bool RequiresEvergreenConfirmation =>
        _form?.Type is FormType.PCP or FormType.ComprehensiveAssessment;
    public string AttestationStatement => _form?.Type switch
    {
        FormType.PCP =>
            "I attest that this Person-Centered Plan was completed in Evergreen on the date entered above.",
        FormType.ComprehensiveAssessment =>
            "I attest that this Comprehensive Assessment was completed in Evergreen on the date entered above.",
        _ => string.Empty
    };
    public string StatusText => _form is null
        ? string.Empty
        : _form.CompletedDate is DateTime completed
            ? $"Attested complete on {completed:MMM d, yyyy}."
            : $"Due {_form.DueDate:MMM d, yyyy}; completion has not been attested.";
    public string PrerequisiteSummary =>
        _prerequisiteStatus?.Summary ?? "Checking the prerequisite…";
    public bool IsPrerequisiteMissing => _prerequisiteStatus is { IsSatisfied: false };
    public bool CanSupervisorOverride =>
        IsPrerequisiteMissing && _prerequisiteStatus?.CanSupervisorOverride == true;
    public bool CanRecordExternal =>
        IsPrerequisiteMissing &&
        _prerequisiteStatus?.Kind is nameof(PrerequisiteKind.DocumentArtifact)
            or nameof(PrerequisiteKind.SafetyPlan)
            or nameof(PrerequisiteKind.PrivacyPracticesAcknowledgment);
    public bool HasAttestationHistory => AttestationHistory.Count > 0;
    public bool HasHistoryStatus => !string.IsNullOrWhiteSpace(HistoryStatusText);
    public string HistoryStatusText => IsHistoryLoading
        ? "Loading attestation history…"
        : !string.IsNullOrWhiteSpace(HistoryError)
            ? HistoryError
            : HasAttestationHistory
                ? string.Empty
                : "No attestation has been recorded for this form cycle.";
    public bool HasCurrentAttestationEvidence =>
        !string.IsNullOrWhiteSpace(CurrentAttestationEvidenceText);
    public string CurrentAttestationEvidenceText
    {
        get
        {
            if (!IsComplete)
                return string.Empty;
            var current = AttestationHistory.FirstOrDefault(item => item.IsAttestation);
            return current is null
                ? IsHistoryLoading
                    ? "Loading signer and timestamp…"
                    : "Signer and timestamp are unavailable for this legacy completion."
                : $"Current evidence: {current.ActorAndTimestampText}";
        }
    }

    public void Begin(
        Form form,
        DateTime effectiveDate,
        string contextLabel,
        int? evidenceNoteId = null)
    {
        var cycle = FormAttestationRules.ResolveCycle(effectiveDate, form.DueDate)
            ?? throw new InvalidOperationException("The form is not attached to a valid compliance cycle.");
        _form = form;
        _cycleStart = cycle.CycleStart;
        _evidenceNoteId = evidenceNoteId;
        var version = ++_loadVersion;
        _prerequisiteStatus = null;
        ContextLabel = contextLabel;
        CompletionDate = null;
        HasConfirmedEvergreenCompletion = false;
        CompletionDateError = string.Empty;
        RevocationReason = string.Empty;
        RevocationReasonError = string.Empty;
        SupervisorOverrideReason = string.Empty;
        ExternalDocumentNote = string.Empty;
        PrerequisiteError = string.Empty;
        AttestationHistory = [];
        HistoryError = string.Empty;
        IsVisible = true;
        NotifyStateChanged();
        _ = LoadPrerequisiteAsync(form, version);
        _ = LoadHistoryAsync(form, version);
    }

    partial void OnCompletionDateChanged(DateTime? value)
    {
        CompletionDateError = value is DateTime date && _form is not null
            ? FormAttestationRules.Evaluate(
                _form.Type.ToString(), date, _cycleStart, DateTime.Today,
                AttestationActorKind.System, []).DateError ?? string.Empty
            : string.Empty;
    }

    partial void OnRevocationReasonChanged(string value)
    {
        RevocationReasonError = string.Empty;
    }

    private bool CanCompleteAttestation()
    {
        if (IsSaving || _form is not Form form || form.CompletedDate is not null ||
            CompletionDate is not DateTime date)
            return false;

        var dateAccepted = FormAttestationRules.Evaluate(
            form.Type.ToString(), date, _cycleStart, DateTime.Today,
            AttestationActorKind.System, []).Accepted;
        var prerequisiteAccepted = _prerequisiteStatus?.IsSatisfied == true ||
            (CanSupervisorOverride && !string.IsNullOrWhiteSpace(SupervisorOverrideReason));
        var attestationConfirmed = !RequiresEvergreenConfirmation || HasConfirmedEvergreenCompletion;
        return dateAccepted && prerequisiteAccepted && attestationConfirmed;
    }

    [RelayCommand(CanExecute = nameof(CanCompleteAttestation))]
    private async Task CompleteAttestation()
    {
        if (_form is null || CompletionDate is not DateTime completedOn)
            return;
        var decision = FormAttestationRules.Evaluate(
            _form.Type.ToString(), completedOn, _cycleStart, DateTime.Today,
            AttestationActorKind.System, []);
        if (!decision.Accepted)
        {
            CompletionDateError = decision.DateError ?? "The attestation could not be recorded.";
            return;
        }

        IsSaving = true;
        try
        {
            await formService.AttestAsync(
                _form,
                completedOn.Date,
                _evidenceNoteId,
                CanSupervisorOverride ? SupervisorOverrideReason.Trim() : null);
            if (AttestationChangedAsync is not null)
                await AttestationChangedAsync();
            await LoadHistoryAsync(_form, _loadVersion);
        }
        finally
        {
            IsSaving = false;
            NotifyStateChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRecordExternalDocument))]
    private async Task RecordExternalDocument()
    {
        if (_form is null)
            return;
        if (string.IsNullOrWhiteSpace(ExternalDocumentNote))
        {
            PrerequisiteError = "Enter where the external document is held or how it was verified.";
            return;
        }

        IsSaving = true;
        try
        {
            await formService.RecordExternalPrerequisiteAsync(_form, ExternalDocumentNote.Trim());
            _prerequisiteStatus = await formService.GetPrerequisiteStatusAsync(_form);
            ExternalDocumentNote = string.Empty;
            PrerequisiteError = string.Empty;
        }
        catch (Exception exception)
        {
            PrerequisiteError = exception.Message;
        }
        finally
        {
            IsSaving = false;
            NotifyStateChanged();
        }
    }

    private bool CanRecordExternalDocument() => !IsSaving && CanRecordExternal;

    private async Task LoadPrerequisiteAsync(Form form, int version)
    {
        try
        {
            var status = await formService.GetPrerequisiteStatusAsync(form);
            if (version != _loadVersion || !ReferenceEquals(form, _form))
                return;
            _prerequisiteStatus = status;
            PrerequisiteError = string.Empty;
        }
        catch (Exception exception)
        {
            if (version != _loadVersion || !ReferenceEquals(form, _form))
                return;
            PrerequisiteError = exception.Message;
        }
        finally
        {
            if (version == _loadVersion)
                NotifyStateChanged();
        }
    }

    private async Task LoadHistoryAsync(Form form, int version)
    {
        IsHistoryLoading = true;
        NotifyHistoryChanged();
        try
        {
            var history = await formService.GetAttestationHistoryAsync(form);
            if (version != _loadVersion || !ReferenceEquals(form, _form))
                return;
            AttestationHistory = history.Select(FormAttestationHistoryItemViewModel.From).ToList();
            HistoryError = string.Empty;
        }
        catch (Exception exception)
        {
            if (version != _loadVersion || !ReferenceEquals(form, _form))
                return;
            HistoryError = $"Attestation history could not be loaded: {exception.Message}";
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsHistoryLoading = false;
                NotifyHistoryChanged();
            }
        }
    }

    private bool CanRevokeAttestation() =>
        !IsSaving && _form?.CompletedDate is not null;

    [RelayCommand(CanExecute = nameof(CanRevokeAttestation))]
    private async Task RevokeAttestation()
    {
        if (_form is null)
            return;
        if (string.IsNullOrWhiteSpace(RevocationReason))
        {
            RevocationReasonError = "Enter why this attestation is being revoked.";
            return;
        }

        IsSaving = true;
        try
        {
            await formService.RevokeAttestationAsync(_form, RevocationReason.Trim());
            if (AttestationChangedAsync is not null)
                await AttestationChangedAsync();
            CompletionDate = null;
            HasConfirmedEvergreenCompletion = false;
            await LoadHistoryAsync(_form, _loadVersion);
        }
        finally
        {
            IsSaving = false;
            NotifyStateChanged();
        }
    }

    [RelayCommand]
    private void Cancel() => IsVisible = false;

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(IsIncomplete));
        OnPropertyChanged(nameof(RequiresEvergreenConfirmation));
        OnPropertyChanged(nameof(AttestationStatement));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(PrerequisiteSummary));
        OnPropertyChanged(nameof(IsPrerequisiteMissing));
        OnPropertyChanged(nameof(CanSupervisorOverride));
        OnPropertyChanged(nameof(CanRecordExternal));
        NotifyHistoryChanged();
        CompleteAttestationCommand.NotifyCanExecuteChanged();
        RevokeAttestationCommand.NotifyCanExecuteChanged();
        RecordExternalDocumentCommand.NotifyCanExecuteChanged();
    }

    private void NotifyHistoryChanged()
    {
        OnPropertyChanged(nameof(HasAttestationHistory));
        OnPropertyChanged(nameof(HasHistoryStatus));
        OnPropertyChanged(nameof(HistoryStatusText));
        OnPropertyChanged(nameof(HasCurrentAttestationEvidence));
        OnPropertyChanged(nameof(CurrentAttestationEvidenceText));
    }
}

public sealed record FormAttestationHistoryItemViewModel(
    long Id,
    string ActionText,
    string DetailText,
    string ActorAndTimestampText,
    string ReasonText,
    bool IsAttestation)
{
    public bool HasDetail => !string.IsNullOrWhiteSpace(DetailText);
    public bool HasReason => !string.IsNullOrWhiteSpace(ReasonText);

    public static FormAttestationHistoryItemViewModel From(FormAttestationHistoryDto entry)
    {
        var recordedAtUtc = entry.RecordedAtUtc.Kind == DateTimeKind.Utc
            ? entry.RecordedAtUtc
            : DateTime.SpecifyKind(entry.RecordedAtUtc, DateTimeKind.Utc);
        var actorKind = entry.ActorKind switch
        {
            nameof(AttestationActorKind.CaseManager) => "case manager",
            nameof(AttestationActorKind.Supervisor) => "supervisor",
            nameof(AttestationActorKind.System) => "automatic Sati evidence",
            _ => entry.ActorKind
        };
        var isAttestation = entry.Kind.Equals("Attested", StringComparison.OrdinalIgnoreCase);
        return new FormAttestationHistoryItemViewModel(
            entry.Id,
            isAttestation ? "Completion attested" : "Attestation revoked",
            entry.CompletedOn is DateTime completedOn
                ? $"Work completed {completedOn:MMM d, yyyy}"
                : string.Empty,
            $"{entry.ActorDisplayName} · {actorKind} · recorded {recordedAtUtc.ToLocalTime():MMM d, yyyy h:mm tt}",
            string.IsNullOrWhiteSpace(entry.Reason) ? string.Empty : $"Reason: {entry.Reason}",
            isAttestation);
    }
}
