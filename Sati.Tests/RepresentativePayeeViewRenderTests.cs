using System.Collections;
using System.ComponentModel;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Views;
using Sati.Views.Finance;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class RepresentativePayeeViewRenderTests
{
    [Fact]
    public void ClientEditorRepPayeeChoiceUpdatesOncePerCheckedButton()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<ISessionService, SessionService>();
                services.AddSingleton<IComprehensiveAssessmentService, StabilizationTests.SmokeAssessmentService>();
                services.AddSingleton<IPersonCenteredPlanSourceService, StabilizationTests.SmokePlanSourceService>();
                services.AddSingleton<IConsumerProviderService, StabilizationTests.SmokeConsumerProviderService>();
                services.AddSingleton<IProviderService, StabilizationTests.SmokeProviderService>();
            })
            .Build();

        WpfUiHarness.RunWithHost(host, () =>
        {
            var profile = new RepPayeeEditorHost();
            var view = new ClientsView { DataContext = profile };
            WpfUiHarness.Realize(view, 1400, 900);

            var yes = WpfUiHarness.FindByAutomationName<RadioButton>(
                view,
                "Case manager is representative payee, yes");
            var no = WpfUiHarness.FindByAutomationName<RadioButton>(
                view,
                "Case manager is representative payee, no");

            Assert.False(profile.CaseManagerIsRepPayee);
            Assert.False(yes.IsChecked);
            Assert.True(no.IsChecked);

            yes.IsChecked = true;
            view.UpdateLayout();
            Assert.True(profile.CaseManagerIsRepPayee);
            Assert.True(yes.IsChecked);
            Assert.False(no.IsChecked);

            no.IsChecked = true;
            view.UpdateLayout();
            Assert.False(profile.CaseManagerIsRepPayee);
            Assert.False(yes.IsChecked);
            Assert.True(no.IsChecked);
            Assert.Equal(2, profile.SourceUpdates);
        });
    }

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

    private sealed class RepPayeeEditorHost : INotifyPropertyChanged, INotifyDataErrorInfo
    {
        private bool caseManagerIsRepPayee;

        public bool ShowClientWorkspace => true;
        public bool IsClientEditorOpen => true;
        public int ClientWorkspaceTabIndex { get; set; }
        public int CompliancePresentationRevision => 0;
        public BillingComplianceRequirements BillingComplianceRequirements =>
            BillingComplianceGate.DefaultRequirements;
        public int PcpOpenDaysBefore => 90;
        public int SourceUpdates { get; private set; }
        public string RepPayeeMonthlyIncomeText { get; set; } = "";
        public string RepPayeeRegularCheckRequestNeeds { get; set; } = "";

        public bool CaseManagerIsRepPayee
        {
            get => caseManagerIsRepPayee;
            set
            {
                if (caseManagerIsRepPayee == value)
                    return;

                caseManagerIsRepPayee = value;
                SourceUpdates++;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CaseManagerIsRepPayee)));
                ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(RepPayeeMonthlyIncomeText)));
                ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(RepPayeeRegularCheckRequestNeeds)));
            }
        }

        public bool HasErrors => CaseManagerIsRepPayee;
        public IEnumerable GetErrors(string? propertyName) =>
            CaseManagerIsRepPayee && propertyName is nameof(RepPayeeMonthlyIncomeText) or nameof(RepPayeeRegularCheckRequestNeeds)
                ? new[] { "Required for this test." }
                : Array.Empty<string>();

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
    }
}
