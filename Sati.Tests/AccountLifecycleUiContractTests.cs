using System.Xml.Linq;
using Xunit;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class AccountLifecycleUiContractTests
{
    private static string Root => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(AccountLifecycleUiContractTests).Assembly.Location)!, "..", "..", "..", "..", ".."));
    private static string Read(string path) => File.ReadAllText(Path.Combine(Root, path));

    [Fact]
    public void ReauthenticationShieldDisablesUnderlyingKeyboardTargetsButNotItsRetrySurface()
    {
        var document = XDocument.Parse(Read("Views/ShellWindow.xaml"));
        var rootGrid = document.Descendants().Single(element => element.Name.LocalName == "Grid" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "RootGrid"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var isolated = new XElement(presentation + "Grid",
            rootGrid.Elements().Where(element => element.Name.LocalName == "Grid.Style").Select(element => new XElement(element)),
            new XElement(presentation + "Button", new XAttribute("Content", "Underlying save action")));
        WpfUiHarness.Run(() =>
        {
            // Load the actual underlay style, without instantiating shell services.
            var underlay = (Grid)XamlReader.Parse(isolated.ToString());
            underlay.DataContext = new { IsAccountTransitionActive = true };
            WpfUiHarness.Realize(underlay);
            Assert.False(underlay.IsEnabled);
            Assert.False(((Button)underlay.Children[0]).IsEnabled);
            var retry = new Button(); // A sibling on the shield is not disabled.
            Assert.True(retry.IsEnabled);
            underlay.DataContext = new { IsAccountTransitionActive = false };
            WpfUiHarness.Realize(underlay);
            Assert.True(underlay.IsEnabled);
        });
    }

    [Fact]
    public void AccountManagementHasExplicitLifecycleCommandsAndVisibleDisabledState()
    {
        var source = Read("Views/UserManagementView.xaml");
        foreach (var command in new[] { "EnableAccountCommand", "DisableAccountCommand", "RevokeSessionsCommand" })
            Assert.Contains(command, source);
        Assert.Contains("Disabled", source);
        Assert.Contains("CanManageAccountLifecycle", Read("ViewModels/Supervisor/UserManagementViewModel.cs"));
        Assert.Contains("IsUserManagementAvailable", Read("ViewModels/ShellViewModel.cs"));
    }

    [Fact]
    public void ReauthenticationInstallsFreshIdentityWithoutResettingSamePermissionDrafts()
    {
        var source = Read("Views/ShellWindow.xaml.cs");
        Assert.Contains("await _shellViewModel.ResumeReauthenticatedSessionAsync(user)", source);
        Assert.Contains("_sessionLifetime.Invalidate()", source);
        var shell = Read("ViewModels/ShellViewModel.cs");
        var start = shell.IndexOf("public async Task ResumeReauthenticatedSessionAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var method = shell[start..shell.IndexOf("private void ApplyEasyEyesMode", start, StringComparison.Ordinal)];
        Assert.Contains("_sessionService.SetUser(user)", method);
        Assert.Contains("NotesViewModel.LoggedInUser = _sessionService.CurrentUser", method);
        Assert.Contains("await Scratchpad.ResumeAfterReauthenticationAsync()", method);
        Assert.DoesNotContain(".Reset()", method);
        Assert.Contains("ReauthenticateCommand", Read("Views/ShellWindow.xaml"));
    }

    [Fact]
    public void PasswordChangeDoesNotSecretlyInstallAnotherAuthenticationSession()
    {
        var source = Read("ViewModels/MyAccountViewModel.cs");
        var start = source.IndexOf("private async Task ChangePassword()", StringComparison.Ordinal);
        Assert.DoesNotContain("_authService.AuthenticateAsync", source[start..]);
    }
}
