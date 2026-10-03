using Sati.Views;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class StatisticsViewRenderTests
{
    [Fact]
    public void ReportingFiltersRenderWithAccessibleNames()
    {
        WpfUiHarness.Run(() =>
        {
            var view = new StatisticsView();
            WpfUiHarness.Realize(view, 1100, 850);

            var start = WpfUiHarness.FindByAutomationName<DatePicker>(
                view, "Statistics start date");
            var end = WpfUiHarness.FindByAutomationName<DatePicker>(
                view, "Statistics end date");
            var period = WpfUiHarness.FindByAutomationName<ComboBox>(
                view, "Group statistics by period");
            var consumer = WpfUiHarness.FindByAutomationName<ComboBox>(
                view, "Statistics consumer filter");
            var apply = WpfUiHarness.FindByAutomationName<Button>(
                view, "Apply statistics filters");

            Assert.All(new FrameworkElement[] { start, end, period, consumer, apply },
                control =>
                {
                    Assert.Equal(Visibility.Visible, control.Visibility);
                    Assert.True(control.ActualWidth > 0);
                    Assert.True(control.ActualHeight > 0);
                });
        });
    }
}
