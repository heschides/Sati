using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Services;

namespace Sati.ViewModels.Billing;

/// <summary>
/// Owns the administrator-only, post-compliance billing recovery workflow. This is a
/// separate child because Administration and Billing are independent permissions: the
/// same safe workflow can appear in Admin without exposing the rest of Billing.
/// </summary>
public partial class BillingComplianceRecoveryViewModel(
    IBillingService billingService,
    ISessionService sessionService,
    IAdminService adminService) : ObservableObject
{
    private readonly LatestRequestTracker _requests = new();

    [ObservableProperty] private bool canManageComplianceRecovery;
    [ObservableProperty] private AdminPersonListItemDto? selectedRecoveryPerson;
    [ObservableProperty] private string recoveryExplanation = string.Empty;
    [ObservableProperty] private bool recoveryAttestationConfirmed;
    [ObservableProperty] private string recoveryStatus =
        "Choose a consumer to find service notes from a resolved compliance gap.";
    [ObservableProperty] private bool isRecoveryBusy;

    public ObservableCollection<AdminPersonListItemDto> RecoveryPeople { get; } = [];
    public ObservableCollection<BillingRecoveryNoteRow> RecoveryNotes { get; } = [];
    public bool HasRecoveryNotes => RecoveryNotes.Count != 0;

    private bool CanLoadRecoveryPlan =>
        CanManageComplianceRecovery && SelectedRecoveryPerson is not null && !IsRecoveryBusy;
    private bool CanRecordComplianceRecovery =>
        CanManageComplianceRecovery &&
        SelectedRecoveryPerson is not null &&
        HasRecoveryNotes &&
        !IsRecoveryBusy;

    /// <summary>
    /// Loads the agency's active consumers. AdminDashboard can supply its already-authorized
    /// list to avoid a duplicate request; BillingOverview uses the authoritative Admin service.
    /// </summary>
    public async Task LoadPeopleAsync(
        IReadOnlyCollection<AdminPersonListItemDto>? knownPeople = null,
        CancellationToken cancellationToken = default)
    {
        var account = sessionService.CurrentUser;
        CanManageComplianceRecovery = account?.HasAdminPermissions == true;
        _requests.Invalidate();
        RecoveryPeople.Clear();
        SelectedRecoveryPerson = null;
        ClearRecoveryPlan();

        if (!CanManageComplianceRecovery || account is null)
        {
            RecoveryStatus = "Administrator permission is required for compliance recovery.";
            return;
        }

        var request = _requests.Begin();
        try
        {
            var people = knownPeople ?? await adminService.GetPeopleAsync(cancellationToken);
            if (!IsCurrentRequest(request, account))
                return;

            foreach (var person in people
                         .Where(person => string.Equals(person.Status, "Active", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(person => person.DisplayName, StringComparer.CurrentCultureIgnoreCase))
                RecoveryPeople.Add(person);
            RecoveryStatus = RecoveryPeople.Count == 0
                ? "There are no active consumers available for recovery review."
                : "Choose a consumer to find service notes from a resolved compliance gap.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Navigation or an account change superseded this load.
        }
        catch (Exception ex)
        {
            if (IsCurrentRequest(request, account))
                RecoveryStatus = $"The recovery consumer list could not be loaded: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoadRecoveryPlan))]
    private async Task LoadRecoveryPlan()
    {
        var account = sessionService.CurrentUser;
        var person = SelectedRecoveryPerson;
        if (!CanManageComplianceRecovery || account?.HasAdminPermissions != true || person is null)
        {
            RecoveryStatus = "Choose a consumer first.";
            return;
        }

        ClearRecoveryPlan();
        var request = _requests.Begin();
        try
        {
            IsRecoveryBusy = true;
            var plan = await billingService.PrepareComplianceRecoveryAsync(
                account.ToAgencyActor(), person.PersonId);
            if (!IsCurrentRequest(request, account, person.PersonId))
                return;
            if (plan.AgencyId != account.AgencyId || plan.PersonId != person.PersonId)
                throw new InvalidOperationException(
                    "The recovery response did not match the selected consumer.");

            var obligationIndex = plan.Obligations.ToDictionary(
                obligation => obligation.ObligationId,
                obligation => obligation,
                StringComparer.Ordinal);
            foreach (var option in plan.NoteOptions
                         .OrderBy(option => option.ServiceDate)
                         .ThenBy(option => option.NoteId))
            {
                var blockers = option.BlockingObligationIds
                    .Select(id => obligationIndex.TryGetValue(id, out var obligation)
                        ? FormatRecoveryObligation(obligation)
                        : $"Unknown obligation ({id})")
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.CurrentCultureIgnoreCase);
                AddRecoveryNote(new BillingRecoveryNoteRow(
                    option.NoteId,
                    option.ServiceDate,
                    string.Join(", ", blockers),
                    option.IsSelectedByDefault));
            }

            OnPropertyChanged(nameof(HasRecoveryNotes));
            RecordComplianceRecoveryCommand.NotifyCanExecuteChanged();
            RecoveryStatus = plan.NoteOptions.Count == 0
                ? plan.UnresolvedNoteIds.Count == 0
                    ? "No eligible unbilled notes were found for this consumer."
                    : $"No notes can be released yet; {plan.UnresolvedNoteIds.Count} still have unresolved compliance blockers."
                : plan.NoteOptions.Count == 1
                    ? "1 eligible note found. It is selected by default; uncheck it if it should remain blocked."
                    : $"{plan.NoteOptions.Count} eligible notes found. All are selected by default; uncheck any that should remain blocked.";
        }
        catch (Exception ex)
        {
            if (IsCurrentRequest(request, account, person.PersonId))
                RecoveryStatus = $"The recovery checklist could not be loaded: {ex.Message}";
        }
        finally
        {
            if (IsCurrentRequest(request, account, person.PersonId))
                IsRecoveryBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRecordComplianceRecovery))]
    private async Task RecordComplianceRecovery()
    {
        var account = sessionService.CurrentUser;
        var person = SelectedRecoveryPerson;
        if (!CanManageComplianceRecovery || account?.HasAdminPermissions != true || person is null)
        {
            RecoveryStatus = "Choose a consumer first.";
            return;
        }

        var selectedIds = RecoveryNotes
            .Where(note => note.IsSelected)
            .Select(note => note.NoteId)
            .ToArray();
        if (selectedIds.Length == 0)
        {
            RecoveryStatus = "Leave at least one eligible note selected.";
            return;
        }
        if (string.IsNullOrWhiteSpace(RecoveryExplanation))
        {
            RecoveryStatus = "Enter an explanation for this recovery decision.";
            return;
        }
        if (!RecoveryAttestationConfirmed)
        {
            RecoveryStatus = "Confirm the administrator attestation before recording the decision.";
            return;
        }

        var request = _requests.Begin();
        try
        {
            IsRecoveryBusy = true;
            var decision = await billingService.RecordComplianceRecoveryAsync(
                account.ToAgencyActor(),
                person.PersonId,
                new CreateBillingComplianceRecoveryRequest(
                    selectedIds,
                    RecoveryExplanation,
                    RecoveryAttestationConfirmed));
            if (!IsCurrentRequest(request, account, person.PersonId))
                return;
            if (decision.AgencyId != account.AgencyId ||
                decision.PersonId != person.PersonId ||
                decision.AdminUserId != account.Id ||
                !decision.NoteIds.Order().SequenceEqual(selectedIds.Order()))
                throw new InvalidOperationException(
                    "The recorded recovery response did not match this decision.");

            var noteLabel = decision.NoteIds.Count == 1 ? "note" : "notes";
            RecoveryStatus =
                $"Recovery decision recorded for {decision.NoteIds.Count} {noteLabel} at {decision.RecordedAtUtc.ToLocalTime():g}.";
            ClearRecoveryPlan(preserveStatus: true);
        }
        catch (Exception ex)
        {
            if (IsCurrentRequest(request, account, person.PersonId))
                RecoveryStatus = $"The recovery decision was not recorded: {ex.Message}";
        }
        finally
        {
            if (IsCurrentRequest(request, account, person.PersonId))
                IsRecoveryBusy = false;
        }
    }

    public void ClearForAccountSwitch()
    {
        _requests.Invalidate();
        CanManageComplianceRecovery = false;
        SelectedRecoveryPerson = null;
        RecoveryPeople.Clear();
        ClearRecoveryPlan();
        IsRecoveryBusy = false;
    }

    private bool IsCurrentRequest(int request, Sati.Models.User account, int? personId = null) =>
        _requests.IsCurrent(request) &&
        ReferenceEquals(sessionService.CurrentUser, account) &&
        (personId is null || SelectedRecoveryPerson?.PersonId == personId);

    private static string FormatRecoveryObligation(BillingRecoveryObligationOption obligation) =>
        $"{obligation.Name} (due {obligation.DueDate:MMM d, yyyy}; " +
        $"completed {obligation.CompletedDate:MMM d, yyyy}; evidence {obligation.EvidenceId})";

    private void AddRecoveryNote(BillingRecoveryNoteRow row)
    {
        RecoveryNotes.Add(row);
    }

    private void ClearRecoveryPlan(bool preserveStatus = false)
    {
        RecoveryNotes.Clear();
        RecoveryExplanation = string.Empty;
        RecoveryAttestationConfirmed = false;
        OnPropertyChanged(nameof(HasRecoveryNotes));
        RecordComplianceRecoveryCommand.NotifyCanExecuteChanged();
        if (!preserveStatus)
            RecoveryStatus = SelectedRecoveryPerson is null
                ? "Choose a consumer to find service notes from a resolved compliance gap."
                : "Select Find eligible notes to review this consumer's resolved compliance gap.";
    }

    partial void OnSelectedRecoveryPersonChanged(AdminPersonListItemDto? value)
    {
        _requests.Invalidate();
        IsRecoveryBusy = false;
        ClearRecoveryPlan();
        LoadRecoveryPlanCommand.NotifyCanExecuteChanged();
    }

    partial void OnCanManageComplianceRecoveryChanged(bool value)
    {
        LoadRecoveryPlanCommand.NotifyCanExecuteChanged();
        RecordComplianceRecoveryCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRecoveryBusyChanged(bool value)
    {
        LoadRecoveryPlanCommand.NotifyCanExecuteChanged();
        RecordComplianceRecoveryCommand.NotifyCanExecuteChanged();
    }

    public partial class BillingRecoveryNoteRow : ObservableObject
    {
        public BillingRecoveryNoteRow(
            int noteId,
            DateTime serviceDate,
            string blockerLabel,
            bool isSelected)
        {
            NoteId = noteId;
            ServiceDate = serviceDate.Date;
            BlockerLabel = blockerLabel;
            this.isSelected = isSelected;
        }

        public int NoteId { get; }
        public DateTime ServiceDate { get; }
        public string BlockerLabel { get; }
        public string NoteLabel => $"Note #{NoteId} · service date {ServiceDate:MMM d, yyyy}";
        [ObservableProperty] private bool isSelected;
    }
}
