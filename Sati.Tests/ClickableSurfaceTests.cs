using Sati.Helpers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The navigation strips and accordion headers Sati builds from a
/// <see cref="Border"/> are operated through <see cref="ClickableSurface"/>. If it
/// stopped activating, every one of those would still look right and do nothing, so
/// this exercises the real routed events rather than the handlers.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class ClickableSurfaceTests
{
    [Fact]
    public void AttachingACommandPutsTheSurfaceInTheTabOrder()
    {
        WpfUiHarness.Run(() =>
        {
            var border = new Border();
            Assert.False(border.Focusable);

            ClickableSurface.SetCommand(border, new RecordingCommand());

            Assert.True(border.Focusable, "A clickable surface must be able to take focus.");
            Assert.True(KeyboardNavigation.GetIsTabStop(border),
                "A clickable surface must be reachable with Tab.");
        });
    }

    [Fact]
    public void ClearingTheCommandTakesTheSurfaceBackOutOfTheTabOrder()
    {
        WpfUiHarness.Run(() =>
        {
            var border = new Border();
            ClickableSurface.SetCommand(border, new RecordingCommand());
            ClickableSurface.SetCommand(border, null);

            // A surface that no longer does anything must not be a stop on the way to
            // the controls that do.
            Assert.False(border.Focusable);
            Assert.False(KeyboardNavigation.GetIsTabStop(border));
        });
    }

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    public void EnterAndSpaceActivateTheSurfaceTheWayTheyActivateAButton(Key key)
    {
        WpfUiHarness.Run(() =>
        {
            var command = new RecordingCommand();
            var border = new Border();
            ClickableSurface.SetCommand(border, command);

            using var source = Host(border);
            border.RaiseEvent(new KeyEventArgs(
                Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });

            Assert.Equal(1, command.Executions);
        });
    }

    [Fact]
    public void AKeyThatIsNotEnterOrSpaceLeavesTheSurfaceAlone()
    {
        WpfUiHarness.Run(() =>
        {
            var command = new RecordingCommand();
            var border = new Border();
            ClickableSurface.SetCommand(border, command);

            using var source = Host(border);
            border.RaiseEvent(new KeyEventArgs(
                Keyboard.PrimaryDevice, source, 0, Key.A) { RoutedEvent = Keyboard.KeyDownEvent });

            // Typing must still reach whatever is inside the surface.
            Assert.Equal(0, command.Executions);
        });
    }

    [Fact]
    public void ClickingStillWorksAndCarriesTheParameter()
    {
        WpfUiHarness.Run(() =>
        {
            var command = new RecordingCommand();
            var border = new Border();
            ClickableSurface.SetCommand(border, command);
            ClickableSurface.SetCommandParameter(border, "row-7");

            border.RaiseEvent(new MouseButtonEventArgs(
                Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonUpEvent });

            Assert.Equal(1, command.Executions);
            Assert.Equal("row-7", command.LastParameter);
        });
    }

    [Fact]
    public void ASurfaceWhoseCommandRefusesDoesNothingAndDoesNotSwallowTheKey()
    {
        WpfUiHarness.Run(() =>
        {
            var command = new RecordingCommand { Allowed = false };
            var border = new Border();
            ClickableSurface.SetCommand(border, command);

            using var source = Host(border);
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter)
            { RoutedEvent = Keyboard.KeyDownEvent };
            border.RaiseEvent(args);

            Assert.Equal(0, command.Executions);
            // Left unhandled so an ancestor still gets its chance at the key.
            Assert.False(args.Handled);
        });
    }

    /// <summary>
    /// A key event needs a presentation source. This is an unshown native window, so
    /// nothing appears on screen and the harness keeps its no-windows rule.
    /// </summary>
    private static HwndSource Host(UIElement element)
    {
        var source = new HwndSource(new HwndSourceParameters("sati-a11y-probe")
        {
            Width = 10,
            Height = 10,
        });
        source.RootVisual = element;
        return source;
    }

    private sealed class RecordingCommand : ICommand
    {
        internal int Executions { get; private set; }
        internal object? LastParameter { get; private set; }
        internal bool Allowed { get; init; } = true;

        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => Allowed;

        public void Execute(object? parameter)
        {
            Executions++;
            LastParameter = parameter;
        }
    }
}
