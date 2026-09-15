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
    [NotifyCanExecuteChangedFor(nameof(CompleteAttestationCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevokeAttestationCommand))]
    private bool isSaving;

    public Func<Task>? AttestationChangedAsync { get; set; }

    public bool IsComplete => _form?.CompletedDate is not null;
    public bool IsIncomplete => !IsComplete;
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
        CompletionDateError = string.Empty;
        ComprehensiveAssessmentCompletionDate = null;
        ComprehensiveAssessmentCompletionDateError = string.Empty;
        RevocationReason = string.Empty;
        RevocationReasonError = string.Empty;
        PrerequisiteError = string.Empty;
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
            IsVisible = false;
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
            IsVisible = false;
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
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(PrerequisiteSummary));
        OnPropertyChanged(nameof(IsPrerequisiteMissing));
        OnPropertyChanged(nameof(IsReclassification));
        OnPropertyChanged(nameof(IsAssessmentDateRequired));
        ValidateAssessmentDate();
        CompleteAttestationCommand.NotifyCanExecuteChanged();
        RevokeAttestationCommand.NotifyCanExecuteChanged();
    }
}
