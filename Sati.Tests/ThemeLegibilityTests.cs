using Sati.Helpers;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Legibility of every theme, measured rather than eyeballed.
/// </summary>
/// <remarks>
/// Two passes, because each catches what the other cannot.
/// <para>
/// The token pass scores the design system itself: every text role against every
/// surface role it can land on. It is complete â€” a pair that no screen uses today
/// still cannot be allowed to fail, because the next screen will use it.
/// </para>
/// <para>
/// The rendered pass loads every view under every theme and reads the brushes WPF
/// actually resolved. That is what catches a literal colour written into a view, a
/// <c>DynamicResource</c> whose key no dictionary defines (WPF resolves those to
/// nothing and leaves the inherited foreground in place, with no error anywhere),
/// and a surface brush used as ink.
/// </para>
/// </remarks>
[Collection(WpfViewCollection.Name)]
public sealed class ThemeLegibilityTests
{
    private const double Minimum = ThemeContrast.NormalTextMinimum;

    /// <summary>Text roles. Any of these can be asked to sit on any surface below.</summary>
    private static readonly string[] TextRoles =
    [
        "TextPrimaryBrush", "TextSecondaryBrush", "TextMutedBrush", "TextSubtleBrush",
        "NarrativeTextBrush", "AccentBrush", "OverdueBrush", "CompliantBrush",
        "WarningBrush", "InfoBrush", "OpenedFormBrush", "SuccessDarkBrush",
        "DangerDarkBrush", "WorkflowPurpleBrush", "AiPanelTextBrush",
    ];

    /// <summary>Surface roles that carry body text.</summary>
    private static readonly string[] SurfaceRoles =
    [
        "WindowBackgroundBrush", "NavBackgroundBrush", "SurfaceBrush", "SurfaceAltBrush",
        "SurfaceMutedBrush", "SurfaceRaisedBrush", "HoverBrush", "HoverStrongBrush",
        "PersonGroupBrush", "PersonGroupAltBrush",
    ];

    /// <summary>Fills that carry their own named ink, so only that one pairing matters.</summary>
    private static readonly (string Ink, string Fill)[] SemanticPairs =
    [
        ("TextPrimaryBrush", "OverdueFillBrush"), ("TextPrimaryBrush", "CompliantFillBrush"),
        ("TextPrimaryBrush", "WarningFillBrush"), ("TextPrimaryBrush", "InfoFillBrush"),
        ("TextPrimaryBrush", "OpenedFormFillBrush"), ("TextPrimaryBrush", "WorkflowPurpleFillBrush"),
        ("TextPrimaryBrush", "SuccessSoftBrush"), ("TextPrimaryBrush", "DangerSoftBrush"),
        ("TextPrimaryBrush", "WarningSoftBrush"),
        ("TextPrimaryBrush", "MatrixGoodBrush"), ("TextPrimaryBrush", "MatrixWatchBrush"),
        ("TextPrimaryBrush", "MatrixConcernBrush"), ("TextPrimaryBrush", "MatrixUrgentBrush"),
        ("TextPrimaryBrush", "MatrixEmptyBrush"),
        ("OverdueBrush", "OverdueFillBrush"), ("CompliantBrush", "CompliantFillBrush"),
        ("WarningBrush", "WarningFillBrush"), ("InfoBrush", "InfoFillBrush"),
        ("OpenedFormBrush", "OpenedFormFillBrush"),
        ("WorkflowPurpleBrush", "WorkflowPurpleFillBrush"),
        ("OnSuccessStrongBrush", "SuccessStrongBrush"),
        ("OnDangerStrongBrush", "DangerStrongBrush"),
        ("OnDangerStrongBrush", "DangerDarkBrush"),
        ("OnAccentBrush", "AccentBrush"), ("OnAccentBrush", "AccentHoverBrush"),
        ("OnAccentBrush", "AccentPressedBrush"),
        ("OnAccentButtonBrush", "AccentButtonBrush"),
        ("OnAccentButtonBrush", "AccentButtonHoverBrush"),
        ("OnAccentButtonBrush", "AccentButtonPressedBrush"),
        ("OnAiAccentBrush", "AiAccentBrush"), ("OnAiAccentBrush", "AiAccentHoverBrush"),
        ("OnAiAccentBrush", "AiAccentPressedBrush"),
        ("AiPanelTextBrush", "AiAccentSoftBrush"),
    ];

    /// <summary>
    /// Read from disk rather than from <c>ThemeService</c>: xUnit enumerates theory
    /// data on the discovery thread, where there is no WPF application yet, and
    /// constructing the service would apply a theme as a side effect.
    /// </summary>
    public static TheoryData<string> EveryTheme()
    {
        var data = new TheoryData<string>();
        foreach (var name in ThemeNamesOnDisk())
            data.Add(name);
        return data;
    }

