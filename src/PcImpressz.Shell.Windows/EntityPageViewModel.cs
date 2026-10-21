using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;

namespace PcImpressz.Shell.Windows;

public interface IWorkspacePage : INotifyPropertyChanged
{
    EntityPageDescriptor Definition { get; }
    bool IsDirty { get; }
    bool IsBusy { get; }
    bool HasUnresolvedOperation => false;
    void CompanyChanged();
    void PermissionsChanged();
}

public sealed class EntityFieldViewModel(EntityField definition) : Observable
{
    private string _text = definition.DefaultText ?? "";
    private bool _checked = definition.DefaultChecked;
    public EntityField Definition => definition;
    public string DisplayLabel { get; set; } = definition.Label;
    public int Span { get; set; } = definition.Id == "name" || definition.Kind == "multiline" ? 2 : 1;
    public bool Visible { get; set; } = true;
    public string Label => DisplayLabel + (definition.Required ? " *" : "");
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

public sealed partial class EntityPageViewModel : Observable, IWorkspacePage, IColumnList
{
    private readonly HostConnection _connection;
    private readonly Func<Guid?> _company;
    private readonly CancellationToken _shutdown;
    private readonly ColumnList _columns;
    private readonly TypedSearch _typed;
    private readonly ListLoader _loader;
    private sealed record AppliedQuery(string Search, string Filter, string Sort, bool Descending, int Page);
    private AppliedQuery? _applied;
    public string AppliedSearchLabel => _applied is null ? "Még nincs betöltött lista." : "Megjelenített keresés: " + (_applied.Search.Length == 0 ? "minden találat" : _applied.Search) + " · " + (Filters.FirstOrDefault(f => f.Value == _applied.Filter)?.Label ?? _applied.Filter);
    private bool _filtersReady;
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
    private bool _dossierOpen;
    private DateTime? _periodFrom;
    private DateTime? _periodTo;
    private string _dossierTitle = "Dosszié";
    private string _dossierSubtitle = "";
    private string _dossierNotice = "";
    private int _dossierRevision;
    public EntityPageDescriptor Definition { get; }
    public ObservableCollection<EntityRow> Rows { get; } = [];
    public ObservableCollection<EntityFieldViewModel> Fields { get; } = [];
    public string Search { get => _search; set { if (_search == value) return; _search = value; Changed(); _typed.Schedule(); } }
    public string Filter
    {
        get => _filter;
        set
        {
            if (string.IsNullOrEmpty(value) || _filter == value) return;
            _filter = value; Changed();
            if (_filtersReady) { _page = 0; _ = LoadAsync(); }
        }
    }
    public bool IsDirty { get => _dirty; private set { _dirty = value; Changed(); Changed(nameof(EditorTitle)); } }
    public EntityPageLabels Labels { get; }
    public EntityFilterOption[] Filters { get; }
    public string EditorTitle => (_editId is null ? Labels.NewTitle : Labels.EditTitle) + (IsDirty ? " · nincs mentve" : "");
    public bool IsBusy { get => _busy; private set { _busy = value; Changed(); Changed(nameof(IsNotBusy)); PermissionsChanged(); } }
    public bool IsNotBusy => !IsBusy;
    public bool CanWrite => !IsBusy && _connection.Allows(Definition.ModuleId+".write");
    public bool CanExport => !IsBusy && _connection.Allows(Definition.ModuleId+".export");
    public bool CanAdmin => !IsBusy && _connection.Session?.Role=="admin";
    public void PermissionsChanged(){Changed(nameof(CanWrite));Changed(nameof(CanExport));Changed(nameof(CanAdmin));Changed(nameof(CanOpenDossier));}
    public bool HasDossier => !string.IsNullOrWhiteSpace(Definition.DossierCommand);
    public bool CanOpenDossier => CanAdmin && HasDossier && _editId is not null && !IsBusy;
    public bool IsDossierOpen { get => _dossierOpen; private set { _dossierOpen = value; Changed(); Changed(nameof(ShowPartnerList)); } }
    public bool ShowPartnerList => !IsDossierOpen;
    public DateTime? PeriodFrom { get => _periodFrom; set { _periodFrom = value; Changed(); } }
    public DateTime? PeriodTo { get => _periodTo; set { _periodTo = value; Changed(); } }
    public string DossierTitle { get => _dossierTitle; private set { _dossierTitle = value; Changed(); } }
    public string DossierSubtitle { get => _dossierSubtitle; private set { _dossierSubtitle = value; Changed(); } }
    public string DossierNotice { get => _dossierNotice; private set { _dossierNotice = value; Changed(); } }
    public int DossierRevision { get => _dossierRevision; private set { _dossierRevision = value; Changed(); } }
    public IReadOnlyList<DossierCardRow> DossierCards { get; private set; } = [];
    public IReadOnlyList<DossierSectionRow> DossierSections { get; private set; } = [];
    public string Message { get => _message; private set { _message = value; Changed(); } }
    public string CountLabel => _total + " " + Labels.CountNoun + " · " + ((_applied?.Page ?? 0) + 1) + ". oldal";
    public string MatchCount => _total + " db találat";
    public int LayoutRevision => _columns.Revision;
    public string SortColumn => _columns.SortColumn;
    public bool SortDescending => _columns.SortDescending;
    public IReadOnlyList<EntityColumn> VisibleColumns => _columns.VisibleColumns;
    public EntityRow? SelectedRow
    {
        get => _selected;
        set
        {
            if (IsBusy || value is null || ReferenceEquals(value, _selected)) return;
            if (!CanDiscard()) { Changed(); return; }
            _selected = value; Populate(value); Changed();
        }
    }

