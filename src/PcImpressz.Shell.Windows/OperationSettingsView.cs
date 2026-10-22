using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PcImpressz.Shell.Customization;

namespace PcImpressz.Shell.Windows;

public sealed class OperationWorkspace : Observable
{
    private readonly OperationSettingsStore _store;
    private readonly Func<bool> _canEdit;
    private OperationSnapshot _snapshot;
    private bool _broken;
    private string _message = "";

    public OperationWorkspace(string directory, Func<bool> canEdit)
    {
        _canEdit = canEdit;
        _store = new OperationSettingsStore(directory);
        try { _snapshot = _store.Load(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Security.Cryptography.CryptographicException)
        {
            _broken = true;
            _snapshot = new(new Dictionary<string, string>(), new Dictionary<string, string>());
            _message = ex.Message + " A fájlt nem írjuk felül.";
        }
    }

    public string Message { get => _message; private set { _message = value; Changed(); } }
    public OperationSnapshot Snapshot => _snapshot;

    public bool TrySave(IReadOnlyDictionary<string, string> submitted)
    {
        if (!_canEdit()) { Message = "Adminbejelentkezés szükséges."; return false; }
        if (_broken) { Message = "A mentett fájl sérült, ezért nem írjuk felül."; return false; }
        try
        {
            var changed = _store.Save(submitted);
            _snapshot = _store.Load();
            Message = changed.Count == 0 ? "Nem változott semmi." : "Mentve. Újraindítás után is megmarad.";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Security.Cryptography.CryptographicException)
        { Message = ex.Message; return false; }
    }

    public bool TryBackup(string folder)
    {
        if (!_canEdit()) { Message = "Adminbejelentkezés szükséges."; return false; }
        if (_broken) { Message = "A mentett fájl sérült, ezért mentést sem készítünk."; return false; }
        try
        {
            var result = _store.CreateBackup(folder);
            _snapshot = _store.Load();
            Message = "A helyi mentés elkészült (" + result.FileCount + " fájl): " + result.Directory
                + (result.Skipped.Count == 0 ? "" : " Kihagyva: " + result.Skipped.Count + ".");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Security.Cryptography.CryptographicException)
        { Message = ex.Message; return false; }
    }

    public void CheckFolder(string folder) =>
        Message = Run(() => _store.CheckFolder(folder));

    private string Run(Func<string> action)
    {
        try { return action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { return ex.Message; }
    }
}

public sealed record OperationSettingsPage(string GroupId);

public sealed class OperationSettingsView : StackPanel
{
    private readonly Dictionary<string, TextBox> _texts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckBox> _checks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ComboBox> _choices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PasswordBox> _secrets = new(StringComparer.Ordinal);
    private OperationWorkspace? _workspace;

    public OperationSettingsView()
    {
        DataContextChanged += (_, _) => Rebuild();
        Loaded += (_, _) => Rebuild();
    }

    private void Rebuild()
    {
        Children.Clear();
        _texts.Clear(); _checks.Clear(); _choices.Clear(); _secrets.Clear();
        if (DataContext is not OperationWorkspace workspace) return;
        _workspace = workspace;
        var page = Window.GetWindow(this)?.DataContext is ShellViewModel shell ? shell.CurrentPage as OperationSettingsPage : null;
        if (page is null || OperationCatalog.Find(page.GroupId) is not { } group) return;
        foreach (var section in group.Sections) Children.Add(Card(workspace, section));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 4, 0, 0) };
        var save = new Button { Content = "Mentés", HorizontalAlignment = HorizontalAlignment.Left };
        save.SetResourceReference(StyleProperty, "PrimaryButton");
        save.Click += (_, _) => { if (workspace.TrySave(Collect())) Rebuild(); };
        row.Children.Add(save);
        if (group.Id == OperationCatalog.Backup)
        {
            var backup = new Button { Content = "Helyi mentés készítése", Margin = new(12, 0, 0, 0) };
            backup.Click += (_, _) =>
            {
                var folder = _texts.TryGetValue("mentes_mappa", out var box) ? box.Text : "";
                if (workspace.TryBackup(folder)) Rebuild();
            };
            row.Children.Add(backup);
        }
        Children.Add(row);
        var message = new TextBlock { Margin = new(0, 14, 0, 0) };
        message.SetResourceReference(StyleProperty, "Muted");
        message.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Message"));
        Children.Add(message);
        Children.Add(Muted("A mezők a helyi adatmappa operation-settings.json fájljába kerülnek. A jelszó, az API-kulcs és a token titkosítva, az operation-secrets.bin fájlba kerül, a gitbe nem."));
    }

