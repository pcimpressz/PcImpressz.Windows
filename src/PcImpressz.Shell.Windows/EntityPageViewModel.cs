using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PcImpressz.Contracts;

namespace PcImpressz.Shell.Windows;

public sealed class EntityFieldViewModel(EntityField definition) : Observable
{
    private string _text = definition.DefaultText ?? "";
    private bool _checked = definition.DefaultChecked;
    public EntityField Definition => definition;
    public string Label => definition.Label + (definition.Required ? " *" : "");
    public string Text { get => _text; set { _text = value; Changed(); Edited?.Invoke(); } }
    public bool Checked { get => _checked; set { _checked = value; Changed(); Edited?.Invoke(); } }
    public Action? Edited { get; set; }
}

public sealed class EntityRow
{
    public Dictionary<string, object?> Values { get; } = new(StringComparer.Ordinal);
    public JsonElement Json { get; }
    public object? this[string key] => Values.GetValueOrDefault(key);
    public EntityRow(JsonElement json)
    {
        Json = json.Clone();
        foreach (var p in json.EnumerateObject()) Values[p.Name] = p.Value.ValueKind switch
        { JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.String => p.Value.GetString(),
          JsonValueKind.Number => p.Value.GetRawText(), _ => "" };
    }
}

public sealed class EntityPageViewModel : Observable
{
    private readonly HostConnection _connection;
    private readonly Func<Guid?> _company;
    private readonly CancellationToken _shutdown;
    private EntityRow? _selected;
    private Guid? _editId;
    private long _version;
    private bool _populating;
    private bool _busy;
    private bool _dirty;
    private string _message = "Válassz céget, majd töltsd be a listát.";
    private int _page;
    private int _total;
    private string _search = "";
    private string _filter = "all";
    public EntityPageDescriptor Definition { get; }
    public ObservableCollection<EntityRow> Rows { get; } = [];
    public ObservableCollection<EntityFieldViewModel> Fields { get; } = [];
    public string Search { get => _search; set { _search = value; Changed(); } }
    public string Filter { get => _filter; set { _filter = value; Changed(); } }
    public bool IsDirty { get => _dirty; private set { _dirty = value; Changed(); Changed(nameof(EditorTitle)); } }
    public string EditorTitle => (_editId is null ? "Új partner" : "Partner szerkesztése") + (IsDirty ? " · nincs mentve" : "");
    public bool IsBusy { get => _busy; private set { _busy = value; Changed(); Changed(nameof(IsNotBusy)); } }
    public bool IsNotBusy => !IsBusy;
    public string Message { get => _message; private set { _message = value; Changed(); } }
    public string CountLabel => _total + " partner · " + (_page + 1) + ". oldal";
    public bool CanImport => Definition.Commands.ImportPreview is not null;
    public EntityRow? SelectedRow
    {
        get => _selected;
        set
        {
            if (value is null || ReferenceEquals(value, _selected)) return;
            if (!CanDiscard()) { Changed(); return; }
            _selected = value; Populate(value); Changed();
        }
    }

