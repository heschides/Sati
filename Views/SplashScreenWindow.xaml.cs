using Sati.Helpers;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Sati.Views;

/// <summary>
/// The retained brand presentation and, on a preparation-mode instance, the opaque
/// surface shown while an authenticated workspace initializes.
/// </summary>
public partial class SplashScreenWindow : Window
{
    private static readonly TimeSpan ReflectionInterval = TimeSpan.FromSeconds(20);
    private readonly DispatcherTimer _activityTimer;
    private readonly DispatcherTimer? _reflectionTimer;
    private readonly Ellipse[] _dots;
    private readonly WorkspaceReflectionSequence? _reflectionSequence;
    private bool _allowPreparationClose;
    private bool _preparationClosed;
    private int _activeDot;

    /// <summary>Creates the brand presentation used by design tools and view tests.</summary>
    public SplashScreenWindow()
        : this(reflectionSequence: null)
    {
    }

    private SplashScreenWindow(WorkspaceReflectionSequence? reflectionSequence)
    {
        InitializeComponent();
        _reflectionSequence = reflectionSequence;

        if (reflectionSequence is null)
        {
            _dots = BrandingActivityDots.Children.OfType<Ellipse>().ToArray();
        }
        else
        {
            ConfigureWorkspacePreparation(reflectionSequence);
            _dots = PreparationActivityDots.Children.OfType<Ellipse>().ToArray();
            _reflectionTimer = new DispatcherTimer
            {
                Interval = ReflectionInterval
            };
            _reflectionTimer.Tick += OnReflectionTimerTick;
            _reflectionTimer.Start();
        }

        _activityTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _activityTimer.Tick += OnActivityTimerTick;
        SetActiveDot();
        _activityTimer.Start();

    }

    internal static SplashScreenWindow CreateWorkspacePreparation() =>
        new(WorkspaceReflections.CreateRandomSequence());

    /// <summary>Deterministic presentation seam for WPF and rotation tests.</summary>
    internal static SplashScreenWindow CreateWorkspacePreparation(int reflectionIndex) =>
        new(new WorkspaceReflectionSequence(reflectionIndex));

    /// <summary>
    /// Closes a preparation instance from the startup coordinator. Window chrome is
    /// intentionally absent, and an accidental Alt+F4 must not hide the only status
    /// surface while startup is still active.
    /// </summary>
    internal void CloseWorkspacePreparation()
    {
        if (_preparationClosed)
            return;

        _preparationClosed = true;
        _allowPreparationClose = true;
        Close();
    }

    private void ConfigureWorkspacePreparation(WorkspaceReflectionSequence sequence)
    {
        Title = "Preparing the Sati workspace";
        ShowInTaskbar = true;
        var surfaceSize = GetPreparationSurfaceSize(SystemParameters.WorkArea);
        WindowSurface.Width = surfaceSize.Width;
        WindowSurface.Height = surfaceSize.Height;
        BrandingContent.Visibility = Visibility.Collapsed;
        WorkspacePreparationContent.Visibility = Visibility.Visible;
        ShowReflection(sequence.Current);
        AutomationProperties.SetName(this, "Preparing the Sati workspace");
    }

    internal static Size GetPreparationSurfaceSize(Rect workArea)
    {
        const double workAreaMargin = 32;
        return new Size(
            Math.Max(1, Math.Min(820, workArea.Width - workAreaMargin)),
            Math.Max(1, Math.Min(550, workArea.Height - workAreaMargin)));
    }

    private void OnActivityTimerTick(object? sender, EventArgs e)
    {
        _activeDot = (_activeDot + 1) % _dots.Length;
        SetActiveDot();
    }

    private void SetActiveDot()
    {
        for (var index = 0; index < _dots.Length; index++)
            _dots[index].Opacity = index == _activeDot ? 1 : 0.3;
    }

    private void OnReflectionTimerTick(object? sender, EventArgs e)
    {
        if (_reflectionSequence is not null)
            ShowReflection(_reflectionSequence.MoveNext());
    }

    private void ShowReflection(string reflection)
    {
        ReflectionText.Text = reflection;
        // Keep the prose out of the live region so rotation never interrupts, but
        // expose the actual passage (not only its label) when a screen-reader user
        // navigates to it.
        AutomationProperties.SetName(ReflectionText, reflection);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_reflectionSequence is not null && !_allowPreparationClose)
        {
            e.Cancel = true;
            return;
        }

        _activityTimer.Stop();
        _reflectionTimer?.Stop();
        base.OnClosing(e);
    }
}
