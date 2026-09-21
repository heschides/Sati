using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sati.Data;
using Sati.ViewModels.ClientDocuments;
using Sati.Views.ClientDocuments;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class AnnualDocumentViewRenderTests
{
    [Fact]
    public void SafetyPlanCommandsAndAccessibleCycleReachTheViewModel()
    {
        WpfUiHarness.Run(() =>
        {
            var model = new SafetyPlanViewModel(null!, new SessionService());
            var view = new SafetyPlanWorkspace { DataContext = model };
            WpfUiHarness.Realize(view, 900, 1200);
            var buttons = WpfUiHarness.Descendants(view).OfType<Button>().ToList();
            var submit = buttons.Single(x => Equals(x.Content, "Submit for review"));
            Assert.Same(model.SubmitCommand, submit.Command); Assert.False(submit.IsEnabled);
            Assert.Same(model.ApproveCommand, buttons.Single(x => Equals(x.Content, "Approve submitted plan")).Command);
            Assert.NotNull(WpfUiHarness.FindByAutomationName<DatePicker>(view, "Safety plan annual period beginning"));
            var openPeriod = buttons.Single(x => Equals(x.Content, "View selected year"));
            Assert.Same(model.ReloadCommand, openPeriod.Command);
            Assert.Equal("Loads the saved safety plan for this annual period without changing it.", openPeriod.ToolTip);
            Assert.DoesNotContain(
                WpfUiHarness.Descendants(view).OfType<FrameworkElement>(),
                element => AutomationProperties.GetName(element) == "Live safety plan preview");
            SavePreview(view, "safety-workspace.png");
        });
    }

    [Fact]
    public void PacketAndReceiptCommandsBindAndRemainDisabledWithoutAConsumer()
    {
        WpfUiHarness.Run(() =>
        {
            var model = new AnnualDocumentsViewModel(null!, null!, null!, new SessionService());
            model.CycleStart = new DateTime(2026, 8, 11);
            var view = new AnnualDocumentsWorkspace
            {
                DataContext = new AnnualFormsHost(model)
            };
            WpfUiHarness.Realize(view, 900, 1200);
            var buttons = WpfUiHarness.Descendants(view).OfType<Button>().ToList();
            var save = buttons.Single(x => Equals(x.Content, "Save Annual Forms Package"));
            Assert.Same(model.SavePacketCommand, save.Command); Assert.False(save.IsEnabled);
            var openPeriod = buttons.Single(x => Equals(x.Content, "View selected year"));
            Assert.Same(model.ReloadCommand, openPeriod.Command);
            Assert.Equal("Loads saved form, document, and signature status for the selected service year without changing any record.", openPeriod.ToolTip);
            Assert.NotNull(WpfUiHarness.FindByAutomationName<DatePicker>(view, "Annual forms service year beginning"));
            Assert.NotNull(WpfUiHarness.FindByAutomationName<FrameworkElement>(view, "Annual form workflow by type"));
            Assert.Contains(buttons, button => Equals(button.Content, "Submit to consumer or guardian for review"));
            var directionLabels = WpfUiHarness.Descendants(view).OfType<TextBlock>()
                .Where(text => Equals(text.Text, "NEXT STEP"))
                .ToList();
            Assert.Equal(5, directionLabels.Count);
            Assert.All(directionLabels, label =>
            {
                Assert.Equal(11, label.FontSize);
                Assert.Equal(FontWeights.Bold, label.FontWeight);
            });
            Assert.NotNull(WpfUiHarness.FindByAutomationName<FrameworkElement>(view, "Annual Forms workflow directions"));
            SavePreview(view, "annual-overview.png");

            var sections = WpfUiHarness.FindByAutomationName<TabControl>(view, "Annual forms sections");
            Assert.NotNull(sections);
            sections!.SelectedIndex = (int)AnnualFormsSection.PrivacyPractices;
            view.UpdateLayout();
            buttons = WpfUiHarness.Descendants(view).OfType<Button>().ToList();
            var receipt = buttons.Single(x => Equals(x.Content, "Record receipt or effort"));
            Assert.Same(model.AcknowledgeCommand, receipt.Command); Assert.False(receipt.IsEnabled);
            Assert.NotNull(WpfUiHarness.FindByAutomationName<DatePicker>(view, "Privacy notice received on"));
            var templateEditor = WpfUiHarness.Descendants(view).OfType<Expander>()
                .Single(x => Equals(x.Header, "Agency privacy template (administrators)"));
            templateEditor.IsExpanded = true;
            view.UpdateLayout();
            Assert.DoesNotContain(
                WpfUiHarness.Descendants(view).OfType<FrameworkElement>(),
                element => AutomationProperties.GetName(element) == "Live privacy template preview");
            SavePreview(view, "annual-privacy-practices.png");
        });
    }

    private sealed class AnnualFormsHost(AnnualDocumentsViewModel annualDocuments)
    {
        public AnnualDocumentsViewModel AnnualDocuments { get; } = annualDocuments;
        public int AnnualFormsTabIndex { get; set; }
    }

    private static void SavePreview(FrameworkElement view, string fileName)
    {
        if (Environment.GetEnvironmentVariable("SATI_DOCUMENT_QA_OUTPUT") is not { Length: > 0 } directory) return;
        var image = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, fileName)); encoder.Save(output);
    }
}
