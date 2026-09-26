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
            var save = buttons.Single(x => Equals(x.Content, "Download this year's packet"));
            Assert.Same(model.SavePacketCommand, save.Command); Assert.False(save.IsEnabled);
            Assert.Same(model.PreviousYearCommand,
                WpfUiHarness.FindByAutomationName<Button>(view, "Previous plan year")!.Command);
            Assert.Same(model.NextYearCommand,
                WpfUiHarness.FindByAutomationName<Button>(view, "Next plan year")!.Command);
            // The view switch is one radio group; its three choices are named for assistive technology.
            var segments = new[] { "List view", "Timeline view", "By purpose view" }
                .Select(name => WpfUiHarness.FindByAutomationName<RadioButton>(view, name))
                .ToList();
            Assert.All(segments, Assert.NotNull);
            Assert.Single(segments.Select(segment => segment!.GroupName).Distinct());
            Assert.True(segments[0]!.IsChecked);
            // The date picker, "View selected year", and NEXT STEP boxes are gone.
            Assert.Empty(WpfUiHarness.Descendants(view).OfType<DatePicker>()
                .Where(picker => AutomationProperties.GetName(picker) == "Annual forms service year beginning"));
            Assert.DoesNotContain(buttons, x => Equals(x.Content, "View selected year"));
            Assert.DoesNotContain(WpfUiHarness.Descendants(view).OfType<TextBlock>(), text => Equals(text.Text, "NEXT STEP"));
            // Document records, verification, and signatures remain, collapsed.
            var records = WpfUiHarness.FindByAutomationName<Expander>(view, "Document records, verification, and signatures");
            Assert.NotNull(records);
            Assert.False(records!.IsExpanded);
            records.IsExpanded = true;
            view.UpdateLayout();
            buttons = WpfUiHarness.Descendants(view).OfType<Button>().ToList();
            Assert.Contains(buttons, button => Equals(button.Content, "Submit to consumer or guardian for review"));
            Assert.Same(model.VerifyCommand, buttons.Single(x => Equals(x.Content, "Choose file and verify")).Command);
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

    [Fact]
    public void AllThreeViewsRenderAPopulatedPlanYear()
    {
        WpfUiHarness.Run(() =>
        {
            var effective = DateTime.Today.AddYears(-1).AddDays(-45);
            var target = effective.AddYears(1);
            var person = Sati.Person.CreatePerson(12, "Synthetic", "Person", "", DateTime.Today.AddYears(-30),
                effective, Sati.WaiverType.Section21, new Sati.Models.Settings());
            var settings = new Sati.Models.Settings();
            // Last year was finished on time; this year the plan and assessment are done.
            foreach (var start in new[] { effective, target })
            {
                person.Forms.RemoveAll(form => form.TargetEffectiveDate == start);
                foreach (var type in Sati.Contracts.V1.PersonSaveRules.FormTypes.Select(Enum.Parse<Sati.FormType>))
                {
                    var form = new Sati.Models.Form(type, Sati.Data.FormDueDateCalculator.Compute(type, start, settings),
                        completedOn: null, targetEffectiveDate: start);
                    var due = form.DueDate.Date;
                    var finished = start == effective ||
                                   type is Sati.FormType.PCP or Sati.FormType.ComprehensiveAssessment or Sati.FormType.Reclassification;
                    if (finished && due <= DateTime.Today)
                        form.Attest(Sati.Models.FormAttestation.Attested(due, Sati.Contracts.V1.AttestationActorKind.CaseManager, 12, DateTime.UtcNow));
                    person.Forms.Add(form);
                }
            }
            var dhhsKey = $"release:v1:{target:yyyy-MM-dd}:dhhs:annual";
            var agencyKey = $"release:v1:{target:yyyy-MM-dd}:agency:annual:link-1";
            person.ReleaseComplianceSnapshots =
            [
                new(dhhsKey, Sati.Contracts.V1.ReleaseObligationCategory.Dhhs, target, target.AddDays(-90), null,
                    [new(dhhsKey, target, DateTime.UtcNow)], Guid.NewGuid(), target, target.AddDays(-90)),
                new(agencyKey, Sati.Contracts.V1.ReleaseObligationCategory.Agency, target, target.AddDays(-90), null,
                    [], Guid.NewGuid(), target, target.AddDays(-90), "Wakanda Outreach and Mobility")
            ];

            var model = new AnnualDocumentsViewModel(new PopulatedAnnualService(), null!, new PopulatedSettings(), new SessionService());
            model.SetPerson(person);
            Assert.True(model.HasPlanYears);
            Assert.Equal(target, model.CycleStart);
            Assert.NotEmpty(model.ListSections);

            var view = new AnnualDocumentsWorkspace { DataContext = new AnnualFormsHost(model) };
            foreach (var (choice, file) in new[]
                     {
                         (Sati.Services.AnnualFormsView.List, "annual-view-list.png"),
                         (Sati.Services.AnnualFormsView.Timeline, "annual-view-timeline.png"),
                         (Sati.Services.AnnualFormsView.ByPurpose, "annual-view-by-purpose.png")
                     })
            {
                model.View = choice;
                WpfUiHarness.Realize(view, 1280, 1700);
                SavePreview(view, file);
            }

            model.View = Sati.Services.AnnualFormsView.Timeline;
            WpfUiHarness.Realize(view, 1280, 1700);
            Assert.NotNull(WpfUiHarness.FindByAutomationName<FrameworkElement>(view, "Dated items"));
            Assert.Contains(WpfUiHarness.Descendants(view).OfType<Button>(),
                button => Equals(button.Content, "Open release") && button.Visibility == Visibility.Visible);
        });
    }

    private sealed class PopulatedSettings : ISettingsService
    {
        public Task<Sati.Models.Settings> LoadAsync() => Task.FromResult(new Sati.Models.Settings());
        public Task SaveAsync(Sati.Models.Settings settings) => throw new NotSupportedException();
    }

    private sealed class PopulatedAnnualService : IAnnualDocumentService
    {
        public Task<Sati.Contracts.V1.AnnualDocumentsStatusDto> GetStatusAsync(int id, DateTime cycle)
        {
            var draft = new Sati.Contracts.V1.DocumentArtifactDto(1, id, 1, "SafetyPlan", cycle, "Draft",
                DateTime.UtcNow.AddDays(-6), 12, null, null, null, [], null);
            var notice = new Sati.Contracts.V1.DocumentArtifactDto(2, id, 1, "PrivacyPractices", cycle, "GeneratedInSati",
                DateTime.UtcNow.AddDays(-6), 12, null, null, null, [], null);
            return Task.FromResult(new Sati.Contracts.V1.AnnualDocumentsStatusDto(
                new(cycle, cycle.AddDays(-30), cycle.AddYears(1).AddDays(-1), true), [draft, notice], [], "", false));
        }
        public Task<Sati.Contracts.V1.DocumentAcknowledgmentDto> AcknowledgeAsync(int id, Sati.Contracts.V1.AcknowledgeDocumentRequest request) => throw new NotSupportedException();
        public Task<Sati.Contracts.V1.DocumentArtifactDto> RecordAuthorizedRepresentativeOnFileAsync(int id, DateTime cycle, string note) => throw new NotSupportedException();
        public Task<Sati.Contracts.V1.VerifyDocumentResult> VerifyAsync(int id, Sati.Contracts.V1.VerifyDocumentRequest request) => throw new NotSupportedException();
        public Task<Sati.Contracts.V1.AgencyReleaseResult> SavePacketAsync(int id, DateTime cycle) => throw new NotSupportedException();
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