    public event Action? EditorRequested;
    public void RequestEditSelected()
    {
        if (IsBusy) return;
        if (_selected is null) { Message = Labels.NeedSelection; return; }
        EditorRequested?.Invoke();
    }
    public ICommand SearchCommand { get; }
    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ArchiveCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand DossierCommand { get; }
    public ICommand DossierBackCommand { get; }
    public ICommand DossierFilterCommand { get; }
    public ICommand PeriodAllCommand { get; }
    public ICommand PeriodYearCommand { get; }
    public ICommand PeriodPreviousYearCommand { get; }
    public ICommand PeriodQuarterCommand { get; }

    internal EntityPageViewModel(EntityPageDescriptor definition, HostConnection connection, Func<Guid?> company, CancellationToken shutdown, ListLayoutStore layouts)
    {
        (Definition, _connection, _company, _shutdown) = (definition, connection, company, shutdown);
        _columns = new ColumnList(layouts, definition);
        _typed = new TypedSearch(() => { _page = 0; _ = LoadAsync(); }, shutdown);
        _loader = new ListLoader(LoadCore, shutdown);
        _columns.Changed += () => Changed(nameof(LayoutRevision));
        _columns.SortChanged += () => { _page = 0; _ = LoadAsync(); };
        Labels = definition.Labels ?? new EntityPageLabels();
        Filters = definition.Filters ?? PartnerFilters;
        foreach (var field in definition.Fields)
        {
            var vm = new EntityFieldViewModel(field);
            vm.Edited = () => { if (!_populating) IsDirty = true; };
            Fields.Add(vm);
        }
        SearchCommand = new RelayCommand(() => { _typed.CancelPending(); _page = 0; _ = LoadAsync(); });
        NewCommand = new RelayCommand(() => { if (!IsBusy && CanDiscard()) { _selected = null; Changed(nameof(SelectedRow)); Populate(null); Message = Labels.NewTitle + " adatlapja megnyitva."; EditorRequested?.Invoke(); } });
        SaveCommand = new RelayCommand(() => _ = SaveAsync());
        ArchiveCommand = new RelayCommand(() => _ = ArchiveAsync());
        ExportCommand = new RelayCommand(() => _ = ExportAsync());
        NextCommand = new RelayCommand(() => { if ((_page + 1) * 50 < _total && CanDiscard()) { _page++; _ = LoadAsync(); } });
        PreviousCommand = new RelayCommand(() => { if (_page > 0 && CanDiscard()) { _page--; _ = LoadAsync(); } });
        DossierCommand = new RelayCommand(() => _ = LoadDossierAsync());
        DossierBackCommand = new RelayCommand(() => IsDossierOpen = false);
        DossierFilterCommand = new RelayCommand(() => _ = LoadDossierAsync());
        PeriodAllCommand = new RelayCommand(() => SetPeriod(null, null));
        PeriodYearCommand = new RelayCommand(() => SetPeriod(new DateTime(DateTime.Today.Year, 1, 1), DateTime.Today));
        PeriodPreviousYearCommand = new RelayCommand(() => SetPeriod(new DateTime(DateTime.Today.Year - 1, 1, 1), new DateTime(DateTime.Today.Year - 1, 12, 31)));
        PeriodQuarterCommand = new RelayCommand(() => SetPeriod(DateTime.Today.AddDays(-90), DateTime.Today));
        InitializeLayoutEditor();
        Populate(null);
        _filtersReady = true;
    }
    public bool IsColumnVisible(string id) => _columns.IsColumnVisible(id);
    public bool SetColumnVisible(string id, bool visible)
    {
        if (_columns.SetColumnVisible(id, visible)) return true;
        Message = "Legalább egy oszlop maradjon látható.";
        return false;
    }
    public void ToggleSort(string columnId) => _columns.ToggleSort(columnId);

