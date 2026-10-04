using System.Windows.Controls;
using System.Windows;
using System.Windows.Threading;
using Sati.ViewModels.Children;

namespace Sati.Views
{
    /// <summary>
    /// The consumer's medical provider list. Its DataContext is a
    /// <see cref="ViewModels.Children.ConsumerProvidersViewModel"/>, supplied by whatever
    /// hosts it — the consumer profile today.
    /// </summary>
    public partial class ConsumerProvidersView : UserControl
    {
        public ConsumerProvidersView()
        {
            InitializeComponent();
        }

        private void OrderMoveClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not ConsumerProviderRowViewModel row ||
                DataContext is not ConsumerProvidersViewModel panel)
                return;
            // Button raises Click before executing its command. Restore focus after Move
            // and its boundary checks, preferring the same action on the same provider.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (!ReferenceEquals(DataContext, panel) || !panel.Current.Contains(row)) return;
                var originalContainer = FindContainer(button);
                if (originalContainer is null) return;
                var list = ItemsControl.ItemsControlFromItemContainer(originalContainer);
                if (list is null) return;
                var container = list.ItemContainerGenerator.ContainerFromItem(row);
                if (container is null) return;
                var candidates = Descendants(container).OfType<Button>()
                    .Where(candidate => candidate.IsEnabled && candidate.CommandParameter == row &&
                        (candidate.Command == panel.MoveUpCommand || candidate.Command == panel.MoveDownCommand)).ToArray();
                (candidates.FirstOrDefault(candidate => Equals(candidate.Content, button.Content)) ?? candidates.FirstOrDefault())?.Focus();
            }));
        }

        private static DependencyObject? FindContainer(DependencyObject item)
        {
            for (var current = item; current is not null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
                if (current is ContentPresenter presenter && ItemsControl.ItemsControlFromItemContainer(presenter) is not null)
                    return presenter;
            return null;
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject item)
        {
            for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(item); index++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(item, index);
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }
    }
}
