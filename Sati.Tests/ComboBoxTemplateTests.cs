using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// App.xaml replaces the framework ComboBox template because the stock one paints
/// hard-coded near-white chrome that no theme can reach. That replacement has to
/// reproduce one subtlety exactly.
///
/// ComboBox writes SelectionBoxItem and SelectionBoxItemTemplate through read-only
/// property keys AFTER the control template is applied. A TemplateBinding does not
/// follow that later write, so the selection box silently falls back to the item's
/// ToString(): a record renders as "ThemeOption { DisplayName = ... }" instead of
/// the display member. An explicit ItemTemplate survives, which is what made the
/// defect look intermittent rather than total.
///
/// These properties must therefore be bound with RelativeSource TemplatedParent.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class ComboBoxTemplateTests
{
    [Fact]
    public void SettingsThemeGroupsPreserveSelectionAndAlphabetizeChoices()
    {
        WpfUiHarness.Run(() =>
        {
            var content = RenderedViews.TryLoad(Path.Combine(RepositoryRoot(), "Views", "SettingsWindow.xaml"));
            Assert.True(content is not null, RenderedViews.LastLoadFailure);
            var selected = new Sati.Services.ThemeOption("Modern Stone", "ModernStone");
            var options = new[]
            {
                selected,
                new Sati.Services.ThemeOption("Legacy", "Legacy"),
                new Sati.Services.ThemeOption("Modern Clay", "ModernClay"),
                new Sati.Services.ThemeOption("Modern", "Modern")
            };
            var window = System.Windows.Window.GetWindow(content!);
            Assert.NotNull(window);
            window.DataContext = new ThemePickerData { ThemeOptions = options, SelectedTheme = selected };
            WpfUiHarness.Realize(content!);
            var tabs = WpfUiHarness.Descendants(content!).OfType<System.Windows.Controls.TabControl>().Single();
            tabs.SelectedItem = tabs.Items.Cast<System.Windows.Controls.TabItem>()
                .Single(tab => Equals(tab.Header, "Appearance"));
            WpfUiHarness.Realize(content!);
            var picker = WpfUiHarness.Descendants(content!)
                .OfType<System.Windows.Controls.ComboBox>()
                .Single(control => System.Windows.Automation.AutomationProperties.GetName(control) == "Theme");
            Assert.Same(selected, picker.SelectedItem);
            Assert.Equal(new[] { "Modern", "Modern Clay", "Modern Stone", "Legacy" },
                picker.Items.Cast<Sati.Services.ThemeOption>().Select(theme => theme.DisplayName));
            Assert.Equal(new[] { "Modern", "Modern · earth tones", "Classic & branded" },
                picker.Items.Groups!.Cast<System.Windows.Data.CollectionViewGroup>().Select(group => group.Name));
            Assert.Single(picker.GroupStyle);
        });
    }

    public sealed class ThemePickerData
    {
        public IReadOnlyList<Sati.Services.ThemeOption> ThemeOptions { get; init; } = [];
        public Sati.Services.ThemeOption? SelectedTheme { get; set; }
    }

    private static readonly string[] MustNotUseTemplateBinding =
    [
        "SelectionBoxItem",
        "SelectionBoxItemTemplate",
        "SelectionBoxItemStringFormat"
    ];

    [Fact]
    public void ComboBoxSelectionBoxUsesBindingsThatFollowLateWrites()
    {
        var appXaml = File.ReadAllText(Path.Combine(RepositoryRoot(), "App.xaml"));

        foreach (var property in MustNotUseTemplateBinding)
        {
            Assert.False(
                Regex.IsMatch(appXaml, $@"\{{\s*TemplateBinding\s+{Regex.Escape(property)}\s*\}}"),
                $"App.xaml binds {property} with TemplateBinding. ComboBox sets that property after " +
                "the template is applied, so a TemplateBinding never sees the value and the selection " +
                "box falls back to ToString(). Use " +
                $"{{Binding {property}, RelativeSource={{RelativeSource TemplatedParent}}}} instead.");
        }
    }

    [Fact]
    public void ComboBoxSelectionBoxStillBindsTheContentProperties()
    {
        // Guards the other direction: the properties must actually be bound. A
        // template that simply dropped them would pass the test above.
        var appXaml = File.ReadAllText(Path.Combine(RepositoryRoot(), "App.xaml"));

        foreach (var property in MustNotUseTemplateBinding)
        {
            Assert.Matches(
                new Regex($@"\{{\s*Binding\s+{Regex.Escape(property)}\s*,\s*RelativeSource\s*=\s*\{{\s*RelativeSource\s+TemplatedParent\s*\}}\s*\}}"),
                appXaml);
        }
    }

    private static string RepositoryRoot([CallerFilePath] string callerPath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerPath)!, ".."));
}
