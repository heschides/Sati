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
    public void OnlyWorkspacesWithFaithfulDocumentViewsPresentALivePreview()
    {
        var root = RepositoryRoot();
        AssertPreview(root, "Views/ATRequestView.xaml", "<views:ATFormDocument");
        AssertPreview(root, "Views/ClientDocuments/CheckRequestsWorkspace.xaml", "LIVE PREVIEW");

        AssertNoPreview(root, "Views/ClientDocuments/AgencyReleaseWorkspace.xaml", "AgencyReleaseDocument");
        AssertNoPreview(root, "Views/ClientDocuments/DhhsFormsWorkspace.xaml", "DhhsFormEntryPreview");
        AssertNoPreview(root, "Views/ClientDocuments/SafetyPlanWorkspace.xaml", "SafetyPlanDocument");
        AssertNoPreview(root, "Views/ClientDocuments/AnnualDocumentsWorkspace.xaml", "DocumentTemplatePreview");
        AssertNoPreview(root, "Views/ClientDocuments/CwicPacketWorkspace.xaml", "Live CWIC packet preview");
        AssertNoPreview(root, "Views/ClientDocuments/HousingSupportFundsWorkspace.xaml", "Live Housing Support Funds application preview");

        var assessment = File.ReadAllText(Path.Combine(root,
            "Views", "ClientDocuments", "ComprehensiveAssessmentWorkspace.xaml"));
        Assert.DoesNotContain("LIVE PREVIEW", assessment, StringComparison.Ordinal);
    }

    [Fact]
    public void EditableDocumentFieldsSendTextToTheDraftWhileTheUserIsTyping()
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
    public void RemainingFaithfulDocumentPreviewsCanBeRealizedByTheDesktop()
    {
        WpfUiHarness.Run(() =>
        {
            AssertRendersLivePreview(new ATRequestView(), "Live AT request preview");
            AssertRendersLivePreview(new CheckRequestsWorkspace(), "Live check request preview");
        });
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

    private static void AssertNoPreview(string root, string relativePath, string marker)
    {
        var content = File.ReadAllText(Path.Combine(root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        Assert.DoesNotContain(marker, content, StringComparison.Ordinal);
        Assert.DoesNotContain("LIVE PREVIEW", content, StringComparison.Ordinal);
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
