using System.Windows.Controls;
using Sati.Views;
using Sati.Views.Finance;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class RepresentativePayeeViewRenderTests
{
    [Fact]
    public void FinanceWorkspaceExposesQueueReleaseReceiptAndAppendOnlyLedgerControls()
    {
        WpfUiHarness.Run(() =>
        {
            var view = new RepresentativePayeeDashboardView();
            WpfUiHarness.Realize(view, 1200, 900);
            Assert.NotNull(WpfUiHarness.FindByAutomationName<DataGrid>(view, "Finance check request queue"));
            Assert.NotNull(WpfUiHarness.FindByAutomationName<DataGrid>(view, "Representative payee ledger"));
            Assert.NotNull(WpfUiHarness.FindByAutomationName<ComboBox>(view, "Representative payee consumer"));
            var buttons = WpfUiHarness.Descendants(view).OfType<Button>().ToList();
            Assert.Contains(buttons, button => Equals(button.Content, "Record check release"));
            Assert.Contains(buttons, button => Equals(button.Content, "Acknowledge receipt"));
            Assert.Contains(buttons, button => Equals(button.Content, "Record ledger entry"));
        });
    }

    [Fact]
    public void SupervisorWorkspaceSeparatesApprovalFromCheckRelease()
    {
        WpfUiHarness.Run(() =>
        {
            var view = new CheckRequestApprovalsView();
            WpfUiHarness.Realize(view, 1000, 750);
            Assert.NotNull(WpfUiHarness.FindByAutomationName<DataGrid>(view, "Submitted check requests"));
            var buttons = WpfUiHarness.Descendants(view).OfType<Button>().ToList();
            Assert.Contains(buttons, button => Equals(button.Content, "Approve for Finance"));
            Assert.Contains(buttons, button => Equals(button.Content, "Return to case manager"));
            Assert.DoesNotContain(buttons, button => Equals(button.Content, "Record check release"));
        });
    }

    [Fact]
    public void UserManagementCanGrantRepresentativePayeeWithoutCaseManagement()
    {
        var root = FindRepositoryRoot();
        var create = File.ReadAllText(Path.Combine(root, "Views", "NewUserWindow.xaml"));
        var edit = File.ReadAllText(Path.Combine(root, "Views", "UserManagementView.xaml"));
        Assert.Contains("Representative payee permission", create);
        Assert.Contains("HasRepresentativePayeePermissions", create);
        Assert.Contains("Representative payee permission", edit);
        Assert.Contains("HasRepresentativePayeePermissions", edit);
    }

    [Fact]
    public void CheckRequestWorkspaceExposesWeeklyDefaultAndSettingsExposeOptOut()
    {
        var root = FindRepositoryRoot();
        var workspace = File.ReadAllText(Path.Combine(
            root, "Views", "ClientDocuments", "CheckRequestsWorkspace.xaml"));
        var settings = File.ReadAllText(Path.Combine(root, "Views", "SettingsWindow.xaml"));
        var prompt = File.ReadAllText(Path.Combine(root, "Views", "CheckRequestPromptWindow.xaml"));
        var timeOffPrompt = File.ReadAllText(Path.Combine(
            root, "Views", "TimeOffCheckRequestPromptWindow.xaml"));

        Assert.Contains("Weekly auto-generation default", workspace);
        Assert.Contains("Save weekly default", workspace);
        Assert.Contains("EnableWeeklyCheckRequestAutomation", settings);
        Assert.Contains("Review now", prompt);
        Assert.Contains("Defer", prompt);
        Assert.Contains("Prepare now", timeOffPrompt);
        Assert.Contains("Later", timeOffPrompt);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SatiLogica.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
