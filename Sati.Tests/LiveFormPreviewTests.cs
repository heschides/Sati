using System.Windows;
using System.Windows.Controls;
using Sati.ViewModels.ClientDocuments;
using Sati.Views;
using Sati.Views.ClientDocuments;
using System.Text.RegularExpressions;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class LiveFormPreviewTests
{
    [Fact]
    public void EveryExistingEditableDocumentWorkspaceCarriesALivePreview()
    {
        var root = RepositoryRoot();
        AssertPreview(root, "Views/ATRequestView.xaml", "<views:ATFormDocument");
        AssertPreview(root, "Views/ClientDocuments/CheckRequestsWorkspace.xaml", "LIVE PREVIEW");
        AssertPreview(root, "Views/ClientDocuments/AgencyReleaseWorkspace.xaml", "AgencyReleaseDocument");
        AssertPreview(root, "Views/ClientDocuments/DhhsFormsWorkspace.xaml", "DhhsFormEntryPreview");
        AssertPreview(root, "Views/ClientDocuments/SafetyPlanWorkspace.xaml", "SafetyPlanDocument");
        AssertPreview(root, "Views/ClientDocuments/AnnualDocumentsWorkspace.xaml", "DocumentTemplatePreview");
        AssertPreview(root, "Views/ClientDocuments/CwicPacketWorkspace.xaml", "Live CWIC packet preview");
        AssertPreview(root, "Views/ClientDocuments/HousingSupportFundsWorkspace.xaml", "Live Housing Support Funds application preview");

        var assessment = File.ReadAllText(Path.Combine(root,
            "Views", "ClientDocuments", "ComprehensiveAssessmentWorkspace.xaml"));
        Assert.DoesNotContain("LIVE PREVIEW", assessment, StringComparison.Ordinal);
    }

    [Fact]
    public void EditablePreviewFieldsSendTextToTheDraftWhileTheUserIsTyping()
    {
        var root = RepositoryRoot();
        var workspaces = new[]
        {
            "Views/ATRequestView.xaml",
            "Views/ClientDocuments/CheckRequestsWorkspace.xaml",
            "Views/ClientDocuments/AgencyReleaseWorkspace.xaml",
            "Views/ClientDocuments/DhhsFormsWorkspace.xaml",
            "Views/ClientDocuments/SafetyPlanWorkspace.xaml",
            "Views/ClientDocuments/AnnualDocumentsWorkspace.xaml",
            "Views/ClientDocuments/CwicPacketWorkspace.xaml",
            "Views/ClientDocuments/HousingSupportFundsWorkspace.xaml"
        };

        foreach (var workspace in workspaces)
        {
            var content = File.ReadAllText(Path.Combine(root,
                workspace.Replace('/', Path.DirectorySeparatorChar)));
            var boundTextBoxes = Regex.Matches(content, @"<TextBox\b[^>]*Text=""\{Binding[^>]*>",
                RegexOptions.Singleline);

            Assert.NotEmpty(boundTextBoxes);
            foreach (Match textBox in boundTextBoxes)
            {
                Assert.Contains("UpdateSourceTrigger=PropertyChanged", textBox.Value,
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void EveryEditablePreviewCanBeRealizedByTheDesktop()
    {
        WpfUiHarness.Run(() =>
        {
            AssertRendersLivePreview(new ATRequestView(), "Live AT request preview");
            AssertRendersLivePreview(new CheckRequestsWorkspace(), "Live check request preview");
            AssertRendersLivePreview(new AgencyReleaseWorkspace(), "Live release document preview");
            AssertRendersLivePreview(new DhhsFormsWorkspace(), "Live DHHS form entry preview");
            AssertRendersLivePreview(new SafetyPlanWorkspace(), "Live safety plan preview");
            AssertRendersLivePreview(new CwicPacketWorkspace(), "Live CWIC packet preview");
            AssertRendersLivePreview(new HousingSupportFundsWorkspace(), "Live Housing Support Funds application preview");

            var annual = new AnnualDocumentsWorkspace();
            WpfUiHarness.Realize(annual, 1200, 900);
            var templateEditor = WpfUiHarness.Descendants(annual).OfType<Expander>()
                .Single(x => Equals(x.Header, "Agency privacy template (administrators)"));
            templateEditor.IsExpanded = true;
            annual.UpdateLayout();
            Assert.NotNull(WpfUiHarness.FindByAutomationName<FrameworkElement>(
                annual, "Live privacy template preview"));
        });
    }

    [Fact]
    public void SafetyPreviewUsesAnHonestPlaceholderUntilTextIsEntered()
    {
        var section = new SafetyPlanSectionViewModel("warning-signs", "");

        Assert.Equal("[Not yet completed]", section.PreviewText);

        section.Text = "The person asks for quiet space and calls a trusted supporter.";

        Assert.Equal("The person asks for quiet space and calls a trusted supporter.", section.PreviewText);
    }

    [Fact]
    public void AdministratorTemplateValidationChangesWhileTyping()
    {
        var model = new AnnualDocumentsViewModel(null!, null!, null!, new Sati.Data.SessionService());

        model.TemplateBody = "# Notice\nPrepared for {{consumer.full_name}}";
        Assert.Equal("Template is valid and ready to publish as a new version.", model.TemplateValidationMessage);

        model.TemplateBody = "# Notice\nPrepared for {{consumer.unknown}}";
        Assert.Contains("Unknown template token", model.TemplateValidationMessage, StringComparison.Ordinal);
    }

    private static void AssertPreview(string root, string relativePath, string marker)
    {
        var content = File.ReadAllText(Path.Combine(root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains(marker, content, StringComparison.Ordinal);
    }

    private static void AssertRendersLivePreview(FrameworkElement workspace, string automationName)
    {
        WpfUiHarness.Realize(workspace, 1200, 900);
        Assert.NotNull(WpfUiHarness.FindByAutomationName<FrameworkElement>(workspace, automationName));
    }

    private static string RepositoryRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string callerPath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerPath)!, ".."));
}
