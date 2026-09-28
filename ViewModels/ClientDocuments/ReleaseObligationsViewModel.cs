using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Cloud;
using Sati.Models;
using Sati.Services;

namespace Sati.ViewModels.ClientDocuments;

/// <summary>
/// Presents the durable, recipient-specific release obligations. The three
/// release categories are labels; each provider assignment is its own row and
/// receives its own attestation.
/// </summary>
public partial class ReleaseObligationsViewModel(
    IReleaseObligationService service,
    IAgencyReleaseService? agencyReleaseService = null) : ObservableObject
{
    private readonly LatestRequestTracker _loads = new();
    private Person? _person;
    private IReadOnlyList<DocumentArtifactDto> _documentArtifacts = [];
    private readonly HashSet<int> _completedArtifactIds = [];
    private readonly HashSet<int> _deliveredArtifactIds = [];

    public ObservableCollection<ReleaseObligationItemViewModel> Items { get; } = [];
    public ObservableCollection<string> LinkageIssues { get; } = [];
    public ObservableCollection<OneOffReleaseItemViewModel> OneOffReleases { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProviderRequirementsVisible))]
    [NotifyPropertyChangedFor(nameof(IsOneOffReleasesVisible))]
    private bool isOneOffMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProviderModal))]
    private OneOffReleaseItemViewModel? selectedOneOffForProvider;

    [ObservableProperty] private ProviderType providerType = ProviderType.Other;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveOneOffProviderCommand))]
    private string providerName = string.Empty;
    [ObservableProperty] private string providerStreet = string.Empty;
    [ObservableProperty] private string providerCity = string.Empty;
    [ObservableProperty] private string providerState = "ME";
    [ObservableProperty] private string providerZip = string.Empty;
    [ObservableProperty] private string providerPhone = string.Empty;
    [ObservableProperty] private string providerEmail = string.Empty;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveOneOffProviderCommand))]
    private bool assignProviderToConsumer = true;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveOneOffProviderCommand))]
    private DateTime? providerAssignmentStartDate = DateTime.Today;
    [ObservableProperty] private string providerAssignmentRole = string.Empty;
    [ObservableProperty] private string providerModalError = string.Empty;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveOneOffProviderCommand))]
    private bool isSavingProvider;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private string signerLabel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAttestationEditor))]
    private ReleaseObligationItemViewModel? selectedForAttestation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompleteAttestationCommand))]
    private DateTime? completionDate;

    [ObservableProperty]
    private string completionDateError = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWithdrawalEditor))]
    private ReleaseObligationItemViewModel? selectedForWithdrawal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRevocationEditor))]
    private ReleaseObligationItemViewModel? selectedForRevocation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompleteRevocationCommand))]
    private string revocationReason = string.Empty;

    [ObservableProperty]
    private string revocationError = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompleteWithdrawalCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompleteRevocationCommand))]
    private DateTime? withdrawalDate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompleteWithdrawalCommand))]
    private string withdrawalReason = string.Empty;

    [ObservableProperty]
    private string withdrawalError = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompleteAttestationCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompleteWithdrawalCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrepareOneOffReleaseCommand))]
    [NotifyPropertyChangedFor(nameof(HasNoItems))]
    private bool isBusy;

    public Func<Task>? ComplianceChangedAsync { get; set; }
    public event Action<IReadOnlyList<ReleaseObligationDto>>? ObligationsChanged;
    public event Action<ReleaseObligationItemViewModel>? PreparationRequested;
    public event Action? OneOffPreparationRequested;

    public bool HasItems => Items.Count != 0;
    public bool HasNoItems => !IsBusy && Items.Count == 0;
    public bool HasLinkageIssues => LinkageIssues.Count != 0;
    public bool HasStatusMessage => StatusMessage.Length != 0;
    public bool HasAttestationEditor => SelectedForAttestation is not null;
    public bool HasWithdrawalEditor => SelectedForWithdrawal is not null;
    public bool HasRevocationEditor => SelectedForRevocation is not null;
    public bool HasOneOffReleases => OneOffReleases.Count != 0;
    public bool HasNoOneOffReleases => OneOffReleases.Count == 0;
    public bool IsProviderRequirementsVisible => !IsOneOffMode;
    public bool IsOneOffReleasesVisible => IsOneOffMode;
    public bool HasProviderModal => SelectedOneOffForProvider is not null;
    public IReadOnlyList<ProviderType> ProviderTypes { get; } =
        Enum.GetValues<ProviderType>();

    public void SetPerson(Person? person)
    {
        _person = person;
        _documentArtifacts = [];
        _completedArtifactIds.Clear();
        _deliveredArtifactIds.Clear();
        IsOneOffMode = false;
        var request = _loads.Begin();
        Items.Clear();
        OneOffReleases.Clear();
        OnPropertyChanged(nameof(HasOneOffReleases));
        OnPropertyChanged(nameof(HasNoOneOffReleases));
        LinkageIssues.Clear();
        SignerLabel = string.Empty;
        StatusMessage = person is not null && person.EffectiveDate is null
            ? "Set the annual effective date before managing releases."
            : string.Empty;
        CancelEditorsCore();
        RaiseCollectionsChanged();
        PrepareOneOffReleaseCommand.NotifyCanExecuteChanged();
        ObligationsChanged?.Invoke([]);

        if (person?.EffectiveDate is not null && person.Id > 0)
            _ = LoadAsync(person, request);
    }

    public void SetDocumentArtifacts(IReadOnlyList<DocumentArtifactDto> artifacts)
    {
        _documentArtifacts = artifacts ?? [];
        foreach (var item in Items)
            item.SetDocumentArtifact(CurrentArtifact(item.RecordId));
        RebuildOneOffReleases();
    }

    public void SetSignatureState(
        IEnumerable<ExternalSignatureEvidenceDto> externalEvidence,
        IEnumerable<SignatureRequestDto> requests)
    {
        ArgumentNullException.ThrowIfNull(externalEvidence);
        ArgumentNullException.ThrowIfNull(requests);
        _completedArtifactIds.Clear();
        _deliveredArtifactIds.Clear();
        foreach (var evidence in externalEvidence)
            _completedArtifactIds.Add(evidence.DocumentArtifactId);
        foreach (var request in requests)
        {
            if (request.State == "Signed")
                _completedArtifactIds.Add(request.DocumentArtifactId);
            else if (request.DeliveryState == "Sent")
                _deliveredArtifactIds.Add(request.DocumentArtifactId);
        }
        RebuildOneOffReleases();
    }

    [RelayCommand]
    private void ShowProviderRequirements() => IsOneOffMode = false;

    [RelayCommand]
    private void ShowOneOffReleases() => IsOneOffMode = true;

    public Task RefreshAsync()
    {
        var person = _person;
        return person?.EffectiveDate is not null && person.Id > 0
            ? RefreshAndNotifyAsync(person, _loads.Begin())
            : Task.CompletedTask;
    }

    private async Task RefreshAndNotifyAsync(Person person, int request)
    {
        if (!await LoadAsync(person, request))
            return;

        // This is the one post-mutation notification path. Both release edits and
        // provider-assignment changes call RefreshAsync, so the profile banner and
        // the caseload/sidebar refresh only after the authoritative facts have been
        // merged into Person, and they refresh exactly once.
        if (ComplianceChangedAsync is not null)
            await ComplianceChangedAsync();
    }

    public async Task<bool> OpenForAttestationAsync(
        Guid obligationId,
        DateTime? targetEffectiveDate = null)
    {
        if (obligationId == Guid.Empty)
            throw new ArgumentException("A release obligation identifier is required.",
                nameof(obligationId));

        var person = _person;
        if (person?.EffectiveDate is null || person.Id <= 0)
            return false;

        var target = targetEffectiveDate?.Date;
        var item = FindExactItem(obligationId, target);
        if (item is null)
        {
            await LoadAsync(person, _loads.Begin());
            item = FindExactItem(obligationId, target);
        }

        if (item is null)
        {
            StatusMessage = target is DateTime exactTarget
                ? $"The exact release obligation for the {exactTarget:MMM d, yyyy} annual cycle is no longer available. Refresh the consumer record before continuing."
                : "The exact release obligation is no longer available. Refresh the consumer record before continuing.";
            return false;
        }

        if (!item.CanAttest)
        {
            StatusMessage = $"{item.Name} for the {item.TargetEffectiveDate:MMM d, yyyy} annual cycle is not available for attestation.";
            return false;
        }

        StatusMessage = string.Empty;
        BeginAttestation(item);
        return true;
    }

    private ReleaseObligationItemViewModel? FindExactItem(
        Guid obligationId,
        DateTime? targetEffectiveDate) =>
        Items.SingleOrDefault(item =>
            item.ObligationId == obligationId &&
            (targetEffectiveDate is null ||
             item.TargetEffectiveDate == targetEffectiveDate.Value.Date));

    private async Task<bool> LoadAsync(Person person, int request)
    {
        IsBusy = true;
        try
        {
            var currentTarget = ComplianceScheduleRules.CurrentTargetEffectiveDate(
                person.EffectiveDate!.Value, DateTime.Today);
            var statuses = new List<ReleaseObligationStatusDto>();

            // Reconciliation intentionally owns only the current and upcoming
            // cycles. Older exact rows are read without regenerating history, but
            // any still-actionable missed target remains visible and selectable.
            var historicalTargets = ((IEventSource)person).ReleaseComplianceFacts
                .Where(item => item.TargetEffectiveDate is DateTime target &&
                               target.Date >= person.EffectiveDate.Value.Date &&
                               target.Date < currentTarget)
                .Where(item => item.RetiredOn is null ||
                               DateTime.Today < item.RetiredOn.Value.Date)
                .Where(item => ReleaseAttestationRules.CompletedOn(
                    item.StableKey, item.Attestations) is null)
                .Select(item => item.TargetEffectiveDate!.Value.Date)
                .Distinct()
                .OrderBy(target => target)
                .ToArray();
            foreach (var historicalTarget in historicalTargets)
                statuses.Add(await service.GetStatusAsync(person.Id, historicalTarget));

            foreach (var target in ComplianceScheduleRules
                         .CurrentAndUpcomingTargetEffectiveDates(
                             person.EffectiveDate.Value, DateTime.Today))
                statuses.Add(await service.ReconcileAsync(person.Id, target));

            if (!_loads.IsCurrent(request) || _person?.Id != person.Id)
                return false;

            MergeAuthoritativeFacts(person, statuses);

            Items.Clear();
            LinkageIssues.Clear();
            foreach (var status in statuses.OrderBy(status => status.TargetEffectiveDate))
            {
                SignerLabel = status.SignerLabel;
                foreach (var obligation in status.Obligations)
                    Items.Add(new ReleaseObligationItemViewModel(
                        obligation, DateTime.Today, CurrentArtifact(obligation.Id)));
                foreach (var issue in status.LinkageIssues.Select(issue => issue.Message))
                {
                    if (!LinkageIssues.Contains(issue, StringComparer.Ordinal))
                        LinkageIssues.Add(issue);
                }
            }

            StatusMessage = string.Empty;
            RaiseCollectionsChanged();
            ObligationsChanged?.Invoke(statuses
                .SelectMany(status => status.Obligations)
                .ToArray());
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                               or ArgumentException
                                               or UnauthorizedAccessException
                                               or CloudApiException
                                               or CloudConnectivityException
                                               or SessionExpiredException)
        {
            if (_loads.IsCurrent(request) && _person?.Id == person.Id)
                StatusMessage = exception.Message;
            return false;
        }
        finally
        {
            if (_loads.IsCurrent(request))
                IsBusy = false;
        }
    }

    private DocumentArtifactDto? CurrentArtifact(long obligationRecordId) =>
        _documentArtifacts
            .Where(artifact => artifact.ReleaseObligationRecordId == obligationRecordId)
            .OrderByDescending(artifact => artifact.GeneratedAtUtc)
            .ThenByDescending(artifact => artifact.Id)
            .FirstOrDefault();

    [RelayCommand]
    private void PrepareRelease(ReleaseObligationItemViewModel? item)
    {
        if (item?.CanPrepare != true)
            return;
        CancelEditorsCore();
        StatusMessage = string.Empty;
        PreparationRequested?.Invoke(item);
    }

    private bool CanPrepareOneOffRelease() =>
        !IsBusy && _person is { Id: > 0, EffectiveDate: not null };

    [RelayCommand(CanExecute = nameof(CanPrepareOneOffRelease))]
    private void PrepareOneOffRelease()
    {
        CancelEditorsCore();
        StatusMessage = string.Empty;
        IsOneOffMode = true;
        OneOffPreparationRequested?.Invoke();
    }

    [RelayCommand]
    private void BeginAddOneOffProvider(OneOffReleaseItemViewModel? item)
    {
        if (item?.CanAddToProviderList != true)
            return;
        var recipient = item.Artifact.OneOffRecipient;
        if (recipient is null)
            return;
        SelectedOneOffForProvider = item;
        ProviderType = item.Artifact.Kind == nameof(AnnualDocumentKind.ReleaseMedical) ||
                       recipient.ContactType?.Contains("health", StringComparison.OrdinalIgnoreCase) == true
            ? ProviderType.Healthcare
            : ProviderType.Other;
        ProviderName = recipient.ContactName;
        ProviderStreet = recipient.Street ?? string.Empty;
        ProviderCity = recipient.City ?? string.Empty;
        ProviderState = string.IsNullOrWhiteSpace(recipient.State) ? "ME" : recipient.State;
        ProviderZip = string.Empty;
        ProviderPhone = recipient.Phone ?? string.Empty;
        ProviderEmail = recipient.Email ?? string.Empty;
        AssignProviderToConsumer = true;
        ProviderAssignmentStartDate = DateTime.Today;
        ProviderAssignmentRole = recipient.Relationship ?? string.Empty;
        ProviderModalError = string.Empty;
    }

    [RelayCommand]
    private void CancelAddOneOffProvider()
    {
        if (IsSavingProvider)
            return;
        SelectedOneOffForProvider = null;
        ProviderModalError = string.Empty;
    }

    private bool CanSaveOneOffProvider() =>
        !IsSavingProvider && SelectedOneOffForProvider is not null &&
        !string.IsNullOrWhiteSpace(ProviderName) &&
        (!AssignProviderToConsumer || ProviderAssignmentStartDate is not null);

    [RelayCommand(CanExecute = nameof(CanSaveOneOffProvider))]
    private async Task SaveOneOffProviderAsync()
    {
        if (_person is not { Id: > 0 } person ||
            SelectedOneOffForProvider is not { } item ||
            agencyReleaseService is null)
            return;
        if (!CanSaveOneOffProvider())
            return;

        IsSavingProvider = true;
        ProviderModalError = string.Empty;
        try
        {
            var provider = new SaveProviderRequest(
                ProviderType.ToString(), ProviderName.Trim(),
                NullIfBlank(ProviderStreet), NullIfBlank(ProviderCity),
                NullIfBlank(ProviderState), NullIfBlank(ProviderZip),
                null, NullIfBlank(ProviderPhone), 0, false,
                null, null, null, Email: NullIfBlank(ProviderEmail));
            var result = await agencyReleaseService.PromoteOneOffToProviderAsync(
                person.Id,
                new PromoteOneOffReleaseRequest(
                    item.Artifact.Id, provider, AssignProviderToConsumer,
                    ProviderAssignmentStartDate?.Date,
                    NullIfBlank(ProviderAssignmentRole)));
            item.MarkPromoted(result.ProviderId);
            SelectedOneOffForProvider = null;
            StatusMessage = result.AssignedToConsumer
                ? $"{ProviderName.Trim()} was added to the provider directory and assigned to this consumer. Any tracked release requirements follow the assignment date; the one-off document remains unchanged."
                : $"{ProviderName.Trim()} was added to the provider directory. The one-off document remains unchanged.";
            if (result.AssignedToConsumer)
                await RefreshAsync();
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                               or ArgumentException
                                               or UnauthorizedAccessException
                                               or CloudApiException
                                               or CloudConnectivityException
                                               or SessionExpiredException)
        {
            ProviderModalError = exception.Message;
        }
        finally
        {
            IsSavingProvider = false;
        }
    }

    private void RebuildOneOffReleases()
    {
        OneOffReleases.Clear();
        foreach (var artifact in _documentArtifacts
                     .Where(artifact => artifact.OneOffReleaseId is not null &&
                                        artifact.OneOffRecipient is not null)
                     .OrderByDescending(artifact => artifact.GeneratedAtUtc))
            OneOffReleases.Add(new OneOffReleaseItemViewModel(
                artifact,
                _completedArtifactIds.Contains(artifact.Id),
                _deliveredArtifactIds.Contains(artifact.Id)));
        OnPropertyChanged(nameof(HasOneOffReleases));
        OnPropertyChanged(nameof(HasNoOneOffReleases));
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void MergeAuthoritativeFacts(
        Person person,
        IReadOnlyCollection<ReleaseObligationStatusDto> statuses)
    {
        var refreshedTargets = statuses
            .Select(status => status.TargetEffectiveDate.Date)
            .ToHashSet();
        var retained = ((IEventSource)person).ReleaseComplianceFacts
            .Where(fact => fact.TargetEffectiveDate is not DateTime target ||
                           !refreshedTargets.Contains(target.Date));
        var refreshed = statuses
            .SelectMany(status => status.Obligations)
            .Select(ToComplianceFact);

        person.ReleaseComplianceSnapshots = retained
            .Concat(refreshed)
            .GroupBy(fact => fact.StableKey, StringComparer.Ordinal)
            .Select(group => group.Last())
            .OrderBy(fact => fact.DueOn)
            .ThenBy(fact => fact.StableKey, StringComparer.Ordinal)
            .ToList();
    }

    private static ReleaseComplianceFact ToComplianceFact(ReleaseObligationDto source) =>
        new(
            source.StableKey,
            Enum.Parse<ReleaseObligationCategory>(source.Category),
            source.DueOn.Date,
            source.AppliesFromOn.Date,
            source.RetiredOn?.Date,
            source.Attestations.Where(attestation => attestation.RevokedAtUtc is null)
                .Select(attestation => new ReleaseAttestationFact(
                source.StableKey,
                attestation.CompletedOn.Date,
                attestation.RecordedAtUtc,
                Enum.Parse<ReleaseAttestationSource>(attestation.Source),
                attestation.SignatureCompletionId,
                attestation.Id > 0 ? attestation.Id : null)).ToArray(),
            source.ObligationId,
            source.TargetEffectiveDate.Date,
            source.AvailableOn.Date,
            source.RecipientDisplayName);

    [RelayCommand]
    private void BeginAttestation(ReleaseObligationItemViewModel? item)
    {
        if (item is null || !item.CanAttest)
            return;
        SelectedForWithdrawal = null;
        SelectedForRevocation = null;
        WithdrawalDate = null;
        WithdrawalReason = string.Empty;
        WithdrawalError = string.Empty;
        SelectedForAttestation = item;
        CompletionDate = null;
        CompletionDateError = string.Empty;
    }

    partial void OnCompletionDateChanged(DateTime? value)
    {
        CompletionDateError = value is DateTime date &&
                              SelectedForAttestation is { } item
            ? ValidateAttestationDate(item, date) ?? string.Empty
            : string.Empty;
    }

    private bool CanCompleteAttestation() =>
        !IsBusy &&
        SelectedForAttestation is { } item &&
        CompletionDate is DateTime date &&
        ValidateAttestationDate(item, date) is null;

    [RelayCommand(CanExecute = nameof(CanCompleteAttestation))]
    private async Task CompleteAttestation()
    {
        if (_person is not { } person ||
            SelectedForAttestation is not { } item ||
            CompletionDate is not DateTime completedOn)
            return;

        var validation = ValidateAttestationDate(item, completedOn);
        if (validation is not null)
        {
            CompletionDateError = validation;
            return;
        }

        await RunMutationAsync(() => service.AttestAsync(
            person.Id, item.ObligationId, completedOn.Date));
    }

    [RelayCommand]
    private void BeginWithdrawal(ReleaseObligationItemViewModel? item)
    {
        if (item is null || !item.CanWithdraw)
            return;
        SelectedForAttestation = null;
        SelectedForRevocation = null;
        CompletionDate = null;
        CompletionDateError = string.Empty;
        SelectedForWithdrawal = item;
        WithdrawalDate = null;
        WithdrawalReason = string.Empty;
        WithdrawalError = string.Empty;
    }

    partial void OnWithdrawalDateChanged(DateTime? value)
    {
        WithdrawalError = value is DateTime date &&
                          SelectedForWithdrawal is { } item
            ? ValidateWithdrawalDate(item, date) ?? string.Empty
            : string.Empty;
    }

    private bool CanCompleteWithdrawal() =>
        !IsBusy &&
        SelectedForWithdrawal is { } item &&
        WithdrawalDate is DateTime date &&
        ValidateWithdrawalDate(item, date) is null &&
        !string.IsNullOrWhiteSpace(WithdrawalReason);

    [RelayCommand(CanExecute = nameof(CanCompleteWithdrawal))]
    private async Task CompleteWithdrawal()
    {
        if (_person is not { } person ||
            SelectedForWithdrawal is not { } item ||
            WithdrawalDate is not DateTime withdrawnOn)
            return;

        var dateError = ValidateWithdrawalDate(item, withdrawnOn);
        if (dateError is not null)
        {
            WithdrawalError = dateError;
            return;
        }
        if (string.IsNullOrWhiteSpace(WithdrawalReason))
        {
            WithdrawalError = "Enter an explanation for the withdrawal.";
            return;
        }

        await RunMutationAsync(() => service.WithdrawAsync(
            person.Id,
            item.ObligationId,
            withdrawnOn.Date,
            WithdrawalReason.Trim()));
    }

    [RelayCommand]
    private void BeginRevocation(ReleaseObligationItemViewModel? item)
    {
        if (item is null || !item.CanRevokeAttestation)
            return;
        SelectedForAttestation = null;
        SelectedForWithdrawal = null;
        SelectedForRevocation = item;
        RevocationReason = string.Empty;
        RevocationError = string.Empty;
    }

    private bool CanCompleteRevocation() =>
        !IsBusy && SelectedForRevocation is not null &&
        !string.IsNullOrWhiteSpace(RevocationReason);

    [RelayCommand(CanExecute = nameof(CanCompleteRevocation))]
    private async Task CompleteRevocation()
    {
        if (_person is not { } person || SelectedForRevocation is not { } item)
            return;
        if (string.IsNullOrWhiteSpace(RevocationReason))
        {
            RevocationError = "Explain why the attestation is being revoked.";
            return;
        }
        await RunMutationAsync(() => service.RevokeAttestationAsync(
            person.Id, item.ObligationId, RevocationReason.Trim()));
    }

    [RelayCommand]
    private void CancelEditors() => CancelEditorsCore();

    private void CancelEditorsCore()
    {
        SelectedForAttestation = null;
        CompletionDate = null;
        CompletionDateError = string.Empty;
        SelectedForWithdrawal = null;
        SelectedForRevocation = null;
        WithdrawalDate = null;
        WithdrawalReason = string.Empty;
        WithdrawalError = string.Empty;
        RevocationReason = string.Empty;
        RevocationError = string.Empty;
    }

    private async Task RunMutationAsync(Func<Task<ReleaseObligationDto>> mutation)
    {
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            await mutation();
            CancelEditorsCore();
            await RefreshAsync();
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                               or ArgumentException
                                               or UnauthorizedAccessException
                                               or CloudApiException
                                               or CloudConnectivityException
                                               or SessionExpiredException)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string? ValidateAttestationDate(
        ReleaseObligationItemViewModel item,
        DateTime completedOn)
    {
        if (completedOn.Date > DateTime.Today)
            return "The completion date cannot be in the future.";
        return completedOn.Date < item.AvailableOn.Date
            ? $"This release was not available for completion before {item.AvailableOn:MMM d, yyyy}."
            : null;
    }

    private static string? ValidateWithdrawalDate(
        ReleaseObligationItemViewModel item,
        DateTime withdrawnOn)
    {
        if (withdrawnOn.Date > DateTime.Today)
            return "The withdrawal date cannot be in the future.";
        return item.CompletedOn is DateTime completedOn && withdrawnOn.Date < completedOn.Date
            ? "The withdrawal date cannot be before the authorization completion date."
            : null;
    }

    private void RaiseCollectionsChanged()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasNoItems));
        OnPropertyChanged(nameof(HasLinkageIssues));
    }
}

