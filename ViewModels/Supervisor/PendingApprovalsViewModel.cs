using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using Sati.Contracts.V1;
using Sati.Services;
using Sati.Data.Cloud;
using Sati.ViewModels;
using System.Net;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Diagnostics;

namespace Sati.ViewModels.Supervisor
{
    public partial class PendingApprovalsViewModel : ObservableObject
    {
        private readonly ISupervisorService _supervisorService;
        private readonly ISessionService _sessionService;
        private readonly IFormAttestationChangeReviewService? _formChangeReviews;
        private readonly IAdminFormNoteCorrectionService? _adminFormCorrections;

        public PendingApprovalsViewModel(
            ISupervisorService supervisorService,
            ISessionService sessionService,
            IFormAttestationChangeReviewService? formChangeReviews = null,
            IAdminFormNoteCorrectionService? adminFormCorrections = null)
        {
            _supervisorService = supervisorService;
            _sessionService = sessionService;
            _formChangeReviews = formChangeReviews;
            _adminFormCorrections = adminFormCorrections;
        }

        // -------------------------------------------------------------------------
        // Collections
        // -------------------------------------------------------------------------

        // Notes whose consumers pass the compliance gate — ready for content review.
        public ObservableCollection<PendingNoteViewModel> PendingNotes { get; } = [];

        // Notes whose consumers fail the compliance gate — waiting for compliance
        // to be met, or for a supervisor override with written justification.
        public ObservableCollection<PendingNoteViewModel> NonCompliantNotes { get; } = [];
        public ObservableCollection<FormAttestationChangeReviewRow> FormChangeReviewFlags { get; } = [];
        public ObservableCollection<NoteReviewCaseManagerOption> CaseManagerOptions { get; } = [];
        public ObservableCollection<NoteReviewClientOption> ClientOptions { get; } = [];
        private IReadOnlyList<NoteReviewClientOption> _allClientOptions = [];

        // -------------------------------------------------------------------------
        // Observable properties
        // -------------------------------------------------------------------------

        [ObservableProperty] private PendingNoteViewModel? selectedNote;
        [ObservableProperty] private string? returnReason;
        [ObservableProperty] private bool isReturnDialogVisible;
        [ObservableProperty] private NoteReviewCaseManagerOption? selectedCaseManager;
        [ObservableProperty] private NoteReviewClientOption? selectedClient;
        [ObservableProperty] private DateTime? fromDate;
        [ObservableProperty] private DateTime? toDate;
        [ObservableProperty] private string searchTerm = string.Empty;
        [ObservableProperty] private bool areFiltersAvailable = true;
        [ObservableProperty] private string filterStatusMessage = string.Empty;
        [ObservableProperty] private string formChangeReviewSummary =
            "No form completion date changes need supervisor review.";

        // Admin correction updates the note date and linked form attestation together.
        [ObservableProperty] private PendingNoteViewModel? correctionNote;
        [ObservableProperty] private AdminFormNoteCorrectionTargetDto? correctionTarget;
        [ObservableProperty] private string correctionTargetSummary = string.Empty;
        [ObservableProperty] private DateTime? correctedActivityDate;
        [ObservableProperty] private string correctionReason = string.Empty;
        [ObservableProperty] private bool correctionEvidenceConfirmed;
        [ObservableProperty] private bool isCorrectionDialogVisible;
        [ObservableProperty] private bool isCorrectionBusy;
        [ObservableProperty] private string correctionStatusMessage = string.Empty;

        // Override dialog state
        [ObservableProperty] private PendingNoteViewModel? overrideNote;
        [ObservableProperty] private string? overrideReason;
        [ObservableProperty] private bool isOverrideDialogVisible;
        [ObservableProperty] private bool overrideAttestationConfirmed;
        public ObservableCollection<ComplianceBlockerSelectionViewModel> OverrideBlockers { get; } = [];

        // -------------------------------------------------------------------------
        // Computed properties
        // -------------------------------------------------------------------------

