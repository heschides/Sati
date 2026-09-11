using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Sati.Services;
using Sati.ViewModels.Children;

namespace Sati.Views
{
    public partial class ScratchpadView : UserControl
    {
        public static readonly DependencyProperty IsHistoryAvailableProperty =
            DependencyProperty.Register(
                nameof(IsHistoryAvailable),
                typeof(bool),
                typeof(ScratchpadView),
                new FrameworkPropertyMetadata(false, OnIsHistoryAvailableChanged));

        private int _lastSelectedTabIndex;
        private int _tabAnimationVersion;

        public ScratchpadView()
        {
            InitializeComponent();
        }

        public bool IsHistoryAvailable
        {
            get => (bool)GetValue(IsHistoryAvailableProperty);
            set => SetValue(IsHistoryAvailableProperty, value);
        }

        private static void OnIsHistoryAvailableChanged(
            DependencyObject sender,
            DependencyPropertyChangedEventArgs e)
        {
            if (sender is ScratchpadView view && e.NewValue is false)
                view.AgendaTabs.SelectedIndex = 0;
        }

        private async void AgendaTabs_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.Source, AgendaTabs))
                return;

            var selectedIndex = AgendaTabs.SelectedIndex;
            var previousIndex = _lastSelectedTabIndex;
            _lastSelectedTabIndex = selectedIndex;
            QueueTabAnimation(previousIndex, selectedIndex);

            if (selectedIndex == 2 &&
                IsHistoryAvailable &&
                DataContext is ScratchpadViewModel viewModel)
            {
                await viewModel.History.InitializeAsync();
                HistoryView.SynchronizeCalendarSelection();
            }
        }

        private void QueueTabAnimation(int previousIndex, int selectedIndex)
        {
            if (previousIndex == selectedIndex ||
                !IsLoaded ||
                !SystemParameters.ClientAreaAnimation)
                return;

            var version = ++_tabAnimationVersion;
            Dispatcher.BeginInvoke(() =>
            {
                if (version != _tabAnimationVersion)
                    return;

                AgendaTabs.ApplyTemplate();
                if (AgendaTabs.Template.FindName("AgendaContentHost", AgendaTabs)
                    is not ContentPresenter presenter)
                {
                    return;
                }

                var width = presenter.ActualWidth > 0
                    ? presenter.ActualWidth
                    : AgendaTabs.ActualWidth;
                if (!double.IsFinite(width) || width <= 0)
                    return;

                var direction = selectedIndex > previousIndex ? 1d : -1d;
                // A transform declared inside a reusable WPF template can be
                // frozen for performance. Give each transition its own writable
                // instance instead of trying to animate that shared object.
                var transform = new TranslateTransform();
                presenter.RenderTransform = transform;

                var slide = new DoubleAnimation
                {
                    From = direction * width,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(175),
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                };
                transform.BeginAnimation(TranslateTransform.XProperty, slide);
            }, DispatcherPriority.Loaded);
        }

        private void AgendaBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter &&
                (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                var timestamp = DateTime.Now.ToString("h:mm tt");
                var divider = $"\n\n ─── {timestamp} ───────────────────\n\n";

                var box = (TextBox)sender;
                var caretIndex = box.CaretIndex;
                box.Text = box.Text.Insert(caretIndex, divider);
                box.CaretIndex = caretIndex + divider.Length;

                e.Handled = true;
            }
        }
        private void ScheduledWorkList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // A Start button is already a single-click action. Letting its second
            // click bubble into the row gesture could ask to open the same item
            // twice while the first navigation is still in flight.
            if (FindVisualAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
                return;

            if (sender is not ListBox list ||
                ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject)
                    is not ListBoxItem container ||
                container.DataContext is not WorkAgendaItem item ||
                DataContext is not ScratchpadViewModel viewModel)
            {
                return;
            }

            if (viewModel.OpenScheduledWorkCommand.CanExecute(item))
                viewModel.OpenScheduledWorkCommand.Execute(item);
            e.Handled = true;
        }

        private static T? FindVisualAncestor<T>(DependencyObject? source)
            where T : DependencyObject
        {
            while (source is not null)
            {
                if (source is T match)
                    return match;
                source = source switch
                {
                    Visual or Visual3D => VisualTreeHelper.GetParent(source),
                    FrameworkContentElement content => content.Parent,
                    _ => LogicalTreeHelper.GetParent(source)
                };
            }

            return null;
        }

        private void ScheduledWorkList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter ||
                sender is not ListBox { SelectedItem: WorkAgendaItem item } ||
                DataContext is not ScratchpadViewModel viewModel)
            {
                return;
            }

            if (viewModel.OpenScheduledWorkCommand.CanExecute(item))
                viewModel.OpenScheduledWorkCommand.Execute(item);
            e.Handled = true;
        }
    }
}
