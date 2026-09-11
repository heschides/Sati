using Sati.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class ScratchpadThemeTests
{
    [Fact]
    public void LiveTabSlidesUseWritablePerTransitionTransforms()
    {
        WpfUiHarness.Run(() =>
        {
            var view = new ScratchpadView { IsHistoryAvailable = true };
            var window = new Window
            {
                Content = view,
                Width = 820,
                Height = 700,
                Left = -32000,
                Top = -32000,
                ShowActivated = false,
                ShowInTaskbar = false
            };

            try
            {
                window.Show();
                window.UpdateLayout();
                Assert.True(view.IsLoaded);

                var tabs = Assert.Single(
                    WpfUiHarness.Descendants(view).OfType<TabControl>());
                tabs.SelectedIndex = 1;
                Dispatcher.CurrentDispatcher.Invoke(
                    () => { }, DispatcherPriority.ApplicationIdle);

                var presenter = Assert.IsType<ContentPresenter>(
                    tabs.Template.FindName("AgendaContentHost", tabs));
                var firstTransform = Assert.IsType<TranslateTransform>(
                    presenter.RenderTransform);
                Assert.False(firstTransform.IsFrozen);
                Assert.True(firstTransform.HasAnimatedProperties);

                tabs.SelectedIndex = 2;
                Dispatcher.CurrentDispatcher.Invoke(
                    () => { }, DispatcherPriority.ApplicationIdle);

                var secondTransform = Assert.IsType<TranslateTransform>(
                    presenter.RenderTransform);
                Assert.NotSame(firstTransform, secondTransform);
                Assert.False(secondTransform.IsFrozen);
                Assert.True(secondTransform.HasAnimatedProperties);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void HistoryIsAThirdOverviewOnlyTab()
    {
        WpfUiHarness.Run(() =>
        {
            var view = new ScratchpadView { IsHistoryAvailable = true };
            WpfUiHarness.Realize(view, 820, 700);
            var tabs = Assert.Single(WpfUiHarness.Descendants(view).OfType<TabControl>());

            Assert.Equal(3, tabs.Items.Count);
            var history = Assert.IsType<TabItem>(tabs.Items[2]);
            Assert.Equal("HISTORY", history.Header);
            Assert.Equal(Visibility.Visible, history.Visibility);

            tabs.SelectedIndex = 2;
            view.IsHistoryAvailable = false;

            Assert.Equal(Visibility.Collapsed, history.Visibility);
            Assert.Equal(0, tabs.SelectedIndex);
        });
    }

    [Fact]
    public void BothScratchpadEditorsUseTheDarkThemesPrimaryTextAndCaretColors()
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
                dictionaries[themeIndex] = new ResourceDictionary
                {
                    Source = new Uri(
                        "/Sati;component/Themes/HarborNight.xaml",
                        UriKind.Relative)
                };
                var view = new ScratchpadView();
                WpfUiHarness.Realize(view);
                var expected = Assert.IsType<SolidColorBrush>(
                    Application.Current.FindResource("TextPrimaryBrush"));

                void AssertEditor(string name)
                {
                    var editor = WpfUiHarness.FindByAutomationName<TextBox>(view, name);
                    Assert.Equal(expected.Color, Assert.IsType<SolidColorBrush>(editor.Foreground).Color);
                    Assert.Equal(expected.Color, Assert.IsType<SolidColorBrush>(editor.CaretBrush).Color);
                }

                AssertEditor("Today's Work freeform scratchpad");
                var tabs = Assert.Single(
                    WpfUiHarness.Descendants(view).OfType<TabControl>());
                tabs.SelectedIndex = 1;
                WpfUiHarness.Realize(view);
                AssertEditor("Tomorrow's Agenda for the next workday");
            }
            finally
            {
                dictionaries[themeIndex] = originalTheme;
            }
        });
    }
}
