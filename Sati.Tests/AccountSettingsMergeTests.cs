using System.Xml.Linq;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Settings is the one window behind the shell's greeting badge. It holds the
/// signed-in user's own profile and password as well as the agency and application
/// tabs, and it is the only place a user switch is asked for.
/// </summary>
/// <remarks>
/// There used to be two doors in the header: a gear that opened Settings and the
/// greeting badge that opened a separate My Account window, which in turn opened the
/// switch-user dialog. These tests hold the merged shape, and in particular the one
/// property that is not cosmetic: the settings window closes before the shell starts
/// the switch, because that flow replaces the session user and rebuilds every view
/// model the window is bound to.
/// </remarks>
public sealed class AccountSettingsMergeTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static string Root => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(AccountSettingsMergeTests).Assembly.Location)!,
        "..", "..", "..", "..", ".."));

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { Root }.Concat(parts).ToArray()));

    [Fact]
    public void TheShellHasOneWayIntoSettingsAndNoGear()
    {
        var shell = Read("Views", "ShellWindow.xaml");

        // E713 is the Segoe MDL2 gear the header used to carry beside the greeting.
        Assert.DoesNotContain("&#xE713;", shell);

        var document = XDocument.Parse(shell);

        // One named control, and it is the greeting badge rather than an icon button.
        var named = document.Descendants()
            .Where(element => (string?)element.Attribute("AutomationProperties.Name") == "Open settings")
            .ToList();
        Assert.Single(named);
        Assert.Equal(Presentation + "Border", named[0].Name);

        // The badge reaches the command through its own input bindings. Any Button
        // binding it would be the second door coming back.
        Assert.DoesNotContain(document.Descendants(Presentation + "Button"),
            element => element.Attributes()
                .Any(a => a.Value.Contains("OpenSettingsWindowCommand", StringComparison.Ordinal)));

        Assert.Contains("OpenSettingsWindowCommand", shell);
    }

    [Fact]
    public void TheGreetingBadgeIsReachableAndOperableFromTheKeyboard()
    {
        var badge = XDocument.Parse(Read("Views", "ShellWindow.xaml"))
            .Descendants(Presentation + "Border")
            .Single(element =>
                (string?)element.Attribute("AutomationProperties.Name") == "Open settings");

        // It is a Border, not a Button, so focus and key activation are not free.
        Assert.Equal("True", (string?)badge.Attribute("Focusable"));
        Assert.Equal("True", (string?)badge.Attribute("KeyboardNavigation.IsTabStop"));

        var keys = badge.Descendants(Presentation + "KeyBinding")
            .Select(element => (string?)element.Attribute("Key"))
            .ToList();
        Assert.Contains("Enter", keys);
        Assert.Contains("Space", keys);
    }

    [Fact]
    public void SettingsLeadsWithTheAccountTabsAndBindsThemToTheAccountViewModel()
    {
        var tabs = XDocument.Parse(Read("Views", "SettingsWindow.xaml"))
            .Descendants(Presentation + "TabItem")
            .Select(element => new
            {
                Header = (string?)element.Attribute("Header"),
                Context = (string?)element.Attribute("DataContext"),
            })
            .ToList();

        Assert.Equal("Profile", tabs[0].Header);
        Assert.Equal("Password & Security", tabs[1].Header);
        Assert.Equal("Appearance", tabs[2].Header);

        // The account tabs read the child view model; the rest read the window's own.
        Assert.Equal("{Binding Account}", tabs[0].Context);
        Assert.Equal("{Binding Account}", tabs[1].Context);
        Assert.All(tabs.Skip(2), tab => Assert.Null(tab.Context));
    }

    [Fact]
    public void TheSettingsWindowClosesBeforeItAsksTheShellToSwitchAccounts()
    {
        var code = Read("Views", "SettingsWindow.xaml.cs");

        var close = code.IndexOf("Close();", StringComparison.Ordinal);
        var raise = code.IndexOf("SwitchUserRequested?.Invoke", StringComparison.Ordinal);

        Assert.True(close >= 0 && raise >= 0,
            "The window no longer closes itself and raises the switch request.");

        // Order is the whole point. Switching replaces the session user and rebuilds
        // every view model behind this window; raising first would leave a live window
        // bound to the outgoing user sitting over a new session.
        Assert.True(close < raise,
            "SettingsWindow must close before raising SwitchUserRequested.");

        var shell = Read("Views", "ShellWindow.xaml.cs");
        var showDialog = shell.IndexOf("win.ShowDialog();", StringComparison.Ordinal);
        var startFlow = shell.IndexOf("await OpenSwitchUserFlowAsync();", StringComparison.Ordinal);
        Assert.True(showDialog < startFlow,
            "The shell must start the switch flow only after the settings dialog returns.");
    }

    [Fact]
    public void SwitchingAccountsIsAskedForFromSettingsAndNowhereElse()
    {
        var settings = Read("Views", "SettingsWindow.xaml");
        Assert.Contains("{Binding Account.RequestSwitchUserCommand}", settings);
        Assert.Contains("AutomationProperties.Name=\"Switch user\"", settings);

        // The shell used to raise a switch request of its own for the account window.
        var shellViewModel = Read("ViewModels", "ShellViewModel.cs");
        Assert.DoesNotContain("RequestSwitchUser", shellViewModel);
        Assert.DoesNotContain("SwitchUserRequested", shellViewModel);
    }

    [Fact]
    public void TheSeparateAccountWindowIsGoneAndNothingStillReachesForIt()
    {
        Assert.False(File.Exists(Path.Combine(Root, "Views", "MyAccountWindow.xaml")));
        Assert.False(File.Exists(Path.Combine(Root, "Views", "MyAccountWindow.xaml.cs")));

        foreach (var directory in new[] { "Views", "ViewModels", "Services" })
        {
            foreach (var file in Directory.EnumerateFiles(
                Path.Combine(Root, directory), "*.*", SearchOption.AllDirectories))
            {
                if (Path.GetExtension(file) is not (".cs" or ".xaml"))
                    continue;

                Assert.DoesNotContain("MyAccountWindow", File.ReadAllText(file));
            }
        }

        // The view model survives the window: it is now a child of SettingsViewModel.
        Assert.Contains("MyAccountViewModel account", Read("ViewModels", "SettingsViewModel.cs"));
        Assert.Contains("services.AddTransient<MyAccountViewModel>();", Read("App.xaml.cs"));
        Assert.DoesNotContain("MyAccountWindow", Read("App.xaml.cs"));
    }

    [Fact]
    public void ThePasswordBoxesStillHandTheirSecureStringsToTheAccountViewModel()
    {
        var code = Read("Views", "SettingsWindow.xaml.cs");

        // PasswordBox.Password is not a DependencyProperty, so the plaintext must never
        // reach a binding; each box passes its SecurePassword instead.
        Assert.DoesNotContain(".Password;", code);
        foreach (var box in new[] { "CurrentPasswordBox", "NewPasswordBox", "ConfirmPasswordBox" })
            Assert.Contains($"{box}.SecurePassword", code);

        // A successful change clears all three, which the view model cannot do itself.
        Assert.Contains("Account.PasswordChanged += OnPasswordChanged", code);
        Assert.Contains("Account.PasswordChanged -= OnPasswordChanged", code);
    }
}
