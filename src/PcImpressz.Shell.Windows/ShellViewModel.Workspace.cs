using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;

namespace PcImpressz.Shell.Windows;

public sealed partial class ShellViewModel
{
    private readonly Dictionary<string, IWorkspacePage> _entityPages = new(StringComparer.Ordinal);
    private CompanyInfo? _selectedCompany;
    private bool _companiesLoaded;
    public ObservableCollection<CompanyInfo> Companies { get; } = [];
    public CompanyInfo? SelectedCompany
    {
        get => _selectedCompany;
        set
        {
            if (value is null || value.Id == _selectedCompany?.Id) return;
            if (!WorkspaceIsIdle) { Changed(); return; }
            if (_entityPages.Values.Any(p => p.IsDirty) && MessageBox.Show("A nem mentett változtatásokat elveted?",
                "Cégváltás", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            { Changed(); return; }
            var previousCompany=_selectedCompany;var previousSession=_connection.Session;
            if(previousCompany is not null && previousSession is not null) _ = RevokePreviousSessionAsync(previousCompany.Id,previousSession);
            _connection.SetSession(null,null);
            _selectedCompany = value;
            ResetDemoMessage();
            _preferences = _preferences with { SelectedCompanyId = value.Id };
            try { _store.Save(_preferences); } catch (System.IO.IOException) { }
            foreach (var page in _entityPages.Values) page.CompanyChanged();
            Changed(); Changed(nameof(CompanyMode)); Changed(nameof(IsDemoCompany)); Changed(nameof(CanSeedDemo)); Changed(nameof(NavCompanyLabel));
            AuthChanged();
            _ = PrepareAuthenticationAsync();
        }
    }
    public bool WorkspaceIsIdle => !_billingBusy && !_authBusy && !_maintenanceBusy && !_entityPages.Values.Any(p => p.IsBusy || p.HasUnresolvedOperation);
    public string CompanyMode => SelectedCompany is { IsDemo: true } ? "DEMÓ CÉG · ELKÜLÖNÍTETT ADATOK" : "SAJÁT CÉGES ADATOK";
    public ICommand CreateCompanyCommand { get; private set; } = null!;

    private void InitializeWorkspace()
    {
        InitializeBackups();
        InitializeDemo();
        CreateCompanyCommand = new RelayCommand(CreateCompany);
        ClearDemoDataCommand = new RelayCommand(() => _ = ClearDemoDataAsync());
    }
    public ICommand ClearDemoDataCommand { get; private set; } = null!;
    private readonly HashSet<string> _disabledModules = new(StringComparer.Ordinal);
    private async Task UpdateWorkspaceAsync(HostDiagnostic status)
    {
        if (status.State != "Ready") return;
        var disabled = status.Modules.Where(module => !module.Enabled).Select(module => module.Id).ToHashSet(StringComparer.Ordinal);
        var disabledChanged = !disabled.SetEquals(_disabledModules);
        _disabledModules.Clear();
        foreach (var id in disabled) _disabledModules.Add(id);
        var addedPage = false;
        foreach (var page in status.Pages ?? [])
        {
            var key = page.ModuleId + "." + page.Id;
            if (_entityPages.ContainsKey(key)) continue;
            IWorkspacePage vm = page.ViewKind == "quotes"
                ? new QuotesPageViewModel(page,_connection,()=>IsSignedIn&&_connection.Allows(page.ModuleId+".read")?SelectedCompany?.Id:null,_shutdown.Token,DataDirectory,()=>SelectedCompany?.Name??"",()=>Operations.MailAccount())
                : page.ViewKind == "orders"
                ? new OrdersPageViewModel(page,_connection,()=>IsSignedIn&&_connection.Allows(page.ModuleId+".read")?SelectedCompany?.Id:null,_shutdown.Token,()=>_preferences.SaveOrderCustomers)
                : page.ViewKind is "inventory" or "sales"
                ? new OperationsPageViewModel(page, _connection, () => IsSignedIn && _connection.Allows(page.ModuleId+".read") ? SelectedCompany?.Id : null, _shutdown.Token, _layouts)
                : page.ViewKind == "receiving"
                ? new ReceivingPageViewModel(page, _connection, () => IsSignedIn && _connection.Allows(page.ModuleId+".read") ? SelectedCompany?.Id : null, _shutdown.Token, _layouts)
                : new EntityPageViewModel(page, _connection, () => IsSignedIn && _connection.Allows(page.ModuleId+".read") ? SelectedCompany?.Id : null, _shutdown.Token, _layouts);
            vm.PropertyChanged += (_, e) => { if (e.PropertyName is "IsBusy" or "HasUnresolvedOperation") { Changed(nameof(WorkspaceIsIdle));Changed(nameof(CanSeedDemo)); } };
            _entityPages.Add(key, vm);
            if (_companiesLoaded && SelectedCompany is not null) vm.CompanyChanged();
            addedPage = true;
        }
        if (addedPage || disabledChanged) SyncNavigation();
        if (addedPage) SyncListSettings();
        if (!_companiesLoaded)
        {
            var response = await _connection.SendAsync("companies.list", new { }, _shutdown.Token);
            var companies = response.Deserialize<CompanyInfo[]>() ?? [];
            Companies.Clear(); foreach (var company in companies) Companies.Add(company);
            SelectedCompany = Companies.FirstOrDefault(c => c.Id == _preferences.SelectedCompanyId) ?? Companies.FirstOrDefault();
            _companiesLoaded = true;
        }
    }

    public bool CanClose()=>CanClose(()=>UnsavedChanges.Confirm("Biztosan kilépsz mentés nélkül? A Nem válasszal a program és a szerkesztett adatok megmaradnak.","Kilépés mentés nélkül"));
    internal bool CanClose(Func<bool> confirmExit)
    {
        if(_entityPages.Values.Any(p=>p.HasUnresolvedOperation)){MessageBox.Show("Az eladás eredménye még bizonytalan. Az eladási képernyő véglegesítés gombjával ellenőrizd vagy próbáld újra ugyanazt a műveletet.","Eladás lezárása");return false;}
        if (!WorkspaceIsIdle)
        {
            MessageBox.Show("Várd meg a folyamatban lévő művelet végét, majd próbáld újra a bezárást.",
                "Folyamatban lévő művelet", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        return !_entityPages.Values.Any(p => p.IsDirty) || confirmExit();
    }
    private void SyncNavigation()
    {
        var selectedId = _selectedNavigation.Id;
        ApplyMenu(Navigation, WorkspaceMenu.Compose(_entityPages.Values.Where(page=>IsSignedIn&&_connection.Allows(page.Definition.ModuleId+".read")).Select(page => page.Definition).ToArray(), _disabledModules));
        var match = FindNavigationOrNull(selectedId);
        if (ReferenceEquals(match, _selectedNavigation)) return;
        _selectedNavigation.IsSelected = false;
        _selectedNavigation = match ?? Navigation[0];
        _selectedNavigation.IsSelected = true;
        Changed(nameof(SelectedNavigation)); Changed(nameof(PageTitle)); Changed(nameof(PageDescription)); Changed(nameof(CurrentPage));
    }

    private void ApplyMenu(ObservableCollection<NavigationItem> items, IReadOnlyList<MenuEntry> desired)
    {
        var ids = desired.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        for (var index = items.Count - 1; index >= 0; index--)
            if (!ids.Contains(items[index].Id)) items.RemoveAt(index);
        for (var index = 0; index < desired.Count; index++)
        {
            var entry = desired[index];
            var existing = items.FirstOrDefault(item => item.Id == entry.Id);
            if (existing is null) { items.Insert(index, CreateItem(entry)); continue; }
            var current = items.IndexOf(existing);
            if (current != index) items.Move(current, index);
            existing.Apply(entry.Title, entry.Icon, entry.Description, entry.PageKey);
            ApplyMenu(existing.Children, entry.Children);
        }
    }

    private NavigationItem CreateItem(MenuEntry entry)
    {
        var item = new NavigationItem(entry.Id, entry.Title, entry.Icon, entry.Description, entry.PageKey);
        item.SelectCommand = new RelayCommand(() => SelectedNavigation = item);
        foreach (var child in entry.Children) item.Children.Add(CreateItem(child));
        return item;
    }

    private NavigationItem? FindNavigationOrNull(string id)
    {
        foreach (var item in Navigation)
        {
            if (item.Id == id) return item;
            foreach (var child in item.Children)
                if (child.Id == id) return child;
        }
        return null;
    }

    private void CreateCompany()
    {
        if (!WorkspaceIsIdle) return;
        var window = new Window { Title = "Új cég", Width = 470, Height = 330, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Application.Current.MainWindow };
        window.SetResourceReference(Window.BackgroundProperty, "SurfaceBrush");
        window.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(new TextBlock { Text = "Indulj tiszta lappal.", FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new(0,0,0,16) });
        panel.Children.Add(new TextBlock { Text = "Az új cégben nem lesznek partner-, termék- vagy mintaadatok.", TextWrapping = TextWrapping.Wrap, Margin = new(0,0,0,14) });
        var name = new TextBox { MinHeight = 40, Padding = new(10), Margin = new(0,0,0,14) };
        name.SetResourceReference(Control.BackgroundProperty, "ElevatedBrush"); name.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        panel.Children.Add(name);
        var demo = new CheckBox { Content = "Demócég", IsChecked = true, Margin = new(0,0,0,20) }; panel.Children.Add(demo);
        var create = new Button { Content = "Üres cég létrehozása", IsDefault = true };
        create.Click += async (_, _) =>
        {
            create.IsEnabled = false;
            try
            {
                var result = await _connection.SendAsync("companies.create", new CreateCompanyRequest(name.Text, demo.IsChecked == true), _shutdown.Token);
                var company = result.Deserialize<CompanyInfo>() ?? throw new InvalidOperationException("Érvénytelen válasz.");
                Companies.Add(company); SelectedCompany = company; window.Close();
            }
            catch (Exception ex) { MessageBox.Show(window, ex.Message, "A cég nem hozható létre"); }
            finally { create.IsEnabled = true; }
        };
        panel.Children.Add(create); window.Content = panel; window.ShowDialog();
    }

    private async Task ClearDemoDataAsync()
    {
        if (!WorkspaceIsIdle)
        {
            _actionMessage = "Várj, amíg a folyamatban lévő művelet befejeződik.";
            Changed(nameof(ActionMessage));
            return;
        }
        if(!IsCompanyAdmin||SelectedCompany is not {IsDemo:true}){_actionMessage="A törléshez a kiválasztott demócég adminjaként jelentkezz be.";Changed(nameof(ActionMessage));return;}
        if (MessageBox.Show("A kiválasztott demó cég partnerei, terméktörzse és minden moduladata törlődik. A cég bejegyzése megmarad, utána üresen tölthető fel. A licenc, a megjelenés és a modulok ki-be állapota megmarad. Más cég adata a helyén marad.",
            "Kísérleti adatbázis törlése", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            var result = await _connection.SendAsync("companies.clear-demo", new { }, _shutdown.Token);
            var clearance = result.Deserialize<DemoDataClearance>();
            if (SelectedCompany is { IsDemo: true })
                foreach (var page in _entityPages.Values) page.CompanyChanged();
            _actionMessage = clearance is { DemoCompanies: > 0 }
                ? "A demó cég moduladatai törölve. A cég megmaradt, a képernyők üresen tölthetők."
                : "Nincs demó cég, ezért nem töröltünk adatot.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { _actionMessage = ex.Message; }
        Changed(nameof(ActionMessage));
    }
}
