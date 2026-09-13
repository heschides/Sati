using System.Net;
using System.Text;
using Sati.Data;
using Sati.Data.Cloud;
using Sati.Models;
using Sati.Services;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class SettingsAccessTests
{
    [Theory]
    [InlineData(UserPermissions.None, false)]
    [InlineData(UserPermissions.CaseManagement, false)]
    [InlineData(UserPermissions.Supervision, false)]
    [InlineData(UserPermissions.Administration, true)]
    [InlineData(UserPermissions.Billing, false)]
    public void OnlyAgencyAdministrationPermissionCanManageOperationalSettings(
        UserPermissions permissions,
        bool expected) =>
        Assert.Equal(expected, SettingsAccessPolicy.CanManageAgencySettings(permissions));

    [Fact]
    public async Task CloudRejectionBecomesAnExpectedSettingsSaveFailure()
    {
        using var http = new HttpClient(new ForbiddenSettingsHandler())
        {
            BaseAddress = new Uri("https://demo.sati.invalid/")
        };
        var api = new CloudApiClient(http);
        api.SetAccessToken("test-token");
        var service = new CloudSettingsService(api);

        var error = await Assert.ThrowsAsync<SettingsSaveException>(() =>
            service.SaveAsync(new Settings()));

        Assert.Contains("not permitted", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<CloudApiException>(error.InnerException);
    }

    [Fact]
    public void PersonalTextShortcutTabIsNotRestrictedToAgencyAdmins()
    {
        var view = File.ReadAllText(Path.Combine(RepositoryRoot(), "Views", "SettingsWindow.xaml"));
        var shortcutTabStart = view.IndexOf("Header=\"Text shortcuts\"", StringComparison.Ordinal);
        var nextTab = view.IndexOf("<TabItem", shortcutTabStart + 1, StringComparison.Ordinal);
        var shortcutTab = view[shortcutTabStart..nextTab];

        Assert.DoesNotContain("CanManageAgencySettings", shortcutTab, StringComparison.Ordinal);
        Assert.Contains("SaveTextShortcutsCommand", shortcutTab, StringComparison.Ordinal);
    }

    [Fact]
    public void PersonalDisplayPreferencesAreOnTheUngatedAppearanceTab()
    {
        var view = File.ReadAllText(Path.Combine(RepositoryRoot(), "Views", "SettingsWindow.xaml"));
        var appearanceStart = view.IndexOf("Header=\"Appearance\"", StringComparison.Ordinal);
        var nextTab = view.IndexOf("<TabItem", appearanceStart + 1, StringComparison.Ordinal);
        var appearanceTab = view[appearanceStart..nextTab];

        Assert.Contains("ShowDailyAgendaAtSignIn", appearanceTab, StringComparison.Ordinal);
        Assert.Contains("EasyEyesMode", appearanceTab, StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.Name=\"Use Easy Eyes mode\"",
            appearanceTab,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.Name=\"Show the daily agenda window at sign-in\"",
            appearanceTab,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CanManageAgencySettings", appearanceTab, StringComparison.Ordinal);
    }

    [Fact]
    public void OadsAuthoringControlsHideOnlyBuildersAndPreserveComplianceAttestations()
    {
        var settings = File.ReadAllText(Path.Combine(RepositoryRoot(), "Views", "SettingsWindow.xaml"));
        var clients = File.ReadAllText(Path.Combine(RepositoryRoot(), "Views", "ClientsView.xaml"));

        Assert.Contains("IsComprehensiveAssessmentAuthoringEnabled", settings, StringComparison.Ordinal);
        Assert.Contains("IsClassificationAuthoringEnabled", settings, StringComparison.Ordinal);
        Assert.Contains("IsPersonCenteredPlanAuthoringEnabled", settings, StringComparison.Ordinal);
        Assert.Contains("keeps historical records, Evergreen attestations, deadlines, reminders, and billing rules", settings, StringComparison.Ordinal);

        Assert.Contains("Header=\"Comprehensive Assessment\"", clients, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding IsComprehensiveAssessmentAuthoringEnabled", clients, StringComparison.Ordinal);
        Assert.Contains("Header=\"Person-Centered Plan\"", clients, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding IsPersonCenteredPlanAuthoringEnabled", clients, StringComparison.Ordinal);
        Assert.Contains("Header=\"Classification\"", clients, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding IsClassificationAuthoringEnabled", clients, StringComparison.Ordinal);

        Assert.Contains("IsChecked=\"{Binding PcpCompliant, Mode=OneWay}\"", clients, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding CompAssessmentCompliant, Mode=OneWay}\"", clients, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding ReclassificationCompliant, Mode=OneWay}\"", clients, StringComparison.Ordinal);
    }

    [Fact]
    public void UnfinishedOadsBuildersAreOffByDefault()
    {
        var defaults = new Settings();

        Assert.False(defaults.IsComprehensiveAssessmentAuthoringEnabled);
        Assert.False(defaults.IsClassificationAuthoringEnabled);
        Assert.False(defaults.IsPersonCenteredPlanAuthoringEnabled);

        var settingsView = File.ReadAllText(Path.Combine(RepositoryRoot(), "Views", "SettingsWindow.xaml"));
        Assert.Contains("unfinished workflows are off by default", settingsView, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string callerPath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerPath)!, ".."));

    private sealed class ForbiddenSettingsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
            });
    }
}
