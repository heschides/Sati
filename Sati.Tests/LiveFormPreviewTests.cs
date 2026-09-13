using Sati.ViewModels.ClientDocuments;
using Xunit;

namespace Sati.Tests;

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

        var assessment = File.ReadAllText(Path.Combine(root,
            "Views", "ClientDocuments", "ComprehensiveAssessmentWorkspace.xaml"));
        Assert.DoesNotContain("LIVE PREVIEW", assessment, StringComparison.Ordinal);
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

    private static string RepositoryRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string callerPath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerPath)!, ".."));
}