    public void CompanyChanged()
    {
        ResetAppearance(); _ = LoadAppearanceAsync();
        _applied = null; Changed(nameof(AppliedSearchLabel)); Rows.Clear(); _page = 0; _total = 0; _selected = null; Populate(null); _ = LoadAsync();
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
        _populating = false; IsDirty = false; Changed(nameof(EditorTitle)); Changed(nameof(CanOpenDossier));
        if (IsDossierOpen && row is not null) _ = LoadDossierAsync();
        else IsDossierOpen = false;
    }

    private void SetPeriod(DateTime? from, DateTime? to)
    {
        PeriodFrom = from; PeriodTo = to; _ = LoadDossierAsync();
    }

    private async Task LoadDossierAsync()
    {
        if (!HasDossier || _editId is not Guid || IsBusy) return;
        if (PeriodFrom is DateTime from && PeriodTo is DateTime to && from.Date > to.Date)
        { Message = "A kezdő dátum nem lehet későbbi, mint a záró."; return; }
        IsBusy = true;
        try
        {
            var result = await ExecuteAsync(Definition.DossierCommand!, new
            {
                id = _editId, from = PeriodFrom?.ToString("yyyy-MM-dd"), to = PeriodTo?.ToString("yyyy-MM-dd")
            });
            DossierTitle = Text(result, "title");
            DossierSubtitle = Text(result, "subtitle");
            DossierNotice = Text(result, "notice");
            DossierCards = result.GetProperty("cards").EnumerateArray().Select(card => new DossierCardRow(Text(card, "label"), Text(card, "value"))).ToArray();
            DossierSections = result.GetProperty("sections").EnumerateArray().Select(Section).ToArray();
            DossierRevision++; IsDossierOpen = true;
        }
        catch (Exception ex) { Message = ex.Message; DossierNotice = ex.Message; IsDossierOpen = true; DossierRevision++; }
        finally { IsBusy = false; }
    }

    private static DossierSectionRow Section(JsonElement section) => new(Text(section, "title"), Text(section, "source"), Text(section, "detail"),
        section.TryGetProperty("lines", out var lines) ? lines.EnumerateArray().Select(Line).ToArray() : []);

