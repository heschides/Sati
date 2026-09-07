using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;

namespace Sati.Tests;

/// <summary>
/// Loads the application's views from their markup alone, with no view model,
/// service, or database behind them, so an audit can measure what WPF actually
/// builds rather than what the XAML appears to say.
/// </summary>
/// <remarks>
/// One owner for the loading rules: the theme legibility audit and the
/// accessibility audit both sweep every view, and two copies of the markup
/// rewriting would drift apart. A view that cannot be built standalone is skipped
/// rather than failed; <c>ViewsAreOverwhelminglyLoadable</c> holds the floor so the
/// skipped set cannot quietly grow.
/// </remarks>
internal static class RenderedViews
{
    private static bool _assembliesLoaded;

    internal static string LastLoadFailure { get; private set; } = string.Empty;

    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sati.csproj")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root was not found.");
    }

    internal static IEnumerable<string> Files() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "Views"), "*.xaml",
            SearchOption.AllDirectories);

    /// <summary>
    /// Parses a view's markup without its code-behind partner. Returns null for
    /// markup that cannot be built standalone; the reason is in
    /// <see cref="LastLoadFailure"/>.
    /// </summary>
    internal static FrameworkElement? TryLoad(string file)
    {
        try
        {
            EnsureReferencedUiAssembliesAreLoaded();

            var markup = File.ReadAllText(file);
            markup = Regex.Replace(markup, @"\s+x:Class=""[^""]*""", string.Empty);

            // Compiled XAML resolves a bare clr-namespace against the assembly it is
            // compiled into. Parsed at runtime from the test assembly it would resolve
            // against this one, so every Sati namespace is named explicitly.
            markup = Regex.Replace(markup, @"clr-namespace:(Sati[\w.]*)(?=""|;)",
                "clr-namespace:$1;assembly=Sati");

            // Handlers and the window icon live in the code-behind and the app's own
            // resources; neither changes a colour or an automation name.
            markup = Regex.Replace(markup, @"\s+(?:[A-Z]\w+)?(?:Click|Checked|Unchecked|Loaded"
                + @"|Unloaded|SelectionChanged|SelectedDatesChanged|SelectedDateChanged"
                + @"|TextChanged|MouseDoubleClick|MouseLeftButtonDown|MouseLeftButtonUp"
                + @"|MouseDown|MouseUp|KeyDown|KeyUp|PreviewKeyDown|PreviewMouseDown"
                + @"|PreviewMouseWheel|GotFocus|LostFocus|Closing|Closed|Drop|DragOver"
                + @"|SizeChanged|Expanded|Collapsed|ValueChanged|ScrollChanged"
                + @"|PasswordChanged|DataContextChanged|SourceInitialized"
                + @"|Handler|Initialized|Activated|Deactivated)=""\w+""",
                string.Empty);
            markup = Regex.Replace(markup, @"\s+Icon=""[^""]*""", string.Empty);

            using var reader = new StringReader(markup);
            using var xml = System.Xml.XmlReader.Create(reader);
            var loaded = XamlReader.Load(xml);

            // A Window cannot be measured without being shown; its content can.
            return loaded switch
            {
                Window window => window.Content as FrameworkElement,
                FrameworkElement element => element,
                _ => null,
            };
        }
        catch (Exception exception)
        {
            LastLoadFailure = exception.Message.ReplaceLineEndings(" ");
            return null;
        }
    }

    /// <summary>
    /// XAML namespace lookup only scans assemblies already loaded, and nothing in
    /// these tests touches the charting types, so they are pulled in deliberately.
    /// </summary>
    private static void EnsureReferencedUiAssembliesAreLoaded()
    {
        if (_assembliesLoaded)
            return;

        _assembliesLoaded = true;
        foreach (var name in new[] { "OxyPlot", "OxyPlot.Wpf", "OxyPlot.SkiaSharp.Wpf" })
        {
            try { System.Reflection.Assembly.Load(name); }
            catch (Exception) { /* Absent charting support just narrows coverage. */ }
        }
    }
}
