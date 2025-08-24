using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace WS_Setup_6.UI.Behaviors
{
    public static class DataGridSelectedItemsBehavior
    {
        public static readonly DependencyProperty SelectedItemsProperty =
            DependencyProperty.RegisterAttached(
                "SelectedItems",
                typeof(IList),
                typeof(DataGridSelectedItemsBehavior),
                new PropertyMetadata(null, OnSelectedItemsChanged));

        public static void SetSelectedItems(DependencyObject element, IList value)
            => element.SetValue(SelectedItemsProperty, value);

        public static IList GetSelectedItems(DependencyObject element)
            => (IList)element.GetValue(SelectedItemsProperty);

        private static void OnSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataGrid grid)
            {
                grid.SelectionChanged -= Grid_SelectionChanged;
                grid.SelectionChanged += Grid_SelectionChanged;

                // Hook into Loaded for first‑time sync from VM → DataGrid
                grid.Loaded -= Grid_Loaded;
                grid.Loaded += Grid_Loaded;

                // Also handle tab focus case (when switching tabs)
                var tabItem = FindParent<TabItem>(grid);
                if (tabItem != null)
                {
                    tabItem.GotFocus -= TabItem_GotFocus;
                    tabItem.GotFocus += TabItem_GotFocus;
                }
            }
        }

        private static void Grid_Loaded(object sender, RoutedEventArgs e)
            => SyncFromVmToGrid((DataGrid)sender);

        private static void TabItem_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TabItem tabItem)
                SyncFromVmToGrid(tabItem.Content as DataGrid ?? FindChild<DataGrid>(tabItem)!);
        }

        private static void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var grid = (DataGrid)sender;
            var boundList = GetSelectedItems(grid);
            if (boundList == null || boundList.IsReadOnly)
                return;

            boundList.Clear();
            foreach (var item in grid.SelectedItems)
                boundList.Add(item);

            System.Diagnostics.Debug.WriteLine($"[Behavior] Mirrored {boundList.Count} items into VM");
        }

        private static void SyncFromVmToGrid(DataGrid grid)
        {
            if (grid == null)
                return;

            var boundList = GetSelectedItems(grid);
            if (boundList == null)
                return;

            // Temporarily detach handler to avoid feedback loop
            grid.SelectionChanged -= Grid_SelectionChanged;
            grid.SelectedItems.Clear();
            foreach (var item in boundList)
                grid.SelectedItems.Add(item);
            grid.SelectionChanged += Grid_SelectionChanged;

            System.Diagnostics.Debug.WriteLine($"[Behavior] Hydrated DataGrid from VM ({boundList.Count} items)");
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
            while (parent != null && !(parent is T))
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            return parent as T;
        }

        private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                    return typedChild;
                var result = FindChild<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }
    }
}