        public bool HasPending => PendingNotes.Count > 0;
        public bool HasNonCompliant => NonCompliantNotes.Count > 0;
        public string EmptyStateMessage => IsLoading ? "Loading notes..." :
            HasMore ? "No compliant notes among the notes loaded so far." :
            HasNonCompliant ? "No notes are ready for ordinary approval. Review Held for Compliance below." :
            "No notes pending approval.";
        public string NonCompliantEmptyMessage => IsLoading ? "Loading notes..." :
            HasMore ? "No compliance holds among the notes loaded so far." : "No notes held for compliance.";

        // -------------------------------------------------------------------------
        // Load
        // -------------------------------------------------------------------------

        private readonly LatestRequestTracker _loads = new();
        private int _generation;
        private NoteReviewQuery _activeFilter = new();
        private int? _nextAfterId;
        private int? _throughId;
        [ObservableProperty] private bool isLoading;
        [ObservableProperty] private bool isBatchApproving;
        [ObservableProperty] private bool hasMore;
        [ObservableProperty] private string statusMessage = string.Empty;
        [ObservableProperty] private string maximumUnitsText = NoteReviewRules.DefaultMaximumUnits.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public bool CanLoadMore => HasMore && !IsLoading && !IsBatchApproving;
        public bool CanApplyFilters => AreFiltersAvailable && !IsLoading && !IsBatchApproving;
        public bool CanBatchApprove => !IsLoading && !IsBatchApproving &&
            int.TryParse(MaximumUnitsText, out var limit) && NoteReviewRules.ValidThreshold(limit);
        partial void OnIsLoadingChanged(bool value) => NotifyActions();
        partial void OnIsBatchApprovingChanged(bool value) => NotifyActions();
        partial void OnHasMoreChanged(bool value) => NotifyActions();
        partial void OnMaximumUnitsTextChanged(string value) => NotifyActions();
        partial void OnAreFiltersAvailableChanged(bool value) => NotifyActions();
        private void NotifyActions()
        {
            OnPropertyChanged(nameof(EmptyStateMessage));
            OnPropertyChanged(nameof(NonCompliantEmptyMessage));
            LoadMoreCommand.NotifyCanExecuteChanged();
            BatchApproveCommand.NotifyCanExecuteChanged();
            ApplyFiltersCommand.NotifyCanExecuteChanged();
            ClearFiltersCommand.NotifyCanExecuteChanged();
        }

        public void Deactivate()
        {
            _generation = _loads.Begin();
            IsLoading = false;
        }

        public void ClearForAccountSwitch()
        {
            Deactivate();
            SelectedNote = null;
            OverrideNote = null;
            IsReturnDialogVisible = false;
            IsOverrideDialogVisible = false;
            ReturnReason = null;
            OverrideReason = null;
            OverrideAttestationConfirmed = false;
            OverrideBlockers.Clear();
            ResetCorrectionDialog();
            SelectedCaseManager = null;
            SelectedClient = null;
            FromDate = null;
            ToDate = null;
            SearchTerm = string.Empty;
            PendingNotes.Clear();
            NonCompliantNotes.Clear();
            FormChangeReviewFlags.Clear();
            FormChangeReviewSummary = "No form completion date changes need supervisor review.";
            CaseManagerOptions.Clear();
            ClientOptions.Clear();
            _allClientOptions = [];
            _activeFilter = new();
            _nextAfterId = null;
            _throughId = null;
            HasMore = false;
            StatusMessage = string.Empty;
            FilterStatusMessage = string.Empty;
            NotifyActions();
        }

