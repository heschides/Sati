using System.Windows;
using System.Windows.Input;

namespace Sati.Helpers
{
    /// <summary>
    /// Makes a non-button element that people click behave like a button for everyone
    /// else: reachable in the tab order, activated by Enter and Space, and visible
    /// when it holds focus.
    /// </summary>
    /// <remarks>
    /// Sati builds several navigation strips and accordion headers out of
    /// <see cref="System.Windows.Controls.Border"/> because the selected state is
    /// carried by style triggers that a button template would have to reproduce. A
    /// bare <c>MouseBinding</c> on such an element is mouse-only: a Border is not
    /// focusable and answers no key, so a keyboard or screen-reader user cannot reach
    /// the control at all, and nothing in WPF reports that.
    /// <para>
    /// Attaching <see cref="CommandProperty"/> instead of an input binding gives all
    /// of it at once, so the four things that have to be right cannot be remembered
    /// three at a time. Set <c>AutomationProperties.Name</c> as well; this cannot
    /// invent one, and <c>AccessibilityAuditTests</c> fails when it is missing.
    /// </para>
    /// </remarks>
    public static class ClickableSurface
    {
        public static readonly DependencyProperty CommandProperty =
            DependencyProperty.RegisterAttached(
                "Command",
                typeof(ICommand),
                typeof(ClickableSurface),
                new PropertyMetadata(null, OnCommandChanged));

        public static readonly DependencyProperty CommandParameterProperty =
            DependencyProperty.RegisterAttached(
                "CommandParameter",
                typeof(object),
                typeof(ClickableSurface),
                new PropertyMetadata(null));

        public static ICommand? GetCommand(DependencyObject element) =>
            (ICommand?)element.GetValue(CommandProperty);

        public static void SetCommand(DependencyObject element, ICommand? value) =>
            element.SetValue(CommandProperty, value);

        public static object? GetCommandParameter(DependencyObject element) =>
            element.GetValue(CommandParameterProperty);

        public static void SetCommandParameter(DependencyObject element, object? value) =>
            element.SetValue(CommandParameterProperty, value);

        private static void OnCommandChanged(
            DependencyObject element, DependencyPropertyChangedEventArgs e)
        {
            if (element is not FrameworkElement surface)
                return;

            surface.MouseLeftButtonUp -= OnMouseLeftButtonUp;
            surface.KeyDown -= OnKeyDown;

            if (e.NewValue is null)
            {
                surface.Focusable = false;
                KeyboardNavigation.SetIsTabStop(surface, false);
                return;
            }

            // A Border ignores hit testing where it paints nothing, so a strip whose
            // resting background is Transparent still needs that brush rather than a
            // null one. That is the caller's business; this only makes it operable.
            surface.Focusable = true;
            KeyboardNavigation.SetIsTabStop(surface, true);
            surface.MouseLeftButtonUp += OnMouseLeftButtonUp;
            surface.KeyDown += OnKeyDown;

            if (surface.TryFindResource("ClickableSurfaceFocusVisual") is Style focusVisual)
                surface.FocusVisualStyle = focusVisual;
        }

        private static void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement surface && Execute(surface))
                e.Handled = true;
        }

        private static void OnKeyDown(object sender, KeyEventArgs e)
        {
            // Enter and Space are what a button answers, so this answers them too.
            if (e.Key is not (Key.Enter or Key.Space))
                return;

            if (sender is FrameworkElement surface && Execute(surface))
                e.Handled = true;
        }

        private static bool Execute(FrameworkElement surface)
        {
            var command = GetCommand(surface);
            var parameter = GetCommandParameter(surface);
            if (command?.CanExecute(parameter) != true)
                return false;

            command.Execute(parameter);
            return true;
        }
    }
}
