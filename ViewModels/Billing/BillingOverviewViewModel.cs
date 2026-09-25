using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Helpers;
using Sati.Models.Billing;
using Sati.Services;
using Sati.ViewModels;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.Billing;

/// <summary>
/// Presents agency billing configuration and a read-only operational projection. The charts
/// derive from the same billing records used by the queue and remittance screens; they do not
/// create a second source of financial truth.
/// </summary>
public partial class BillingOverviewViewModel : ObservableObject
{
    private readonly IBillingService _billingService;
    private readonly ISessionService _sessionService;
    private readonly IFormAttestationChangeReviewService? _formChangeReviews;
    private readonly IAdminFormNoteCorrectionService? _adminFormCorrections;
    private readonly IReleaseObligationService? _releaseObligations;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly LatestRequestTracker _accountLoads = new();
    private IReadOnlyList<BillingMonthPoint> _revenueMonths = [];
    private BillingOutcomeCounts _outcomeCounts = new(0, 0, 0);

    public BillingOverviewViewModel(
        IBillingService billingService,
        ISessionService sessionService,
        ThemeService? themeService = null,
        BillingComplianceRecoveryViewModel? complianceRecovery = null,
        IFormAttestationChangeReviewService? formChangeReviews = null,
        IAdminFormNoteCorrectionService? adminFormCorrections = null,
        IReleaseObligationService? releaseObligations = null)
    {
        _billingService = billingService;
        _sessionService = sessionService;
        _formChangeReviews = formChangeReviews;
        _adminFormCorrections = adminFormCorrections;
        _releaseObligations = releaseObligations;
        ComplianceRecovery = complianceRecovery;

        // OxyPlot colors are copied into a PlotModel, so rebuild the lightweight models when
        // the WPF palette changes. No billing data is refetched.
        if (themeService is not null)
            themeService.ThemeChanged += (_, _) => RebuildCharts();
    }

    [ObservableProperty] private string procedureCode = string.Empty;
    [ObservableProperty] private string? modifier;
    [ObservableProperty] private decimal? unitRate;
    [ObservableProperty] private string ediSubmitterId = string.Empty;
    [ObservableProperty] private string payerName = string.Empty;
    [ObservableProperty] private string payerId = string.Empty;
    [ObservableProperty] private string contactName = string.Empty;
    [ObservableProperty] private string contactPhone = string.Empty;
    [ObservableProperty] private string? statusMessage;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private PlotModel? revenueTrendChartModel;
    [ObservableProperty] private PlotModel? claimOutcomeChartModel;
    [ObservableProperty] private string readyRevenueLabel = "$0";
    [ObservableProperty] private string draftRevenueLabel = "$0";
    [ObservableProperty] private string billedRevenueLabel = "$0";
    [ObservableProperty] private string paidRevenueLabel = "$0";
    [ObservableProperty] private string readinessDetail = "No services are waiting in the billing queue.";
    [ObservableProperty] private string paidOutcomeLabel = "0 paid";
    [ObservableProperty] private string partialOutcomeLabel = "0 partially paid";
    [ObservableProperty] private string attentionOutcomeLabel = "0 need attention";
    [ObservableProperty] private string paidClaimPercentLabel = "—";
    [ObservableProperty] private bool hasRevenueData;
    [ObservableProperty] private bool hasRemittanceData;
    [ObservableProperty] private bool hasBillingPolicyReviewFlags;
    [ObservableProperty] private string billingPolicyReviewSummary =
        "No unresolved billing-policy review flags.";
    public BillingComplianceRecoveryViewModel? ComplianceRecovery { get; }
    public ObservableCollection<BillingPolicyReviewRow> BillingPolicyReviewFlags { get; } = [];
    public ObservableCollection<FormAttestationChangeReviewRow> FormChangeReviewFlags { get; } = [];
    [ObservableProperty] private string formChangeReviewSummary =
        "No form or release date changes need billing review.";
    [ObservableProperty] private bool canAdminCorrectFormDate;
    [ObservableProperty] private string correctionNoteIdText = string.Empty;
    [ObservableProperty] private AdminFormNoteCorrectionTargetDto? correctionTarget;
    [ObservableProperty] private AdminReleaseNoteCorrectionTargetDto? releaseCorrectionTarget;
    [ObservableProperty] private bool hasCorrectionTarget;
    [ObservableProperty] private string correctionTargetSummary = string.Empty;
    [ObservableProperty] private DateTime? correctedActivityDate;
    [ObservableProperty] private string correctionReason = string.Empty;
    [ObservableProperty] private bool correctionEvidenceConfirmed;
    [ObservableProperty] private string correctionStatusMessage = string.Empty;
    [ObservableProperty] private bool isFormDateCorrectionBusy;

