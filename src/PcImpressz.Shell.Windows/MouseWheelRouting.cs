using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PcImpressz.Shell.Windows;

// A ScrollViewer marks the wheel as handled even when it has nothing left to scroll.
// Nested viewers (page, grid, text box) then swallow the wheel, so only the outer bar moves.
static class MouseWheelRouting
{
    private static int _enabled;

    public static void Enable()
    {
        if (Interlocked.Exchange(ref _enabled, 1) != 0) return;
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(Preview));
    }

    private static void Preview(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0 || sender is not ScrollViewer current || CanScroll(current, e.Delta)) return;
        var parent = Parent(current);
        while (parent is not null && !CanScroll(parent, e.Delta)) parent = Parent(parent);
        if (parent is null) return;
        e.Handled = true;
        parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = parent
        });
    }

    private static bool CanScroll(ScrollViewer scroll, int delta) =>
        delta > 0 ? scroll.VerticalOffset > 0 : scroll.VerticalOffset < scroll.ScrollableHeight;

    private static ScrollViewer? Parent(DependencyObject start)
    {
        for (var node = VisualTreeHelper.GetParent(start); node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is ScrollViewer viewer) return viewer;
        return null;
    }
}
