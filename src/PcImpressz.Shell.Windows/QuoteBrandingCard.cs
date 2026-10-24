using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
namespace PcImpressz.Shell.Windows;

public sealed class QuoteBrandingCard : UserControl
{
    private readonly Image _logo = new() { MaxHeight = 72, MaxWidth = 180, Stretch = System.Windows.Media.Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Image _signature = new() { MaxHeight = 72, MaxWidth = 220, Stretch = System.Windows.Media.Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private ShellViewModel? _shell;
    public QuoteBrandingCard()
    {
        var panel = new StackPanel { Margin = new Thickness(16), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(new TextBlock { Text = "Árajánlat képei", FontSize = 18, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "A logó és az aláírás az árajánlat PDF-jének tetején és alján jelenik meg. A végleges lapmintát később, a feltöltött sablon szerint cseréljük. A képek a cég mappájában maradnak, a jelszavakkal nem keverednek.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
        panel.Children.Add(new TextBlock { Text = "Logó", Margin = new Thickness(0, 8, 0, 4) });
        panel.Children.Add(_logo);
        panel.Children.Add(Buttons("Logó feltöltése", "Logó törlése", "logo.jpg"));
        panel.Children.Add(new TextBlock { Text = "Aláírás", Margin = new Thickness(0, 12, 0, 4) });
        panel.Children.Add(_signature);
        panel.Children.Add(Buttons("Aláírás feltöltése", "Aláírás törlése", "signature.jpg"));
        panel.Children.Add(_message);
        Content = panel;
        DataContextChanged += (_, _) => { if (_shell is not null) _shell.PropertyChanged -= Changed; _shell = DataContext as ShellViewModel; if (_shell is not null) _shell.PropertyChanged += Changed; Refresh(); };
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "SelectedCompany" or "IsCompanyAdmin") Refresh();
    }
    private WrapPanel Buttons(string upload, string remove, string file)
    {
        var panel = new WrapPanel();
        var load = new Button { Content = upload, Margin = new Thickness(0, 8, 8, 0) };
        var clear = new Button { Content = remove, Margin = new Thickness(0, 8, 8, 0) };
        load.Click += (_, _) => Upload(file);
        clear.Click += (_, _) => { if (Folder() is { } folder) { QuoteBranding.Remove(folder, file); Refresh(); _message.Text = "A kép törölve."; } };
        panel.Children.Add(load);
        panel.Children.Add(clear);
        return panel;
    }
    private void Upload(string file)
    {
        if (Folder() is not { } folder) { _message.Text = "Adminbejelentkezés és kiválasztott cég kell."; return; }
        var picker = new OpenFileDialog { Title = "Kép", Filter = "Képek (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
        if (picker.ShowDialog() != true) return;
        try { QuoteBranding.Save(folder, file, picker.FileName); Refresh(); _message.Text = "A kép mentve. Az árajánlat nyomtatási képe ezt használja."; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { _message.Text = ex.Message; }
    }
    private string? Folder()
    {
        if (_shell?.SelectedCompany?.Id is not Guid company || !_shell.IsCompanyAdmin) return null;
        return QuoteBranding.Folder(_shell.DataDirectory, company);
    }
    private void Refresh()
    {
        _logo.Source = null;
        _signature.Source = null;
        if (_shell?.SelectedCompany?.Id is not Guid company) { _message.Text = "Válassz céget, majd töltsd fel a képeket."; return; }
        var folder = QuoteBranding.Folder(_shell.DataDirectory, company);
        _logo.Source = Load(QuoteBranding.Logo(folder));
        _signature.Source = Load(QuoteBranding.Signature(folder));
        if (_message.Text.Length == 0) _message.Text = _shell.IsCompanyAdmin ? "PNG vagy JPEG, legfeljebb 2 MB." : "A képeket a cég adminja cserélheti.";
    }
    private static BitmapImage? Load(string? path)
    {
        if (path is null) return null;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
