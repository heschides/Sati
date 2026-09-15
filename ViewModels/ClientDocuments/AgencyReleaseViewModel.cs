using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.ClientDocuments;

public partial class AgencyReleaseViewModel : ObservableObject
{
    private readonly IAgencyReleaseService _service;
    private readonly List<ReleaseObligationDto> _knownReleaseObligations = [];
    private int? _personId;
    private int _personVersion;

    public AgencyReleaseViewModel(IAgencyReleaseService service)
    {
        _service = service;
        ReleaseKindChoices =
        [
            new(AnnualDocumentKind.ReleaseAgency, "Agency release"),
            new(AnnualDocumentKind.ReleaseMedical, "Medical release")
        ];
        selectedReleaseKind = ReleaseKindChoices[0];
        YesNoChoices = [new("Yes", true), new("No", false)];
        ScopeChoices =
        [
            new("One-time disclosure", AgencyReleaseScope.OneTime),
            new("Multiple disclosures", AgencyReleaseScope.Multiple),
        ];
        ContactTypeChoices =
        [
            "Home support",
            "Community support",
            "Shared living",
            "Healthcare provider",
            "Service provider",
            "Education",
            "Family / guardian",
            "Other",
        ];
        InformationCategories = AgencyReleaseInformation.All
            .Select(value => new AgencyReleaseCategoryOption(value, AgencyReleaseInformation.DisplayName(value)))
            .ToList();
        ResetInputs();
    }