    public ICommand SearchCommand { get; }
    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ArchiveCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }

    internal EntityPageViewModel(EntityPageDescriptor definition, HostConnection connection, Func<Guid?> company, CancellationToken shutdown)
    {
        (Definition, _connection, _company, _shutdown) = (definition, connection, company, shutdown);
        foreach (var field in definition.Fields)
        {
            var vm = new EntityFieldViewModel(field);
            vm.Edited = () => { if (!_populating) IsDirty = true; };
            Fields.Add(vm);
        }
        SearchCommand = new RelayCommand(() => { if (CanDiscard()) { Populate(null); _page = 0; _ = LoadAsync(); } });
        NewCommand = new RelayCommand(() => { if (CanDiscard()) Populate(null); });
        SaveCommand = new RelayCommand(() => _ = SaveAsync());
        ArchiveCommand = new RelayCommand(() => _ = ArchiveAsync());
        ExportCommand = new RelayCommand(() => _ = ExportAsync());
        ImportCommand = new RelayCommand(() => ImportDialog.Show(this));
        NextCommand = new RelayCommand(() => { if ((_page + 1) * 50 < _total && CanDiscard()) { _page++; _ = LoadAsync(); } });
        PreviousCommand = new RelayCommand(() => { if (_page > 0 && CanDiscard()) { _page--; _ = LoadAsync(); } });
        Populate(null);
    }

    public void CompanyChanged()
    {
        Rows.Clear(); _page = 0; _total = 0; _selected = null; Populate(null); _ = LoadAsync();
    }
    private bool CanDiscard() => !IsDirty || MessageBox.Show("Elveted a nem mentett változtatásokat?",
        "Nem mentett adatlap", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    private void Populate(EntityRow? row)
    {
        _populating = true;
        _editId = row?.Json.GetProperty("id").GetGuid();
        _version = row?.Json.GetProperty("version").GetInt64() ?? 0;
        foreach (var field in Fields)
        {
            if (field.Definition.Kind == "boolean")
                field.Checked = row?[field.Definition.Id] is bool b ? b : field.Definition.DefaultChecked;
            else field.Text = row?[field.Definition.Id]?.ToString() ?? field.Definition.DefaultText ?? "";
        }
        _populating = false; IsDirty = false; Changed(nameof(EditorTitle));
    }

    private Guid CompanyId => _company() ?? throw new InvalidOperationException("Először válassz céget.");
    internal async Task<JsonElement> ExecuteAsync(string command, object input) =>
        await _connection.SendAsync("module.execute", new ModuleCommandRequest(CompanyId, Definition.ModuleId, command,
            JsonSerializer.SerializeToElement(input)), _shutdown);

    public async Task LoadAsync()
    {
        if (IsBusy || _shutdown.IsCancellationRequested) return;
        IsBusy = true;
        try
        {
            var result = await ExecuteAsync(Definition.Commands.List, new { search = Search, filter = Filter, page = _page, pageSize = 50 });
            Rows.Clear();
            foreach (var row in result.GetProperty("rows").EnumerateArray()) Rows.Add(new(row));
            _total = result.GetProperty("total").GetInt32();
            Message = _total == 0 ? "Ebben a nézetben még nincs partner. Az Új partner gombbal vehetsz fel egyet." : "A lista naprakész.";
            Changed(nameof(CountLabel));
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task SaveAsync()
    {
        if (IsBusy) return;
        var saved = false;
        IsBusy = true;
        try
        {
            var data = new JsonObject();
            foreach (var field in Fields)
            {
                if (field.Definition.Required && string.IsNullOrWhiteSpace(field.Text))
                    throw new ArgumentException(field.Definition.Label + ": kötelező mező.");
                data[field.Definition.Id] = field.Definition.Kind == "boolean"
                    ? JsonValue.Create(field.Checked) : JsonValue.Create(field.Text);
            }
            var result = await ExecuteAsync(Definition.Commands.Save, new { id = _editId, version = _version, data });
            _selected = new(result); Populate(_selected);
            saved = true;
            Message = "A partner mentése sikerült.";
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { IsBusy = false; }
        if (saved) { await LoadAsync(); Message = "A partner mentése sikerült."; }
    }
    private async Task ArchiveAsync()
    {
        if (IsBusy || _editId is null) { Message = "Először válassz meglévő partnert."; return; }
        if (MessageBox.Show("A partner inaktív állapotba kerül. Az adatai megmaradnak.", "Partner archiválása",
            MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        var archived = false;
        IsBusy = true;
        try
        {
            await ExecuteAsync(Definition.Commands.Archive, new { id = _editId, version = _version });
            _selected = null; Populate(null); archived = true; Message = "A partner archiválva.";
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { IsBusy = false; }
        if (archived) { await LoadAsync(); Message = "A partner archiválva."; }
    }
    private async Task ExportAsync()
    {
        if (IsBusy) return;
        var dialog = new SaveFileDialog { Filter = "CSV fájl (*.csv)|*.csv", FileName = "partnerek.csv" };
        if (dialog.ShowDialog() != true) return;
        IsBusy = true;
        try
        {
            var result = await ExecuteAsync(Definition.Commands.Export, new { search = Search, filter = Filter });
            await File.WriteAllTextAsync(dialog.FileName, result.GetProperty("csv").GetString(), new UTF8Encoding(true), _shutdown);
            Message = "A szűrt partnerlista exportálva.";
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { IsBusy = false; }
    }
    internal void Notify(string message) => Message = message;
}
