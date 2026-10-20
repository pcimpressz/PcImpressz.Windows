using System.ComponentModel;
using System.Windows;

namespace PcImpressz.Shell.Windows;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;
    public MainWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Width = Math.Clamp(viewModel.InitialWidth, MinWidth, Math.Max(MinWidth, SystemParameters.WorkArea.Width));
        Height = Math.Clamp(viewModel.InitialHeight, MinHeight, Math.Max(MinHeight, SystemParameters.WorkArea.Height));
        Loaded += async (_, _) => await viewModel.StartAsync();
        Closing += OnClosing;
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_viewModel.CanClose()) { e.Cancel = true; return; }
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _viewModel.SaveWindow(bounds.Width, bounds.Height);
        _viewModel.Stop();
    }
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
