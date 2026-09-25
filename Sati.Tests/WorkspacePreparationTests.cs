using Sati.Helpers;
using Sati.Views;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class WorkspacePreparationTests
{
    private static string Root => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(WorkspacePreparationTests).Assembly.Location)!,
        "..", "..", "..", "..", ".."));

    [Fact]
    public void ReflectionBankIsExactlyOneHundredDistinctReadablePassages()
    {
        Assert.Equal(100, WorkspaceReflections.Count);
        Assert.Equal(100, WorkspaceReflections.All
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .Count());

        foreach (var reflection in WorkspaceReflections.All)
        {
            Assert.Equal(reflection.Trim(), reflection);
            Assert.InRange(reflection.Length, 80, 180);
            Assert.Matches(@"[.!?]['’""]?$", reflection);
            Assert.DoesNotContain('\r', reflection);
            Assert.DoesNotContain('\n', reflection);
            Assert.DoesNotMatch(new Regex(@"https?://|www\.", RegexOptions.IgnoreCase), reflection);
        }
    }

    [Fact]
    public void DeterministicSequenceVisitsTheWholeBankBeforeRepeating()
    {
        var first = new WorkspaceReflectionSequence(17);
        var second = new WorkspaceReflectionSequence(17);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var startingReflection = first.Current;

        for (var index = 0; index < WorkspaceReflections.Count; index++)
        {
            Assert.Equal(second.Current, first.Current);
            Assert.True(visited.Add(first.Current),
                $"Reflection repeated after only {index} steps.");
            first.MoveNext();
            second.MoveNext();
        }

        Assert.Equal(WorkspaceReflections.Count, visited.Count);
        Assert.Equal(startingReflection, first.Current);
        Assert.Equal(startingReflection, second.Current);
    }

    [Fact]
    public void StartupShowsAndPaintsPreparationAroundRealWorkWithoutAFixedSplashDelay()
    {
        var app = File.ReadAllText(Path.Combine(Root, "App.xaml.cs"));

        Assert.DoesNotContain("Task.Delay(3000)", app, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(
            @"Task\.Delay\s*\(\s*TimeSpan\.FromSeconds\s*\(\s*[2-9]",
            RegexOptions.CultureInvariant), app);
        Assert.Contains("shellWindow.Closed += failIfShellCloses", app,
            StringComparison.Ordinal);
        Assert.Contains("Task.Delay(TimeSpan.FromSeconds(15))", app,
            StringComparison.Ordinal);

        var login = app.IndexOf("loginWindow.ShowDialog()", StringComparison.Ordinal);
        var selectSession = app.IndexOf("session.SetUser(user)", login, StringComparison.Ordinal);
        var create = app.IndexOf("SplashScreenWindow.CreateWorkspacePreparation()", selectSession,
            StringComparison.Ordinal);
        var showPreparation = app.IndexOf("workspacePreparation.Show()", create,
            StringComparison.Ordinal);
        var firstRender = app.IndexOf("DispatcherPriority.Render", showPreparation,
            StringComparison.Ordinal);
        var startSession = app.IndexOf("StartSessionAsync(user, reporter)", firstRender,
            StringComparison.Ordinal);
        var initialize = app.IndexOf("await shellVm.InitializeAsync()", firstRender,
            StringComparison.Ordinal);
        var assignMain = app.IndexOf("MainWindow = shellWindow", initialize,
            StringComparison.Ordinal);
        var keepAbove = app.IndexOf("workspacePreparation.Topmost = true", assignMain,
            StringComparison.Ordinal);
        var showShell = app.IndexOf("shellWindow.Show()", assignMain, StringComparison.Ordinal);
        var contentRendered = app.IndexOf("await shellRendered.Task", showShell,
            StringComparison.Ordinal);
        var closePreparation = app.IndexOf("workspacePreparation.CloseWorkspacePreparation()", contentRendered,
            StringComparison.Ordinal);
        var activateShell = app.IndexOf("shellWindow.Activate()", closePreparation,
            StringComparison.Ordinal);
        var finallyBlock = app.IndexOf("finally", activateShell, StringComparison.Ordinal);

        Assert.True(login >= 0 && login < selectSession);
        Assert.True(selectSession < create && create < showPreparation);
        Assert.True(showPreparation < firstRender && firstRender < startSession);
        Assert.True(startSession < initialize && initialize < assignMain);
        Assert.True(assignMain < keepAbove && keepAbove < showShell);
        Assert.True(showShell < contentRendered && contentRendered < closePreparation);
        Assert.True(closePreparation < activateShell && activateShell < finallyBlock);
    }

    [Fact]
    public void PreparationMarkupUsesThemeRolesAndSeparatesStatusFromReflectionAnnouncements()
    {
        var xaml = File.ReadAllText(Path.Combine(Root, "Views", "SplashScreenWindow.xaml"));

        Assert.Contains("Text=\"We are preparing the Sati workspace.\"", xaml,
            StringComparison.Ordinal);
        Assert.Contains("Text=\"A Sati reflection\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ReflectionText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml,
            StringComparison.Ordinal);
        Assert.Contains("{DynamicResource BrandLeafImage}", xaml, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource SurfaceBrush}", xaml, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource SurfaceRaisedBrush}", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Color=\"#", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Background=\"#", xaml, StringComparison.OrdinalIgnoreCase);

        var reflectionStart = xaml.IndexOf("x:Name=\"ReflectionText\"", StringComparison.Ordinal);
        var reflectionEnd = xaml.IndexOf("/>", reflectionStart, StringComparison.Ordinal);
        Assert.True(reflectionStart >= 0 && reflectionEnd > reflectionStart);
        Assert.DoesNotContain("LiveSetting", xaml[reflectionStart..reflectionEnd],
            StringComparison.Ordinal);
    }

    [Fact]
    public void PreparationSurfaceRendersTheChosenReflectionAcrossLightDarkAndLegacyThemes()
    {
        WpfUiHarness.Run(() =>
        {
            var dictionaries = Application.Current.Resources.MergedDictionaries;
            var themeIndex = dictionaries
                .Select((dictionary, index) => new { dictionary, index })
                .Single(item => item.dictionary.Source?.OriginalString.Contains(
                    "Themes/", StringComparison.OrdinalIgnoreCase) == true &&
                    !item.dictionary.Source.OriginalString.EndsWith(
                        "States.xaml", StringComparison.OrdinalIgnoreCase))
                .index;
            var originalTheme = dictionaries[themeIndex];

            try
            {
                foreach (var theme in new[] { "SunlitShell", "MidnightOpal", "LegacyDark" })
                {
                    dictionaries[themeIndex] = new ResourceDictionary
                    {
                        Source = new Uri(
                            $"/Sati;component/Themes/{theme}.xaml", UriKind.Relative)
                    };

                    var window = SplashScreenWindow.CreateWorkspacePreparation(17);
                    try
                    {
                        var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
                        WpfUiHarness.Realize(content, 820, 550);

                        Assert.True(window.ShowInTaskbar);
                        Assert.Equal("Preparing the Sati workspace",
                            AutomationProperties.GetName(window));

                        var heading = WpfUiHarness.FindByAutomationName<TextBlock>(
                            content, "We are preparing the Sati workspace.");
                        Assert.Equal(AutomationHeadingLevel.Level1,
                            AutomationProperties.GetHeadingLevel(heading));

                        var expectedReflection = WorkspaceReflections.At(17);
                        var reflection = WpfUiHarness.FindByAutomationName<TextBlock>(
                            content, expectedReflection);
                        Assert.Equal(WorkspaceReflections.At(17), reflection.Text);
                        Assert.Equal(AutomationLiveSetting.Off,
                            AutomationProperties.GetLiveSetting(reflection));

                        var expectedSurface = Assert.IsAssignableFrom<Brush>(
                            Application.Current.FindResource("SurfaceBrush"));
                        var paintedSurface = Assert.IsType<Border>(content);
                        Assert.Same(expectedSurface, paintedSurface.Background);
                    }
                    finally
                    {
                        window.CloseWorkspacePreparation();
                    }
                }
            }
            finally
            {
                dictionaries[themeIndex] = originalTheme;
            }
        });
    }

    [Fact]
    public void PreparationSurfaceFitsInsideMagnifiedWorkAreas()
    {
        var constrained = SplashScreenWindow.GetPreparationSurfaceSize(
            new Rect(0, 0, 683, 500));
        Assert.Equal(new Size(651, 468), constrained);

        var spacious = SplashScreenWindow.GetPreparationSurfaceSize(
            new Rect(0, 0, 1920, 1080));
        Assert.Equal(new Size(820, 550), spacious);
    }

    [Fact]
    public void ReflectionDedicationIsExplicitAndShipsWithTheDesktop()
    {
        var notice = File.ReadAllText(Path.Combine(Root, "WORKSPACE_REFLECTIONS.md"));
        var project = File.ReadAllText(Path.Combine(Root, "Sati.csproj"));

        Assert.Contains("SPDX-License-Identifier: CC0-1.0", notice, StringComparison.Ordinal);
        Assert.Contains("100 short passages", notice, StringComparison.Ordinal);
        Assert.Contains("not quotations or adaptations", notice, StringComparison.Ordinal);
        Assert.Contains("WORKSPACE_REFLECTIONS.md", project, StringComparison.Ordinal);
        Assert.Contains("CopyToPublishDirectory=\"PreserveNewest\"", project,
            StringComparison.Ordinal);
    }

    private static string Normalize(string value) =>
        Regex.Replace(value.Trim(), @"\s+", " ");
}