        public async Task LoadAsync(int? filterByUserId = null)
        {
            var actor = _sessionService.CurrentUser;
            if (actor is null) return;
            _generation = _loads.Begin();
            var optionsGeneration = _generation;
            try
            {
                var options = await _supervisorService.GetReviewFilterOptionsAsync(actor.Id);
                if (!_loads.IsCurrent(optionsGeneration) || _sessionService.CurrentUser != actor) return;
                CaseManagerOptions.Clear();
                foreach (var option in options.CaseManagers) CaseManagerOptions.Add(option);
                _allClientOptions = options.Clients;
                SelectedCaseManager = CaseManagerOptions.FirstOrDefault(option => option.UserId == filterByUserId);
                SelectedClient = null;
                FromDate = ToDate = null;
                SearchTerm = string.Empty;
                RefreshClientOptions();
                AreFiltersAvailable = true;
                FilterStatusMessage = string.Empty;
            }
            catch (Exception)
            {
                if (!_loads.IsCurrent(optionsGeneration) || _sessionService.CurrentUser != actor) return;
                AreFiltersAvailable = false;
                FilterStatusMessage = "Filters are unavailable because their choices could not be loaded. " +
                    "If you are using Demo, this version of Sati may not match the Demo service. " +
                    "Install the matching update or contact support. The approval queue remains available.";
            }
            _activeFilter = BuildFilter();
            await ReloadAsync();
        }

        [RelayCommand(CanExecute = nameof(CanApplyFilters))]
        private async Task ApplyFilters()
        {
            if (FromDate?.Date > ToDate?.Date)
            {
                StatusMessage = "The start date must be on or before the end date.";
                return;
            }
            _activeFilter = BuildFilter();
            await ReloadAsync();
        }

        [RelayCommand(CanExecute = nameof(CanApplyFilters))]
        private async Task ClearFilters()
        {
            SelectedCaseManager = null;
            SelectedClient = null;
            FromDate = ToDate = null;
            SearchTerm = string.Empty;
            RefreshClientOptions();
            _activeFilter = new();
            await ReloadAsync();
        }

        partial void OnSelectedCaseManagerChanged(NoteReviewCaseManagerOption? value)
        {
            if (SelectedClient is not null && value is not null && SelectedClient.UserId != value.UserId)
                SelectedClient = null;
            RefreshClientOptions();
        }

        private void RefreshClientOptions()
        {
            ClientOptions.Clear();
            foreach (var option in _allClientOptions.Where(option =>
                         SelectedCaseManager is null || option.UserId == SelectedCaseManager.UserId))
                ClientOptions.Add(option);
        }

        private NoteReviewQuery BuildFilter() => new(
            SelectedCaseManager?.UserId,
            SelectedClient?.PersonId,
            FromDate?.Date,
            ToDate?.Date,
            SearchTerm);

        private async Task ReloadAsync()
        {
            _generation = _loads.Begin();
            _nextAfterId = 0;
            _throughId = null;
            PendingNotes.Clear();
            NonCompliantNotes.Clear();
            IsReturnDialogVisible = IsOverrideDialogVisible = false;
            SelectedNote = OverrideNote = null;
            ResetCorrectionDialog();
            HasMore = true;
            StatusMessage = string.Empty;
            NotifyCounts();
            await FetchPageAsync(_generation);
            await LoadFormChangeReviewsAsync(_generation);
        }

        private async Task LoadFormChangeReviewsAsync(int generation)
        {
            if (_formChangeReviews is null) return;
            var actor = _sessionService.CurrentUser;
            if (actor is null) return;
            try
            {
                var flags = await _formChangeReviews.GetForSupervisorAsync();
                if (!_loads.IsCurrent(generation) || _sessionService.CurrentUser != actor) return;
                var current = FormAttestationChangeReviewRow.Latest(flags);
                FormChangeReviewFlags.Clear();
                foreach (var flag in current) FormChangeReviewFlags.Add(flag);
                FormChangeReviewSummary = current.Count == 0
                    ? "No form completion date changes need supervisor review."
                    : $"{current.Count} form-work note{(current.Count == 1 ? "" : "s")} changed after submission; " +
                      $"{current.Count(flag => flag.MustHoldBilling)} currently held for billing.";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Supervisor form change reviews failed: {ex.Message}");
                if (_loads.IsCurrent(generation) && _sessionService.CurrentUser == actor)
                    FormChangeReviewSummary = "Form completion date change notices are temporarily unavailable.";
            }
        }

        [RelayCommand(CanExecute = nameof(CanLoadMore))]
        private Task LoadMore() => FetchPageAsync(_generation);

