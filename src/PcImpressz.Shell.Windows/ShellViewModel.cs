using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;

namespace PcImpressz.Shell.Windows;

public sealed class NavigationItem : Observable
{
    private bool _selected;
    public NavigationItem(string id, string title, string icon, string description, string pageKey)
    {
        (Id, Title, Icon, Description, PageKey) = (id, title, icon, description, pageKey);
        Children.CollectionChanged += (_, _) => Changed(nameof(HasChildren));
    }
    public string Id { get; }
    public string Title { get; private set; }
    public string Icon { get; private set; }
    public string Description { get; private set; }
    public string PageKey { get; private set; }
    public ObservableCollection<NavigationItem> Children { get; } = [];
    public bool HasChildren => Children.Count > 0;
    public bool IsSelected { get => _selected; set { if (_selected == value) return; _selected = value; Changed(); } }
    public ICommand SelectCommand { get; set; } = null!;
    public void Apply(string title, string icon, string description, string pageKey)
    {
        if (Title == title && Icon == icon && Description == description && PageKey == pageKey) return;
        (Title, Icon, Description, PageKey) = (title, icon, description, pageKey);
        Changed(nameof(Title)); Changed(nameof(Icon)); Changed(nameof(Description)); Changed(nameof(PageKey));
    }
}
public sealed record AccentOption(string Name, string Color);
public sealed record FontOption(string Name, double Scale);
public sealed record DashboardPage;
public sealed record ModulesPage;
public sealed record LicensePage;
public sealed record AppearancePage;
public sealed record SettingsPage;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}

public sealed partial class ShellViewModel : Observable
{
    private readonly AppearanceStore _store;
    private readonly HostConnection _connection;
    private readonly CancellationTokenSource _shutdown = new();
    private AppearancePreferences _preferences;
    private NavigationItem _selectedNavigation;
    private SkinDefinition _selectedSkin;
    private HostDiagnostic? _status;
    private string _connectionMessage = "Kapcsolódás a maghoz…";
    private string _notice = "A megjelenésedet automatikusan mentjük ezen a gépen.";
    private bool _ready;
    private string? _lastConnectionError;

