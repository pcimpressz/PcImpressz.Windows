using System.IO;
using System.Windows;
using PcImpressz.Shell.Customization;

namespace PcImpressz.Shell.Windows;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var arguments = e.Args;
            var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PcImpressz", "WindowsEdition");
            string? hostPath = null;
            for (var i = 0; i < arguments.Length; i++)
            {
                if (arguments[i] == "--data-dir" && i + 1 < arguments.Length) dataDirectory = arguments[++i];
                else if (arguments[i] == "--host" && i + 1 < arguments.Length) hostPath = arguments[++i];
                else throw new ArgumentException("Ismeretlen vagy hiányos indítási kapcsoló.");
            }
            var store = new AppearanceStore(Path.GetFullPath(dataDirectory));
            var vm = new ShellViewModel(store, Path.GetFullPath(dataDirectory), hostPath);
            var window = new MainWindow(vm);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "PC-Impressz – indítási hiba", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