        private async Task FetchPageAsync(int generation)
        {
            var actor = _sessionService.CurrentUser;
            if (actor is null || _nextAfterId is not int after) return;
            IsLoading = true;
            try
            {
                var page = await _supervisorService.GetReviewPageAsync(actor.Id, after, _throughId, _activeFilter);
                if (!_loads.IsCurrent(generation) || _sessionService.CurrentUser != actor) return;
                foreach (var note in page.Notes)
                {
                    var target = note.ComplianceFailureReasons.Count == 0 ? PendingNotes : NonCompliantNotes;
                    if (!target.Any(existing => existing.NoteId == note.Id))
                        target.Add(new PendingNoteViewModel(note,
                            CaseManagerOptions.FirstOrDefault(option => option.UserId == note.Person.UserId)?.DisplayName,
                            actor.HasAdminPermissions && _adminFormCorrections is not null));
                }
                _throughId = page.ThroughId;
                _nextAfterId = page.NextAfterId;
                HasMore = page.NextAfterId.HasValue;
                StatusMessage = HasMore ? "Scroll down or choose Load more for the next 10 notes." : "All notes in this queue have been loaded.";
                if (AreFiltersAvailable)
                {
                    var loaded = PendingNotes.Count + NonCompliantNotes.Count;
                    FilterStatusMessage = HasActiveFilter(_activeFilter)
                        ? $"Filters applied. {loaded} matching note{(loaded == 1 ? string.Empty : "s")} loaded" +
                          (HasMore ? " so far." : ".")
                        : "No filters applied.";
                }
                NotifyCounts();
            }
            catch (Exception)
            {
                if (_loads.IsCurrent(generation) && _sessionService.CurrentUser == actor)
                    StatusMessage = "The next notes could not be loaded. Choose Load more to retry.";
            }
            finally
            {
                if (_loads.IsCurrent(generation)) IsLoading = false;
            }
        }

        private void NotifyCounts()
        {
            OnPropertyChanged(nameof(HasPending));
            OnPropertyChanged(nameof(HasNonCompliant));
            OnPropertyChanged(nameof(EmptyStateMessage));
            OnPropertyChanged(nameof(NonCompliantEmptyMessage));
        }

        private static bool HasActiveFilter(NoteReviewQuery filter) =>
            filter.UserId.HasValue || filter.PersonId.HasValue || filter.FromDate.HasValue ||
            filter.ToDate.HasValue || !string.IsNullOrWhiteSpace(filter.SearchTerm);

        [RelayCommand(CanExecute = nameof(CanBatchApprove))]
        private async Task BatchApprove()
        {
            if (!int.TryParse(MaximumUnitsText, out var limit) || !NoteReviewRules.ValidThreshold(limit)) return;
            var actor = _sessionService.CurrentUser;
            if (actor is null) return;
            var generation = _generation;
            var filter = _activeFilter;
            IsBatchApproving = true;
            var approved = 0;
            var skipped = 0;
            var stopped = false;
            try
            {
                int? cursor = 0;
                int? ceiling = null;
                while (cursor is int after && _loads.IsCurrent(generation) && _sessionService.CurrentUser == actor)
                {
                    var page = await _supervisorService.GetReviewPageAsync(actor.Id, after, ceiling, filter);
                    ceiling = page.ThroughId;
                    cursor = page.NextAfterId;
                    foreach (var note in page.Notes)
                    {
                        if (!_loads.IsCurrent(generation) || _sessionService.CurrentUser != actor) return;
                        if (note.Status != NoteStatus.Logged ||
                            note.ComplianceFailureReasons.Count != 0)
                        {
                            skipped++;
                            continue;
                        }
                        try
                        {
                            // The service rechecks threshold, validity, current compliance,
                            // revision, and reviewer scope before each individual commit.
                            await _supervisorService.ApproveNoteAsync(note.Id, actor.Id, note.Revision, limit);
                            approved++;
                        }
                        catch (NoteConcurrencyException) { skipped++; }
                        catch (InvalidOperationException) { skipped++; }
                        catch (CloudApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict || ex.StatusCode == HttpStatusCode.NotFound)
                        { skipped++; }
                        if (!_loads.IsCurrent(generation) || _sessionService.CurrentUser != actor) return;
                        StatusMessage = $"Approved {approved}; skipped {skipped}. Working...";
                    }
                }
            }
            catch (Exception)
            {
                stopped = true;
            }
            finally
            {
                IsBatchApproving = false;
            }
            if (!_loads.IsCurrent(generation) || _sessionService.CurrentUser != actor) return;
            await ReloadAsync();
            var reloadGeneration = _generation;
            if (_loads.IsCurrent(reloadGeneration) && _sessionService.CurrentUser == actor)
                StatusMessage = $"Approved {approved}; skipped {skipped}. " +
                    (stopped ? "Stopped after an error. Reload before retrying; the last save may be unconfirmed."
                        : "Batch complete. Skipped notes remain for individual review.");
        }

