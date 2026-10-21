using System.Windows;
using System.Windows.Controls;

namespace PcImpressz.Shell.Windows;

public sealed class NavConnectionCard : Border
{
    private readonly PasswordBox _password = SecretBox();
    private readonly PasswordBox _sign = SecretBox();
    private readonly PasswordBox _exchange = SecretBox();
    private int _generation = -1;
    private ShellViewModel? _shell;

    public NavConnectionCard()
    {
        SetResourceReference(StyleProperty, "Card");
        MaxWidth=760;HorizontalAlignment=HorizontalAlignment.Left;
        var panel = new StackPanel();
        panel.Children.Add(BoundTitle());
        panel.Children.Add(Muted("A bevételezés élő NAV-lekérdezéséhez technikai felhasználó kell. A jelszó, az aláírókulcs és a cserekulcs a Windows-felhasználódhoz kötve, titkosítva kerül a cég mappájába. Másik gépen vagy másik felhasználónál a fájl nem nyitható meg."));
        panel.Children.Add(Field("Adószám", "NavTaxNumber"));
        panel.Children.Add(Field("Technikai felhasználó", "NavUserName"));
        panel.Children.Add(Caption("Jelszó"));
        panel.Children.Add(_password);
        panel.Children.Add(Caption("Aláírókulcs"));
        panel.Children.Add(_sign);
        panel.Children.Add(Caption("Cserekulcs"));
        panel.Children.Add(_exchange);
        var test = new CheckBox { Content = "Teszt rendszer", Margin = new(0, 4, 0, 12) };
        test.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding("NavTestMode"));
        panel.Children.Add(test);
        panel.Children.Add(BoundMuted("NavSecretHint"));
        var save = new Button { Content = "NAV-kapcsolat mentése", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 8, 0, 0) };
        save.SetResourceReference(StyleProperty, "PrimaryButton");
        save.SetBinding(IsEnabledProperty, new System.Windows.Data.Binding("NavIdle"));
        save.Click += async (_, _) =>
        {
            if (DataContext is not ShellViewModel shell) return;
            await shell.SaveNavAsync(_password.Password, _sign.Password, _exchange.Password);
            // Successful save increments NavGeneration; a failed save retains typed values.
        };
        panel.Children.Add(save);
        panel.Children.Add(BoundMuted("NavMessage"));
        Child = panel;
        DataContextChanged += (_, _) => Hook();
        Loaded += async (_, _) => { if (DataContext is ShellViewModel shell) await shell.LoadNavAsync(); };
    }

    private void Hook()
    {
        if (_shell is not null) _shell.PropertyChanged -= ShellChanged;
        _shell = DataContext as ShellViewModel;
        if (_shell is null) return;
        _shell.PropertyChanged += ShellChanged;
        _generation = _shell.NavGeneration;
    }

    private void ShellChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.NavGeneration) && _shell is not null && _shell.NavGeneration != _generation)
        {
            _generation = _shell.NavGeneration;
            _password.Clear(); _sign.Clear(); _exchange.Clear();
        }
    }

    private static TextBlock BoundTitle()
    {
        var block = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 10) };
        block.SetResourceReference(TextBlock.FontSizeProperty, "SubtitleSize");
        block.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("NavCompanyLabel"));
        return block;
    }

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, Margin = new(0, 0, 0, 14) };
        block.SetResourceReference(StyleProperty, "Muted");
        return block;
    }

    private static TextBlock BoundMuted(string path)
    {
        var block = Muted("");
        block.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(path));
        return block;
    }

    private static TextBlock Caption(string text)
    {
        var block = new TextBlock { Text = text, Margin = new(0, 0, 0, 4) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return block;
    }

    private static UIElement Field(string label, string path)
    {
        var panel = new StackPanel { Margin = new(0, 0, 0, 10) };
        panel.Children.Add(Caption(label));
        var box = new TextBox { MinHeight = 36, Padding = new(8) };
        box.SetResourceReference(Control.BackgroundProperty, "ElevatedBrush");
        box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        box.SetResourceReference(Control.BorderBrushProperty, "StrokeBrush");
        box.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(path) { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
        panel.Children.Add(box);
        return panel;
    }

    private static PasswordBox SecretBox()
    {
        var box = new PasswordBox { MinHeight = 36, Padding = new(8), Margin = new(0, 0, 0, 10) };
        box.SetResourceReference(Control.BackgroundProperty, "ElevatedBrush");
        box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        box.SetResourceReference(Control.BorderBrushProperty, "StrokeBrush");
        return box;
    }
}
