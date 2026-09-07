using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// What a screen reader and a keyboard actually get from Sati's windows.
/// </summary>
/// <remarks>
/// Narrator, JAWS and NVDA all read a WPF application through UI Automation, and
/// what they announce for a control is its <see cref="AutomationPeer"/> name, not the
/// text that happens to sit nearby. So this asks the peers, exactly as the screen
/// reader would, rather than reading the markup and inferring. TalkBack does not
/// apply here: it is Android, and this is the desktop client.
/// <para>
/// An automated pass cannot replace sitting down with a screen reader. It proves
/// that every control has a name, that nothing clickable is mouse-only, and that
/// focus is never made invisible. It cannot judge whether the name is a good one,
/// whether the reading order makes sense as a sentence, or whether a live region
/// fires at a useful moment. Those remain hands-on work; see AGENDA.md.
/// </para>
/// </remarks>
[Collection(WpfViewCollection.Name)]
public sealed class AccessibilityAuditTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    /// <summary>
    /// Controls a screen reader stops on and announces. A name is not optional on
    /// any of these; without one the user hears only the control type.
    /// </summary>
    private static bool NeedsAName(DependencyObject element) => element switch
    {
        // Item containers take their name from the bound item, which is absent when a
        // view is loaded without a view model, so they cannot be judged here.
        ListBoxItem or ComboBoxItem or TreeViewItem or DataGridRow => false,
        ButtonBase and not ToggleButton => true,
        TextBox or PasswordBox or ComboBox or DatePicker or Slider => true,
        CheckBox or RadioButton or TabItem or ListBox or DataGrid => true,
        _ => false,
    };

    /// <summary>
    /// Pieces of a framework control template rather than controls Sati wrote. The
    /// scroll bar's arrows and a DataGrid's filler header are named by convention
    /// with a PART_ prefix, sit inside a template Sati does not own, and are not
    /// what a screen reader user is navigating between.
    /// </summary>
    private static bool IsFrameworkTemplatePart(FrameworkElement control)
    {
        if (control.Name.StartsWith("PART_", StringComparison.Ordinal))
            return true;

        for (var node = VisualTreeHelper.GetParent(control); node is not null;
             node = VisualTreeHelper.GetParent(node))
        {
            if (node is ScrollBar)
                return true;
        }

        return false;
    }

    /// <summary>
    /// A control whose name is supplied by a binding. Views load here without a view
    /// model, so the binding produces nothing and the peer falls back to the content,
    /// which for an icon button is the glyph. The markup has done its part; whether
    /// the bound text is any good is a question for a live session.
    /// </summary>
    private static bool NameComesFromABinding(FrameworkElement control)
    {
        static bool Bound(DependencyObject target, DependencyProperty property) =>
            System.Windows.Data.BindingOperations.GetBindingExpressionBase(target, property) is not null;

        if (Bound(control, System.Windows.Automation.AutomationProperties.NameProperty))
            return true;

        // A button with no explicit name is announced by its content, and a label
        // bound to the view model is empty here for the same reason. Both are the
        // markup doing its job; neither can be judged without a live session.
        return control is ContentControl && Bound(control, ContentControl.ContentProperty);
    }

    [Fact]
    public void EveryControlAScreenReaderStopsOnHasANameToAnnounce()
    {
        WpfUiHarness.Run(() =>
        {
            var failures = new List<string>();

            foreach (var file in RenderedViews.Files())
            {
                var root = RenderedViews.TryLoad(file);
                if (root is null)
                    continue;

                WpfUiHarness.Realize(root);
                foreach (var element in WpfUiHarness.Descendants(root))
                {
                    if (!NeedsAName(element) || element is not FrameworkElement control)
                        continue;

                    if (IsFrameworkTemplatePart(control))
                        continue;

                    if (NameComesFromABinding(control))
                        continue;

                    // A control inside a collapsed branch is still reachable once that
                    // branch opens, but one that never renders cannot be measured.
                    if (control.ActualWidth <= 0 && control.ActualHeight <= 0)
                        continue;

                    var announced = Announced(control);
                    if (!string.IsNullOrWhiteSpace(announced))
                        continue;

                    failures.Add($"  {Path.GetFileName(file)}  {control.GetType().Name}"
                        + $"{Describe(control)}");
                }
            }

            Assert.True(failures.Count == 0,
                $"{failures.Count} controls announce no name:{Environment.NewLine}"
                + string.Join(Environment.NewLine, failures.Distinct().OrderBy(line => line)));
        });
    }

    [Fact]
    public void NoControlIsAnnouncedOnlyAsAnIconGlyph()
    {
        WpfUiHarness.Run(() =>
        {
            var failures = new List<string>();

            foreach (var file in RenderedViews.Files())
            {
                var root = RenderedViews.TryLoad(file);
                if (root is null)
                    continue;

                WpfUiHarness.Realize(root);
                foreach (var element in WpfUiHarness.Descendants(root))
                {
                    if (!NeedsAName(element) || element is not FrameworkElement control)
                        continue;

                    if (IsFrameworkTemplatePart(control))
                        continue;

                    if (NameComesFromABinding(control))
                        continue;

                    var announced = Announced(control);
                    if (string.IsNullOrWhiteSpace(announced) || !IsGlyphOnly(announced))
                        continue;

                    // Segoe MDL2 icons live in the Unicode private use area. A button
                    // whose content is one of those has a technically non-empty name
                    // that a screen reader reads as nothing, or as a wrong character.
                    failures.Add($"  {Path.GetFileName(file)}  {control.GetType().Name} "
                        + $"announces U+{(int)announced.Trim()[0]:X4}{Describe(control)}");
                }
            }

            Assert.True(failures.Count == 0,
                $"{failures.Count} controls announce only an icon glyph:{Environment.NewLine}"
                + string.Join(Environment.NewLine, failures.Distinct().OrderBy(line => line)));
        });
    }

    [Fact]
    public void EveryClickableSurfaceSaysWhatItIs()
    {
        var failures = new List<string>();

        foreach (var file in RenderedViews.Files())
        {
            foreach (var element in XDocument.Parse(File.ReadAllText(file)).Descendants())
            {
                var command = element.Attributes()
                    .FirstOrDefault(a => a.Name.LocalName == "ClickableSurface.Command");
                if (command is null)
                    continue;

                // The helper supplies focus, the tab stop and key activation. The one
                // thing it cannot invent is what the surface should be called.
                var name = (string?)element.Attribute("AutomationProperties.Name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    failures.Add($"  {Path.GetFileName(file)}  <{element.Name.LocalName}> "
                        + $"is clickable via {command.Value} but announces no name");
                }
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} clickable surfaces have no name:{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures.OrderBy(line => line)));
    }

    [Fact]
    public void NothingClickableIsReachableOnlyWithAMouse()
    {
        var failures = new List<string>();

        foreach (var file in RenderedViews.Files())
        {
            var document = XDocument.Parse(File.ReadAllText(file));

            foreach (var bindings in document.Descendants()
                .Where(element => element.Name.LocalName.EndsWith(".InputBindings", StringComparison.Ordinal)))
            {
                var owner = bindings.Parent;
                if (owner is null)
                    continue;

                // A Button already answers Space and Enter and is in the tab order. It
                // is every other element that has to opt in by hand.
                if (owner.Name.LocalName is "Button" or "ToggleButton" or "RadioButton"
                    or "CheckBox" or "MenuItem" or "ListBoxItem")
                {
                    continue;
                }

                var mouseOnly = bindings.Elements(Presentation + "MouseBinding").Any();
                if (!mouseOnly)
                    continue;

                var focusable = (string?)owner.Attribute("Focusable") == "True";
                var tabStop = (string?)owner.Attribute("KeyboardNavigation.IsTabStop") == "True";
                var named = !string.IsNullOrWhiteSpace(
                    (string?)owner.Attribute("AutomationProperties.Name"));
                var keys = bindings.Elements(Presentation + "KeyBinding")
                    .Select(element => (string?)element.Attribute("Key"))
                    .ToList();
                var activatable = keys.Contains("Enter") || keys.Contains("Space");

                if (focusable && tabStop && named && activatable)
                    continue;

                var missing = new List<string>();
                if (!focusable) missing.Add("Focusable");
                if (!tabStop) missing.Add("IsTabStop");
                if (!named) missing.Add("a name");
                if (!activatable) missing.Add("an Enter or Space binding");

                failures.Add($"  {Path.GetFileName(file)}  <{owner.Name.LocalName}> "
                    + $"has a mouse binding but no {string.Join(", ", missing)}");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} elements can be clicked but not operated from the keyboard:"
            + Environment.NewLine + string.Join(Environment.NewLine, failures.OrderBy(line => line)));
    }

    [Fact]
    public void FocusIsNeverMadeInvisible()
    {
        var offenders = RenderedViews.Files()
            .Append(Path.Combine(RenderedViews.RepositoryRoot(), "App.xaml"))
            .Where(file => File.ReadAllText(file)
                .Contains("FocusVisualStyle=\"{x:Null}\"", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        // Removing the focus visual leaves a keyboard user with no way to tell where
        // they are. Sati's own styles draw their own focus ring instead.
        Assert.True(offenders.Count == 0,
            "These files switch the focus visual off: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TabOrderFollowsTheVisualOrderRatherThanAHandWrittenSequence()
    {
        var offenders = RenderedViews.Files()
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"\bTabIndex\s*="))
            .Select(Path.GetFileName)
            .ToList();

        // WPF tabs in declaration order, which matches the reading order as long as
        // nobody renumbers by hand. A single explicit TabIndex silently sends every
        // unnumbered control in the same scope to the end, so this stays at zero
        // unless a view has a reason worth writing down here.
        Assert.True(offenders.Count == 0,
            "These files set TabIndex by hand: " + string.Join(", ", offenders));
    }

    [Fact]
    public void StatusTextThatChangesByItselfIsAnnouncedWithoutStealingFocus()
    {
        var announced = 0;

        foreach (var file in RenderedViews.Files())
        {
            foreach (var element in XDocument.Parse(File.ReadAllText(file)).Descendants())
            {
                var setting = (string?)element.Attribute("AutomationProperties.LiveSetting");
                if (setting is null)
                    continue;

                announced++;
                Assert.True(setting is "Polite" or "Assertive",
                    $"{Path.GetFileName(file)} sets an unknown LiveSetting '{setting}'.");
            }
        }

        // A live region is how a result reaches a screen reader when nothing moved the
        // focus. Sati announces saves, sign-in errors and background progress this way,
        // so the count going to zero would mean that channel had been dismantled.
        Assert.True(announced >= 20,
            $"Only {announced} live regions remain; status changes reach a screen reader "
            + "through these, and nothing else announces them.");
    }

    private static string? Announced(FrameworkElement control) =>
        UIElementAutomationPeer.CreatePeerForElement(control)?.GetName();

    private static bool IsGlyphOnly(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is 0 or > 2)
            return false;

        // Private use area, where icon fonts put their glyphs.
        return trimmed.All(character => character is >= '\uE000' and <= '\uF8FF');
    }

    private static string Describe(FrameworkElement control)
    {
        var name = control.Name;
        var help = System.Windows.Automation.AutomationProperties.GetHelpText(control);
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(name)) parts.Add($"x:Name={name}");
        if (!string.IsNullOrEmpty(help)) parts.Add($"help=\"{help}\"");
        return parts.Count == 0 ? string.Empty : "  (" + string.Join("; ", parts) + ")";
    }
}