public sealed partial class OneOffReleaseItemViewModel(
    DocumentArtifactDto artifact,
    bool isCompleted = false,
    bool isDelivered = false) : ObservableObject
{
    public DocumentArtifactDto Artifact { get; private set; } = artifact;
    public string RecipientName => Artifact.OneOffRecipient?.ContactName ?? "One-off recipient";
    public string DocumentType => Artifact.Kind == nameof(AnnualDocumentKind.ReleaseMedical)
        ? "Medical release"
        : "Agency release";
    public string PreparedLabel => Artifact.Origin == nameof(DocumentArtifactOrigin.Draft)
        ? $"Draft saved {Artifact.GeneratedAtUtc.ToLocalTime():MMM d, yyyy}"
        : $"Prepared {Artifact.GeneratedAtUtc.ToLocalTime():MMM d, yyyy}";
    public string WorkflowStage => isCompleted
        ? "Completed"
        : isDelivered
            ? "Delivered"
            : Artifact.Origin == nameof(DocumentArtifactOrigin.Draft)
                ? "Generated"
                : "Prepared";
    public bool CanAddToProviderList =>
        Artifact.Origin == nameof(DocumentArtifactOrigin.GeneratedInSati) &&
        Artifact.PromotedProviderId is null;
    public bool HasPromotedProvider => Artifact.PromotedProviderId is not null;
    public string ProviderStatus => Artifact.PromotedProviderId is int id
        ? $"Added to provider list (directory #{id})"
        : string.Empty;

    public void MarkPromoted(int providerId)
    {
        Artifact = Artifact with { PromotedProviderId = providerId };
        OnPropertyChanged(nameof(Artifact));
        OnPropertyChanged(nameof(CanAddToProviderList));
        OnPropertyChanged(nameof(HasPromotedProvider));
        OnPropertyChanged(nameof(ProviderStatus));
    }
}