    public ShellViewModel(AppearanceStore store, string directory, string? hostPath)
    {
        (_store, DataDirectory) = (store, directory);
        _connection = new(directory, hostPath);
        _preferences = store.Load();
        Skins = new(store.ReadSkins());
        _selectedSkin = Skins.FirstOrDefault(s => s.Id == _preferences.SkinId) ?? Skins[0];
        _preferences = _preferences with { SkinId = _selectedSkin.Id };
        ApplyMenu(Navigation, WorkspaceMenu.Compose([]));
        _selectedNavigation = Navigation[0];
        _selectedNavigation.IsSelected = true;
        ThemeManager.Apply(_selectedSkin, _preferences);
        _notice = store.LastWarning ?? _notice;
        ToggleNavigationCommand = new RelayCommand(() => CollapsedNavigation = !CollapsedNavigation);
        GoModulesCommand = new RelayCommand(() => SelectedNavigation = FindNavigation("modules"));
        GoLicenseCommand = new RelayCommand(() => SelectedNavigation = FindNavigation("license"));
        GoAppearanceCommand = new RelayCommand(() => SelectedNavigation = FindNavigation("appearance"));
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync());
        ImportSkinCommand = new RelayCommand(ImportSkin);
        ExportSkinCommand = new RelayCommand(ExportSkin);
        ResetAppearanceCommand = new RelayCommand(ResetAppearance);
        OpenDataCommand = new RelayCommand(() => OpenFolder(DataDirectory));
        OpenLogsCommand = new RelayCommand(() => OpenFolder(Path.Combine(DataDirectory, "logs")));
        OpenModulesCommand = new RelayCommand(() => OpenFolder(Path.Combine(DataDirectory, "modules")));
        InitializeWorkspace();
        _ready = true;
    }

    public ObservableCollection<NavigationItem> Navigation { get; } = [];
    public ObservableCollection<SkinDefinition> Skins { get; }
    public ObservableCollection<ModuleDiagnostic> Modules { get; } = [];
    public IReadOnlyList<AccentOption> Accents { get; } = [new("Menta", "#38D9BC"), new("Kék", "#78B2FF"),
        new("Ibolya", "#B6A0FF"), new("Arany", "#EBC16C"), new("Korall", "#FF9C90")];
    public IReadOnlyList<FontOption> FontSizes { get; } = [new("Normál · 100%", 1), new("Nagyobb · 110%", 1.1), new("Nagy · 125%", 1.25)];
    public string DataDirectory { get; }
    public string Notice => _notice;
    public string ConnectionMessage => _connectionMessage;
    public string HostLabel => _status?.State == "Ready" ? "A mag működik" : "Kapcsolódás";
    public string HostDetail => _status?.State == "Ready" ? "Helyi kapcsolat · naprakész állapot" : _connectionMessage;
    public string InstallationId => _status?.InstallationId ?? "Kapcsolódás után elérhető";
    public string ContractVersion => _status?.ContractVersion ?? "—";
    public string ActiveModuleCount => _status is null ? "—" : Modules.Count(m => m.State == "Active").ToString();
    public string ModuleSummary => Modules.Count == 0 ? "A mag készen áll az első modul fogadására."
        : $"{Modules.Count} telepített modul állapotát látod.";
    public bool HasModules => Modules.Count > 0;
    public bool HasNoModules => !HasModules;
    public string LicenseLabel => _status?.LicenseState switch
    {
        "Active" => "Érvényes licenc", "Missing" => "Még nincs aktiválva", "Expired" => "Lejárt jogosultság",
        null => "Ellenőrzés alatt", _ => "Ellenőrzés szükséges"
    };
    public string LicenseReason => _status?.LicenseReason ?? "A licencállapot a maghoz kapcsolódás után jelenik meg.";
    public string LicenseUntil => _status?.LicenseValidUntil?.ToLocalTime().ToString("yyyy. MM. dd. HH:mm") ?? "Nincs aktív időszak";
    public string LastUpdated => _status?.ObservedAt.ToLocalTime().ToString("HH:mm:ss") ?? "—";
    public double InitialWidth => _preferences.WindowWidth;
    public double InitialHeight => _preferences.WindowHeight;

    public NavigationItem SelectedNavigation
    {
        get => _selectedNavigation;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedNavigation)) return;
            _selectedNavigation.IsSelected = false;
            _selectedNavigation = value;
            value.IsSelected = true;
            Changed(); Changed(nameof(PageTitle)); Changed(nameof(PageDescription)); Changed(nameof(CurrentPage));
        }
    }
    public string PageTitle => SelectedNavigation.Title;
    public string PageDescription => SelectedNavigation.Description;
    public object CurrentPage => _entityPages.TryGetValue(SelectedNavigation.PageKey, out var entityPage)
        ? entityPage : SelectedNavigation.PageKey switch
    { "modules" => new ModulesPage(), "license" => new LicensePage(), "appearance" => new AppearancePage(), "settings" => new SettingsPage(), _ => new DashboardPage() };

    private NavigationItem FindNavigation(string id)
    {
        foreach (var item in Navigation)
        {
            if (item.Id == id) return item;
            foreach (var child in item.Children)
                if (child.Id == id) return child;
        }
        throw new InvalidOperationException("Hiányzó menüpont: " + id);
    }

    public SkinDefinition SelectedSkin
    {
        get => _selectedSkin;
        set
        {
            if (value is null || !_ready) return;
            _selectedSkin = value; _preferences = _preferences with { SkinId = value.Id, AccentOverride = null };
            ApplyAndSave(); Changed(); Changed(nameof(SelectedAccent));
        }
    }
    public AccentOption? SelectedAccent
    {
        get => Accents.FirstOrDefault(a => a.Color == (_preferences.AccentOverride ?? SelectedSkin.Palette.Accent));
        set { if (value is null || !_ready) return; _preferences = _preferences with { AccentOverride = value.Color }; ApplyAndSave(); Changed(); }
    }
    public FontOption SelectedFontSize
    {
        get => FontSizes.First(s => s.Scale == _preferences.FontScale);
        set { if (value is null || !_ready) return; _preferences = _preferences with { FontScale = value.Scale }; ApplyAndSave(); Changed(); }
    }
    public bool CollapsedNavigation
    {
        get => _preferences.CollapsedNavigation;
        set { _preferences = _preferences with { CollapsedNavigation = value }; ApplyAndSave(); Changed(); }
    }
    public bool CompactDensity
    {
        get => _preferences.CompactDensity;
        set { _preferences = _preferences with { CompactDensity = value }; ApplyAndSave(); Changed(); }
    }

    public ICommand ToggleNavigationCommand { get; }
    public ICommand GoModulesCommand { get; }
    public ICommand GoLicenseCommand { get; }
    public ICommand GoAppearanceCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ImportSkinCommand { get; }
    public ICommand ExportSkinCommand { get; }
    public ICommand ResetAppearanceCommand { get; }
    public ICommand OpenDataCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand OpenModulesCommand { get; }

    public async Task StartAsync()
    {
        try { await RefreshAsync(); }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(4));
        try
        {
            while (await timer.WaitForNextTickAsync(_shutdown.Token)) await RefreshAsync();
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
    }

    private bool _refreshing;
    private async Task RefreshAsync()
    {
        if (_refreshing || _shutdown.IsCancellationRequested) return;
        _refreshing = true;
        try
        {
            HostDiagnostic status;
            try { status = await _connection.ReadAsync(_shutdown.Token); }
            catch (Exception ex) when (ex is IOException or OperationCanceledException && !_shutdown.IsCancellationRequested)
            {
                _connection.StartIfNeeded();
                status = await _connection.ReadAsync(_shutdown.Token);
            }
            _status = status;
            _lastConnectionError = null;
            _connectionMessage = "A maghoz kapcsolódva";
            Modules.Clear(); foreach (var module in status.Modules) Modules.Add(module);
            await UpdateWorkspaceAsync(status);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _status = null;
            _lastConnectionError = ex is OperationCanceledException ? "A mag még nem válaszol." : ex.Message;
            _connectionMessage = "A kapcsolat nem elérhető. Frissíts, vagy ellenőrizd a naplót.";
        }
        finally
        {
            _refreshing = false;
            foreach (var property in new[] { nameof(ConnectionMessage), nameof(HostLabel), nameof(HostDetail), nameof(InstallationId),
                nameof(ContractVersion), nameof(ActiveModuleCount), nameof(ModuleSummary), nameof(HasModules), nameof(HasNoModules),
                nameof(LicenseLabel), nameof(LicenseReason), nameof(LicenseUntil), nameof(LastUpdated), nameof(DiagnosticDetail) }) Changed(property);
        }
    }

    public string DiagnosticDetail => _lastConnectionError ?? "A kapcsolat csak helyi állapotadatokat továbbít. Üzleti művelet nem érhető el rajta.";

    private void ApplyAndSave()
    {
        ThemeManager.Apply(SelectedSkin, _preferences);
        try { _store.Save(_preferences); _notice = "A megjelenési beállítások mentve."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _notice = "A változás látható, de nem menthető: " + ex.Message; }
        Changed(nameof(Notice));
    }

    public void SaveWindow(double width, double height)
    {
        _preferences = _preferences with { WindowWidth = Math.Clamp(width, 1000, 4000), WindowHeight = Math.Clamp(height, 680, 2400) };
        try { _store.Save(_preferences); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void ResetAppearance()
    {
        _preferences = new AppearancePreferences { WindowWidth = _preferences.WindowWidth, WindowHeight = _preferences.WindowHeight };
        _selectedSkin = Skins[0]; ApplyAndSave();
        foreach (var property in new[] { nameof(SelectedSkin), nameof(SelectedAccent), nameof(SelectedFontSize), nameof(CollapsedNavigation), nameof(CompactDensity) }) Changed(property);
    }

    private void ImportSkin()
    {
        var dialog = new OpenFileDialog { Title = "Saját skin importálása", Filter = "PC-Impressz skin (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var skin = _store.Import(dialog.FileName);
            var old = Skins.FirstOrDefault(s => s.Id == skin.Id);
            if (old is not null) Skins.Remove(old);
            Skins.Add(skin); SelectedSkin = skin;
            _notice = "Saját skin importálva és alkalmazva.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { _notice = "A skin nem tölthető be: " + ex.Message; }
        Changed(nameof(Notice));
    }

    private void ExportSkin()
    {
        var dialog = new SaveFileDialog { Title = "Skin mentése", Filter = "PC-Impressz skin (*.json)|*.json", FileName = "sajat-skin.json" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _store.Export(dialog.FileName, SelectedSkin with
            {
                Id = "sajat-skin", Name = "Saját skin",
                Palette = SelectedSkin.Palette with { Accent = _preferences.AccentOverride ?? SelectedSkin.Palette.Accent }
            });
            _notice = "A skinfájl elkészült; a színeit szerkesztheted és visszaimportálhatod.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _notice = "A skin nem menthető: " + ex.Message; }
        Changed(nameof(Notice));
    }

    private void OpenFolder(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { directory } });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        { _notice = "A mappa nem nyitható meg: " + ex.Message; Changed(nameof(Notice)); }
    }

    public void Stop() { _shutdown.Cancel(); _connection.Release(); }
}
