using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Cloud;
using Sati.Models;
using Sati.Services;

namespace Sati.ViewModels.Children;

/// <summary>
/// One consumer's healthcare and waiver/service provider assignments.
/// <para>
/// Medical practice and network are never stored on a row here. They are resolved from the
/// agency directory every time the list is built. Waiver providers remain ordinary direct
/// assignments and do not inherit medical-only labels or controls.
/// </para>
/// <para>
/// Tidiness comes from state rather than a cap: current providers are listed, ended ones sit
/// behind a disclosure, and ending a relationship keeps the row.
/// </para>
/// </summary>
public partial class ConsumerProvidersViewModel : ObservableObject
{
    // Selection-driven loads race. Click three consumers quickly and the slowest response
    // would otherwise publish one consumer's providers under another's name.
    private readonly LatestRequestTracker _loads = new();
    private readonly IConsumerProviderService _linkService;
    private readonly IProviderService _providerService;
    private readonly Func<DateTime> _today;

    private List<Provider> _directory = [];
    private List<ProviderAffiliationNode> _nodes = [];
    private int? _personId;
    private string _orderVersion = string.Empty;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetryOrderCommand))]
    private bool hasOrderChanges;
    [ObservableProperty] private string orderMessage = string.Empty;

    // The free-text fields that predate the directory. Held so the panel can offer to link
    // them; never written to, and never cleared — the typed value is the only record of what
    // somebody actually entered.
    private string? _legacyPrimaryCare;
    private string? _legacyHealthcareSystem;

    /// <summary>
    /// Called after a provider assignment has been durably changed and the provider list has
    /// reloaded. The profile host uses it to reconcile recipient-specific release obligations.
    /// </summary>
    public Func<Task>? ProviderAssignmentsChangedAsync { get; set; }

    public ConsumerProvidersViewModel(
        IConsumerProviderService linkService,
        IProviderService providerService)
        : this(linkService, providerService, () => DateTime.Today)
    {
    }

    internal ConsumerProvidersViewModel(
        IConsumerProviderService linkService,
        IProviderService providerService,
        Func<DateTime> today)
    {
        _linkService = linkService;
        _providerService = providerService;
        _today = today;
    }

    /// <summary>Current relationships, primary care first.</summary>
    public ObservableCollection<ConsumerProviderRowViewModel> Current { get; } = [];

    /// <summary>Ended relationships. Kept, not deleted — and collapsed by default.</summary>
    public ObservableCollection<ConsumerProviderRowViewModel> Past { get; } = [];

    /// <summary>Directory entries offered in the picker, individuals first.</summary>
    public ObservableCollection<Provider> ProviderOptions { get; } = [];

    [ObservableProperty] private bool hasLoadedPerson;
    [ObservableProperty] private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedProviderAffiliation))]
    [NotifyPropertyChangedFor(nameof(HasSelectedProviderAffiliation))]
    private int? newProviderId;

    [ObservableProperty] private string newRole = string.Empty;
    [ObservableProperty] private DateTime? newStartDate;
    [ObservableProperty] private bool newIsPrimaryCare;
    [ObservableProperty] private bool newHasActiveRelease;
    [ObservableProperty] private bool showPast;

    public bool HasStatusMessage => StatusMessage.Length > 0;
    public bool HasCurrent => Current.Count > 0;
    public bool HasPast => Past.Count > 0;
    public bool CanAdd =>
        HasLoadedPerson && NewProviderId is > 0 && NewStartDate is not null && !IsBusy && !HasOrderChanges;
    public bool HasSelectedProvider => SelectedProvider is not null;
    public bool SelectedProviderIsMedical => SelectedProvider?.Type == ProviderType.Healthcare;
    public bool SelectedProviderIsService => SelectedProvider?.Type == ProviderType.Waiver;
    public string SelectedProviderKindLabel => SelectedProvider?.Type switch
    {
        ProviderType.Healthcare => "Medical provider assignment",
        ProviderType.Waiver => "Waiver / service provider assignment",
        _ => string.Empty
    };
    public string AssignmentStartGuidance => SelectedProvider?.Type switch
    {
        ProviderType.Waiver =>
            "Enter the actual first day this provider is assigned to deliver the service. The related Agency release is due before that service begins.",
        ProviderType.Healthcare =>
            "Enter the actual date this healthcare relationship began or is scheduled to begin.",
        _ => "Choose a provider, then enter the actual assignment start date."
    };

    public string PastDisclosureLabel => Past.Count == 1
        ? "1 past provider"
        : $"{Past.Count} past providers";

    /// <summary>
    /// The chain above the provider being added, shown live under the picker so the case
    /// manager can see they picked the right clinician before committing.
    /// </summary>
    public string SelectedProviderAffiliation => NewProviderId is { } id
        && SelectedProviderIsMedical
        ? ProviderAffiliation.DescribeAffiliation(id, _nodes)
        : string.Empty;

    public bool HasSelectedProviderAffiliation => SelectedProviderAffiliation.Length > 0;

    private Provider? SelectedProvider => NewProviderId is int id
        ? _directory.FirstOrDefault(item => item.Id == id)
        : null;

    partial void OnNewProviderIdChanged(int? value)
    {
        if (!SelectedProviderIsMedical)
        {
            NewIsPrimaryCare = false;
            NewHasActiveRelease = false;
        }
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(HasSelectedProvider));
        OnPropertyChanged(nameof(SelectedProviderIsMedical));
        OnPropertyChanged(nameof(SelectedProviderIsService));
        OnPropertyChanged(nameof(SelectedProviderKindLabel));
        OnPropertyChanged(nameof(AssignmentStartGuidance));
    }
    partial void OnNewStartDateChanged(DateTime? value) => OnPropertyChanged(nameof(CanAdd));
    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(CanLinkLegacyPrimaryCare));
        UpdateOrderCommands();
    }
    partial void OnHasLoadedPersonChanged(bool value) => OnPropertyChanged(nameof(CanAdd));

    /// <summary>
    /// Points the panel at a consumer. Every call takes a request identity, so a slow load
    /// for a consumer the case manager has already navigated away from is discarded rather
    /// than written over the newer one.
    /// </summary>
    /// <remarks>
    /// Takes the consumer rather than an id because the panel also reports on the two free-text
    /// fields that predate the directory, and those live on the record.
    /// </remarks>
    public void SetPerson(Person? person)
    {
        var request = _loads.Begin();

        IsBusy = false;
        HasOrderChanges = false;
        OrderMessage = string.Empty;
        _orderVersion = string.Empty;

        Current.Clear();
        Past.Clear();
        ProviderOptions.Clear();
        _directory = [];
        _nodes = [];
        ClearEditor();
        StatusMessage = string.Empty;
        _personId = person?.Id;
        _legacyPrimaryCare = person?.PrimaryCareProvider;
        _legacyHealthcareSystem = person?.HealthcareSystemName;
        HasLoadedPerson = person is not null;
        RaiseListChanged();

        if (person is not null)
            _ = LoadAsync(person.Id, request);
    }

    public async Task RefreshAsync()
    {
        if (HasOrderChanges || IsBusy) return;
        if (_personId is { } id)
            await LoadAsync(id, _loads.Begin());
    }

    private async Task LoadAsync(int personId, int request)
    {
        IsBusy = true;
        try
        {
            var directory = await _providerService.GetAllAsync();
            var links = await _linkService.GetByPersonAsync(personId);

            if (!_loads.IsCurrent(request) || _personId != personId)
                return;

            _directory = directory;
            _nodes = directory.ToAffiliationNodes();

            ProviderOptions.Clear();
            // Keep medical entries together (individuals first), followed by waiver/service
            // agencies. "Other" directory contacts do not create release obligations and are
            // deliberately outside this assignment workflow.
            foreach (var provider in directory
                         .Where(candidate => candidate.Type is ProviderType.Healthcare or ProviderType.Waiver)
                         .OrderBy(candidate => candidate.Type == ProviderType.Healthcare ? 0 : 1)
                         .ThenBy(candidate => candidate.Type == ProviderType.Healthcare
                             ? candidate.MedicalKind switch
                         {
                             MedicalProviderKind.Individual => 0,
                             MedicalProviderKind.Practice => 1,
                             _ => 2
                         }
                             : 0)
                         .ThenBy(candidate => candidate.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                ProviderOptions.Add(provider);
            }

            Populate(links);
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                              or UnauthorizedAccessException
                                              or CloudApiException
                                              or CloudConnectivityException
                                              or SessionExpiredException)
        {
            if (_loads.IsCurrent(request) && _personId == personId)
                StatusMessage = exception.Message;
        }
        finally
        {
            if (_loads.IsCurrent(request) && _personId == personId) IsBusy = false;
        }
    }

    private void Populate(IEnumerable<PersonProvider> links)
    {
        var snapshot = links.ToList();
        _orderVersion = ConsumerProviderOrder.Version(snapshot.Select(ConsumerProviderOrderSnapshot.Fact));
        HasOrderChanges = false;
        Current.Clear();
        Past.Clear();

        var rows = snapshot.Select(BuildRow).ToList();
        foreach (var row in ConsumerProviderRules.OrderForDisplay(
                     rows.Where(row => row.IsCurrent),
                     row => row.IsPrimaryCare,
                     row => row.SortOrder,
                     row => row.ProviderName))
        {
            Current.Add(row);
        }

        // Most recently ended first: the provider someone stopped seeing last month is the
        // one a case manager is most likely to be looking for.
        foreach (var row in rows.Where(row => !row.IsCurrent)
                     .OrderByDescending(row => row.EndDate ?? DateTime.MinValue)
                     .ThenBy(row => row.ProviderName, StringComparer.CurrentCultureIgnoreCase))
        {
            Past.Add(row);
        }

        RaiseListChanged();
        UpdateOrderCommands();
    }

    private bool CanMoveUp(ConsumerProviderRowViewModel? row) => !IsBusy && row is not null &&
        Current.IndexOf(row) > 0 && !row.IsPrimaryCare && !Current[Current.IndexOf(row) - 1].IsPrimaryCare;
    private bool CanMoveDown(ConsumerProviderRowViewModel? row) => !IsBusy && row is not null &&
        Current.Contains(row) && Current.IndexOf(row) < Current.Count - 1 && !row.IsPrimaryCare;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp(ConsumerProviderRowViewModel? row) => Move(row!, -1);
    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown(ConsumerProviderRowViewModel? row) => Move(row!, 1);
    private void Move(ConsumerProviderRowViewModel row, int direction)
    {
        if (direction < 0 ? !CanMoveUp(row) : !CanMoveDown(row)) return;
        var index = Current.IndexOf(row);
        Current.Move(index, index + direction);
        HasOrderChanges = true;
        OrderMessage = "Order changed. Save order to keep this arrangement. Primary care stays first; past assignments keep their history order.";
        UpdateOrderCommands();
    }

    private bool CanSaveOrder() => HasOrderChanges && !IsBusy && _personId is not null;
    [RelayCommand(CanExecute = nameof(CanSaveOrder))]
    private Task SaveOrder() => PersistOrderAsync(false);
    [RelayCommand(CanExecute = nameof(CanSaveOrder))]
    private Task RetryOrder() => PersistOrderAsync(true);

    private bool CanReloadOrder() => !IsBusy && _personId is not null;
    [RelayCommand(CanExecute = nameof(CanReloadOrder))]
    private async Task ReloadOrder()
    {
        if (IsBusy) return;
        HasOrderChanges = false;
        OrderMessage = string.Empty;
        await RefreshAsync();
    }

    private async Task PersistOrderAsync(bool refreshVersion)
    {
        if (!CanSaveOrder() || _personId is not int personId) return;
        var requestId = _loads.Begin();
        var ids = Current.Select(row => row.Id).ToArray();
        var version = _orderVersion;
        IsBusy = true;
        OrderMessage = "Saving provider order…";
        try
        {
            if (refreshVersion)
            {
                var fresh = await _linkService.GetByPersonAsync(personId);
                if (!_loads.IsCurrent(requestId) || _personId != personId) return;
                var facts = fresh.Select(ConsumerProviderOrderSnapshot.Fact).ToList();
                version = ConsumerProviderOrder.Version(facts);
                if (ConsumerProviderOrder.Validate(new(version, ids), facts) is { } error)
                    throw new InvalidOperationException(error + " Reload to review the changed assignments.");
            }
            var saved = await _linkService.ReorderAsync(personId, new(version, ids));
            if (!_loads.IsCurrent(requestId) || _personId != personId) return;
            // Keep existing row containers/focus. Reordering cannot change relationship fields.
            _orderVersion = ConsumerProviderOrder.Version(saved.Select(ConsumerProviderOrderSnapshot.Fact));
            if (refreshVersion) Populate(saved);
            HasOrderChanges = false;
            OrderMessage = "Provider order saved.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException
            or CloudApiException or CloudConnectivityException or SessionExpiredException)
        {
            if (_loads.IsCurrent(requestId) && _personId == personId)
                OrderMessage = exception.Message + " Your intended order is still shown. Reload discards it; retry applies it to fresh assignments.";
        }
        finally
        {
            if (_loads.IsCurrent(requestId) && _personId == personId) IsBusy = false;
        }
    }

    private void UpdateOrderCommands()
    {
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        SaveOrderCommand.NotifyCanExecuteChanged();
        RetryOrderCommand.NotifyCanExecuteChanged();
        ReloadOrderCommand.NotifyCanExecuteChanged();
        EndProviderCommand.NotifyCanExecuteChanged();
        RemoveProviderCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(CanLinkLegacyPrimaryCare));
    }

    private bool CanChangeAssignment(ConsumerProviderRowViewModel? row) =>
        !IsBusy && !HasOrderChanges && row is not null && Current.Contains(row);

    private ConsumerProviderRowViewModel BuildRow(PersonProvider link)
    {
        var provider = _directory.FirstOrDefault(candidate => candidate.Id == link.ProviderId);
        var isMedical = provider?.Type == ProviderType.Healthcare;
        var practice = isMedical
            ? ProviderAffiliation.NearestAncestorOfKind(
                link.ProviderId, MedicalProviderKind.Practice, _nodes)
            : null;
        var network = isMedical
            ? ProviderAffiliation.NearestAncestorOfKind(
                link.ProviderId, MedicalProviderKind.Network, _nodes)
            : null;

        return new ConsumerProviderRowViewModel(
            link,
            // A directory entry the case manager cannot see is named rather than blank, so a
            // profile row never renders as an unexplained empty line.
            provider?.Name ?? "Provider no longer in the directory",
            provider?.Type,
            practice?.Name ?? string.Empty,
            network?.Name ?? string.Empty);
    }

    [RelayCommand]
    private async Task AddProvider()
    {
        if (IsBusy || HasOrderChanges) return;
        if (_personId is not { } personId || NewProviderId is not { } providerId)
            return;
        if (NewStartDate is not DateTime startDate)
        {
            StatusMessage =
                "Enter the actual provider assignment start date before adding this assignment.";
            return;
        }

        var isMedical = SelectedProviderIsMedical;
        var link = new PersonProvider
        {
            PersonId = personId,
            ProviderId = providerId,
            Role = string.IsNullOrWhiteSpace(NewRole) ? null : NewRole.Trim(),
            IsPrimaryCare = isMedical && NewIsPrimaryCare,
            HasActiveRelease = isMedical && NewHasActiveRelease,
            StartDate = startDate.Date,
            SortOrder = Current.Count
        };

        if (await RunAsync(() => _linkService.SaveAsync(link)))
            ClearEditor();
    }

    [RelayCommand(CanExecute = nameof(CanChangeAssignment))]
    private async Task EndProvider(ConsumerProviderRowViewModel? row)
    {
        if (!CanChangeAssignment(row)) return;
        if (row is null || _personId is not { } personId)
            return;

        // Ended, not deleted. The row stays so the record can still answer who was treating
        // this consumer in a given year.
        await RunAsync(() => _linkService.EndAsync(personId, row.Id, _today()));
    }

    [RelayCommand(CanExecute = nameof(CanChangeAssignment))]
    private async Task RemoveProvider(ConsumerProviderRowViewModel? row)
    {
        if (!CanChangeAssignment(row)) return;
        if (row is null || _personId is not { } personId)
            return;

        // For a link recorded against the wrong consumer. Ending is the command for a
        // relationship that really happened.
        await RunAsync(() => _linkService.RemoveAsync(personId, row.Id));
    }

    private async Task<bool> RunAsync(Func<Task> operation)
    {
        if (_personId is not int personId || IsBusy || HasOrderChanges) return false;
        var request = _loads.Begin();
        StatusMessage = string.Empty;
        IsBusy = true;
        try
        {
            await operation();
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                              or UnauthorizedAccessException
                                              or CloudApiException
                                              or CloudConnectivityException
                                              or SessionExpiredException)
        {
            // The rules reject an edit rather than correcting it, so the entered values stay
            // on screen with the reason beside them.
            if (_loads.IsCurrent(request) && _personId == personId)
                StatusMessage = exception.Message;
            return false;
        }
        finally
        {
            if (_loads.IsCurrent(request) && _personId == personId)
                IsBusy = false;
        }

        if (!_loads.IsCurrent(request) || _personId != personId) return false;
        var refresh = _loads.Begin();
        await LoadAsync(personId, refresh);
        if (!_loads.IsCurrent(refresh) || _personId != personId) return false;
        if (ProviderAssignmentsChangedAsync is not null)
            await ProviderAssignmentsChangedAsync();
        return _loads.IsCurrent(refresh) && _personId == personId;
    }

    private void ClearEditor()
    {
        NewProviderId = null;
        NewRole = string.Empty;
        NewStartDate = null;
        NewIsPrimaryCare = false;
        NewHasActiveRelease = false;
    }

    // ── Linking the free-text fields that predate the directory ──────────────
    //
    // Nothing here is written automatically. A bulk name-match backfill across live consumer
    // records is exactly the operation that should not run unreviewed, and a wrong provider on
    // a medical record is worse than an unlinked one: unlinked is visibly unfinished, wrong
    // looks finished. The panel proposes; a case manager confirms one consumer at a time.
    //
    // The legacy strings are never cleared either way. They are the only record of what
    // somebody actually typed, and a link is an addition beside them, not a replacement.

    private LegacyProviderMatch _legacyMatch;

    /// <summary>
    /// True when free text names a primary care provider and no current link says the same.
    /// A consumer with a current primary-care link has nothing left to reconcile.
    /// </summary>
    public bool NeedsPrimaryCareLinking =>
        _legacyMatch.Outcome != LegacyMatchOutcome.NoLegacyValue &&
        !Current.Any(row => row.IsPrimaryCare);

    public string PrimaryCareLinkGuidance => LegacyProviderLinking.PrimaryCareGuidance(_legacyMatch);

    /// <summary>Only an unambiguous single match can be linked in one click.</summary>
    public bool CanLinkLegacyPrimaryCare => NeedsPrimaryCareLinking && _legacyMatch.CanLink && !IsBusy && !HasOrderChanges;

    public string LinkLegacyPrimaryCareLabel => $"Link {_legacyMatch.ProviderName}";

    /// <summary>
    /// Whether the typed healthcare system still agrees with the network the linked provider
    /// resolves to. A disagreement is surfaced rather than silently resolved: one of the two is
    /// stale and only a person knows which.
    /// </summary>
    public string HealthcareSystemGuidance => LegacyProviderLinking.HealthcareSystemGuidance(
        _legacyHealthcareSystem,
        Current.FirstOrDefault(row => row.IsPrimaryCare)?.NetworkName
            ?? Current.FirstOrDefault(row => row.NetworkName.Length > 0)?.NetworkName);

    public bool HasHealthcareSystemGuidance => HealthcareSystemGuidance.Length > 0;

    [RelayCommand]
    private async Task LinkLegacyPrimaryCare()
    {
        if (!CanLinkLegacyPrimaryCare) return;
        if (_personId is not { } personId || !_legacyMatch.CanLink)
            return;

        // Recorded as primary care because that is what the legacy field meant. No start date:
        // when the relationship began is not something the free text ever knew, and inventing
        // today would assert a fact nobody entered.
        await RunAsync(() => _linkService.SaveAsync(new PersonProvider
        {
            PersonId = personId,
            ProviderId = _legacyMatch.ProviderId,
            IsPrimaryCare = true,
            SortOrder = 0
        }));
    }

    private void RefreshLegacyReconciliation()
    {
        _legacyMatch = LegacyProviderLinking.Match(_legacyPrimaryCare, _nodes);
        OnPropertyChanged(nameof(NeedsPrimaryCareLinking));
        OnPropertyChanged(nameof(PrimaryCareLinkGuidance));
        OnPropertyChanged(nameof(CanLinkLegacyPrimaryCare));
        OnPropertyChanged(nameof(LinkLegacyPrimaryCareLabel));
        OnPropertyChanged(nameof(HealthcareSystemGuidance));
        OnPropertyChanged(nameof(HasHealthcareSystemGuidance));
    }

    private void RaiseListChanged()
    {
        OnPropertyChanged(nameof(HasCurrent));
        OnPropertyChanged(nameof(HasPast));
        OnPropertyChanged(nameof(PastDisclosureLabel));
        RefreshLegacyReconciliation();
    }
}