public sealed partial class ReleaseObligationItemViewModel : ObservableObject
{
    public ReleaseObligationItemViewModel(
        ReleaseObligationDto source,
        DateTime today,
        DocumentArtifactDto? artifact = null)
    {
        RecordId = source.Id;
        ObligationId = source.ObligationId;
        Category = source.Category;
        RecipientDisplayName = source.RecipientDisplayName;
        RecipientProviderId = source.RecipientProviderId;
        TargetEffectiveDate = source.TargetEffectiveDate.Date;
        AvailableOn = source.AvailableOn.Date;
        DueOn = source.DueOn.Date;
        RetiredOn = source.RetiredOn?.Date;
        CompletedOn = source.CompletedOn?.Date;
        WithdrawnOn = source.WithdrawnOn?.Date;
        IsAuthorizationActive = source.IsAuthorizationActive;
        CanAttest = CompletedOn is null && today.Date >= AvailableOn;
        CanWithdraw = CompletedOn is not null && IsAuthorizationActive;
        CanRevokeAttestation = source.Attestations.Count(item => item.RevokedAtUtc is null) == 1 &&
            source.Attestations.Any(item => item.RevokedAtUtc is null && item.Source == "Manual");
        CanPrepare = CompletedOn is null &&
            RetiredOn is null &&
            today.Date >= AvailableOn &&
            Category is nameof(ReleaseObligationCategory.Agency) or
                nameof(ReleaseObligationCategory.Medical);
        SetDocumentArtifact(artifact);
    }

