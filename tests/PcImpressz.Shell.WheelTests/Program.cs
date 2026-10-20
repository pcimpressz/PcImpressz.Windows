using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PcImpressz.Shell.Windows;

namespace PcImpressz.Shell.WheelTests;

static class Program
{
    [STAThread]
    static int Main()
    {
        var code = 1;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += (_, _) =>
        {
            try
            {
                MouseWheelRouting.Enable();
                StuckInnerPassesToPage();
                InnerThatCanMoveKeepsTheWheel();
                TextBoxPassesToPage();
                ShortGridPassesToPage();
                code = 0;
                Console.WriteLine("wheel-ok");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
            app.Shutdown(code);
        };
        return app.Run();
    }

    static void StuckInnerPassesToPage()
    {
        var page = Viewer(100);
        var inner = Viewer(300);
        var hit = new Border { Height = 300, Background = Brushes.White };
        inner.Content = hit;
        page.Content = inner;
        using var _ = Show(page);
        Expect(page.ScrollableHeight > 0, "az oldalnak görgethetőnek kell lennie");
        Expect(inner.ScrollableHeight == 0, "a belső nézetnek ki kell töltenie a saját keretét");
        Wheel(hit, -120);
        Expect(page.VerticalOffset > 0, "a beragadt belső görgető a lapot görgeti");
    }

    static void InnerThatCanMoveKeepsTheWheel()
    {
        var page = Viewer(100);
        var inner = Viewer(80);
        var hit = new Border { Height = 400, Background = Brushes.White };
        inner.Content = hit;
        page.Content = new StackPanel { Children = { inner, new Border { Height = 400 } } };
        using var _ = Show(page);
        Expect(inner.ScrollableHeight > 0, "a belső lista görgethető");
        Expect(page.ScrollableHeight > 0, "a lap is görgethető");
        Wheel(hit, -120);
        Expect(inner.VerticalOffset > 0, "a görgő a belső listán marad");
        Expect(page.VerticalOffset == 0, "a lap addig nem mozdul, amíg a belső lista tud görgetni");
    }

    static void TextBoxPassesToPage()
    {
        var page = Viewer(80);
        var box = new TextBox { Width = 140, Text = "egy sor" };
        page.Content = new StackPanel { Children = { box, new Border { Height = 500, Background = Brushes.White } } };
        using var _ = Show(page);
        var host = Find<ScrollViewer>(box) ?? throw new InvalidOperationException("a szövegmezőnek nincs görgetője");
        Expect(host.ScrollableHeight == 0, "az egysoros mező nem görget");
        Wheel(host, -120);
        Expect(page.VerticalOffset > 0, "a szövegmező fölött a lap görget");
    }

    static void ShortGridPassesToPage()
    {
        var page = Viewer(90);
        var grid = new DataGrid
        {
            Height = 160, AutoGenerateColumns = true, HeadersVisibility = DataGridHeadersVisibility.Column,
            ItemsSource = new[] { new Row("egy"), new Row("kettő") }
        };
        page.Content = new StackPanel { Children = { grid, new Border { Height = 500, Background = Brushes.White } } };
        using var _ = Show(page);
        var host = Find<ScrollViewer>(grid) ?? throw new InvalidOperationException("a táblázatnak nincs görgetője");
        Expect(host.ScrollableHeight == 0, "a rövid táblázat nem görget magában");
        var hit = Find<DataGridCell>(grid) ?? (UIElement)host;
        Wheel(hit, -120);
        Expect(page.VerticalOffset > 0, "a rövid táblázat fölött a lap görget");
    }

    static ScrollViewer Viewer(double height) => new()
    {
        Height = height, Width = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    static IDisposable Show(FrameworkElement root)
    {
        var window = new Window
        {
            Width = 360, Height = 280, WindowStyle = WindowStyle.None,
            ShowInTaskbar = false, ShowActivated = false, Content = root
        };
        window.Show();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        root.UpdateLayout();
        return new Closer(window);
    }

    static void Wheel(UIElement source, int delta)
    {
        var preview = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
        {
            RoutedEvent = UIElement.PreviewMouseWheelEvent
        };
        source.RaiseEvent(preview);
        if (!preview.Handled)
            source.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent
            });
        source.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }

    static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var nested = Find<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    sealed class Closer(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }

    sealed record Row(string Name);
}
