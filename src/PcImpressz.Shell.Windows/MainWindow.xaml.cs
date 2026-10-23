using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PcImpressz.Shell.Windows;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private readonly VisualLayoutEditor _visualEditor;
    private bool _checkingClose,_closeApproved;
    public MainWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        _visualEditor = new VisualLayoutEditor(this);
        DataContext = viewModel;
        Width = Math.Clamp(viewModel.InitialWidth, MinWidth, Math.Max(MinWidth, SystemParameters.WorkArea.Width));
        Height = Math.Clamp(viewModel.InitialHeight, MinHeight, Math.Max(MinHeight, SystemParameters.WorkArea.Height));
        SourceInitialized += (_, _) => ApplyCaptionChrome();
        ThemeManager.Applied += ApplyCaptionChrome;
        Closed += (_, _) => ThemeManager.Applied -= ApplyCaptionChrome;
        Loaded += async (_, _) => await viewModel.StartAsync();
        Closing += OnClosing;
    }

    private void ApplyCaptionChrome()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || Application.Current is null) return;
        if (Application.Current.Resources["SidebarBrush"] is not SolidColorBrush sidebar) return;
        if (Application.Current.Resources["TextBrush"] is not SolidColorBrush text) return;
        var caption = ColorRef(sidebar.Color);
        var letters = ColorRef(text.Color);
        var dark = IsLight(text.Color) ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
        DwmSetWindowAttribute(hwnd, 35, ref caption, 4);
        DwmSetWindowAttribute(hwnd, 36, ref letters, 4);
        var border = Application.Current.Resources["StrokeBrush"] is SolidColorBrush stroke ? ColorRef(stroke.Color) : caption;
        DwmSetWindowAttribute(hwnd, 34, ref border, 4);
    }

    private static int ColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    private static bool IsLight(Color color)
    {
        static double Channel(byte value)
        {
            var amount = value / 255d;
            return amount <= 0.04045 ? amount / 12.92 : Math.Pow((amount + 0.055) / 1.055, 2.4);
        }
        var luminance = 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        return luminance >= 0.45;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if(!_closeApproved)
        {
            e.Cancel=true;if(_checkingClose)return;_checkingClose=true;
            try
            {
                var flush=_viewModel.FlushInvoicePreparationsAsync();var completed=flush.IsCompletedSuccessfully;
                if(!await flush||!_viewModel.CanClose())return;
                _closeApproved=true;
                if(!completed){_=Dispatcher.BeginInvoke(new Action(Close));return;}
                e.Cancel=false;
            }
            finally{_checkingClose=false;}
            if(e.Cancel)return;
        }
        e.Cancel=false;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _viewModel.SaveWindow(bounds.Width, bounds.Height);
        _viewModel.Stop();
    }
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
