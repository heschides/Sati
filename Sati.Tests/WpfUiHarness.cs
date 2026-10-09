using Sati.Views;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;

namespace Sati.Tests;

/// <summary>
/// The test assembly's single WPF <see cref="Application"/> and the STA thread it
/// lives on. Every test that needs a real view goes through here.
/// </summary>
/// <remarks>
/// WPF permits one <see cref="Application"/> per AppDomain <em>for the life of the
/// process</em> — the flag that enforces it is not cleared by
/// <see cref="Application.Shutdown()"/> — so a second creator does not merely
/// conflict, it fails permanently, and which test fails depends on run order. One
/// owner, created lazily and never shut down, removes that entirely. The thread is
/// a background thread, so it does not hold the process open.
/// <para>
/// WPF queues <c>OnStartup</c> from its constructor, even without <c>Run()</c>.
/// The harness overrides that callback so no production startup, generic host,
/// database connection, or login window is built. A test that needs
/// <c>App.Services</c> supplies its own host through <see cref="RunWithHost"/>.
/// </para>
/// <para>
/// Structural assertions that read XAML as XML prove a panel is declared in the
/// right cell; they cannot prove that <c>{StaticResource {x:Type TextBox}}</c>
/// resolves, that a <c>RelativeSource</c> binding finds what it names, or that a
/// locked field is genuinely read-only. That is what this is for.
/// </para>
/// </remarks>
internal static class WpfUiHarness
{
    private static readonly object Gate = new();
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);
    private static Dispatcher? _dispatcher;
    private static App? _application;

    /// <summary>Executes <paramref name="work"/> on the UI thread and rethrows anything it throws.</summary>
    internal static void Run(Action work, TimeSpan? timeout = null)
    {
        var dispatcher = EnsureStarted();
        Exception? failure = null;

        var operation = dispatcher.InvokeAsync(() =>
        {
            try
            {
                work();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        if (!operation.Task.Wait(timeout ?? DefaultTimeout))
            throw new TimeoutException(
                $"UI work did not finish within {(timeout ?? DefaultTimeout).TotalSeconds:0} seconds.");

        if (failure is not null)
            throw new InvalidOperationException(
                $"The UI thread threw while loading or exercising a view: {failure.Message}", failure);
    }

    /// <summary>
    /// Runs <paramref name="work"/> with <paramref name="host"/> installed as the
    /// application's service provider, then removes it again. For views that
    /// resolve their own dependencies from <c>App.Services</c>.
    /// </summary>
    internal static void RunWithHost(object host, Action work, TimeSpan? timeout = null)
    {
        var field = typeof(App).GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("App._host was not found.");

        try
        {
            Run(() => field.SetValue(_application, host), timeout);
            Run(work, timeout);
        }
        finally
        {
            // Cleared even on failure: the host is disposed by the caller, and a
            // later test resolving services from a disposed provider would fail
            // somewhere far away from the cause.
            Run(() => field.SetValue(_application, null), timeout);
        }
    }

    /// <summary>
    /// Measures, arranges and pumps the element so styles, triggers, bindings, and
    /// item containers are all in the state a user would see.
    /// </summary>
    internal static void Realize(FrameworkElement element)
    {
        element.Measure(new Size(1400, 4000));
        element.Arrange(new Rect(0, 0, 1400, 4000));
        element.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
    }

    internal static void Realize(FrameworkElement element, double width, double height)
    {
        element.Width = width;
        element.Height = height;
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Finishes content created by a tab selection or queued scroll command before
    /// its geometry and bindings are inspected. Opt in only for those interactions.
    /// </summary>
    internal static void RealizePendingContent(FrameworkElement element, double width, double height)
    {
        Realize(element, width, height);
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Realize(element, width, height);
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>The first descendant of that type carrying that automation name.</summary>
    internal static T FindByAutomationName<T>(DependencyObject root, string name)
        where T : DependencyObject
    {
        var match = Descendants(root)
            .OfType<T>()
            .FirstOrDefault(candidate => AutomationProperties.GetName(candidate) == name);

        return match ?? throw new InvalidOperationException(
            $"No {typeof(T).Name} named \"{name}\" is present in the rendered tree.");
    }

    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static Dispatcher EnsureStarted()
    {
        lock (Gate)
        {
            if (_dispatcher is not null)
                return _dispatcher;

            var ready = new ManualResetEventSlim();
            Exception? startupFailure = null;

            var thread = new Thread(() =>
            {
                try
                {
                    // Read the canonical Application.Resources without compiled
                    // App code-behind: LoadComponent requires the root object's
                    // assembly to match, so it cannot initialize a test subclass.
                    _application = new HarnessApplication { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    _application.Resources = LoadApplicationResources();
                    _dispatcher = Dispatcher.CurrentDispatcher;
                }
                catch (Exception exception)
                {
                    startupFailure = exception;
                }
                finally
                {
                    ready.Set();
                }

                if (startupFailure is null)
                    Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "Sati.Tests WPF harness"
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();

            if (startupFailure is not null)
                throw new InvalidOperationException(
                    $"The application resource dictionary failed to load: {startupFailure.Message}",
                    startupFailure);

            return _dispatcher!;
        }
    }

    private static ResourceDictionary LoadApplicationResources()
    {
        var source = XDocument.Load(Path.Combine(RenderedViews.RepositoryRoot(), "App.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var resources = new XElement(source.Root!.Element(presentation + "Application.Resources")!
            .Elements().Single());
        foreach (var declaration in source.Root.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            resources.SetAttributeValue(declaration.Name, declaration.Value);
        using var markup = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            RenderedViews.QualifyDesktopNamespaces(resources.ToString())));
        return (ResourceDictionary)XamlReader.Load(markup, new ParserContext
        {
            BaseUri = new Uri("pack://application:,,,/Sati;component/App.xaml", UriKind.Absolute)
        });
    }

    private sealed class HarnessApplication : App
    {
        // Application's constructor posts startup to this dispatcher. Calling
        // App.OnStartup would acquire the user's single-instance guard and launch
        // the environment/login workflow, regardless of whether Run was called.
        protected override void OnStartup(StartupEventArgs e) { }
    }
}