    public long RecordId { get; }
    public Guid ObligationId { get; }
    public string Category { get; }
    public string? RecipientDisplayName { get; }
    public int? RecipientProviderId { get; }
    public DateTime TargetEffectiveDate { get; }
    public DateTime AvailableOn { get; }
    public DateTime DueOn { get; }
    public DateTime? RetiredOn { get; }
    public DateTime? CompletedOn { get; }
    public DateTime? WithdrawnOn { get; }
    public bool IsAuthorizationActive { get; }
    public bool CanAttest { get; }
    public bool CanWithdraw { get; }
    public bool CanRevokeAttestation { get; }
    public bool CanPrepare { get; }
    public int? CurrentDocumentArtifactId { get; private set; }
    public string WorkflowStage { get; private set; } = "Not started";
    public string WorkflowDescription { get; private set; } = "Prepare the recipient-specific release.";

    public void SetDocumentArtifact(DocumentArtifactDto? artifact)
    {
        CurrentDocumentArtifactId = artifact?.Id;
        if (CompletedOn is not null)
        {
            WorkflowStage = "Completed";
            WorkflowDescription = "Signed evidence is retained and the obligation is satisfied.";
        }
        else if (artifact is { Origin: "GeneratedInSati", BlankFields.Count: 0 })
        {
            WorkflowStage = "Prepared";
            WorkflowDescription = "The final signing copy is ready for external or internal signing.";
        }
        else if (artifact is not null)
        {
            WorkflowStage = "Generated";
            WorkflowDescription = "A draft exists. Review and prepare the final signing copy.";
        }
        else
        {
            WorkflowStage = "Not started";
            WorkflowDescription = "Prepare the recipient-specific release.";
        }
        OnPropertyChanged(nameof(CurrentDocumentArtifactId));
        OnPropertyChanged(nameof(WorkflowStage));
        OnPropertyChanged(nameof(WorkflowDescription));
        OnPropertyChanged(nameof(AutomationName));
    }