    private static DossierLineRow Line(JsonElement line)
    {
        var items = line.TryGetProperty("items", out var rows)
            ? rows.EnumerateArray().Select(item => new DossierItemRow(Text(item, "name"), Text(item, "quantity"), Text(item, "amount"))).ToArray()
            : [];
        return new(Text(line, "title"), Text(line, "detail"), Text(line, "amount"), items);
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private Guid CompanyId => _company() ?? throw new InvalidOperationException("Először válassz céget.");
    internal async Task<JsonElement> ExecuteAsync(string command, object input) =>
        await _connection.SendAsync("module.execute", new ModuleCommandRequest(CompanyId, Definition.ModuleId, command,
            JsonSerializer.SerializeToElement(input)), _shutdown);

    public Task LoadAsync() => _loader.Run();
    private async Task LoadCore(int generation)
    {
        while (IsBusy && _loader.IsCurrent(generation) && !_shutdown.IsCancellationRequested)
        {
            try { await Task.Delay(40, _shutdown); }
            catch (OperationCanceledException) { return; }
        }
        if (!_loader.IsCurrent(generation) || _shutdown.IsCancellationRequested) return;
        IsBusy = true;
        try
        {
            var query = new AppliedQuery(Search, Filter, SortColumn, SortDescending, _page);
            var result = await ExecuteAsync(Definition.Commands.List, new
            {
                search = query.Search, filter = query.Filter, page = query.Page, pageSize = 50,
                sort = query.Sort, descending = query.Descending
            });
            if (!_loader.IsCurrent(generation)) return;
            var receivedRows = result.GetProperty("rows").EnumerateArray().Select(row => new EntityRow(row)).ToArray();
            var receivedTotal = result.GetProperty("total").GetInt32();
            _applied = query; Changed(nameof(AppliedSearchLabel));
            Rows.Clear();
            foreach (var row in receivedRows) Rows.Add(row);
            _total = receivedTotal;
            Message = _total == 0 ? Labels.EmptyMessage : "A lista naprakész.";
            Changed(nameof(CountLabel)); Changed(nameof(MatchCount));
        }
        catch (Exception ex) { if (_loader.IsCurrent(generation)) Message = ex.Message; }
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
                if(field.Definition.Kind=="decimal")
                {
                    if(string.IsNullOrWhiteSpace(field.Text)){data[field.Definition.Id]=null;continue;}
                    if(!decimal.TryParse(field.Text.Replace(" ","").Replace(",","."),System.Globalization.NumberStyles.AllowDecimalPoint|System.Globalization.NumberStyles.AllowLeadingSign,System.Globalization.CultureInfo.InvariantCulture,out var amount))throw new ArgumentException(field.Definition.Label+": számot adj meg.");
                    data[field.Definition.Id]=JsonValue.Create(amount);continue;
                }
                data[field.Definition.Id] = field.Definition.Kind == "boolean"
                    ? JsonValue.Create(field.Checked) : JsonValue.Create(field.Text);
            }
            var result = await ExecuteAsync(Definition.Commands.Save, new { id = _editId, version = _version, data });
            var warning = result.TryGetProperty("warning", out var warningValue) && warningValue.ValueKind == JsonValueKind.String
                ? warningValue.GetString() : null;
            _selected = new(result); Populate(_selected);
            saved = true;
            Message = string.IsNullOrWhiteSpace(warning) ? Labels.SavedMessage : warning;
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { IsBusy = false; }
        if (saved) { var notice = Message; await LoadAsync(); Message = notice; }
    }
    private async Task ArchiveAsync()
    {
        if (IsBusy || _editId is null) { Message = Labels.NeedSelection; return; }
        if (MessageBox.Show(Labels.ArchiveQuestion, Labels.ArchiveTitle,
            MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        var archived = false;
        IsBusy = true;
        try
        {
            await ExecuteAsync(Definition.Commands.Archive, new { id = _editId, version = _version });
            _selected = null; Populate(null); archived = true; Message = Labels.ArchivedMessage;
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { IsBusy = false; }
        if (archived) { await LoadAsync(); Message = Labels.ArchivedMessage; }
    }
    private async Task ExportAsync()
    {
        if (IsBusy) return;
        if (_applied is not { } query) { Message = "Először töltsd be az exportálandó listát."; return; }
        var dialog = new SaveFileDialog { Filter = "CSV fájl (*.csv)|*.csv", FileName = Labels.ExportFileName };
        if (dialog.ShowDialog() != true) return;
        IsBusy = true;
        try
        {
            var result = await ExecuteAsync(Definition.Commands.Export, new { search = query.Search, filter = query.Filter, sort = query.Sort, descending = query.Descending });
            await AtomicExport.WriteAsync(dialog.FileName, result.GetProperty("csv").GetString() ?? "", _shutdown);
            Message = Labels.ExportedMessage;
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { IsBusy = false; }
    }
    internal void Notify(string message) => Message = message;
    public sealed record DossierCardRow(string Label, string Value);
    public sealed record DossierItemRow(string Name, string Quantity, string Amount);
    public sealed record DossierLineRow(string Title, string Detail, string Amount, DossierItemRow[] Items);
    public sealed record DossierSectionRow(string Title, string Source, string Detail, DossierLineRow[] Lines);
    private static readonly EntityFilterOption[] PartnerFilters =
    [
        new("all", "Aktív partnerek"), new("customer", "Vevők"), new("supplier", "Szállítók"),
        new("manufacturer", "Gyártók"), new("archived", "Archiváltak")
    ];
}
