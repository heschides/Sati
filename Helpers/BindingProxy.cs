using System.Windows;

namespace Sati.Helpers
{
    /// <summary>
    /// A binding intermediary for targets that do not inherit DataContext
    /// (RowDefinition, ColumnDefinition, ContextMenu, Popup, DataGridColumn).
    ///
    /// Freezable is the key: unlike most DependencyObjects, a Freezable
    /// participates in the "inheritance context" of whatever holds it. When
    /// placed in a Resources dictionary, it receives the owning element's
    /// DataContext through that context. A binding can then name the proxy as
    /// its Source — so it no longer depends on tree inheritance to find data.
    ///
    /// Usage:
    ///   <helpers:BindingProxy x:Key="LayoutProxy" Data="{Binding}" />
    ///   ... Source={StaticResource LayoutProxy}, Path=Data.SomeProperty ...
    /// </summary>
    public class BindingProxy : Freezable
    {
        // Freezable requires this factory override. WPF calls it when it needs
        // a fresh, unfrozen clone of the object; returning a new instance is all
        // that's needed for a proxy that carries a single Data payload.
        protected override Freezable CreateInstanceCore() => new BindingProxy();

        public object Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        // Standard DependencyProperty registration. It must be a DP (not a
        // plain CLR property) so that Data="{Binding}" is a valid binding target.
        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(
                nameof(Data),
                typeof(object),
                typeof(BindingProxy),
                new UIPropertyMetadata(null));
    }
}