        // -------------------------------------------------------------------------
        // Approval commands
        // -------------------------------------------------------------------------

        [RelayCommand]
        private async Task Approve(PendingNoteViewModel note)
        {
            if (!note.CanApprove) return;
            try
            {
                var supervisor = _sessionService.CurrentUser!;
                await _supervisorService.ApproveNoteAsync(note.NoteId, supervisor.Id, note.Revision);
                PendingNotes.Remove(note);
                NonCompliantNotes.Remove(note);
                OnPropertyChanged(nameof(HasPending));
                OnPropertyChanged(nameof(HasNonCompliant));
            }
            catch (NoteConcurrencyException)
            {
                await HandleNoteConflictAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Approve failed: {ex.Message}");
            }
        }

        // Opens the override dialog for a non-compliant note.
        // Supervisor must provide written justification before the override
        // is submitted — the dialog enforces this via ConfirmOverride.
        [RelayCommand]
        private void OpenOverrideDialog(PendingNoteViewModel note)
        {
            if (!note.CanOverrideOrdinaryBlockers) return;
            OverrideNote = note;
            OverrideReason = string.Empty;
            OverrideAttestationConfirmed = false;
            OverrideBlockers.Clear();
            foreach (var blocker in note.ComplianceBlockers)
                OverrideBlockers.Add(new ComplianceBlockerSelectionViewModel(blocker));
            IsOverrideDialogVisible = true;
        }

        [RelayCommand]
        private async Task ConfirmOverride()
        {
            var selectedBlockers = OverrideBlockers
                .Where(option => option.IsSelected)
                .Select(option => option.Blocker.ObligationId)
                .ToArray();
            if (OverrideNote is null || string.IsNullOrWhiteSpace(OverrideReason) ||
                !OverrideAttestationConfirmed || selectedBlockers.Length == 0)
                return;

            try
            {
                var supervisor = _sessionService.CurrentUser!;
                await _supervisorService.ApproveWithOverrideAsync(
                    OverrideNote.NoteId,
                    supervisor.Id,
                    OverrideReason,
                    OverrideNote.Revision,
                    selectedBlockers,
                    OverrideAttestationConfirmed);

                NonCompliantNotes.Remove(OverrideNote);
                IsOverrideDialogVisible = false;
                OverrideNote = null;
                OverrideReason = string.Empty;
                OverrideAttestationConfirmed = false;
                OverrideBlockers.Clear();
                OnPropertyChanged(nameof(HasNonCompliant));
            }
            catch (NoteConcurrencyException)
            {
                await HandleNoteConflictAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Override failed: {ex.Message}");
            }
        }

        [RelayCommand]
        private void CancelOverride()
        {
            IsOverrideDialogVisible = false;
            OverrideNote = null;
            OverrideReason = string.Empty;
            OverrideAttestationConfirmed = false;
            OverrideBlockers.Clear();
        }

        // -------------------------------------------------------------------------
        // Admin source-date correction
        // -------------------------------------------------------------------------

