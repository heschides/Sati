using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.ClientDocuments;
using Sati.Views.ClientDocuments;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class SafetyDeviceWorkspaceRenderTests
{
    [Fact]
    public void Device_step_shows_one_editor_and_keeps_progress_controls_available()
    {
        WpfUiHarness.Run(() =>
        {
            var model = new SafetyDeviceViewModel(
                new UnusedSafetyDeviceService(), new TestFormWizardProgressService());
            var person = Person.Rehydrate(501, 1);
            person.FirstName = "Test";
            person.LastName = "Consumer";
            model.SetPerson(person);

            var view = new SafetyDeviceWorkspace { DataContext = model };
            WpfUiHarness.Realize(view, 1200, 900);
            var next = WpfUiHarness.FindByAutomationName<Button>(
                view, "Next safety device form step");
            Assert.Same(model.NextCommand, next.Command);
            Assert.NotNull(WpfUiHarness.FindByAutomationName<Button>(
                view, "Save this form's answers and current step"));
            SavePreviewIfRequested(view, "member");

            model.ShowDeviceStepCommand.Execute(null);
            WpfUiHarness.Realize(view, 1200, 900);
            var devices = WpfUiHarness.FindByAutomationName<ListBox>(
                view, "Safety device recommendations");
            Assert.Single(devices.Items);
            var name = WpfUiHarness.FindByAutomationName<TextBox>(
                view, "Safety device name and type");
            name.Text = "Door alarm";
            Assert.Equal("Door alarm", model.Devices[0].NameAndType);

            model.AddDeviceCommand.Execute(null);
            WpfUiHarness.Realize(view, 1200, 900);
            Assert.Equal(2, devices.Items.Count);
            Assert.Equal(2, model.CurrentDevice.Number);
            Assert.Equal(string.Empty, name.Text);
            model.SelectedDevice = model.Devices[0];
            WpfUiHarness.Realize(view, 1200, 900);
            SavePreviewIfRequested(view, "devices");

            model.ShowPlanningStepCommand.Execute(null);
            WpfUiHarness.Realize(view, 1200, 900);
            model.ShowReviewStepCommand.Execute(null);
            WpfUiHarness.Realize(view, 1200, 900);
            Assert.NotNull(WpfUiHarness.FindByAutomationName<Button>(
                view, "Generate editable OADS Safety Device Request draft PDF"));
            SavePreviewIfRequested(view, "review");
        });
    }

    private static void SavePreviewIfRequested(SafetyDeviceWorkspace view, string step)
    {
        var path = Environment.GetEnvironmentVariable("SATI_SAFETY_DEVICE_QA_OUTPUT");
        if (string.IsNullOrWhiteSpace(path)) return;
        var outputPath = Path.Combine(Path.GetDirectoryName(path)!,
            $"{Path.GetFileNameWithoutExtension(path)}-{step}{Path.GetExtension(path)}");
        var image = new RenderTargetBitmap(1200, 900, 96, 96, PixelFormats.Pbgra32);
        image.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var output = File.Create(outputPath);
        encoder.Save(output);
    }

    private sealed class UnusedSafetyDeviceService : ISafetyDeviceService
    {
        public Task<SafetyDeviceResult> GenerateAsync(int personId, SafetyDeviceRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