    private static IEnumerable<string> ThemeNamesOnDisk() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "Themes"), "*.xaml")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null and not "States")
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal);

    /// <summary>
    /// Reads the picker's list from the service's source rather than constructing it:
    /// the constructor applies the saved theme through a relative pack URI that only
    /// resolves inside the Sati application, so building one here throws.
    /// </summary>
    [Fact]
    public void ThemePickerOffersEveryThemeThatShipped()
    {
        var source = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "Services", "ThemeService.cs"));
        var offered = Regex.Matches(source, @"new\(""[^""]+"",\s*""(\w+)""\)")
            .Select(match => match.Groups[1].Value)
            .OrderBy(name => name, StringComparer.Ordinal);

        // A theme file nobody can select is a palette no one ever audits by eye, and
        // a picker entry with no file throws the moment it is chosen.
        Assert.Equal(ThemeNamesOnDisk(), offered);
    }

    [Theory]
    [MemberData(nameof(EveryTheme))]
    public void EveryTextRoleClearsAaOnEverySurfaceItCanLandOn(string theme)
    {
        WpfUiHarness.Run(() =>
        {
            using var _ = ThemeSwap.To(theme);
            var failures = new List<string>();

            void Score(string inkKey, string fillKey)
            {
                var ink = Application.Current.TryFindResource(inkKey) as Brush;
                var fill = Application.Current.TryFindResource(fillKey) as Brush;

                Assert.True(ink is not null, $"{theme}: '{inkKey}' resolves to no brush.");
                Assert.True(fill is not null, $"{theme}: '{fillKey}' resolves to no brush.");

                var ratio = ThemeContrast.WorstRatio(ink, fill, Colors.Black);
                if (ratio is not null && ratio < Minimum)
                    failures.Add($"  {ratio,5:N2}:1  {inkKey} on {fillKey}");
            }

            foreach (var fill in SurfaceRoles)
                foreach (var ink in TextRoles)
                    Score(ink, fill);

            foreach (var (ink, fill) in SemanticPairs)
                Score(ink, fill);

            Assert.True(failures.Count == 0,
                $"{theme} has {failures.Count} token pairs below {Minimum:N1}:1:{Environment.NewLine}"
                + string.Join(Environment.NewLine, failures.OrderBy(line => line)));
        });
    }

    [Theory]
    [MemberData(nameof(EveryTheme))]
    public void NoViewPaintsTextThatDisappearsIntoItsOwnBackground(string theme)
    {
        WpfUiHarness.Run(() =>
        {
            using var _ = ThemeSwap.To(theme);
            var failures = new List<string>();

            foreach (var file in ViewFiles())
            {
                var element = TryLoad(file);
                if (element is null)
                    continue;

                WpfUiHarness.Realize(element);
                foreach (var text in WpfUiHarness.Descendants(element).OfType<TextBlock>())
                {
                    if (string.IsNullOrWhiteSpace(text.Text) || text.ActualHeight <= 0)
                        continue;

                    var (behind, source) = PaintedBehind(element, text);
                    if (behind is null)
                        continue;

                    var ratio = ThemeContrast.WorstRatio(text.Foreground, behind, Colors.Black);
                    if (ratio is not null && ratio < Minimum)
                    {
                        var excerpt = text.Text.Length > 40 ? text.Text[..40] + "â€¦" : text.Text;
                        failures.Add($"  {ratio,5:N2}:1  {Path.GetFileName(file)}  "
                            + $"{Describe(text.Foreground)} on {Describe(behind)} [{source}]  \"{excerpt}\"");
                    }
                }
            }

            Assert.True(failures.Count == 0,
                $"{theme} renders {failures.Count} text runs below {Minimum:N1}:1:{Environment.NewLine}"
                + string.Join(Environment.NewLine, failures.Distinct().OrderBy(line => line)));
        });
    }

    [Fact]
    public void NoViewAsksForAThemeBrushNoDictionaryDefines()
    {
        WpfUiHarness.Run(() =>
        {
            var keys = new SortedSet<string>();
            foreach (var file in ViewFiles().Append(Path.Combine(RepositoryRoot(), "App.xaml")))
            {
                foreach (Match match in Regex.Matches(
                    File.ReadAllText(file), @"\{(?:Dynamic|Static)Resource\s+(\w*Brush|\w*Color)\}"))
                {
                    keys.Add(match.Groups[1].Value);
                }
            }

            // A view may define its own brush locally; only application-wide theme
            // keys are this test's business, and every one of those must resolve.
            var missing = keys
                .Where(key => Application.Current.TryFindResource(key) is null)
                .Where(key => !LocallyDefined().Contains(key))
                .ToList();

            Assert.True(missing.Count == 0,
                "These theme keys are referenced but no dictionary defines them, so WPF "
                + "silently leaves the inherited value in place: " + string.Join(", ", missing));
        });
    }

    private static HashSet<string> LocallyDefined()
    {
        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in ViewFiles())
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"x:Key=""(\w+)"""))
                defined.Add(match.Groups[1].Value);
        return defined;
    }

    private static string Describe(Brush? brush) => brush switch
    {
        null => "(none)",
        SolidColorBrush solid => solid.Color.ToString(),
        _ => brush.GetType().Name + string.Concat(
            ThemeContrast.PaintedColors(brush, Colors.Black).Take(2).Select(c => " " + c)),
    };

    /// <summary>
    /// What actually paints under a run of text, found by hit testing the point the
    /// glyphs occupy rather than by walking ancestors.
    /// </summary>
    /// <remarks>
    /// Walking ancestors gets templated controls wrong. A <see cref="CheckBox"/>
    /// carries a white <c>Background</c>, but its template spends that brush on the
    /// small box, not behind the label â€” so an ancestor walk reports white behind
    /// text that in fact sits on the panel. A hit test only reports visuals whose
    /// rendered content covers the point, so parts of a template that paint
    /// elsewhere, and layout panels that paint nothing, drop out on their own.
    /// </remarks>
    private static (Brush? Brush, string Source) PaintedBehind(Visual root, TextBlock text)
    {
        Point centre;
        try
        {
            centre = text.TranslatePoint(
                new Point(text.ActualWidth / 2, text.ActualHeight / 2), (UIElement)root);
        }
        catch (InvalidOperationException)
        {
            return (null, string.Empty);  // Not connected to this render.
        }

        Brush? found = null;
        var source = string.Empty;

        VisualTreeHelper.HitTest(
            root,
            // Only the text's own ancestors. A visual that is not an ancestor either
            // paints somewhere else or paints over the top â€” a modal scrim, say â€” and
            // neither is what the reader sees behind these glyphs.
            target => target == text
                ? HitTestFilterBehavior.ContinueSkipSelfAndChildren
                : IsInside(text, target)
                    ? HitTestFilterBehavior.Continue
                    : HitTestFilterBehavior.ContinueSkipSelfAndChildren,
            result =>
            {
                var background = result.VisualHit switch
                {
                    Panel panel => panel.Background,
                    Border border => border.Background,
                    Control control => control.Background,
                    TextBlock block => block.Background,
                    _ => null,
                };

                if (background is null || background == Brushes.Transparent)
                    return HitTestResultBehavior.Continue;

                var colors = ThemeContrast.PaintedColors(background, Colors.Black);
                if (colors.Count == 0 || colors.All(color => color.A == 0))
                    return HitTestResultBehavior.Continue;

                found = background;
                source = result.VisualHit is FrameworkElement { Name.Length: > 0 } named
                    ? $"{result.VisualHit.GetType().Name}:{named.Name}"
                    : result.VisualHit.GetType().Name;
                return HitTestResultBehavior.Stop;
            },
            new PointHitTestParameters(centre));

        return (found, source);
    }

    private static bool IsInside(DependencyObject candidate, DependencyObject container)
    {
        for (var node = candidate; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node == container)
                return true;
        }

        return false;
    }

    // Loading every view from markup alone has one owner; the accessibility audit
    // sweeps the same set and must see exactly what this pass sees.
    private static IEnumerable<string> ViewFiles() => RenderedViews.Files();
    private static FrameworkElement? TryLoad(string file) => RenderedViews.TryLoad(file);
    private static string LastLoadFailure => RenderedViews.LastLoadFailure;
    private static string RepositoryRoot() => RenderedViews.RepositoryRoot();

    /// <summary>Installs a theme dictionary and puts the previous one back.</summary>
    private sealed class ThemeSwap : IDisposable
    {
        private readonly int _index;
        private readonly ResourceDictionary _original;

        private ThemeSwap(int index, ResourceDictionary original)
        {
            _index = index;
            _original = original;
        }

        internal static ThemeSwap To(string theme)
        {
            var dictionaries = Application.Current.Resources.MergedDictionaries;
            var index = dictionaries
                .Select((dictionary, position) => new { dictionary, position })
                .Single(item =>
                    item.dictionary.Source?.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase) == true
                    && !item.dictionary.Source.OriginalString.EndsWith("States.xaml", StringComparison.OrdinalIgnoreCase))
                .position;

            var swap = new ThemeSwap(index, dictionaries[index]);
            dictionaries[index] = new ResourceDictionary
            {
                Source = new Uri($"/Sati;component/Themes/{theme}.xaml", UriKind.Relative),
            };
            return swap;
        }

        public void Dispose() =>
            Application.Current.Resources.MergedDictionaries[_index] = _original;
    }
}
