using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PcImpressz.Contracts;

namespace PcImpressz.Shell.Windows;

public sealed partial class ShellViewModel
{
    private readonly Dictionary<string, EntityPageViewModel> _entityPages = new(StringComparer.Ordinal);
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
            _selectedCompany = value;
            _preferences = _preferences with { SelectedCompanyId = value.Id };
            try { _store.Save(_preferences); } catch (System.IO.IOException) { }
            foreach (var page in _entityPages.Values) page.CompanyChanged();
            Changed(); Changed(nameof(CompanyMode));
        }
    }
    public bool WorkspaceIsIdle => !_entityPages.Values.Any(p => p.IsBusy);
    public string CompanyMode => SelectedCompany is { IsDemo: true } ? "DEMÓ CÉG · ELKÜLÖNÍTETT ADATOK" : "SAJÁT CÉGES ADATOK";
    public ICommand CreateCompanyCommand { get; private set; } = null!;

    private void InitializeWorkspace()
    {
        CreateCompanyCommand = new RelayCommand(CreateCompany);
    }
    private async Task UpdateWorkspaceAsync(HostDiagnostic status)
    {
        if (status.State != "Ready") return;
        foreach (var page in status.Pages ?? [])
        {
            var key = page.ModuleId + "." + page.Id;
            if (_entityPages.ContainsKey(key)) continue;
            var vm = new EntityPageViewModel(page, _connection, () => SelectedCompany?.Id, _shutdown.Token);
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(EntityPageViewModel.IsBusy)) Changed(nameof(WorkspaceIsIdle)); };
            _entityPages.Add(key, vm);
            Navigation.Insert(Math.Min(1 + _entityPages.Count - 1, Navigation.Count),
                new(key, page.Title, page.Icon, page.Description));
        }
        if (!_companiesLoaded)
        {
            var response = await _connection.SendAsync("companies.list", new { }, _shutdown.Token);
            var companies = response.Deserialize<CompanyInfo[]>() ?? [];
            Companies.Clear(); foreach (var company in companies) Companies.Add(company);
            SelectedCompany = Companies.FirstOrDefault(c => c.Id == _preferences.SelectedCompanyId) ?? Companies.FirstOrDefault();
            _companiesLoaded = true;
        }
    }

    public bool CanClose() => !_entityPages.Values.Any(p => p.IsDirty) || MessageBox.Show(
        "Elveted a nem mentett adatlapváltozásokat?", "Kilépés", MessageBoxButton.YesNo,
        MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void CreateCompany()
    {
        if (!WorkspaceIsIdle) return;
        var window = new Window { Title = "Új cég", Width = 470, Height = 330, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Application.Current.MainWindow };
        window.SetResourceReference(Window.BackgroundProperty, "SurfaceBrush");
        window.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(new TextBlock { Text = "Indulj tiszta lappal.", FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new(0,0,0,16) });
        panel.Children.Add(new TextBlock { Text = "Az új cégben nem lesznek partnerek vagy mintaadatok.", TextWrapping = TextWrapping.Wrap, Margin = new(0,0,0,14) });
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
}
