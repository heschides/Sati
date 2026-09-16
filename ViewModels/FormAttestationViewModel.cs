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
    private DateTime? comprehensiveAssessmentCompletionDate;

    [ObservableProperty]
    private string comprehensiveAssessmentCompletionDateError = string.Empty;

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
    public bool IsReclassification => _form?.Type == FormType.Reclassification;
    public bool IsAssessmentDateRequired =>
        IsReclassification && _prerequisiteStatus is { IsSatisfied: false };
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
        var cycle = FormAttestationRules.ResolveCycleForForm(
                effectiveDate,
                form.Type.ToString(),
                form.DueDate,
                form.TargetEffectiveDate == default ? null : form.TargetEffectiveDate)
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
        ComprehensiveAssessmentCompletionDate = null;
        ComprehensiveAssessmentCompletionDateError = string.Empty;
        RevocationReason = string.Empty;
        RevocationReasonError = string.Empty;
        PrerequisiteError = string.Empty;
        AttestationHistory = [];
        HistoryError = string.Empty;
        IsVisible = true;
        NotifyStateChanged();
        if (form.Type == FormType.Reclassification)
        {
            _ = LoadPrerequisiteAsync(form, version);
        }
        else
        {
            _prerequisiteStatus = new FormPrerequisiteStatusDto(
                PrerequisiteKind.None.ToString(),
                true,
                "Attestation is sufficient; no separate document prerequisite applies.",
                [],
                CanSupervisorOverride: false);
            NotifyStateChanged();
        }
        _ = LoadHistoryAsync(form, version);
    }

    partial void OnCompletionDateChanged(DateTime? value)
    {
        CompletionDateError = value is DateTime date && _form is not null
            ? FormAttestationRules.ValidateCompletionDate(
                date, _cycleStart, DateTime.Today) ?? string.Empty
            : string.Empty;
        ValidateAssessmentDate();
    }

    partial void OnComprehensiveAssessmentCompletionDateChanged(DateTime? value)
    {
        _ = value;
        ValidateAssessmentDate();
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

        var dateAccepted = FormAttestationRules.ValidateCompletionDate(
            date, _cycleStart, DateTime.Today) is null;
        if (!dateAccepted)
            return false;
        if (RequiresEvergreenConfirmation && !HasConfirmedEvergreenCompletion)
            return false;

        if (!IsReclassification)
            return true;
        if (_prerequisiteStatus?.IsSatisfied == true)
            return true;
        if (!IsAssessmentDateRequired ||
            ComprehensiveAssessmentCompletionDate is not DateTime assessmentCompletedOn)
            return false;

        return FormAttestationRules.ValidateAssessmentCompletionDate(
            assessmentCompletedOn,
            date,
            _cycleStart,
            DateTime.Today) is null;
    }

    [RelayCommand(CanExecute = nameof(CanCompleteAttestation))]
    private async Task CompleteAttestation()
    {
        if (_form is null || CompletionDate is not DateTime completedOn)
            return;
        var dateError = FormAttestationRules.ValidateCompletionDate(
            completedOn, _cycleStart, DateTime.Today);
        if (dateError is not null)
        {
            CompletionDateError = dateError;
            return;
        }
        if (IsAssessmentDateRequired)
        {
            if (ComprehensiveAssessmentCompletionDate is not DateTime assessmentCompletedOn)
            {
                ComprehensiveAssessmentCompletionDateError =
                    "Enter the actual Comprehensive Assessment completion date.";
                return;
            }

            var assessmentDateError = FormAttestationRules.ValidateAssessmentCompletionDate(
                assessmentCompletedOn,
                completedOn,
                _cycleStart,
                DateTime.Today);
            if (assessmentDateError is not null)
            {
                ComprehensiveAssessmentCompletionDateError = assessmentDateError;
                return;
            }
        }

        IsSaving = true;
        try
        {
            if (IsReclassification)
            {
                await formService.AttestReclassificationAsync(
                    _form,
                    completedOn.Date,
                    IsAssessmentDateRequired
                        ? ComprehensiveAssessmentCompletionDate?.Date
                        : null,
                    _evidenceNoteId);
            }
            else
            {
                await formService.AttestAsync(
                    _form,
                    completedOn.Date,
                    _evidenceNoteId);
            }
            if (AttestationChangedAsync is not null)
                await AttestationChangedAsync();
            await LoadHistoryAsync(_form, _loadVersion);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            if (exception.ParamName == "comprehensiveAssessmentCompletedOn")
                ComprehensiveAssessmentCompletionDateError = exception.Message;
            else
                CompletionDateError = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            PrerequisiteError = exception.Message;
        }
        finally
        {
            IsSaving = false;
            NotifyStateChanged();
        }
    }

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

    private void ValidateAssessmentDate()
    {
        ComprehensiveAssessmentCompletionDateError =
            IsAssessmentDateRequired &&
            ComprehensiveAssessmentCompletionDate is DateTime assessmentCompletedOn &&
            CompletionDate is DateTime reclassificationCompletedOn
                ? FormAttestationRules.ValidateAssessmentCompletionDate(
                    assessmentCompletedOn,
                    reclassificationCompletedOn,
                    _cycleStart,
                    DateTime.Today) ?? string.Empty
                : string.Empty;
        CompleteAttestationCommand.NotifyCanExecuteChanged();
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
        OnPropertyChanged(nameof(IsReclassification));
        OnPropertyChanged(nameof(IsAssessmentDateRequired));
        ValidateAssessmentDate();
        NotifyHistoryChanged();
        CompleteAttestationCommand.NotifyCanExecuteChanged();
        RevokeAttestationCommand.NotifyCanExecuteChanged();
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
