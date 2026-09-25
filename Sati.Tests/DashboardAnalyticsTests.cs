using OxyPlot.Series;
using Sati.Contracts.V1;
using Sati.Models;
using Sati.Models.Billing;
using Sati.ViewModels.Billing;
using Sati.ViewModels.Supervisor;
using System.Xml.Linq;
using Xunit;

namespace Sati.Tests;

public sealed class DashboardAnalyticsTests
{
    [Fact]
    public void BillingAnalyticsUsesQueueClaimsAndRemittancesWithoutInventingValues()
    {
        var ready = Note.Create("Ready", new DateTime(2026, 9, 4), NoteStatus.Logged, 30, 1);
        var blocked = Note.Create("Blocked", new DateTime(2026, 9, 5), NoteStatus.Logged, 15, 1);
        var validations = new[]
        {
            new BillingValidationResult(true, ready, []),
            new BillingValidationResult(false, blocked, ["Missing payer data"])
        };
        var periodOverview = new BillingPeriodOverviewDto(
            DraftRevenue: 100m,
            Months:
            [
                new(2026, 4, 0m),
                new(2026, 5, 0m),
                new(2026, 6, 0m),
                new(2026, 7, 0m),
                new(2026, 8, 0m),
                new(2026, 9, 300m)
            ]);
        var outcomes = new[]
        {
            Outcome("Paid", 160m),
            Outcome("PartiallyPaid", 40m),
            Outcome("Denied", 0m)
        };

        var analytics = BillingOverviewViewModel.CreateAnalytics(
            new BillingConfiguration("H2014", null, 20m, "S", "Payer", "P", "C", "2075550100"),
            validations,
            periodOverview,
            outcomes);

        Assert.Equal(40m, analytics.ReadyRevenue);
        Assert.Equal(100m, analytics.DraftRevenue);
        Assert.Equal(300m, analytics.SixMonthBilledRevenue);
        Assert.Equal(200m, analytics.SixMonthPaidRevenue);
        Assert.Equal(1, analytics.ReadyClaimCount);
        Assert.Equal(1, analytics.BlockedClaimCount);
        Assert.Equal(new[] { 1, 1, 1 },
            new[] { analytics.Outcomes.Paid, analytics.Outcomes.PartiallyPaid, analytics.Outcomes.NeedsAttention });
        Assert.Equal(6, analytics.Months.Count);

        var trend = BillingOverviewViewModel.BuildRevenueTrendChart(analytics.Months);
        Assert.Equal(4, trend.Series.Count);
        Assert.All(trend.Series.Cast<DataPointSeries>(), series => Assert.Equal(6, series.Points.Count));

        var outcomeChart = BillingOverviewViewModel.BuildClaimOutcomeChart(analytics.Outcomes);
        var outcomeLayers = outcomeChart.Series.Cast<PieSeries>().ToArray();
        Assert.Equal(7, outcomeLayers.Length);
        Assert.All(outcomeLayers, layer => Assert.Equal(3, layer.Slices.Count));
        Assert.Equal(7, outcomeLayers.Select(layer => layer.Slices[0].Fill).Distinct().Count());
        Assert.True(outcomeLayers[3].Slices[0].Fill.B > outcomeLayers[3].Slices[0].Fill.G);
        Assert.Equal((byte)0xE3, outcomeLayers[3].Slices[1].Fill.R);
        Assert.Equal((byte)0xAD, outcomeLayers[3].Slices[1].Fill.G);
        Assert.Equal((byte)0xD4, outcomeLayers[3].Slices[2].Fill.R);
        Assert.Equal((byte)0x7A, outcomeLayers[3].Slices[2].Fill.G);
    }

    [Fact]
    public void SupervisorComplianceCountsClientsRatherThanProductivityThresholds()
    {
        var first = Summary(
            1,
            "Alex Morgan",
            4,
            new UpcomingEvent { PersonId = 10, Kind = UpcomingEventKind.LateReview },
            new UpcomingEvent { PersonId = 10, Kind = UpcomingEventKind.LateReview },
            new UpcomingEvent { PersonId = 11, Kind = UpcomingEventKind.LateReview });
        var second = Summary(2, "Sam Rivera", 1);

        Assert.Equal(2, first.ClientsWithOverdueItems);
        Assert.Equal(2, first.ClientsClearOfOverdueItems);
        Assert.Equal(50m, first.CompliancePercent);

        var overview = new TeamOverviewViewModel();
        overview.Refresh([first, second]);

        Assert.Equal(3, overview.ClientsClear);
        Assert.Equal(2, overview.ClientsNeedingAttention);
        Assert.Equal("60%", overview.AvgComplianceLabel);
        Assert.Equal(2, overview.ComplianceChartModel!.Series.Count);
        var complianceLayers = overview.TeamComplianceChartModel!.Series.Cast<PieSeries>().ToArray();
        Assert.Equal(7, complianceLayers.Length);
        Assert.All(complianceLayers, layer => Assert.Equal(2, layer.Slices.Count));
        Assert.Equal(7, complianceLayers.Select(layer => layer.Slices[0].Fill).Distinct().Count());
        Assert.True(complianceLayers[3].Slices[0].Fill.B > complianceLayers[3].Slices[0].Fill.G);
        Assert.Equal((byte)0xD4, complianceLayers[3].Slices[1].Fill.R);
        Assert.Equal((byte)0x7A, complianceLayers[3].Slices[1].Fill.G);
    }

    [Fact]
    public void DashboardViewsExposeChartsAndPlainLanguageMetricLabels()
    {
        var root = FindRepositoryRoot();
        var billing = XDocument.Load(Path.Combine(root, "Views", "Billing", "BillingOverviewView.xaml"));
        var supervisor = XDocument.Load(Path.Combine(root, "Views", "TeamOverviewView.xaml"));

        Assert.Equal(2, billing.Descendants().Count(element => element.Name.LocalName == "PlotView"));
        Assert.Contains(billing.Descendants(), element =>
            (string?)element.Attribute("Text") == "6-MONTH PAYMENTS");
        Assert.Equal(2, supervisor.Descendants().Count(element => element.Name.LocalName == "PlotView"));
        Assert.Contains(supervisor.Descendants(), element =>
            (string?)element.Attribute("Text") == "CURRENT COMPLIANCE");
        Assert.DoesNotContain(supervisor.ToString(), "ProgressPercent", StringComparison.Ordinal);
    }

    private static RemittanceClaimOutcomeDto Outcome(string status, decimal paidAmount) => new(
        1,
        1,
        Guid.NewGuid().ToString("N"),
        "MaineCare",
        new DateTime(2026, 9, 10),
        new DateTime(2026, 9, 9),
        status,
        200m,
        paidAmount,
        paidAmount,
        0m,
        0m,
        null,
        null,
        "EFT-1",
        false);

    private static CaseManagerSummaryViewModel Summary(
        int id,
        string name,
        int clientCount,
        params UpcomingEvent[] events)
    {
        var people = Enumerable.Range(1, clientCount)
            .Select(personId => new PersonSummary { Id = id * 100 + personId })
            .ToList();
        var summary = new CaseManagerSummaryViewModel(
            User.Create(id, $"user{id}", name, "hash", "salt", UserRole.CaseManager, null, 1),
            people,
            [],
            events.ToList());
        summary.SetThreshold(100);
        return summary;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SatiLogica.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the Sati repository root.");
    }
}