/// <summary>
/// One row of the list. The practice and network are passed in already resolved — the row
/// holds no directory of its own, so it cannot resolve them a second, different way.
/// </summary>
public sealed class ConsumerProviderRowViewModel
{
    public ConsumerProviderRowViewModel(
        PersonProvider link,
        string providerName,
        ProviderType? providerType,
        string practiceName,
        string networkName)
    {
        Id = link.Id;
        ProviderId = link.ProviderId;
        ProviderName = providerName;
        ProviderType = providerType;
        PracticeName = practiceName;
        NetworkName = networkName;
        Role = link.Role;
        IsPrimaryCare = link.IsPrimaryCare;
        HasActiveRelease = link.HasActiveRelease;
        SortOrder = link.SortOrder;
        StartDate = link.StartDate;
        EndDate = link.EndDate;
        IsCurrent = link.IsActive;
        Affiliation = string.Join(
            " · ", new[] { practiceName, networkName }.Where(part => part.Length > 0));
    }

    public int Id { get; }
    public int ProviderId { get; }
    public string ProviderName { get; }
    public ProviderType? ProviderType { get; }
    public string PracticeName { get; }
    public string NetworkName { get; }
    public string? Role { get; }
    public bool IsPrimaryCare { get; }
    public bool HasActiveRelease { get; }
    public int SortOrder { get; }
    public DateTime? StartDate { get; }
    public DateTime? EndDate { get; }
    public bool IsCurrent { get; }
    public bool IsMedicalProvider => ProviderType == global::Sati.ProviderType.Healthcare;
    public string ProviderKindLabel => ProviderType switch
    {
        global::Sati.ProviderType.Healthcare => "Medical provider",
        global::Sati.ProviderType.Waiver => "Waiver / service provider",
        global::Sati.ProviderType.Other => "Other provider",
        _ => "Provider type unavailable"
    };