    private Border Card(OperationWorkspace workspace, OperationSection section)
    {
        var card = new Border { Margin = new(0, 0, 0, 16) };
        card.SetResourceReference(StyleProperty, "Card");
        var panel = new StackPanel();
        var title = new TextBlock { Text = section.Title };
        title.SetResourceReference(StyleProperty, "SectionTitle");
        panel.Children.Add(title);
        if (section.Description.Length > 0) panel.Children.Add(Muted(section.Description));
        foreach (var field in section.Fields) panel.Children.Add(Editor(workspace, field));
        card.Child = panel;
        return card;
    }

    private UIElement Editor(OperationWorkspace workspace, OperationField field)
    {
        var panel = new StackPanel { Margin = new(0, 8, 0, 0) };
        if (field.Kind == "check")
        {
            var box = new CheckBox { Content = field.Label, IsChecked = Value(workspace, field) == "true", Margin = new(0, 4, 0, 0) };
            _checks[field.Key] = box;
            panel.Children.Add(box);
        }
        else
        {
            panel.Children.Add(Caption(field.Label));
            panel.Children.Add(Input(workspace, field));
        }
        if (field.Kind == "secret") panel.Children.Add(Muted(workspace.Snapshot.SecretHints.TryGetValue(field.Key, out var hint) ? hint : "Nincs beállítva."));
        if (field.Help.Length > 0) panel.Children.Add(Muted(field.Help));
        return panel;
    }

    private UIElement Input(OperationWorkspace workspace, OperationField field)
    {
        if (field.Kind == "secret")
        {
            var box = SecretBox();
            _secrets[field.Key] = box;
            return box;
        }
        if (field.Kind == "choice")
        {
            var box = new ComboBox { ItemsSource = field.Choices, DisplayMemberPath = "Label", MinHeight = 36 };
            box.SelectedItem = field.Choices?.FirstOrDefault(choice => choice.Value == Value(workspace, field)) ?? field.Choices?.FirstOrDefault();
            _choices[field.Key] = box;
            return box;
        }
        var text = FieldBox(Value(workspace, field));
        _texts[field.Key] = text;
        if (field.Kind != "folder") return text;
        var row = new Grid { Margin = new(0, 0, 0, 0) };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Grid.SetColumn(text, 0);
        row.Children.Add(text);
        var browse = new Button { Content = "Tallózás", Margin = new(8, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = "Mappa kiválasztása" };
            if (Directory.Exists(text.Text)) dialog.InitialDirectory = text.Text;
            if (dialog.ShowDialog() == true) text.Text = dialog.FolderName;
        };
        Grid.SetColumn(browse, 1);
        row.Children.Add(browse);
        var check = new Button { Content = "Ellenőrzés", Margin = new(8, 0, 0, 0) };
        check.Click += (_, _) => _workspace?.CheckFolder(text.Text);
        Grid.SetColumn(check, 2);
        row.Children.Add(check);
        return row;
    }

    private Dictionary<string, string> Collect()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, box) in _texts) values[key] = box.Text;
        foreach (var (key, box) in _checks) values[key] = box.IsChecked == true ? "true" : "false";
        foreach (var (key, box) in _choices) values[key] = box.SelectedItem is OperationChoice choice ? choice.Value : "";
        foreach (var (key, box) in _secrets) values[key] = box.Password;
        return values;
    }

    private static string Value(OperationWorkspace workspace, OperationField field) =>
        workspace.Snapshot.Values.TryGetValue(field.Key, out var value) ? value : "";

    private static TextBlock Caption(string text)
    {
        var block = new TextBlock { Text = text, Margin = new(0, 0, 0, 4), FontWeight = FontWeights.SemiBold };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        return block;
    }

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, Margin = new(0, 4, 0, 0) };
        block.SetResourceReference(StyleProperty, "Muted");
        return block;
    }

    private static TextBox FieldBox(string text)
    {
        var box = new TextBox { Text = text, MinHeight = 36, Padding = new(8) };
        box.SetResourceReference(Control.BackgroundProperty, "ElevatedBrush");
        box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        box.SetResourceReference(Control.BorderBrushProperty, "StrokeBrush");
        box.SetResourceReference(TextBox.CaretBrushProperty, "TextBrush");
        return box;
    }

    private static PasswordBox SecretBox()
    {
        var box = new PasswordBox { MinHeight = 36, Padding = new(8) };
        box.SetResourceReference(Control.BackgroundProperty, "ElevatedBrush");
        box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        box.SetResourceReference(Control.BorderBrushProperty, "StrokeBrush");
        box.SetResourceReference(PasswordBox.CaretBrushProperty, "TextBrush");
        return box;
    }
}
