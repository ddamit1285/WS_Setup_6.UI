using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace WS_Setup_6.UI.Behaviors
{
    public static class ListBoxExtensions
    {
        public static readonly DependencyProperty AutoScrollToEndProperty =
          DependencyProperty.RegisterAttached(
            "AutoScrollToEnd",
            typeof(bool),
            typeof(ListBoxExtensions),
            new PropertyMetadata(false, OnAutoScrollChanged));

        public static void SetAutoScrollToEnd(DependencyObject d, bool value) =>
            d.SetValue(AutoScrollToEndProperty, value);

        public static bool GetAutoScrollToEnd(DependencyObject d) =>
            (bool)d.GetValue(AutoScrollToEndProperty);

        private static void OnAutoScrollChanged(
            DependencyObject d,
            DependencyPropertyChangedEventArgs e)
        {
            if (d is ListBox lb && (bool)e.NewValue)
            {
                ((INotifyCollectionChanged)lb.Items).CollectionChanged += (_, args) =>
                {
                    if (args.Action == NotifyCollectionChangedAction.Add && args.NewItems != null && args.NewItems.Count > 0)
                    {
                        var last = args.NewItems.Cast<object>().Last();
                        lb.Dispatcher.BeginInvoke(
                          () => lb.ScrollIntoView(last),
                                  DispatcherPriority.Background);
                    }
                };
            }
        }
    }
}