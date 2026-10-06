using CommunityToolkit.Mvvm.ComponentModel;
using OxyPlot;
using CommunityToolkit.Mvvm.Input;
using OxyPlot.Axes;
using OxyPlot.Series;
using Sati.Data;
using Sati.Contracts.V1;
using Sati.Helpers;
using Sati.Models;
using Sati.Services;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Sati.ViewModels
{
    // One row of the history table. A record because it's an immutable snapshot of a
    // computed month — nothing about a past month changes once we've built it.
    public record ProductivityMonth(
        string MonthLabel,
        int Units,
        int? Threshold,
        decimal? AttainmentPercent,
        decimal? Incentive);

    public sealed record StatisticsPeriodChoice(StatisticsPeriod Value, string Label);
    public sealed record StatisticsConsumerChoice(int? PersonId, string Label);
    public sealed record StatisticsMetricChoice(string Key, string Label);
    public sealed record StatisticsPeriodDisplayRow(string Period, StatisticsMetrics Metrics);
    public sealed record StatisticsFocusRow(string Period, string Value);

    public partial class StatisticsViewModel : ObservableObject
    {
        private readonly ISessionService _sessionService;
        private readonly IProductivityReportService _productivityReportService;
        private readonly IIncentiveService _incentiveService;
        private readonly IExemptDateService _exemptDateService;
        private readonly IConsumerBillingLossReportService _billingLossReportService;
        private readonly IStatisticsBreakdownService _statisticsBreakdownService;
        private readonly LatestRequestTracker _loadRequests = new();
        private int? _consumerChoicesUserId;

        public StatisticsViewModel(
            ISessionService sessionService,
            IProductivityReportService productivityReportService,
            IIncentiveService incentiveService,
            IExemptDateService exemptDateService,
            IConsumerBillingLossReportService billingLossReportService,
            IStatisticsBreakdownService statisticsBreakdownService,
            ThemeService themeService)
        {
            _sessionService = sessionService;
            _productivityReportService = productivityReportService;
            _incentiveService = incentiveService;
            _exemptDateService = exemptDateService;
            _billingLossReportService = billingLossReportService;
            _statisticsBreakdownService = statisticsBreakdownService;
            SelectedPeriodChoice = PeriodChoices[1];
            SelectedConsumerChoice = ConsumerChoices[0];
            SelectedMetricChoice = MetricChoices[0];
            themeService.ThemeChanged += (_, _) =>
            {
                if (HasBreakdownReport)
                    UnitsChartModel = BuildUnitsChart(BreakdownPeriods);
            };
        }

        public ObservableCollection<ProductivityMonth> Months { get; } = [];
        public ObservableCollection<ConsumerBillingLossRow> ConsumerBillingRows { get; } = [];
        public ObservableCollection<StatisticsPeriodDisplayRow> BreakdownPeriods { get; } = [];
        public ObservableCollection<StatisticsClientRow> BreakdownClients { get; } = [];
        public ObservableCollection<StatisticsFocusRow> FocusRows { get; } = [];
        public ObservableCollection<StatisticsConsumerChoice> ConsumerChoices { get; } =
            [new(null, "All consumers")];

        public IReadOnlyList<StatisticsPeriodChoice> PeriodChoices { get; } =
        [
            new(StatisticsPeriod.Week, "Week"),
            new(StatisticsPeriod.Month, "Month"),
            new(StatisticsPeriod.Quarter, "Quarter"),
            new(StatisticsPeriod.Year, "Year")
        ];

        public IReadOnlyList<StatisticsMetricChoice> MetricChoices { get; } =
        [
            new("documented", "Documented units"),
            new("billable-marked", "Documented units marked billable"),
            new("nonbillable", "Intentionally unbilled documented units"),
            new("pending", "Pending units"),
            new("blocked", "Compliance blocked units"),
            new("form", "Form work units"),
            new("visit", "Visit work units"),
            new("claimed", "Original payer-transmitted claim units"),
            new("claimed-form", "Payer-transmitted units on Form notes"),
            new("claimed-visit", "Payer-transmitted units on Visit notes"),
            new("locked", "Claim units locked for billing"),
            new("days", "Documented service days"),
            new("billable-days", "Service dates with billable-marked documentation"),
            new("nonbillable-days", "Service dates with intentionally unbilled documentation"),
            new("expired", "Service dates with expired pending work"),
            new("abandoned", "Service dates with abandoned work"),
            new("notes", "Documented notes"),
            new("forms-count", "Documented notes marked Form"),
            new("visits-count", "Documented notes marked Visit"),
            new("claims-count", "Original payer-transmitted claim lines"),
            new("locked-count", "Claim lines locked for billing")
        ];

        [ObservableProperty] private PlotModel? unitsChartModel;
        [ObservableProperty] private string windowLabel = string.Empty;
        [ObservableProperty] private int totalUnits;
        [ObservableProperty] private decimal? totalIncentive;
        [ObservableProperty] private bool hasData;
        [ObservableProperty] private bool showProductivityEmpty;
        [ObservableProperty] private bool hasConsumerBillingRows;
        [ObservableProperty] private bool showConsumerBillingEmpty;
        [ObservableProperty] private int totalBillableWorkUnits;
        [ObservableProperty] private int totalNonBillableWorkUnits;
        [ObservableProperty] private string totalLostWorkPercentageLabel = "—";
        [ObservableProperty] private DateTime? selectedStartDate =
            new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        [ObservableProperty] private DateTime? selectedEndDate = DateTime.Today;
        [ObservableProperty] private string dateFilterMessage = string.Empty;
        [ObservableProperty] private bool isLoading;
        [ObservableProperty] private bool hasLoadError;
        [ObservableProperty] private string loadErrorMessage = string.Empty;
        [ObservableProperty] private StatisticsPeriodChoice selectedPeriodChoice =
            new(StatisticsPeriod.Month, "Month");
        [ObservableProperty] private StatisticsConsumerChoice selectedConsumerChoice =
            new(null, "All consumers");
        [ObservableProperty] private StatisticsMetricChoice selectedMetricChoice =
            new("documented", "Documented units");
        [ObservableProperty] private StatisticsMetrics? breakdownTotals;
        [ObservableProperty] private bool hasBreakdownReport;
        [ObservableProperty] private bool hasLoadedSuccessfully;
        [ObservableProperty] private bool hasIncompleteClaimUnits;
        [ObservableProperty] private string incompleteClaimUnitsMessage = string.Empty;
        [ObservableProperty] private string complianceWindowMessage = string.Empty;
        [ObservableProperty] private string consumerBillingEmptyMessage = string.Empty;
        [ObservableProperty] private string focusedMetricLabel = "Documented units";

        partial void OnSelectedMetricChoiceChanged(StatisticsMetricChoice value) =>
            RefreshFocusRows();

        [RelayCommand]
        private async Task ShowThisMonthAsync()
        {
            SelectedStartDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            SelectedEndDate = DateTime.Today;
            await LoadAsync(preserveConsumerSelection: true);
        }

        [RelayCommand]
        private async Task ShowLastMonthAsync()
        {
            var thisMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            SelectedStartDate = thisMonth.AddMonths(-1);
            SelectedEndDate = thisMonth.AddDays(-1);
            await LoadAsync(preserveConsumerSelection: true);
        }

        [RelayCommand]
        private async Task ShowLast30DaysAsync()
        {
            SelectedStartDate = DateTime.Today.AddDays(-29);
            SelectedEndDate = DateTime.Today;
            await LoadAsync(preserveConsumerSelection: true);
        }

        [RelayCommand]
        private async Task ShowThisYearAsync()
        {
            SelectedStartDate = new DateTime(DateTime.Today.Year, 1, 1);
            SelectedEndDate = DateTime.Today;
            await LoadAsync(preserveConsumerSelection: true);
        }

        [RelayCommand]
        private async Task ApplyDateWindowAsync() =>
            await LoadAsync(preserveConsumerSelection: true);

        // Rebuilt on every navigation and filter application. Independent reads
        // are started together, and only the newest request may publish results.
        public async Task LoadAsync(bool preserveConsumerSelection = false)
        {
            var request = _loadRequests.Begin();
            ClearDisplayedReport();
            HasLoadError = false;
            LoadErrorMessage = string.Empty;
            var user = _sessionService.CurrentUser;
            if (user is null)
            {
                ResetConsumerChoices();
                _consumerChoicesUserId = null;
                IsLoading = false;
                return;
            }
            if (!preserveConsumerSelection || _consumerChoicesUserId != user.Id)
                ResetConsumerChoices();

            if (SelectedStartDate is not DateTime selectedStart
                || SelectedEndDate is not DateTime selectedEnd)
            {
                DateFilterMessage = "Choose both a start date and an end date.";
                IsLoading = false;
                return;
            }

            var windowStart = selectedStart.Date;
            var windowEnd = selectedEnd.Date;
            if (windowEnd < windowStart)
            {
                DateFilterMessage = "The end date must be on or after the start date.";
                IsLoading = false;
                return;
            }
            if (windowStart.Year < 2000 || windowEnd.Year > 2200 ||
                (windowEnd - windowStart).TotalDays > 3_660)
            {
                DateFilterMessage =
                    "Choose a reporting window from 2000 through 2200 that is no longer than 10 years.";
                IsLoading = false;
                return;
            }

            DateFilterMessage = string.Empty;
            IsLoading = true;
            WindowLabel = FormatWindowLabel(windowStart, windowEnd);

            try
            {
                var period = SelectedPeriodChoice?.Value ?? StatisticsPeriod.Month;
                var selectedPersonId = SelectedConsumerChoice?.PersonId;
                var window = BuildMonthWindow(windowStart, windowEnd);
                var years = window.Select(m => m.Year).Distinct().ToList();
                var productivityTask = _productivityReportService.GetUnitsAsync(
                    windowStart, windowEnd);
                var complianceEnd = windowEnd < DateTime.Today ? windowEnd : DateTime.Today;
                var billingLossTask = windowStart <= complianceEnd
                    ? _billingLossReportService.GetAsync(user.Id, windowStart, complianceEnd)
                    : Task.FromResult(new ConsumerBillingLossReport([], 0, 0, null));
                // Validate a retained filter against a fresh authorized caseload.
                // Future-only windows use today's caseload just for filter choices.
                var caseloadTask = windowStart <= complianceEnd
                    ? billingLossTask
                    : _billingLossReportService.GetAsync(user.Id, DateTime.Today, DateTime.Today);
                var breakdownTask = GetValidatedBreakdownAsync(
                    caseloadTask, windowStart, windowEnd, period, selectedPersonId);
                var historyTask = _incentiveService.GetHistoryAsync(user.Id);
                var exemptTasks = years
                    .Select(year => _exemptDateService.GetByYearAsync(user.Id, year))
                    .ToArray();

                // Partial months need an exact eligible-day count. Start those calls
                // before awaiting any database or API request so network latency overlaps.
                var eligibleDayTasks = new Dictionary<(int Year, int Month), Task<int>>();
                foreach (var month in window)
                {
                    var periodStart = month > windowStart ? month : windowStart;
                    var monthEnd = month.AddMonths(1).AddDays(-1);
                    var periodEnd = monthEnd < windowEnd ? monthEnd : windowEnd;
                    if (periodStart != month || periodEnd != monthEnd)
                    {
                        eligibleDayTasks[(month.Year, month.Month)] =
                            _incentiveService.GetEligibleDaysAsync(periodStart, periodEnd);
                    }
                }

                var exemptResultsTask = Task.WhenAll(exemptTasks);
                var eligibleDaysCompletion = Task.WhenAll(eligibleDayTasks.Values);
                await Task.WhenAll(
                    productivityTask,
                    breakdownTask,
                    billingLossTask,
                    caseloadTask,
                    historyTask,
                    exemptResultsTask,
                    eligibleDaysCompletion);

                if (!_loadRequests.IsCurrent(request) || _sessionService.CurrentUser?.Id != user.Id)
                    return;

                var (breakdown, validatedPersonId) = await breakdownTask;
                BreakdownTotals = breakdown.Totals;
                HasBreakdownReport = true;
                BreakdownPeriods.Clear();
                foreach (var row in breakdown.Periods)
                    BreakdownPeriods.Add(new StatisticsPeriodDisplayRow(
                        FormatWindowLabel(row.Start.Date, row.End.Date), row.Metrics));
                BreakdownClients.Clear();
                foreach (var row in breakdown.Clients)
                    BreakdownClients.Add(row);
                HasIncompleteClaimUnits = breakdown.Totals.SubmittedClaimsWithoutUnits > 0;
                IncompleteClaimUnitsMessage = HasIncompleteClaimUnits
                    ? $"{breakdown.Totals.SubmittedClaimsWithoutUnits:N0} payer-transmitted claim " +
                      (breakdown.Totals.SubmittedClaimsWithoutUnits == 1 ? "line has" : "lines have") +
                      " no frozen unit value. Payer-transmitted unit totals are incomplete."
                    : string.Empty;
                RefreshFocusRows();
                UnitsChartModel = BuildUnitsChart(BreakdownPeriods);

                var unitsByMonth = (await productivityTask)
                    .ToDictionary(item => (item.Year, item.Month), item => item.Units);
                var exempt = (await exemptResultsTask).SelectMany(items => items).ToList();

                // Detached history snapshots can be safely adjusted in memory for
                // a partial first/last month; report reads never persist an Incentive.
                var snapshots = (await historyTask)
                    .ToDictionary(i => (i.Year, i.Month));
                var computedMonths = new List<ProductivityMonth>(window.Count);
                foreach (var month in window)
                {
                    var key = (month.Year, month.Month);
                    var units = unitsByMonth.GetValueOrDefault(key, 0);
                    snapshots.TryGetValue(key, out var snapshot);

                    var periodStart = month > windowStart ? month : windowStart;
                    var monthEnd = month.AddMonths(1).AddDays(-1);
                    var periodEnd = monthEnd < windowEnd ? monthEnd : windowEnd;
                    var coversFullMonth = periodStart == month && periodEnd == monthEnd;

                    if (snapshot is not null)
                    {
                        var exemptDays = exempt.Count(e => e.Date.Date >= periodStart
                                                        && e.Date.Date <= periodEnd);
                        var eligibleDays = coversFullMonth
                            ? snapshot.DaysScheduled
                            : await eligibleDayTasks[key];
                        snapshot.DaysScheduled = Math.Max(0, eligibleDays - exemptDays);
                    }

                    int? threshold = snapshot?.Threshold;
                    decimal? attainment = threshold is > 0
                        ? Math.Round(100m * units / threshold.Value, 0)
                        : null;
                    // Incentives are monthly awards. A partial date window can show
                    // prorated attainment, but must not claim a prorated award.
                    decimal? incentive = snapshot is not null && coversFullMonth
                        ? snapshot.Calculate(units)
                        : null;

                    computedMonths.Add(new ProductivityMonth(
                        MonthLabel: FormatPeriodLabel(periodStart, periodEnd, month, monthEnd),
                        Units: units,
                        Threshold: threshold,
                        AttainmentPercent: attainment,
                        Incentive: incentive));
                }

                if (!_loadRequests.IsCurrent(request) || _sessionService.CurrentUser?.Id != user.Id)
                    return;

                Months.Clear();
                foreach (var month in computedMonths)
                    Months.Add(month);

                WindowLabel = FormatWindowLabel(windowStart, windowEnd);
                TotalUnits = computedMonths.Sum(m => m.Units);
                TotalIncentive = computedMonths.All(m => m.Incentive.HasValue)
                    ? computedMonths.Sum(m => m.Incentive!.Value)
                    : null;
                HasData = computedMonths.Any(m => m.Units > 0);
                ShowProductivityEmpty = !HasData;

                var billingLossReport = await billingLossTask;
                ConsumerBillingRows.Clear();
                foreach (var row in billingLossReport.Consumers)
                    ConsumerBillingRows.Add(row);

                TotalBillableWorkUnits = billingLossReport.TotalBillableUnits;
                TotalNonBillableWorkUnits = billingLossReport.TotalNonBillableUnits;
                TotalLostWorkPercentageLabel = billingLossReport.LostWorkPercentage is decimal percentage
                    ? $"{percentage:0.0}%"
                    : "—";
                HasConsumerBillingRows = ConsumerBillingRows.Count > 0;
                ShowConsumerBillingEmpty = !HasConsumerBillingRows;
                ComplianceWindowMessage = windowStart > DateTime.Today
                    ? "No elapsed days fall in this window. Compliance day counts begin when those dates occur."
                    : windowEnd > DateTime.Today
                        ? $"Compliance day counts stop at {DateTime.Today:MMM d, yyyy}; future days are excluded."
                        : "Compliance day counts cover the selected window.";
                ConsumerBillingEmptyMessage = windowStart > DateTime.Today
                    ? "No elapsed compliance days fall in this future reporting window."
                    : "No consumers are available for this reporting window.";

                var caseload = await caseloadTask;
                ConsumerChoices.Clear();
                ConsumerChoices.Add(new StatisticsConsumerChoice(null, "All consumers"));
                foreach (var row in caseload.Consumers)
                    ConsumerChoices.Add(new StatisticsConsumerChoice(row.PersonId, row.ConsumerName));
                SelectedConsumerChoice = ConsumerChoices.FirstOrDefault(
                    choice => choice.PersonId == validatedPersonId) ?? ConsumerChoices[0];
                _consumerChoicesUserId = user.Id;
                HasLoadedSuccessfully = true;
            }
            catch (Exception ex)
            {
                if (!_loadRequests.IsCurrent(request) || _sessionService.CurrentUser?.Id != user.Id)
                    return;

                ClearDisplayedReport();
                var reference = AppErrorLog.Record(ex, "statistics.load");
                HasLoadError = true;
                LoadErrorMessage =
                    "Statistics could not be loaded. Try Apply again. " +
                    $"Support reference: {reference}.";
            }
            finally
            {
                if (_loadRequests.IsCurrent(request))
                    IsLoading = false;
            }
        }

        private void ClearDisplayedReport()
        {
            Months.Clear();
            ConsumerBillingRows.Clear();
            BreakdownPeriods.Clear();
            BreakdownClients.Clear();
            FocusRows.Clear();
            UnitsChartModel = null;
            WindowLabel = string.Empty;
            TotalUnits = 0;
            TotalIncentive = null;
            HasData = false;
            ShowProductivityEmpty = false;
            HasConsumerBillingRows = false;
            ShowConsumerBillingEmpty = false;
            TotalBillableWorkUnits = 0;
            TotalNonBillableWorkUnits = 0;
            TotalLostWorkPercentageLabel = "—";
            BreakdownTotals = null;
            HasBreakdownReport = false;
            HasIncompleteClaimUnits = false;
            IncompleteClaimUnitsMessage = string.Empty;
            ComplianceWindowMessage = string.Empty;
            ConsumerBillingEmptyMessage = string.Empty;
            HasLoadedSuccessfully = false;
        }

        private void ResetConsumerChoices()
        {
            ConsumerChoices.Clear();
            ConsumerChoices.Add(new StatisticsConsumerChoice(null, "All consumers"));
            SelectedConsumerChoice = ConsumerChoices[0];
        }

        private async Task<(StatisticsBreakdownReportDto Report, int? PersonId)>
            GetValidatedBreakdownAsync(
                Task<ConsumerBillingLossReport> caseloadTask,
                DateTime windowStart,
                DateTime windowEnd,
                StatisticsPeriod period,
                int? requestedPersonId)
        {
            var caseload = await caseloadTask;
            var personId = requestedPersonId is int id &&
                           caseload.Consumers.Any(row => row.PersonId == id)
                ? id
                : (int?)null;
            var report = await _statisticsBreakdownService.GetAsync(
                windowStart, windowEnd, period, personId);
            return (report, personId);
        }

        private void RefreshFocusRows()
        {
            FocusRows.Clear();
            FocusedMetricLabel = SelectedMetricChoice?.Label ?? "Documented units";
            var metric = SelectedMetricChoice?.Key ?? "documented";
            foreach (var row in BreakdownPeriods)
            {
                var value = metric switch
                {
                    "billable-marked" => row.Metrics.BillableMarkedUnits.ToString("N0", CultureInfo.CurrentCulture),
                    "nonbillable" => row.Metrics.NonBillableUnits.ToString("N0", CultureInfo.CurrentCulture),
                    "pending" => row.Metrics.PendingUnits.ToString("N0", CultureInfo.CurrentCulture),
                    "blocked" => row.Metrics.ComplianceBlockedUnits.ToString("N0", CultureInfo.CurrentCulture),
                    "form" => row.Metrics.FormDocumentedUnits.ToString("N0", CultureInfo.CurrentCulture),
                    "visit" => row.Metrics.VisitDocumentedUnits.ToString("N0", CultureInfo.CurrentCulture),
                    "claimed" => row.Metrics.SubmittedClaimUnits.ToString("N2", CultureInfo.CurrentCulture),
                    "claimed-form" => row.Metrics.SubmittedFormUnits.ToString("N2", CultureInfo.CurrentCulture),
                    "claimed-visit" => row.Metrics.SubmittedVisitUnits.ToString("N2", CultureInfo.CurrentCulture),
                    "locked" => row.Metrics.LockedClaimUnits.ToString("N2", CultureInfo.CurrentCulture),
                    "days" => row.Metrics.DocumentedServiceDays.ToString("N0", CultureInfo.CurrentCulture),
                    "billable-days" => row.Metrics.BillableMarkedServiceDays.ToString("N0", CultureInfo.CurrentCulture),
                    "nonbillable-days" => row.Metrics.NonBillableServiceDays.ToString("N0", CultureInfo.CurrentCulture),
                    "expired" => row.Metrics.ExpiredPendingServiceDays.ToString("N0", CultureInfo.CurrentCulture),
                    "abandoned" => row.Metrics.AbandonedServiceDays.ToString("N0", CultureInfo.CurrentCulture),
                    "notes" => row.Metrics.DocumentedNoteCount.ToString("N0", CultureInfo.CurrentCulture),
                    "forms-count" => row.Metrics.FormNoteCount.ToString("N0", CultureInfo.CurrentCulture),
                    "visits-count" => row.Metrics.VisitNoteCount.ToString("N0", CultureInfo.CurrentCulture),
                    "claims-count" => row.Metrics.SubmittedClaimCount.ToString("N0", CultureInfo.CurrentCulture),
                    "locked-count" => row.Metrics.LockedClaimCount.ToString("N0", CultureInfo.CurrentCulture),
                    _ => row.Metrics.DocumentedUnits.ToString("N0", CultureInfo.CurrentCulture)
                };
                FocusRows.Add(new StatisticsFocusRow(row.Period, value));
            }
        }

        private static List<DateTime> BuildMonthWindow(DateTime start, DateTime end)
        {
            var months = new List<DateTime>();
            for (var month = new DateTime(start.Year, start.Month, 1);
                 month <= end;
                 month = month.AddMonths(1))
            {
                months.Add(month);
            }

            return months;
        }

        private static string FormatPeriodLabel(
            DateTime periodStart,
            DateTime periodEnd,
            DateTime monthStart,
            DateTime monthEnd)
        {
            if (periodStart == monthStart && periodEnd == monthEnd)
                return monthStart.ToString("MMM yyyy");
            if (periodStart == periodEnd)
                return periodStart.ToString("MMM d, yyyy");
            if (periodStart.Month == periodEnd.Month && periodStart.Year == periodEnd.Year)
                return $"{periodStart:MMM d}–{periodEnd:d, yyyy}";

            return $"{periodStart:MMM d, yyyy}–{periodEnd:MMM d, yyyy}";
        }

        private static string FormatWindowLabel(DateTime start, DateTime end)
        {
            if (start == end)
                return start.ToString("MMMM d, yyyy");
            if (start.Month == end.Month && start.Year == end.Year)
                return $"{start:MMMM d}–{end:d, yyyy}";
            if (start.Year == end.Year)
                return $"{start:MMM d}–{end:MMM d, yyyy}";

            return $"{start:MMM d, yyyy}–{end:MMM d, yyyy}";
        }

        private static PlotModel BuildUnitsChart(IReadOnlyList<StatisticsPeriodDisplayRow> periods)
        {
            var textColor = PlotTheme.Color(
                "TextPrimaryBrush", OxyColor.FromRgb(0x3D, 0x2B, 0x1F));
            var mutedColor = PlotTheme.Color(
                "TextMutedBrush", OxyColor.FromRgb(0x8A, 0x7A, 0x6A));
            var inputBorderColor = PlotTheme.Color(
                "InputBorderBrush", OxyColor.FromRgb(0xED, 0xD9, 0xC0));

            var model = new PlotModel
            {
                Background = OxyColors.Transparent,
                PlotAreaBackground = OxyColors.Transparent,
                TextColor = textColor,
                PlotMargins = new OxyThickness(170, 10, 16, 30),
            };

            // Keep the chart compact for long windows; both tables retain every period.
            var chartPeriods = periods.TakeLast(18).ToList();
            var categoryAxis = new CategoryAxis
            {
                Position = AxisPosition.Left,
                TextColor = textColor,
                TicklineColor = OxyColors.Transparent,
                MajorGridlineStyle = LineStyle.None,
                GapWidth = 0.4,
            };

            var valueAxis = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Minimum = 0,
                Title = "Documented units (Logged + Approved)",
                TitleColor = mutedColor,
                TextColor = textColor,
                TicklineColor = inputBorderColor,
                MajorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = PlotTheme.ColorWithAlpha(
                    "TextPrimaryBrush", 80, textColor),
            };

            var series = new BarSeries
            {
                StrokeColor = OxyColors.Transparent,
                BarWidth = 0.6,
            };

            foreach (var period in chartPeriods)
            {
                categoryAxis.Labels.Add(period.Period);
                series.Items.Add(new BarItem
                {
                    Value = period.Metrics.DocumentedUnits,
                    Color = PlotTheme.Color("AccentBrush", OxyColor.FromRgb(0xD4, 0xA8, 0x82)),
                });
            }

            model.Axes.Add(categoryAxis);
            model.Axes.Add(valueAxis);
            model.Series.Add(series);
            return model;
        }
    }
}