    public bool HasLoaded { get; private set; }

    public async Task LoadAsync(bool waitForExisting = false)
    {
        if (waitForExisting)
            await _loadGate.WaitAsync();
        else if (!await _loadGate.WaitAsync(0))
            return;
        var account = _sessionService.CurrentUser;
        var request = _accountLoads.Begin();

        try
        {
            IsBusy = true;
            CanAdminCorrectFormDate = account?.HasAdminPermissions == true &&
                (_adminFormCorrections is not null || _releaseObligations is not null);
            CorrectionTarget = null;
            ReleaseCorrectionTarget = null;
            HasCorrectionTarget = false;
            CorrectionTargetSummary = string.Empty;
            var actor = account?.ToAgencyActor()
                ?? throw new UnauthorizedAccessException("A signed-in user is required.");
            ResetAnalyticsDisplay();
            var configuration = await _billingService.GetBillingConfigurationAsync(actor);
            if (!_accountLoads.IsCurrent(request) || !ReferenceEquals(_sessionService.CurrentUser, account))
                return;

            PublishConfiguration(configuration);

            if (ComplianceRecovery is not null)
                await ComplianceRecovery.LoadPeopleAsync();
            if (!_accountLoads.IsCurrent(request) || !ReferenceEquals(_sessionService.CurrentUser, account))
                return;

            try
            {
                // Each service owns its own data context/API call. Once the account is confirmed,
                // these independent reads can run together without delaying one another.
                var notesTask = _billingService.GetApprovedUnbilledNotesAsync(actor);
                var periodOverviewTask = _billingService.GetBillingPeriodOverviewAsync(
                    actor,
                    DateTime.Today);
                var outcomesTask = _billingService.GetRemittanceOutcomesAsync(actor);
                var policyReviewsTask =
                    _billingService.GetBillingCompliancePolicyReviewFlagsAsync(actor);
                await Task.WhenAll(notesTask, periodOverviewTask, outcomesTask, policyReviewsTask);
                if (!_accountLoads.IsCurrent(request) || !ReferenceEquals(_sessionService.CurrentUser, account))
                    return;

                var validations = notesTask.Result
                    .Select(_billingService.ValidateNoteForBilling)
                    .ToList();
                PublishAnalytics(CreateAnalytics(
                    configuration,
                    validations,
                    periodOverviewTask.Result,
                    outcomesTask.Result));
                PublishBillingPolicyReviewFlags(policyReviewsTask.Result);
                StatusMessage = "Billing dashboard and configuration loaded.";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Billing overview analytics failed: {ex.Message}");
                if (_accountLoads.IsCurrent(request) && ReferenceEquals(_sessionService.CurrentUser, account))
                    StatusMessage = "Billing configuration loaded, but the dashboard statistics are temporarily unavailable.";
            }

            await LoadFormChangeReviewsAsync(request, account);
            if (!_accountLoads.IsCurrent(request) || !ReferenceEquals(_sessionService.CurrentUser, account))
                return;
            HasLoaded = true;
        }
        catch (Exception ex)
        {
            if (_accountLoads.IsCurrent(request) && ReferenceEquals(_sessionService.CurrentUser, account))
                StatusMessage = $"Unable to load billing configuration: {ex.Message}";
        }
        finally
        {
            if (_accountLoads.IsCurrent(request) && ReferenceEquals(_sessionService.CurrentUser, account))
                IsBusy = false;
            _loadGate.Release();
        }
    }

