using OxyPlot.Series;
using Sati.Models;
using Sati.ViewModels.Billing;
using Sati.ViewModels.Supervisor;
using Sati.Views;
using Sati.Views.Billing;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class DashboardVisualRenderTests
{
    [Fact]
    public void BillingAndSupervisorChartsRenderAtDashboardSize()
    {
        WpfUiHarness.Run(() =>
        {
            var months = Enumerable.Range(0, 6)
                .Select(index => new BillingOverviewViewModel.BillingMonthPoint(
                    new DateTime(2026, 4, 1).AddMonths(index),
                    8400m + index * 1750m,
                    7100m + index * 1600m))
                .ToList();
            var billingModel = new BillingOverviewViewModel(null!, null!)
            {
                ReadyRevenueLabel = "$12,480",
                DraftRevenueLabel = "$18,920",
                BilledRevenueLabel = "$76,350",
                PaidRevenueLabel = "$68,740",
                ReadinessDetail = "42 ready · 3 need attention",
                PaidOutcomeLabel = "86 paid",
                PartialOutcomeLabel = "7 partially paid",
                AttentionOutcomeLabel = "4 need attention",
                PaidClaimPercentLabel = "89%",
                HasRevenueData = true,
                HasRemittanceData = true,
                RevenueTrendChartModel = BillingOverviewViewModel.BuildRevenueTrendChart(months),
                ClaimOutcomeChartModel = BillingOverviewViewModel.BuildClaimOutcomeChart(
                    new BillingOverviewViewModel.BillingOutcomeCounts(86, 7, 4))
            };
            var billing = new BillingOverviewView { DataContext = billingModel };
            WpfUiHarness.Realize(billing, 1180, 900);
            var billingScroll = WpfUiHarness.Descendants(billing).OfType<System.Windows.Controls.ScrollViewer>().First();
            Assert.Equal(0, billingScroll.VerticalOffset);
            Assert.Equal(0, billingScroll.HorizontalOffset);
            Assert.Equal(2, WpfUiHarness.Descendants(billing).OfType<OxyPlot.Wpf.PlotView>().Count());
            SavePreview(billing, "billing-dashboard.png");

            var supervisorModel = new TeamOverviewViewModel();
            supervisorModel.Refresh([
                Summary(1, "Alex Morgan", 18, 0),
                Summary(2, "Sam Rivera", 16, 2),
                Summary(3, "Jordan Lee", 14, 1),
                Summary(4, "Taylor Brooks", 12, 3)
            ]);
            var supervisor = new TeamOverviewView { DataContext = supervisorModel };
            WpfUiHarness.Realize(supervisor, 1180, 900);
            Assert.Equal(2, WpfUiHarness.Descendants(supervisor).OfType<OxyPlot.Wpf.PlotView>().Count());
            Assert.All(
                WpfUiHarness.Descendants(supervisor).OfType<OxyPlot.Wpf.PlotView>(),
                plot => Assert.True(plot.ActualWidth > 250 && plot.ActualHeight > 180));
            SavePreview(supervisor, "supervisor-dashboard.png");
        });
    }

    private static CaseManagerSummaryViewModel Summary(
        int id,
        string name,
        int clientCount,
        int clientsWithOverdueItems)
    {
        var people = Enumerable.Range(1, clientCount)
            .Select(personId => new PersonSummary { Id = id * 100 + personId })
            .ToList();
        var events = Enumerable.Range(1, clientsWithOverdueItems)
            .Select(personId => new UpcomingEvent
            {
                PersonId = id * 100 + personId,
                Kind = UpcomingEventKind.LateReview
            })
            .ToList();
        return new CaseManagerSummaryViewModel(
            User.Create(id, $"user{id}", name, "hash", "salt", UserRole.CaseManager, null, 1),
            people,
            [],
            events);
    }

    private static void SavePreview(FrameworkElement view, string fileName)
    {
        if (Environment.GetEnvironmentVariable("SATI_DASHBOARD_QA_OUTPUT") is not { Length: > 0 } directory)
            return;

        const int scale = 2;
        var image = new RenderTargetBitmap(
            (int)view.ActualWidth * scale,
            (int)view.ActualHeight * scale,
            96 * scale,
            96 * scale,
            PixelFormats.Pbgra32);
        image.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, fileName));
        encoder.Save(output);
    }
}