        [RelayCommand]
        private async Task OpenCorrectionDialog(PendingNoteViewModel note)
        {
            if (IsCorrectionBusy) return;
            var account = _sessionService.CurrentUser;
            if (account?.HasAdminPermissions != true ||
                _adminFormCorrections is null || !note.CanAdminCorrectSourceDate)
                return;
            IsCorrectionBusy = true;
            try
            {
                var target = await _adminFormCorrections.GetTargetAsync(note.NoteId);
                if (_sessionService.CurrentUser != account) return;
                if (target is null)
                {
                    StatusMessage = "This linked form note is no longer eligible for source-date correction.";
                    return;
                }
                CorrectionNote = note;
                CorrectionTarget = target;
                CorrectionTargetSummary =
                    $"Note #{target.NoteId}, form #{target.FormId}, {target.Status}; " +
                    $"activity {target.ActivityDate:MM/dd/yyyy}, completion " +
                    $"{target.CurrentCompletedOn?.ToString("MM/dd/yyyy") ?? "revoked / no current completion"}, " +
                    $"due {target.DueDate:MM/dd/yyyy}.";
                CorrectedActivityDate = target.ActivityDate;
                CorrectionReason = string.Empty;
                CorrectionEvidenceConfirmed = false;
                CorrectionStatusMessage = target.HasClaimRecord
                    ? "This note already has a claim record. Review that claim before changing the source date."
                    : string.Empty;
                IsCorrectionDialogVisible = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Admin form-note target lookup failed: {ex.Message}");
                StatusMessage = ex.Message;
            }
            finally
            {
                IsCorrectionBusy = false;
            }
        }

        [RelayCommand]
        private async Task ConfirmCorrection()
        {
            if (IsCorrectionBusy) return;
            if (_sessionService.CurrentUser?.HasAdminPermissions != true ||
                _adminFormCorrections is null || CorrectionTarget is not { } target)
                return;
            if (target.HasClaimRecord)
            {
                CorrectionStatusMessage = "Review the existing claim before changing the source date.";
                return;
            }
            if (CorrectedActivityDate is not DateTime corrected ||
                corrected.Date == target.ActivityDate.Date ||
                string.IsNullOrWhiteSpace(CorrectionReason) || !CorrectionEvidenceConfirmed)
            {
                CorrectionStatusMessage =
                    "Choose the actual work date, explain the correction, and confirm the source evidence. " +
                    "The corrected date must differ from the current date.";
                return;
            }

            IsCorrectionBusy = true;
            CorrectionStatusMessage = string.Empty;
            try
            {
                await _adminFormCorrections.CorrectAsync(
                    target.NoteId, target.Revision, corrected.Date,
                    CorrectionReason.Trim(), CorrectionEvidenceConfirmed);
                ResetCorrectionDialog();
                await ReloadAsync();
            }
            catch (NoteConcurrencyException)
            {
                ResetCorrectionDialog();
                await HandleNoteConflictAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Admin form-note date correction failed: {ex.Message}");
                CorrectionStatusMessage = ex.Message;
            }
            finally
            {
                IsCorrectionBusy = false;
            }
        }

        [RelayCommand]
        private void CancelCorrection()
        {
            if (!IsCorrectionBusy) ResetCorrectionDialog();
        }

        private void ResetCorrectionDialog()
        {
            IsCorrectionDialogVisible = false;
            CorrectionNote = null;
            CorrectionTarget = null;
            CorrectionTargetSummary = string.Empty;
            CorrectedActivityDate = null;
            CorrectionReason = string.Empty;
            CorrectionEvidenceConfirmed = false;
            CorrectionStatusMessage = string.Empty;
        }

        // -------------------------------------------------------------------------
        // Return commands
        // -------------------------------------------------------------------------

        [RelayCommand]
        private void OpenReturnDialog(PendingNoteViewModel note)
        {
            SelectedNote = note;
            ReturnReason = string.Empty;
            IsReturnDialogVisible = true;
        }