    private async Task LoadFormChangeReviewsAsync(int request, Sati.Models.User? account)
    {
        if (_formChangeReviews is null) return;
        try
        {
            var flags = await _formChangeReviews.GetForBillingAsync();
            if (!_accountLoads.IsCurrent(request) || !ReferenceEquals(_sessionService.CurrentUser, account))
                return;
            var current = FormAttestationChangeReviewRow.Latest(flags);
            FormChangeReviewFlags.Clear();
            foreach (var flag in current) FormChangeReviewFlags.Add(flag);
            FormChangeReviewSummary = current.Count == 0
                ? "No form or release date changes need billing review."
                : $"{current.Count} form or release note{(current.Count == 1 ? "" : "s")} changed after submission; " +
                  $"{current.Count(flag => flag.MustHoldBilling)} currently held for billing.";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Billing form change reviews failed: {ex.Message}");
            if (_accountLoads.IsCurrent(request) && ReferenceEquals(_sessionService.CurrentUser, account))
                FormChangeReviewSummary = "Form and release date change notices are temporarily unavailable.";
        }
    }

    [RelayCommand]
    private async Task FindFormDateCorrectionTarget()
    {
        if (IsFormDateCorrectionBusy) return;
        if (!CanAdminCorrectFormDate) return;
        if (!int.TryParse(CorrectionNoteIdText, out var noteId) || noteId <= 0)
        {
            CorrectionStatusMessage = "Enter a valid note ID.";
            return;
        }
        await OpenFormDateCorrectionTargetAsync(noteId);
    }

    [RelayCommand]
    private async Task ReviewFormDateFlag(int noteId)
    {
        if (IsFormDateCorrectionBusy) return;
        if (!CanAdminCorrectFormDate) return;
        CorrectionNoteIdText = noteId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await OpenFormDateCorrectionTargetAsync(noteId);
    }