    public IReadOnlyList<YesNoChoice> YesNoChoices { get; }
    public IReadOnlyList<ReleaseKindChoice> ReleaseKindChoices { get; }
    public IReadOnlyList<AgencyReleaseScopeChoice> ScopeChoices { get; }
    public IReadOnlyList<string> ContactTypeChoices { get; }
    public IReadOnlyList<AgencyReleaseCategoryOption> InformationCategories { get; }
    public ObservableCollection<ReleaseDocumentObligationChoice> ReleaseObligationChoices { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkspaceTitle))]
    [NotifyPropertyChangedFor(nameof(WorkspaceDescription))]
    [NotifyPropertyChangedFor(nameof(GenerateButtonText))]
    private ReleaseKindChoice selectedReleaseKind;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedReleaseObligation))]
    [NotifyPropertyChangedFor(nameof(ObligationSelectionGuidance))]
    private ReleaseDocumentObligationChoice? selectedReleaseObligation;

    [ObservableProperty]
    private string personName = "Select a consumer";

    [ObservableProperty]
    private YesNoChoice? authorizationChoice;

    [ObservableProperty]
    private string? contactType;

    [ObservableProperty]
    private string contactName = string.Empty;

    [ObservableProperty]
    private string relationship = string.Empty;

    [ObservableProperty]
    private string contactAddress = string.Empty;

    [ObservableProperty]
    private string contactCity = string.Empty;

    [ObservableProperty]
    private string contactState = "ME";

    [ObservableProperty]
    private string contactFax = string.Empty;

    [ObservableProperty]
    private string contactPhone = string.Empty;

    [ObservableProperty]
    private string contactEmail = string.Empty;

    [ObservableProperty]
    private string otherInformation = string.Empty;

    [ObservableProperty]
    private DateTime? startDate;

    [ObservableProperty]
    private DateTime? expirationDate;

    [ObservableProperty]
    private AgencyReleaseScopeChoice? selectedScope;

    [ObservableProperty]
    private YesNoChoice? drugAlcoholChoice;

    [ObservableProperty]
    private YesNoChoice? mentalHealthChoice;

    [ObservableProperty]
    private YesNoChoice? hivAidsChoice;

    [ObservableProperty]
    private YesNoChoice? releaseWithoutReviewChoice;

    [ObservableProperty]
    private bool isRevocation;

    [ObservableProperty]
    private DateTime? revokedOn;

    [ObservableProperty]
    private bool didObtainRoi;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    private bool isBusy;

    [ObservableProperty]
    private string validationMessage = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    public bool HasPerson => _personId.HasValue;
    public bool CanGenerate => HasPerson && !IsBusy;
    public bool HasReleaseObligationChoices => ReleaseObligationChoices.Count != 0;
    public bool HasSelectedReleaseObligation => SelectedReleaseObligation is not null;
    public bool CanSelectReleaseObligation => !IsRevocation && HasReleaseObligationChoices;
    public string ObligationSelectionGuidance
    {
        get
        {
            if (IsRevocation)
                return "Record the withdrawal in the tracked obligation above. A revocation PDF cannot replace the original authorization evidence.";
            if (!HasReleaseObligationChoices)
                return $"No available tracked {DocumentName} obligation matches this consumer. You may still prepare an unlinked release, but it cannot satisfy recipient-specific compliance.";
            if (SelectedReleaseObligation is null)
                return "Choose the exact recipient obligation when this document is meant to satisfy compliance. Leave it blank only for an authorization that is not one of the tracked provider releases.";
            return $"This PDF will be linked only to {SelectedReleaseObligation.DisplayName}. A later supported electronic signature can attest that exact obligation.";
        }
    }
    public string WorkspaceTitle => SelectedReleaseKind.Kind == AnnualDocumentKind.ReleaseMedical
        ? "MEDICAL RELEASE OF INFORMATION"
        : "AGENCY RELEASE OF INFORMATION";
    public string WorkspaceDescription => SelectedReleaseKind.Kind == AnnualDocumentKind.ReleaseMedical
        ? "Prepare Sati's medical release for a healthcare recipient. Consumer identity, guardian, agency, and case-manager details come from the signed-in record."
        : "Prepare Sati's agency release to disclose or obtain information. Consumer identity, guardian, agency, and case-manager details come from the signed-in record.";
    public string GenerateButtonText => SelectedReleaseKind.Kind == AnnualDocumentKind.ReleaseMedical
        ? "Generate medical release PDF"
        : "Generate agency release PDF";
    private string DocumentName => SelectedReleaseKind.Kind == AnnualDocumentKind.ReleaseMedical
        ? "medical release"
        : "agency release";

    public event EventHandler<AgencyReleasePdfReadyEventArgs>? PdfReady;
    public event EventHandler<AgencyReleaseProblemEventArgs>? Problem;
    public event Func<AgencyReleaseAttestationEventArgs, bool>? AttestationRequested;

    partial void OnIsBusyChanged(bool value)
    {
        GenerateCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedReleaseKindChanged(ReleaseKindChoice value) =>
        RebuildReleaseObligationChoices();

    partial void OnSelectedReleaseObligationChanged(ReleaseDocumentObligationChoice? value)
    {
        if (value is null || !string.IsNullOrWhiteSpace(ContactName))
            return;

        ContactName = value.RecipientDisplayName;
        ContactType = value.DocumentKind == AnnualDocumentKind.ReleaseMedical
            ? "Healthcare provider"
            : "Service provider";
    }

    partial void OnSelectedScopeChanged(AgencyReleaseScopeChoice? value)
    {
        if (StartDate is not DateTime start || value is null)
            return;
        ExpirationDate = value.Value == AgencyReleaseScope.OneTime
            ? start.AddDays(90)
            : start.AddYears(1);
    }

    partial void OnStartDateChanged(DateTime? value)
    {
        if (value is not DateTime start || SelectedScope is null)
            return;
        ExpirationDate = SelectedScope.Value == AgencyReleaseScope.OneTime
            ? start.AddDays(90)
            : start.AddYears(1);
    }

    partial void OnIsRevocationChanged(bool value)
    {
        if (value && RevokedOn is null)
            RevokedOn = DateTime.Today;
        if (value)
            SelectedReleaseObligation = null;
        OnPropertyChanged(nameof(CanSelectReleaseObligation));
        OnPropertyChanged(nameof(ObligationSelectionGuidance));
    }

    public void SetPerson(Person? person)
    {
        _personVersion++;
        _personId = person?.Id;
        PersonName = person?.FullName ?? "Select a consumer";
        _knownReleaseObligations.Clear();
        ResetInputs();
        RebuildReleaseObligationChoices();
        OnPropertyChanged(nameof(HasPerson));
        OnPropertyChanged(nameof(CanGenerate));
        GenerateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Supplies the currently reconciled release rows. Only exact rows for the selected
    /// consumer and document category are offered; a display name is never used as identity.
    /// </summary>
    public void SetReleaseObligations(IEnumerable<ReleaseObligationDto> obligations)
    {
        ArgumentNullException.ThrowIfNull(obligations);
        var personId = _personId;
        _knownReleaseObligations.Clear();
        if (personId is not null)
        {
            _knownReleaseObligations.AddRange(obligations
                .Where(item => item.PersonId == personId && item.ObligationId != Guid.Empty)
                .GroupBy(item => item.ObligationId)
                .Select(group => group.First()));
        }
        RebuildReleaseObligationChoices();
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        if (_personId is not int personId)
            return;

        var request = BuildRequest();
        var errors = AgencyReleaseRules.Validate(request);
        if (errors.Count > 0)
        {
            ValidationMessage = string.Join(
                Environment.NewLine,
                errors.Values.SelectMany(values => values).Select(message => $"• {message}"));
            StatusMessage = "Review the highlighted requirements before generating the release.";
            return;
        }

        if (request.ConfirmedObtainedRoi)
        {
            var confirmed = AttestationRequested?.Invoke(new AgencyReleaseAttestationEventArgs(
                AgencyReleaseRules.StaffAttestation,
                AgencyReleaseRules.AttestationScopeNotice)) == true;
            if (!confirmed)
            {
                StatusMessage = "The staff generation confirmation was not recorded; no PDF was generated.";
                return;
            }
        }

        var version = _personVersion;
        IsBusy = true;
        ValidationMessage = string.Empty;
        var documentName = DocumentName;
        var selectedObligation = SelectedReleaseObligation;
        StatusMessage = $"Preparing the Sati {documentName}...";
        try
        {
            var result = (SelectedReleaseKind.Kind, selectedObligation) switch
            {
                (AnnualDocumentKind.ReleaseMedical, not null) =>
                    await _service.GenerateMedicalForObligationAsync(
                        personId, request, selectedObligation.ObligationId),
                (AnnualDocumentKind.ReleaseMedical, null) =>
                    await _service.GenerateMedicalAsync(personId, request),
                (_, not null) =>
                    await _service.GenerateForObligationAsync(
                        personId, request, selectedObligation.ObligationId),
                _ => await _service.GenerateAsync(personId, request)
            };
            if (version != _personVersion || _personId != personId)
                return;

            var linkageMessage = selectedObligation is null
                ? " It is not linked to a tracked recipient obligation."
                : $" It is linked to {selectedObligation.DisplayName}, which remains outstanding until its separate completion attestation or a supported electronic signature.";
            StatusMessage = (request.ConfirmedObtainedRoi
                ? $"The {documentName} and staff generation confirmation are ready to save. Consumer or guardian signature lines remain blank. This generation did not complete tracked compliance."
                : $"The {documentName} draft is ready to save. No staff generation confirmation was recorded.") +
                linkageMessage;
            PdfReady?.Invoke(this, new AgencyReleasePdfReadyEventArgs(result.Pdf, result.FileName));
        }
        catch (Exception ex)
        {
            if (version != _personVersion || _personId != personId)
                return;
            StatusMessage = $"The {documentName} could not be generated.";
            Problem?.Invoke(this, new AgencyReleaseProblemEventArgs(
                "Release Not Generated",
                $"The {documentName} could not be generated.\n\n{ex.Message}"));
        }
        finally
        {
            if (version == _personVersion)
                IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        ResetInputs();
        StatusMessage = "The agency-release entries were cleared.";
    }

    private bool CanClear() => !IsBusy;

    [RelayCommand]
    private void ClearReleaseObligationLink() => SelectedReleaseObligation = null;

    private void RebuildReleaseObligationChoices()
    {
        var selectedId = SelectedReleaseObligation?.ObligationId;
        var expectedCategory = SelectedReleaseKind.Kind == AnnualDocumentKind.ReleaseMedical
            ? nameof(ReleaseObligationCategory.Medical)
            : nameof(ReleaseObligationCategory.Agency);
        var today = DateTime.Today;

        ReleaseObligationChoices.Clear();
        foreach (var item in _knownReleaseObligations
                     .Where(item => string.Equals(
                         item.Category, expectedCategory, StringComparison.OrdinalIgnoreCase))
                     .Where(item => item.AvailableOn.Date <= today &&
                                    (item.RetiredOn is null || item.RetiredOn.Value.Date > today) &&
                                    item.WithdrawnOn is null)
                     .OrderBy(item => item.DueOn)
                     .ThenBy(item => item.RecipientDisplayName, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(item => item.RecipientProviderId))
        {
            ReleaseObligationChoices.Add(new ReleaseDocumentObligationChoice(
                item.ObligationId,
                SelectedReleaseKind.Kind,
                item.RecipientDisplayName ?? "provider not linked",
                item.RecipientProviderId,
                item.TargetEffectiveDate.Date,
                item.DueOn.Date,
                item.CompletedOn?.Date));
        }

        SelectedReleaseObligation = selectedId is Guid id
            ? ReleaseObligationChoices.SingleOrDefault(item => item.ObligationId == id)
            : null;
        OnPropertyChanged(nameof(HasReleaseObligationChoices));
        OnPropertyChanged(nameof(CanSelectReleaseObligation));
        OnPropertyChanged(nameof(ObligationSelectionGuidance));
    }

    internal AgencyReleaseRequest BuildRequest() => new(
        AuthorizationChoice?.Value,
        ContactType,
        ContactName,
        Relationship,
        ContactAddress,
        ContactCity,
        ContactState,
        ContactFax,
        ContactPhone,
        ContactEmail,
        InformationCategories.Where(option => option.IsSelected).Select(option => option.Value).ToList(),
        OtherInformation,
        StartDate is DateTime start ? DateOnly.FromDateTime(start) : null,
        ExpirationDate is DateTime expiration ? DateOnly.FromDateTime(expiration) : null,
        SelectedScope?.Value.ToString(),
        DrugAlcoholChoice?.Value,
        MentalHealthChoice?.Value,
        HivAidsChoice?.Value,
        ReleaseWithoutReviewChoice?.Value,
        IsRevocation,
        IsRevocation && RevokedOn is DateTime revoked ? DateOnly.FromDateTime(revoked) : null,
        DidObtainRoi,
        IsDraft: !DidObtainRoi);

    private void ResetInputs()
    {
        AuthorizationChoice = null;
        ContactType = null;
        ContactName = string.Empty;
        Relationship = string.Empty;
        ContactAddress = string.Empty;
        ContactCity = string.Empty;
        ContactState = "ME";
        ContactFax = string.Empty;
        ContactPhone = string.Empty;
        ContactEmail = string.Empty;
        OtherInformation = string.Empty;
        foreach (var option in InformationCategories)
            option.IsSelected = false;
        StartDate = DateTime.Today;
        SelectedScope = null;
        ExpirationDate = null;
        DrugAlcoholChoice = null;
        MentalHealthChoice = null;
        HivAidsChoice = null;
        ReleaseWithoutReviewChoice = null;
        IsRevocation = false;
        RevokedOn = null;
        DidObtainRoi = false;
        SelectedReleaseObligation = null;
        ValidationMessage = string.Empty;
        StatusMessage = string.Empty;
    }
}

public sealed record YesNoChoice(string DisplayName, bool Value);
public sealed record AgencyReleaseScopeChoice(string DisplayName, AgencyReleaseScope Value);
public sealed record ReleaseKindChoice(AnnualDocumentKind Kind, string DisplayName);

public sealed record ReleaseDocumentObligationChoice(
    Guid ObligationId,
    AnnualDocumentKind DocumentKind,
    string RecipientDisplayName,
    int? RecipientProviderId,
    DateTime TargetEffectiveDate,
    DateTime DueOn,
    DateTime? CompletedOn)
{
    public string DisplayName =>
        $"{RecipientDisplayName}" +
        (RecipientProviderId is int providerId ? $" (directory #{providerId})" : string.Empty) +
        $" — due {DueOn:MMM d, yyyy} (effective {TargetEffectiveDate:MMM d, yyyy})";

    public string Status => CompletedOn is DateTime completedOn
        ? $"Already attested {completedOn:MMM d, yyyy}; document remains linked to this recipient."
        : "Attestation outstanding.";
}

public partial class AgencyReleaseCategoryOption(string value, string displayName) : ObservableObject
{
    public string Value { get; } = value;
    public string DisplayName { get; } = displayName;

    [ObservableProperty]
    private bool isSelected;
}

public sealed class AgencyReleasePdfReadyEventArgs(byte[] content, string suggestedFileName) : EventArgs
{
    public byte[] Content { get; } = content;
    public string SuggestedFileName { get; } = suggestedFileName;
}

public sealed class AgencyReleaseProblemEventArgs(string title, string message) : EventArgs
{
    public string Title { get; } = title;
    public string Message { get; } = message;
}

public sealed record AgencyReleaseAttestationEventArgs(string Statement, string ScopeNotice);