    public string Name => Category switch
    {
        nameof(ReleaseObligationCategory.Dhhs) => "DHHS release",
        nameof(ReleaseObligationCategory.Medical) =>
            $"Medical release — {RecipientLabel}",
        nameof(ReleaseObligationCategory.Agency) =>
            $"Agency release — {RecipientLabel}",
        _ => $"{Category} release"
    };

    private string RecipientLabel =>
        $"{RecipientDisplayName ?? "provider not linked"}" +
        (RecipientProviderId is int providerId ? $" (directory #{providerId})" : string.Empty);

    public string CycleLabel => $"Annual effective date {TargetEffectiveDate:MMM d, yyyy}";
    public string TimingLabel => $"Available {AvailableOn:MMM d, yyyy}; due {DueOn:MMM d, yyyy}";
    public string AutomationName => $"{Name}. {CycleLabel}. {TimingLabel}. {WorkflowStage}. {StatusLabel}";
    public string AttestAutomationName => $"Attest {Name} for {TargetEffectiveDate:MMM d, yyyy}";
    public string WithdrawAutomationName =>
        $"Withdraw authorization for {Name}, effective-date cycle {TargetEffectiveDate:MMM d, yyyy}";
    public string RevokeAutomationName =>
        $"Revoke manual attestation for {Name}, effective-date cycle {TargetEffectiveDate:MMM d, yyyy}";
    public string StatusLabel => CompletedOn is DateTime completed
        ? WithdrawnOn is DateTime withdrawn
            ? $"Attested {completed:MMM d, yyyy}; authorization withdrawn {withdrawn:MMM d, yyyy}."
            : $"Attested {completed:MMM d, yyyy}."
        : RetiredOn is DateTime retired
            ? $"Not attested; assignment retired {retired:MMM d, yyyy}."
            : DateTime.Today < AvailableOn
                ? $"Not yet available; opens {AvailableOn:MMM d, yyyy}."
                : "Attestation outstanding.";
}