    /// <summary>
    /// "Coastal Women's Healthcare · MaineHealth", or empty when the provider stands alone.
    /// Read-only in the interface: an editable derived value is a stored copy in disguise.
    /// </summary>
    public string Affiliation { get; }

    public bool HasAffiliation => Affiliation.Length > 0;

    public string RoleLabel => string.IsNullOrWhiteSpace(Role)
        ? (IsPrimaryCare
            ? "Primary care"
            : ProviderType == global::Sati.ProviderType.Waiver
                ? "Service / role not recorded"
                : "Role not recorded")
        : Role;

    public string DateRangeLabel => StartDate is DateTime started
        ? EndDate is DateTime ended
            ? $"{started:d MMM yyyy} – {ended:d MMM yyyy}"
            : $"Started {started:d MMM yyyy}"
        : EndDate is DateTime endedWithoutStart
            ? $"Start date not recorded · ended {endedWithoutStart:d MMM yyyy}"
            : "Start date not recorded";

    /// <summary>
    /// The status a screen reader announces and a non-colour cue for sighted users, so
    /// "ended" never depends on noticing a shade of grey.
    /// </summary>
    public string StatusLabel => IsCurrent
        ? (IsPrimaryCare ? "Current · primary care" : "Current")
        : EndDate is { } ended ? $"Ended {ended:d MMM yyyy}" : "Ended";

    public string AutomationName =>
        $"{ProviderName}, {ProviderKindLabel}, {RoleLabel}, {DateRangeLabel}, {StatusLabel}" +
        (HasAffiliation ? $", {Affiliation}" : string.Empty);
    public string EndAutomationName => $"End the provider assignment for {ProviderName}";
    public string MoveUpAutomationName => $"Move {ProviderName} up in the current provider order";
    public string MoveDownAutomationName => $"Move {ProviderName} down in the current provider order";
    public string RemoveAutomationName =>
        $"Remove the incorrectly entered provider assignment for {ProviderName}";
}
