using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Sati.Helpers
{
    /// <summary>
    /// Makes a data grid's context menu act on the row it was opened for.
    /// </summary>
    /// <remarks>
    /// The note grids' menu commands act on the selected note. A WPF
    /// <see cref="DataGrid"/> selects the row under the pointer when its menu opens, but
    /// it still opens the menu when that selection does not hold, and over the column
    /// header or empty space. Both then act on a note other than the one the case
    /// manager pointed at: the view model puts the old selection back when the case
    /// manager chooses to keep an unsaved draft, and a header has no note at all.
    /// With <see cref="SelectsRowProperty"/> set, the menu opens only when the row
    /// under the pointer is the selected note.
    /// <para>
    /// The keyboard menu (Shift+F10 or the Menu key) opens for the focused row, which
    /// is selected the same way.
    /// </para>
    /// </remarks>
    public static class RowContextMenu
    {
        public static readonly DependencyProperty SelectsRowProperty =
            DependencyProperty.RegisterAttached(
                "SelectsRow",
                typeof(bool),
                typeof(RowContextMenu),
                new PropertyMetadata(false, OnSelectsRowChanged));

        public static bool GetSelectsRow(DependencyObject element) =>
            (bool)element.GetValue(SelectsRowProperty);

        public static void SetSelectsRow(DependencyObject element, bool value) =>
            element.SetValue(SelectsRowProperty, value);

        private static void OnSelectsRowChanged(
            DependencyObject element, DependencyPropertyChangedEventArgs e)
        {
            if (element is not DataGrid grid)
                return;

            grid.ContextMenuOpening -= OnContextMenuOpening;
            if (e.NewValue is true)
                grid.ContextMenuOpening += OnContextMenuOpening;
        }

        internal static void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is not DataGrid grid)
                return;

            if (e.OriginalSource is not DependencyObject source ||
                FindRow(source, grid) is not { Item: var item })
            {
                e.Handled = true;
                return;
            }

            // The two-way SelectedItem binding carries this to the view model, which
            // may put the old selection back; read the result rather than assume it.
            grid.SelectedItem = item;
            if (!Equals(grid.SelectedItem, item))
                e.Handled = true;
        }

        private static DataGridRow? FindRow(DependencyObject source, DataGrid grid)
        {
            for (var current = source; current is not null && !ReferenceEquals(current, grid);
                 current = current is Visual or System.Windows.Media.Media3D.Visual3D
                     ? VisualTreeHelper.GetParent(current)
                     : LogicalTreeHelper.GetParent(current))
            {
                if (current is DataGridRow row)
                    return row;
            }

            return null;
        }
    }
}