        [RelayCommand]
        private async Task ConfirmReturn()
        {
            if (SelectedNote is null || string.IsNullOrWhiteSpace(ReturnReason))
                return;

            try
            {
                var supervisor = _sessionService.CurrentUser!;
                await _supervisorService.ReturnNoteAsync(
                    SelectedNote.NoteId,
                    supervisor.Id,
                    ReturnReason,
                    SelectedNote.Revision);

                PendingNotes.Remove(SelectedNote);
                NonCompliantNotes.Remove(SelectedNote);
                OnPropertyChanged(nameof(HasNonCompliant));
                IsReturnDialogVisible = false;
                SelectedNote = null;
                ReturnReason = string.Empty;
                OnPropertyChanged(nameof(HasPending));
            }
            catch (NoteConcurrencyException)
            {
                await HandleNoteConflictAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Return failed: {ex.Message}");
            }
        }

        [RelayCommand]
        private void CancelReturn()
        {
            IsReturnDialogVisible = false;
            SelectedNote = null;
            ReturnReason = string.Empty;
        }

        private async Task HandleNoteConflictAsync()
        {
            IsOverrideDialogVisible = false;
            IsReturnDialogVisible = false;
            ResetCorrectionDialog();
            OverrideNote = null;
            SelectedNote = null;
            await ReloadAsync();
            System.Windows.MessageBox.Show(
                "This note changed after you opened the approval queue. The queue has been refreshed; review the latest copy before acting.",
                "Note Updated",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
    }

    // -------------------------------------------------------------------------
    // Row view-model
    // -------------------------------------------------------------------------

    public class PendingNoteViewModel
    {
        public int NoteId { get; }
        public int Revision { get; }
        public int? FormId { get; }
        public bool CanAdminCorrectSourceDate { get; }
        public bool CanApprove { get; }
        public bool IsCorrectionOnly => !CanApprove;
        public string ClientName { get; }
        public int PersonId { get; }
        public int CaseManagerUserId { get; }
        public string CaseManagerName { get; }
        public DateTime? EventDate { get; }
        public NoteType? NoteType { get; }
        public decimal? Units { get; }
        public string Narrative { get; }
        public IReadOnlyList<string> ComplianceFailureReasons { get; }
        public IReadOnlyList<BillingComplianceBlocker> ComplianceBlockers { get; }
        public bool HasComplianceFailures => ComplianceFailureReasons.Count > 0;
        public bool HasHardFormWorkHold =>
            ComplianceFailureReasons.Any(FormWorkBillingRules.IsFormWorkReason);
        public bool CanOverrideOrdinaryBlockers => CanApprove && ComplianceBlockers.Count > 0;
        public bool IsComplianceException => false; // set by non-compliant queue context

        public PendingNoteViewModel(Note note, string? caseManagerName = null, bool isAdmin = false)
        {
            NoteId = note.Id;
            Revision = note.Revision;
            CanApprove = note.Status == NoteStatus.Logged;
            FormId = note.FormId;
            CanAdminCorrectSourceDate = isAdmin && NoteActivityRules.Has(note.Activities, note.NoteType?.ToString(), NoteActivity.Form) &&
                note.FormId is > 0 && note.FormType is FormType formType &&
                !FormWorkBillingRules.IsRelease(formType.ToString());
            ClientName = note.Person.FullName;
            PersonId = note.PersonId;
            CaseManagerUserId = note.Person.UserId;
            CaseManagerName = string.IsNullOrWhiteSpace(caseManagerName) ? "Case manager" : caseManagerName;
            EventDate = note.EventDate;
            NoteType = note.NoteType;
            Units = note.Units;
            Narrative = note.Narrative;
            ComplianceFailureReasons = note.ComplianceFailureReasons;
            ComplianceBlockers = note.ComplianceBlockers;
        }
    }

    public partial class ComplianceBlockerSelectionViewModel(
        BillingComplianceBlocker blocker) : ObservableObject
    {
        public BillingComplianceBlocker Blocker { get; } = blocker;
        public string DisplayText => $"{Blocker.Name} — due {Blocker.DueDate:MMM d, yyyy}";
        [ObservableProperty] private bool isSelected;
    }
}
