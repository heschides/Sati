using Sati.Services;
using Sati.Views;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class ReleaseReadinessViewTests
{
    [Theory]
    [InlineData(820, 1.0, false)]
    [InlineData(620, 1.3, false)]
    [InlineData(620, 1.3, true)]
    public void RealThermometersBindRenderWrapAndExposePercentagesWithoutDependingOnColor(double width, double scale, bool highContrast)
    {
        WpfUiHarness.Run(() =>
        {
            var report = ReleaseReadiness.Parse(ReleaseReadinessTests.Ledger("tested").ToJsonString(), "1.3.37");
            var panel = new ReleaseReadinessPanel { DataContext = report, Background = Brushes.White, LayoutTransform = new ScaleTransform(scale, scale) };
            if (highContrast) ApplyHighContrastPalette(panel);
            var scroller = new ScrollViewer
            {
                Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = panel.Background
            };
            WpfUiHarness.Realize(scroller, width, 800);
            var meters = WpfUiHarness.Descendants(panel).OfType<ProgressBar>().ToArray();
            Assert.Equal(3, meters.Length);
            foreach (var meter in meters)
            {
                Assert.Equal(50, meter.Value);
                Assert.Equal(Orientation.Vertical, meter.Orientation);
                Assert.Equal(100, meter.Maximum);
                Assert.NotNull(BindingOperations.GetBindingExpression(meter, ProgressBar.ValueProperty));
                Assert.Equal(AutomationProperties.GetName(meter), meter.GetValue(AutomationProperties.NameProperty));
                Assert.Contains("50%", AutomationProperties.GetName(meter));
                Assert.Contains("no earlier assessment", AutomationProperties.GetName(meter));
                Assert.Equal(DependencyProperty.UnsetValue, meter.ReadLocalValue(Control.ForegroundProperty));
                Assert.True(meter.ActualHeight > 0);
                var track = (FrameworkElement)meter.Template.FindName("PART_Track", meter);
                var indicator = (FrameworkElement)meter.Template.FindName("PART_Indicator", meter);
                Assert.InRange(indicator.ActualWidth / track.ActualWidth, 0.49, 0.51);
            }
            Assert.Equal(report.Status, WpfUiHarness.FindByAutomationName<TextBlock>(panel, "Readiness status").Text);
            Assert.Equal(report.Summary, WpfUiHarness.FindByAutomationName<TextBlock>(panel, "Current readiness explanation").Text);
            Assert.Equal(report.NextSteps.Count, WpfUiHarness.FindByAutomationName<ItemsControl>(panel, "Future release priorities").Items.Count);
            Assert.Equal(report.KnownBlockers.Count, WpfUiHarness.FindByAutomationName<ItemsControl>(panel, "Known problems stopping launch").Items.Count);
            Assert.Equal(report.Blockers.Count, ((ItemsControl)WpfUiHarness.FindByAutomationName<Expander>(panel, "All open launch checks").Content).Items.Count);
            Assert.True(scroller.ExtentWidth <= scroller.ViewportWidth + 1);
            Assert.All(WpfUiHarness.Descendants(panel).OfType<TextBlock>().Where(t => t.IsVisible), text =>
            {
                var bounds = text.TransformToAncestor(scroller).TransformBounds(new Rect(text.RenderSize));
                Assert.True(bounds.Left >= -1 && bounds.Right <= scroller.ActualWidth + 1,
                    $"Readiness text extends beyond the visible width: {text.Text}");
            });
            Assert.All(WpfUiHarness.Descendants(panel).OfType<TextBlock>().Where(t => BindingOperations.GetBindingExpression(t, TextBlock.TextProperty) is not null),
                text => Assert.Equal(TextWrapping.Wrap, text.TextWrapping));
            foreach (var expander in WpfUiHarness.Descendants(panel).OfType<Expander>())
            {
                Assert.True(expander.Focusable);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(expander)));
            }
            if (highContrast)
            {
                Assert.All(meters, meter => Assert.Equal(Colors.Yellow, ((SolidColorBrush)meter.Foreground).Color));
                Assert.All(meters, meter => Assert.Equal(Colors.Black, ((SolidColorBrush)meter.Background).Color));
            }
            Capture(scroller, highContrast ? "readiness-high-contrast.png" : scale > 1 ? "readiness-easy-eyes.png" : "readiness-default.png");
        });
    }

    [Fact]
    public void UnavailableAssessmentRendersAVisibleExplanationWithoutStaleProgress()
    {
        WpfUiHarness.Run(() =>
        {
            var panel = new ReleaseReadinessPanel { DataContext = ReleaseReadiness.Parse(null, "1.3.37") };
            WpfUiHarness.Realize(panel, 700, 1400);
            Assert.Equal("Readiness unavailable", WpfUiHarness.FindByAutomationName<TextBlock>(panel, "Readiness status").Text);
            var meters = WpfUiHarness.Descendants(panel).OfType<ProgressBar>().ToArray();
            Assert.Equal(3, meters.Length);
            Assert.All(meters, meter => { Assert.Equal(0, meter.Value); Assert.False(meter.IsEnabled); Assert.Contains("Unavailable", AutomationProperties.GetName(meter)); });
            Assert.Single(((ItemsControl)WpfUiHarness.FindByAutomationName<Expander>(panel, "All open launch checks").Content).Items);
        });
    }

    [Fact]
    public void TheActualReleaseBaselineRendersCurrentStateAndFuturePriorities()
    {
        WpfUiHarness.Run(() =>
        {
            var report = ReleaseReadiness.LoadInstalled();
            var panel = new ReleaseReadinessPanel { DataContext = report };
            var scroller = new ScrollViewer { Content = panel, Background = Brushes.White, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            WpfUiHarness.Realize(scroller, 820, 1050);
            Assert.True(report.IsAvailable);
            Assert.Equal(report.Scores.Select(s => s.Percentage), WpfUiHarness.Descendants(panel).OfType<ProgressBar>().Select(m => m.Value));
            Assert.Equal(report.Blockers.Count, ((ItemsControl)WpfUiHarness.FindByAutomationName<Expander>(panel, "All open launch checks").Content).Items.Count);
            Capture(scroller, "readiness-actual-baseline.png");
        });
    }

    private static void ApplyHighContrastPalette(ReleaseReadinessPanel panel)
    {
        panel.Background = Brushes.Black;
        panel.Resources[SystemColors.WindowBrushKey] = Brushes.Black;
        panel.Resources[SystemColors.WindowTextBrushKey] = Brushes.White;
        panel.Resources[SystemColors.HighlightBrushKey] = Brushes.Yellow;
        panel.Resources["TextPrimaryBrush"] = Brushes.White;
        panel.Resources["TextMutedBrush"] = Brushes.White;
        panel.Resources["BorderBrush"] = Brushes.White;
    }

    private static void Capture(FrameworkElement view, string name)
    {
        if (Environment.GetEnvironmentVariable("SATI_READINESS_QA_OUTPUT") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        var image = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(directory, name)); encoder.Save(file);
    }
}