    private async Task OpenFormDateCorrectionTargetAsync(int noteId)
    {
        if (!CanAdminCorrectFormDate) return;
        IsFormDateCorrectionBusy = true;
        CorrectionStatusMessage = string.Empty;
        CorrectionTarget = null;
        ReleaseCorrectionTarget = null;
        HasCorrectionTarget = false;
        try
        {
            var account = _sessionService.CurrentUser;
            var target = _adminFormCorrections is null
                ? null : await _adminFormCorrections.GetTargetAsync(noteId);
            if (!ReferenceEquals(_sessionService.CurrentUser, account)) return;
            if (target is null)
            {
                var releaseTarget = _releaseObligations is null
                    ? null : await _releaseObligations.GetAdminCorrectionTargetAsync(noteId);
                if (!ReferenceEquals(_sessionService.CurrentUser, account)) return;
                if (releaseTarget is null)
                {
                    CorrectionStatusMessage =
                        "No eligible linked form or release note was found in this agency.";
                    return;
                }
                ReleaseCorrectionTarget = releaseTarget;
                HasCorrectionTarget = true;
                CorrectedActivityDate = releaseTarget.ActivityDate;
                CorrectionReason = string.Empty;
                CorrectionEvidenceConfirmed = false;
                CorrectionTargetSummary =
                    $"Note #{releaseTarget.NoteId}, {releaseTarget.Recipient} release #{releaseTarget.ReleaseObligationId}, {releaseTarget.Status}; " +
                    $"activity {releaseTarget.ActivityDate:MM/dd/yyyy}, completion " +
                    $"{releaseTarget.CurrentCompletedOn?.ToString("MM/dd/yyyy") ?? "revoked / no current completion"}, " +
                    $"due {releaseTarget.DueDate:MM/dd/yyyy}.";
                if (releaseTarget.HasClaimRecord)
                    CorrectionStatusMessage =
                        "The existing claim will be preserved and flagged for billing review after this Admin correction.";
                return;
            }
            CorrectionTarget = target;
            HasCorrectionTarget = true;
            CorrectedActivityDate = target.ActivityDate;
            CorrectionReason = string.Empty;
            CorrectionEvidenceConfirmed = false;
            CorrectionTargetSummary =
                $"Note #{target.NoteId}, linked form #{target.FormId}, {target.Status}; " +
                $"activity {target.ActivityDate:MM/dd/yyyy}, completion " +
                $"{target.CurrentCompletedOn?.ToString("MM/dd/yyyy") ?? "revoked / no current completion"}, " +
                $"due {target.DueDate:MM/dd/yyyy}.";
            if (target.HasClaimRecord)
                CorrectionStatusMessage =
                    "The existing claim will be preserved and flagged for billing review after this Admin correction.";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Admin form note lookup failed: {ex.Message}");
            CorrectionStatusMessage = ex.Message;
        }
        finally
        {
            IsFormDateCorrectionBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveFormDateCorrection()
    {
        if (IsFormDateCorrectionBusy) return;
        if (!CanAdminCorrectFormDate ||
            (CorrectionTarget is null && ReleaseCorrectionTarget is null))
            return;
        var currentDate = CorrectionTarget?.ActivityDate ?? ReleaseCorrectionTarget!.ActivityDate;
        if (CorrectedActivityDate is not DateTime corrected ||
            corrected.Date == currentDate.Date ||
            string.IsNullOrWhiteSpace(CorrectionReason) || !CorrectionEvidenceConfirmed)
        {
            CorrectionStatusMessage =
                "Choose the actual work date, explain the correction, and confirm the source evidence. " +
                "The corrected date must differ from the current date.";
            return;
        }
        if (ReleaseCorrectionTarget is not null && CorrectionReason.Trim().Length > 500)
        {
            CorrectionStatusMessage = "Keep the release correction explanation to 500 characters or fewer.";
            return;
        }

        IsFormDateCorrectionBusy = true;
        CorrectionStatusMessage = string.Empty;
        try
        {
            if (CorrectionTarget is { } target && _adminFormCorrections is not null)
                await _adminFormCorrections.CorrectAsync(
                    target.NoteId, target.Revision, corrected.Date,
                    CorrectionReason.Trim(), CorrectionEvidenceConfirmed);
            else if (ReleaseCorrectionTarget is { } releaseTarget && _releaseObligations is not null)
                await _releaseObligations.CorrectNoteDateAsAdminAsync(
                    releaseTarget.NoteId, releaseTarget.Revision, corrected.Date,
                    CorrectionReason.Trim(), CorrectionEvidenceConfirmed);
            CorrectionTarget = null;
            ReleaseCorrectionTarget = null;
            HasCorrectionTarget = false;
            CorrectionNoteIdText = string.Empty;
            CorrectedActivityDate = null;
            CorrectionReason = string.Empty;
            CorrectionEvidenceConfirmed = false;
            await LoadAsync(waitForExisting: true);
            CorrectionStatusMessage = "The source date was corrected and billing was recalculated.";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Admin form note correction failed: {ex.Message}");
            CorrectionStatusMessage = ex.Message;
        }
        finally
        {
            IsFormDateCorrectionBusy = false;
        }
    }

    private void PublishConfiguration(BillingConfiguration configuration)
    {
        ProcedureCode = configuration.ProcedureCode;
        Modifier = configuration.Modifier;
        UnitRate = configuration.UnitRate;
        EdiSubmitterId = configuration.EdiSubmitterId;
        PayerName = configuration.PayerName;
        PayerId = configuration.PayerId;
        ContactName = configuration.ContactName;
        ContactPhone = configuration.ContactPhone;
    }

    private void PublishAnalytics(BillingOverviewAnalytics analytics)
    {
        ReadyRevenueLabel = analytics.ReadyRevenue.ToString("C0");
        DraftRevenueLabel = analytics.DraftRevenue.ToString("C0");
        BilledRevenueLabel = analytics.SixMonthBilledRevenue.ToString("C0");
        PaidRevenueLabel = analytics.SixMonthPaidRevenue.ToString("C0");
        ReadinessDetail = analytics.ReadyClaimCount == 0 && analytics.BlockedClaimCount == 0
            ? "No services are waiting in the billing queue."
            : $"{analytics.ReadyClaimCount} ready · {analytics.BlockedClaimCount} need attention";

        _revenueMonths = analytics.Months;
        _outcomeCounts = analytics.Outcomes;
        HasRevenueData = analytics.Months.Any(month => month.Billed > 0 || month.Paid > 0);
        HasRemittanceData = analytics.Outcomes.Total > 0;
        PaidOutcomeLabel = $"{analytics.Outcomes.Paid} paid";
        PartialOutcomeLabel = $"{analytics.Outcomes.PartiallyPaid} partially paid";
        AttentionOutcomeLabel = $"{analytics.Outcomes.NeedsAttention} need attention";
        PaidClaimPercentLabel = analytics.Outcomes.Total == 0
            ? "—"
            : $"{100m * analytics.Outcomes.Paid / analytics.Outcomes.Total:0}%";
        RebuildCharts();
    }

    private void RebuildCharts()
    {
        RevenueTrendChartModel = BuildRevenueTrendChart(_revenueMonths);
        ClaimOutcomeChartModel = BuildClaimOutcomeChart(_outcomeCounts);
    }

    private void PublishBillingPolicyReviewFlags(
        IReadOnlyList<BillingCompliancePolicyReviewFlagDto> flags)
    {
        BillingPolicyReviewFlags.Clear();
        foreach (var flag in flags)
            BillingPolicyReviewFlags.Add(new BillingPolicyReviewRow(flag));
        HasBillingPolicyReviewFlags = flags.Count > 0;
        BillingPolicyReviewSummary = flags.Count == 0
            ? "No unresolved billing-policy review flags."
            : $"{flags.Count:N0} unresolved billing-policy review " +
              $"{(flags.Count == 1 ? "flag" : "flags")}. Submitted records remain unchanged until reviewed.";
    }

    private void ResetAnalyticsDisplay()
    {
        _revenueMonths = [];
        _outcomeCounts = new(0, 0, 0);
        ReadyRevenueLabel = "$0";
        DraftRevenueLabel = "$0";
        BilledRevenueLabel = "$0";
        PaidRevenueLabel = "$0";
        ReadinessDetail = "No services are waiting in the billing queue.";
        PaidOutcomeLabel = "0 paid";
        PartialOutcomeLabel = "0 partially paid";
        AttentionOutcomeLabel = "0 need attention";
        PaidClaimPercentLabel = "—";
        HasRevenueData = false;
        HasRemittanceData = false;
        RevenueTrendChartModel = null;
        ClaimOutcomeChartModel = null;
        BillingPolicyReviewFlags.Clear();
        HasBillingPolicyReviewFlags = false;
        BillingPolicyReviewSummary = "No unresolved billing-policy review flags.";
        FormChangeReviewFlags.Clear();
        FormChangeReviewSummary = "No form or release date changes need billing review.";
    }

    internal static BillingOverviewAnalytics CreateAnalytics(
        BillingConfiguration configuration,
        IReadOnlyCollection<BillingValidationResult> validations,
        BillingPeriodOverviewDto periodOverview,
        IEnumerable<RemittanceClaimOutcomeDto> outcomes)
    {
        var outcomeList = outcomes.ToList();

        var points = periodOverview.Months.Select(month =>
        {
            var date = new DateTime(month.Year, month.Month, 1);
            return new BillingMonthPoint(
                date,
                month.BilledAmount,
                outcomeList
                    .Where(outcome => SameMonth(outcome.PaymentDate ?? outcome.ReceivedAtUtc, date))
                    .Sum(outcome => outcome.PaidAmount));
        })
            .ToList();

        var ready = validations.Where(result => result.IsValid).ToList();
        var rate = configuration.UnitRate.GetValueOrDefault();
        var readyRevenue = ready.Sum(result => BillingRules.CalculateCharge(
            BillingRules.CalculateSection13Units(result.Note.Minutes), rate));
        var paid = outcomeList.Count(outcome =>
            string.Equals(outcome.Status, RemittanceClaimStatus.Paid.ToString(), StringComparison.OrdinalIgnoreCase));
        var partiallyPaid = outcomeList.Count(outcome =>
            string.Equals(outcome.Status, RemittanceClaimStatus.PartiallyPaid.ToString(), StringComparison.OrdinalIgnoreCase));

        return new BillingOverviewAnalytics(
            readyRevenue,
            periodOverview.DraftRevenue,
            points.Sum(point => point.Billed),
            points.Sum(point => point.Paid),
            ready.Count,
            validations.Count - ready.Count,
            points,
            new BillingOutcomeCounts(paid, partiallyPaid, outcomeList.Count - paid - partiallyPaid));
    }

    private static bool SameMonth(DateTime date, DateTime month) =>
        date.Year == month.Year && date.Month == month.Month;

    internal static PlotModel BuildRevenueTrendChart(IReadOnlyList<BillingMonthPoint> months)
    {
        var text = PlotTheme.Color("TextPrimaryBrush", OxyColor.FromRgb(0x3D, 0x2B, 0x1F));
        var muted = PlotTheme.Color("TextMutedBrush", OxyColor.FromRgb(0x8A, 0x7A, 0x6A));
        var border = PlotTheme.ColorWithAlpha("TextPrimaryBrush", 50, text);
        var billedColor = PlotTheme.Color("AccentBrush", OxyColor.FromRgb(0xD7, 0x92, 0x54));
        var paidColor = PlotTheme.Color("ChartBlueBrush", OxyColor.FromRgb(0x17, 0x69, 0xD2));

        var model = new PlotModel
        {
            Background = OxyColors.Transparent,
            PlotAreaBackground = OxyColors.Transparent,
            PlotAreaBorderColor = OxyColors.Transparent,
            PlotAreaBorderThickness = new OxyThickness(0),
            TextColor = text,
            PlotMargins = new OxyThickness(52, 18, 16, 42)
        };
        var monthAxis = new CategoryAxis
        {
            Position = AxisPosition.Bottom,
            TextColor = muted,
            TicklineColor = OxyColors.Transparent,
            AxislineStyle = LineStyle.None,
            GapWidth = 0.45
        };
        foreach (var month in months)
            monthAxis.Labels.Add(month.Month.ToString("MMM"));

        var amountAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Minimum = 0,
            TextColor = muted,
            TicklineColor = OxyColors.Transparent,
            AxislineStyle = LineStyle.None,
            MajorGridlineStyle = LineStyle.Solid,
            MajorGridlineColor = border,
            MinorGridlineStyle = LineStyle.None,
            LabelFormatter = value => value switch
            {
                >= 1_000_000 => $"${value / 1_000_000:0.#}M",
                >= 1_000 => $"${value / 1_000:0.#}K",
                _ => $"${value:0}"
            }
        };

        var billed = new AreaSeries
        {
            Title = "Billed",
            Color = billedColor,
            Fill = OxyColor.FromArgb(70, billedColor.R, billedColor.G, billedColor.B),
            StrokeThickness = 2,
            TrackerFormatString = "{1}\nBilled: {2:C0}"
        };
        var billedDepth = new LineSeries
        {
            Color = OxyColor.FromArgb(45, billedColor.R, billedColor.G, billedColor.B),
            StrokeThickness = 8,
            TrackerFormatString = string.Empty
        };
        var paid = new LineSeries
        {
            Title = "Paid",
            Color = paidColor,
            StrokeThickness = 3,
            MarkerType = MarkerType.Circle,
            MarkerSize = 4,
            MarkerFill = paidColor,
            TrackerFormatString = "{1}\nPaid: {2:C0}"
        };
        var paidDepth = new LineSeries
        {
            Color = OxyColor.FromArgb(55, paidColor.R, paidColor.G, paidColor.B),
            StrokeThickness = 9,
            TrackerFormatString = string.Empty
        };
        for (var index = 0; index < months.Count; index++)
        {
            var billedPoint = new DataPoint(index, (double)months[index].Billed);
            var paidPoint = new DataPoint(index, (double)months[index].Paid);
            billedDepth.Points.Add(billedPoint);
            billed.Points.Add(billedPoint);
            paidDepth.Points.Add(paidPoint);
            paid.Points.Add(paidPoint);
        }

        model.Axes.Add(monthAxis);
        model.Axes.Add(amountAxis);
        model.Series.Add(billedDepth);
        model.Series.Add(billed);
        model.Series.Add(paidDepth);
        model.Series.Add(paid);
        return model;
    }

    internal static PlotModel BuildClaimOutcomeChart(BillingOutcomeCounts outcomes)
    {
        var text = PlotTheme.Color("TextPrimaryBrush", OxyColor.FromRgb(0x3D, 0x2B, 0x1F));
        var paidColor = PlotTheme.Color("ChartBlueBrush", OxyColor.FromRgb(0x17, 0x69, 0xD2));
        var partialColor = PlotTheme.Color("ChartGoldBrush", OxyColor.FromRgb(0xE3, 0xAD, 0x3F));
        var attentionColor = PlotTheme.Color("ChartCopperBrush", OxyColor.FromRgb(0xD4, 0x7A, 0x24));
        var emptyColor = PlotTheme.ColorWithAlpha("TextPrimaryBrush", 30, text);

        var model = new PlotModel
        {
            Background = OxyColors.Transparent,
            PlotAreaBackground = OxyColors.Transparent,
            TextColor = text,
            PlotMargins = new OxyThickness(4)
        };
        PlotDonut.AddGradientLayers(model,
        [
            new PlotDonut.Slice("Paid", outcomes.Paid, paidColor),
            new PlotDonut.Slice("Partially paid", outcomes.PartiallyPaid, partialColor),
            new PlotDonut.Slice("Needs attention", outcomes.NeedsAttention, attentionColor)
        ], emptyColor);
        return model;
    }

    public void ClearForAccountSwitch()
    {
        _accountLoads.Invalidate();
        ComplianceRecovery?.ClearForAccountSwitch();
        ProcedureCode = string.Empty;
        Modifier = null;
        UnitRate = null;
        EdiSubmitterId = string.Empty;
        PayerName = string.Empty;
        PayerId = string.Empty;
        ContactName = string.Empty;
        ContactPhone = string.Empty;
        StatusMessage = null;
        IsBusy = false;
        HasLoaded = false;
        CanAdminCorrectFormDate = false;
        CorrectionNoteIdText = string.Empty;
        CorrectionTarget = null;
        ReleaseCorrectionTarget = null;
        HasCorrectionTarget = false;
        CorrectionTargetSummary = string.Empty;
        CorrectedActivityDate = null;
        CorrectionReason = string.Empty;
        CorrectionEvidenceConfirmed = false;
        CorrectionStatusMessage = string.Empty;
        IsFormDateCorrectionBusy = false;
        ResetAnalyticsDisplay();
    }

    [RelayCommand]
    private async Task Refresh() => await LoadAsync(waitForExisting: true);

    [RelayCommand]
    private async Task Save()
    {
        try
        {
            IsBusy = true;
            await _billingService.SaveBillingConfigurationAsync(CurrentActor(), new BillingConfiguration(
                ProcedureCode, Modifier, UnitRate, EdiSubmitterId,
                PayerName, PayerId, ContactName, ContactPhone));
            await LoadAsync();
            StatusMessage = "Billing configuration saved. Refresh the queue to apply it.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to save billing configuration: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private AgencyActor CurrentActor() =>
        _sessionService.CurrentUser?.ToAgencyActor()
        ?? throw new UnauthorizedAccessException("A signed-in user is required.");

    internal sealed record BillingMonthPoint(DateTime Month, decimal Billed, decimal Paid);
    internal sealed record BillingOutcomeCounts(int Paid, int PartiallyPaid, int NeedsAttention)
    {
        public int Total => Paid + PartiallyPaid + NeedsAttention;
    }

    public sealed record BillingPolicyReviewRow(BillingCompliancePolicyReviewFlagDto Flag)
    {
        public string RecordLabel => Flag.ClaimRecordId is int claimId
            ? $"Claim record {claimId:N0} (note {Flag.NoteId:N0})"
            : $"Note {Flag.NoteId:N0}";
        public string ChangeLabel => Flag.ChangeKind switch
        {
            BillingCompliancePolicyImpactChangeKind.NewlyBlocked => "Newly blocked",
            BillingCompliancePolicyImpactChangeKind.NewlyUnblocked => "Newly unblocked",
            _ => "Blocked by different requirements"
        };
        public string DateLabel =>
            $"Service {Flag.ServiceDate:MMM d, yyyy}; policy effective {Flag.PolicyEffectiveOn:MMM d, yyyy}";
        public string BlockerLabel =>
            $"Before: {Describe(Flag.PreviousBlockingObligationIds)}. " +
            $"After: {Describe(Flag.NewBlockingObligationIds)}.";
        public string StatusLabel => $"{Flag.Status} · created {Flag.CreatedAtUtc.ToLocalTime():g}";

        private static string Describe(IReadOnlyList<string> ids) =>
            ids.Count == 0 ? "none" : string.Join(", ", ids);
    }

    internal sealed record BillingOverviewAnalytics(
        decimal ReadyRevenue,
        decimal DraftRevenue,
        decimal SixMonthBilledRevenue,
        decimal SixMonthPaidRevenue,
        int ReadyClaimCount,
        int BlockedClaimCount,
        IReadOnlyList<BillingMonthPoint> Months,
        BillingOutcomeCounts Outcomes);

}
