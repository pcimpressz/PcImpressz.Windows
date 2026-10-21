using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;

namespace PcImpressz.Shell.Windows;

public sealed record SupplierOption(Guid Id, string Name, string TaxId)
{
    public string Label => string.IsNullOrWhiteSpace(TaxId) ? Name : Name + " · " + TaxId;
}
public sealed record ProductOption(Guid Id, string Name, string Sku, string Barcode, string UnitCode)
{
    public string Label => string.IsNullOrWhiteSpace(Sku) ? Name : Name + " · " + Sku;
}

public sealed class ReceiptRow
{
    public ReceiptRow(JsonElement json)
    {
        Id = json.GetProperty("id").GetGuid();
        Version = json.GetProperty("version").GetInt64();
        Number = json.GetProperty("number").GetString() ?? "";
        SupplierName = json.GetProperty("supplierName").GetString() ?? "";
        IssuedOn = json.GetProperty("issuedOn").GetString() ?? "";
        SourceLabel = json.GetProperty("sourceLabel").GetString() ?? "";
        Status = json.GetProperty("status").GetString() ?? "";
        StatusLabel = json.GetProperty("statusLabel").GetString() ?? "";
        Net = json.GetProperty("net").GetDecimal().ToString("N2", CultureInfo.GetCultureInfo("hu-HU"));
    }
    public Guid Id { get; }
    public long Version { get; }
    public string Number { get; }
    public string SupplierName { get; }
    public string IssuedOn { get; }
    public string SourceLabel { get; }
    public string Net { get; }
    public string Status { get; }
    public string StatusLabel { get; }
    public object? this[string key] => key switch
    {
        "number" => Number, "supplierName" => SupplierName, "issuedOn" => IssuedOn,
        "sourceLabel" => SourceLabel, "net" => Net, "statusLabel" => StatusLabel, _ => ""
    };
}

internal sealed class LineEditor : Observable
{
    private readonly ReceivingPageViewModel _page;
    private bool _filling, _isService;
    private string _name = "", _sku = "", _barcode = "", _unit = "DB", _quantity = "1", _price = "", _vat = "27", _search = "";
    private ProductOption? _match;
    public LineEditor(ReceivingPageViewModel page)
    {
        _page = page;
        SearchCommand = new RelayCommand(() => _ = page.SearchProductAsync(this));
        NewProductCommand = new RelayCommand(() => _ = page.CreateProductAsync(this));
        RemoveCommand = new RelayCommand(() => page.RemoveLine(this));
    }
    public Guid Id { get; set; }
    public Guid? ProductId { get; set; }
    public bool IsService { get => _isService; set { _isService = value; if (value) ProductId = null; Changed(); Touch(); } }
    public string Name { get => _name; set { _name = value; Changed(); Touch(); } }
    public string Sku { get => _sku; set { _sku = value; Changed(); Touch(); } }
    public string Barcode { get => _barcode; set { _barcode = value; Changed(); Touch(); } }
    public string UnitCode { get => _unit; set { _unit = value; Changed(); Touch(); } }
    public string QuantityText { get => _quantity; set { _quantity = value; Changed(); Changed(nameof(SuggestedSale)); Touch(); } }
    public string PriceText { get => _price; set { _price = value; Changed(); Changed(nameof(SuggestedSale)); Touch(); } }
    public string VatText { get => _vat; set { _vat = value; Changed(); Touch(); } }
    public string SearchText { get => _search; set { _search = value; Changed(); } }
    public string MatchLabel { get; private set; } = "";
    public string SuggestedSale => ReceivingPageViewModel.TryMoney(PriceText, out var price) && price > 0
        ? "Javasolt eladási ár 30%: " + decimal.Round(price * 1.30m, 0, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.GetCultureInfo("hu-HU")) + " Ft" : "";
    public ObservableCollection<ProductOption> Matches { get; } = [];
    public ProductOption? SelectedMatch
    {
        get => _match;
        set
        {
            _match = value; Changed();
            if (value is null || _filling) return;
            _filling = true;
            IsService = false; ProductId = value.Id; Name = value.Name; Sku = value.Sku; Barcode = value.Barcode; UnitCode = value.UnitCode;
            _filling = false;
            _page.MarkDirty();
        }
    }
    public ICommand SearchCommand { get; }
    public ICommand NewProductCommand { get; }
    public ICommand RemoveCommand { get; }
    public void ShowMatch(string label) { MatchLabel = label; Changed(nameof(MatchLabel)); }
    public void FillQuiet(Action fill) { _filling = true; fill(); _filling = false; }
    private void Touch() { if (!_filling) _page.MarkDirty(); }
}

internal sealed class ReceivingPageViewModel : Observable, IWorkspacePage, IColumnList
{
    private readonly HostConnection _connection;
    private readonly Func<Guid?> _company;
    private readonly CancellationToken _shutdown;
    private readonly ColumnList _columns;
    private readonly TypedSearch _typed;
    private readonly ListLoader _loader;
    private int _matches;
    private string _mode = "home";
    private bool _busy, _dirty, _populating;
    private Guid? _editId;
    private long _version;
    private string _status = "uj";
    private string _message = "Válassz céget, majd töltsd be a számlákat.";
    private string _counts = "";
    private string _search = "", _filter = "uj", _number = "", _issued = "", _due = "", _notes = "", _warehouse = "Bolt";
    private string _net = "0", _gross = "0";
    private SupplierOption? _supplier;
    private ReceiptRow? _selected;
    public ReceivingPageViewModel(EntityPageDescriptor definition, HostConnection connection, Func<Guid?> company, CancellationToken shutdown, ListLayoutStore layouts)
    {
        (Definition, _connection, _company, _shutdown) = (definition, connection, company, shutdown);
        _columns = new ColumnList(layouts, definition);
        _typed = new TypedSearch(() => _ = LoadAsync(), shutdown);
        _loader = new ListLoader(LoadCore, shutdown);
        _columns.Changed += () => Changed(nameof(LayoutRevision));
        _columns.SortChanged += () => _ = LoadAsync();
        ManualCommand = new RelayCommand(BeginManual);
        NavCommand = new RelayCommand(() => BeginIntake("nav"));
        ImportCommand = new RelayCommand(() => BeginIntake("import"));
        BackCommand = new RelayCommand(Back);
        RefreshCommand = new RelayCommand(() => _ = LoadAsync());
        OpenCommand = new RelayCommand(() => _ = OpenAsync());
        SaveCommand = new RelayCommand(() => _ = SaveAsync());
        ReceiveCommand = new RelayCommand(() => _ = ClassifyAsync("bevetelezve"));
        ExpenseCommand = new RelayCommand(() => _ = ClassifyAsync("koltseg"));
        PendingCommand = new RelayCommand(() => _ = ClassifyAsync("fuggo"));
        AddLineCommand = new RelayCommand(AddLine);
        NewSupplierCommand = new RelayCommand(() => _ = CreateSupplierAsync());
        PickFileCommand = new RelayCommand(() => _ = ImportFileAsync());
        ExportCommand = new RelayCommand(() => _ = ExportAsync());
        SearchCommand = new RelayCommand(() => { _typed.CancelPending(); _ = LoadAsync(); });
    }
    public int LayoutRevision => _columns.Revision;
    public string SortColumn => _columns.SortColumn;
    public bool SortDescending => _columns.SortDescending;
    public IReadOnlyList<EntityColumn> VisibleColumns => _columns.VisibleColumns;
    public bool IsColumnVisible(string id) => _columns.IsColumnVisible(id);
    public bool SetColumnVisible(string id, bool visible)
    {
        if (_columns.SetColumnVisible(id, visible)) return true;
        Message = "Legalább egy oszlop maradjon látható.";
        return false;
    }
    public void ToggleSort(string columnId) => _columns.ToggleSort(columnId);
    public EntityPageDescriptor Definition { get; }
    public ObservableCollection<ReceiptRow> Rows { get; } = [];
    public ObservableCollection<LineEditor> Lines { get; } = [];
    public ObservableCollection<SupplierOption> Suppliers { get; } = [];
    public EntityFilterOption[] Filters { get; } =
    [
        new("uj", "Új, besorolásra vár"), new("bevetelezve", "Bevételezve"), new("koltseg", "Költség"),
        new("fuggo", "Függő"), new("mind", "Minden számla")
    ];
    public bool IsHome => _mode == "home";
    public bool IsNav => _mode == "nav";
    public bool IsImport => _mode == "import";
    public bool IsEditor => _mode == "editor";
    public bool IsBusy { get => _busy; private set { _busy = value; Changed(); Changed(nameof(IsNotBusy)); } }
    public bool IsNotBusy => !IsBusy;
    public bool IsDirty { get => _dirty; private set { _dirty = value; Changed(); Changed(nameof(EditorTitle)); } }
    public bool IsEditable => _editId is null || _status is "uj" or "fuggo";
    public bool CanPickSupplier => IsNav || IsImport || IsEditable;
    public bool IsLocked => !IsEditable;
    public bool CanClassify => _editId is not null && _status is "uj" or "fuggo";
    public string Message { get => _message; private set { _message = value; Changed(); } }
    public string CountsLabel { get => _counts; private set { _counts = value; Changed(); } }
    public string MatchCount => _matches + " db találat";
    public string Search { get => _search; set { if (_search == value) return; _search = value; Changed(); _typed.Schedule(); } }
    public string Filter { get => _filter; set { if (string.IsNullOrEmpty(value) || _filter == value) return; _filter = value; Changed(); if (!_populating) _ = LoadAsync(); } }
    public string Number { get => _number; set { _number = value; Changed(); MarkDirty(); Changed(nameof(EditorTitle)); } }
    public string IssuedOn { get => _issued; set { _issued = value; Changed(); MarkDirty(); } }
    public string DueOn { get => _due; set { _due = value; Changed(); MarkDirty(); } }
    public string Notes { get => _notes; set { _notes = value; Changed(); MarkDirty(); } }
    public string Warehouse { get => _warehouse; set { _warehouse = value; Changed(); MarkDirty(); } }
    public string NetLabel => "Nettó: " + _net + " Ft";
    public string GrossLabel => "Bruttó: " + _gross + " Ft";
    public string EditorTitle => (_editId is null ? "Új kézi számla" : Number) + " · " + ReceivingStatus(_status) + (IsDirty ? " · nincs mentve" : "");
    public string IntakeTitle => IsNav ? "NAV-számla besorolása" : "Számla importálása";
    public string IntakeHelp => IsNav
        ? "Az élő NAV Online Számla lekérdezéshez technikai felhasználó kell; azt a következő kör köti be. A letöltött NAV-számla XML ugyanúgy új állapotba kerül, és eldöntöd: bevételezés, költség vagy függő."
        : "Excel (xlsx) vagy CSV: termék neve, mennyiség, nettó egységár. A saját cikkszám, gyártói cikkszám és vonalkód segít a párosításban. A PDF olvasása a következő kör.";
    public SupplierOption? SelectedSupplier { get => _supplier; set { _supplier = value; Changed(); MarkDirty(); } }
    public ReceiptRow? SelectedRow { get => _selected; set { _selected = value; Changed(); } }
    public ICommand ManualCommand { get; }
    public ICommand NavCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ReceiveCommand { get; }
    public ICommand ExpenseCommand { get; }
    public ICommand PendingCommand { get; }
    public ICommand AddLineCommand { get; }
    public ICommand NewSupplierCommand { get; }
    public ICommand PickFileCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand SearchCommand { get; }

    public void CompanyChanged()
    {
        _mode = "home"; NotifyMode(); Rows.Clear(); Lines.Clear(); Suppliers.Clear();
        _editId = null; _version = 0; _status = "uj"; _selected = null; IsDirty = false; _ = LoadAsync();
    }
    public void MarkDirty() { if (!_populating && IsEditor) { IsDirty = true; UpdateTotals(); } }
    public void RemoveLine(LineEditor line)
    {
        if (!IsEditable || Lines.Count < 2) { Message = "Legalább egy tétel maradjon a számlán."; return; }
        Lines.Remove(line); MarkDirty();
    }
    public async Task SearchProductAsync(LineEditor line)
    {
        if (IsBusy || !IsEditable) return;
        IsBusy = true;
        try
        {
            var result = await ModuleAsync("catalog", "products-list", new { search = string.IsNullOrWhiteSpace(line.SearchText) ? line.Name : line.SearchText, filter = "all", page = 0, pageSize = 30 });
            line.Matches.Clear();
            foreach (var row in result.GetProperty("rows").EnumerateArray())
                line.Matches.Add(new(row.GetProperty("id").GetGuid(), row.GetProperty("name").GetString() ?? "", row.GetProperty("sku").GetString() ?? "",
                    row.TryGetProperty("barcode", out var barcode) ? barcode.GetString() ?? "" : "", row.GetProperty("unitCode").GetString() ?? ""));
            Message = line.Matches.Count == 0 ? "Nincs ilyen aktív termék. Az Új termék gombbal felveheted." : line.Matches.Count + " termék a találatok között.";
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { IsBusy = false; }
    }
    public async Task CreateProductAsync(LineEditor line)
    {
        if (IsBusy || !IsEditable) return;
        var (name, sku) = Ask("Új termék", "A termék bekerül a terméktörzsbe, mértékegysége Darab (DB).", "Termék neve", line.Name, "Cikkszám", line.Sku);
        if (name is null || sku is null) return;
        IsBusy = true;
        try
        {
            await EnsurePieceAsync();
            var saved = await ModuleAsync("catalog", "products-save", new { id = (Guid?)null, version = 0, data = new { name, sku, unitCode = "DB", isPurchaseable = true, isSalable = true, isActive = true } });
            line.FillQuiet(() =>
            {
                line.ProductId = saved.GetProperty("id").GetGuid();
                line.Name = saved.GetProperty("name").GetString() ?? name;
                line.Sku = saved.GetProperty("sku").GetString() ?? sku;
                line.UnitCode = saved.GetProperty("unitCode").GetString() ?? "DB";
            });
            MarkDirty();
            Message = "Az új termék bent van a terméktörzsben, és ehhez a sorhoz tartozik.";
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { IsBusy = false; }
    }

    private Task LoadAsync() => _loader.Run();
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
            var result = await ModuleAsync("receiving", "list", new
            {
                search = Search, filter = Filter, page = 0, pageSize = 100,
                sort = SortColumn, descending = SortDescending
            });
            if (!_loader.IsCurrent(generation)) return;
            Rows.Clear();
            foreach (var row in result.GetProperty("rows").EnumerateArray()) Rows.Add(new(row));
            var counts = result.GetProperty("counts");
            CountsLabel = counts.GetProperty("uj").GetInt32() + " új · " + counts.GetProperty("bevetelezve").GetInt32()
                + " bevételezve · " + counts.GetProperty("koltseg").GetInt32() + " költség · " + counts.GetProperty("fuggo").GetInt32() + " függő";
            _matches = result.GetProperty("total").GetInt32();
            Changed(nameof(MatchCount));
            if (IsHome) Message = _matches == 0
                ? "Ebben a nézetben nincs számla. Kézi felvétel, NAV XML vagy import indítható."
                : "A lista naprakész. Új számlánál döntesz: bevételezés, költség vagy függő.";
        }
        catch (Exception ex) { if (_loader.IsCurrent(generation)) Message = ex.Message; }
        finally { IsBusy = false; }
    }
    private async Task OpenAsync()
    {
        if (SelectedRow is null) { Message = "Először válassz számlát."; return; }
        if (!CanLeave() || IsBusy) return;
        IsBusy = true;
        try
        {
            var detail = await ModuleAsync("receiving", "get", new { id = SelectedRow.Id });
            await LoadSuppliersAsync();
            Fill(detail);
            _mode = "editor"; NotifyMode();
            Message = "A szállító a partnertörzs beszállítója, a termék a terméktörzsből jön.";
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { IsBusy = false; }
    }
    private async Task SaveAsync()
    {
        if (IsBusy || !IsEditable) return;
        if (SelectedSupplier is null) { Message = "Válassz szállítót, vagy vegyél fel újat."; return; }
        IsBusy = true;
        try
        {
            var saved = await ModuleAsync("receiving", "save", new
            {
                id = _editId, version = _version,
                draft = new
                {
                    number = Number, supplierId = SelectedSupplier.Id, issuedOn = IssuedOn, dueOn = DueOn, notes = Notes, warehouse = Warehouse,
                    lines = Lines.Select(LinePayload).ToArray()
                }
            });
            Fill(saved);
            Message = "A számla mentve, besorolásra vár. Bevételezheted, költségként hagyhatod, vagy függőben tarthatod.";
            IsDirty = false;
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { IsBusy = false; }
    }
    private async Task ClassifyAsync(string status)
    {
        Guid id; long version;
        if (IsEditor && _editId is Guid open)
        {
            if (IsDirty) { Message = "Előbb mentsd a számlát, utána jöhet a besorolás."; return; }
            id = open; version = _version;
        }
        else if (SelectedRow is ReceiptRow row) { id = row.Id; version = row.Version; }
        else { Message = "Először válassz számlát."; return; }
        if (IsBusy) return;
        IsBusy = true;
        var classified = false;
        var classifiedMessage = "";
        try
        {
            var saved = await ModuleAsync("receiving", "classify", new { id, version, status });
            if (IsEditor) Fill(saved);
            classifiedMessage = status switch
            {
                "bevetelezve" => "Bevételezve. A párosított termékek ehhez a számlához kötve megmaradnak.",
                "koltseg" => "Költségként rögzítve, készletre nem kerül.",
                "fuggo" => "Függőben hagyva. Később még bevételezhető vagy költségként lezárható.",
                _ => "A számla visszakerült az új tételek közé."
            };
            classified = true;
            Message = classifiedMessage;
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { IsBusy = false; }
        if (classified && IsHome)
        {
            await LoadAsync();
            Message = classifiedMessage;
        }
    }
    private async Task ImportFileAsync()
    {
        if (IsBusy) return;
        if (IsImport && SelectedSupplier is null) { Message = "Az importhoz válassz szállítót, vagy vegyél fel újat."; return; }
        var dialog = new OpenFileDialog { Title = IsNav ? "NAV-számla XML" : "Számla importálása", CheckFileExists = true,
            Filter = IsNav ? "NAV XML (*.xml)|*.xml|PDF (*.pdf)|*.pdf" : "Excel, CSV vagy XML (*.xlsx;*.csv;*.xml)|*.xlsx;*.csv;*.xml|PDF (*.pdf)|*.pdf" };
        if (dialog.ShowDialog() != true) return;
        IsBusy = true;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 1_500_000) throw new IOException("A fájl nagyobb 1,5 MB-nál.");
            var bytes = await File.ReadAllBytesAsync(dialog.FileName, _shutdown);
            var saved = await ModuleAsync("receiving", "import", new
            {
                supplierId = SelectedSupplier?.Id, number = Number, issuedOn = IssuedOn, dueOn = DueOn,
                source = IsNav ? "nav" : "xls", fileName = Path.GetFileName(dialog.FileName), contentBase64 = Convert.ToBase64String(bytes)
            });
            await LoadSuppliersAsync();
            Fill(saved);
            _mode = "editor"; NotifyMode(); IsDirty = false;
            Message = "A számla új állapotban bekerült. Nézd át a párosítást, majd válassz besorolást.";
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { IsBusy = false; }
    }
    private async Task ExportAsync()
    {
        if (IsBusy) return;
        var dialog = new SaveFileDialog { Filter = "CSV fájl (*.csv)|*.csv", FileName = "bevetelezes.csv" };
        if (dialog.ShowDialog() != true) return;
        IsBusy = true;
        try
        {
            var result = await ModuleAsync("receiving", "export", new { search = Search, filter = Filter, page = 0, pageSize = 50, sort = SortColumn, descending = SortDescending });
            await File.WriteAllTextAsync(dialog.FileName, result.GetProperty("csv").GetString(), _shutdown);
            Message = "A szűrt számlalista exportálva.";
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { IsBusy = false; }
    }
    private async Task CreateSupplierAsync()
    {
        if (IsBusy || !IsEditable && !IsImport && !IsNav) return;
        var (name, tax) = Ask("Új szállító", "Beszállítóként kerül a partnertörzsbe, és azonnal kiválasztható.", "Szállító neve", "", "Adószám", "");
        if (name is null || tax is null) return;
        IsBusy = true;
        try
        {
            var saved = await ModuleAsync("partners", "save", new { id = (Guid?)null, version = 0, data = new { name, taxId = tax, country = "HU", isSupplier = true, isActive = true } });
            await LoadSuppliersAsync();
            var id = saved.GetProperty("id").GetGuid();
            SelectedSupplier = Suppliers.FirstOrDefault(item => item.Id == id);
            Message = "Az új szállító bent van a partnertörzsben.";
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { IsBusy = false; }
    }
    private async Task EnsurePieceAsync()
    {
        var list = await ModuleAsync("catalog", "units-list", new { search = "DB", filter = "all", page = 0, pageSize = 50 });
        if (list.GetProperty("rows").EnumerateArray().Any(row => string.Equals(row.GetProperty("code").GetString(), "DB", StringComparison.OrdinalIgnoreCase))) return;
        await ModuleAsync("catalog", "units-save", new { id = (Guid?)null, version = 0, data = new { name = "Darab", code = "DB", symbol = "db", isActive = true } });
    }
    private async Task LoadSuppliersAsync()
    {
        var selected = _supplier?.Id;
        Suppliers.Clear();
        var page = 0;
        int total;
        do
        {
            var result = await ModuleAsync("partners", "list", new { filter = "supplier", search = "", page, pageSize = 200 });
            total = result.GetProperty("total").GetInt32();
            foreach (var row in result.GetProperty("rows").EnumerateArray())
                Suppliers.Add(new(row.GetProperty("id").GetGuid(), row.GetProperty("name").GetString() ?? "", row.TryGetProperty("taxId", out var tax) ? tax.GetString() ?? "" : ""));
            page++;
        } while (Suppliers.Count < total && page < 50);
        _populating = true;
        _supplier = Suppliers.FirstOrDefault(item => item.Id == selected);
        _populating = false;
        Changed(nameof(SelectedSupplier));
    }
    private void Fill(JsonElement detail)
    {
        _populating = true;
        _editId = detail.GetProperty("id").GetGuid();
        _version = detail.GetProperty("version").GetInt64();
        _status = detail.GetProperty("status").GetString() ?? "uj";
        _number = detail.GetProperty("number").GetString() ?? "";
        _issued = detail.GetProperty("issuedOn").GetString() ?? "";
        _due = detail.GetProperty("dueOn").GetString() ?? "";
        _notes = detail.TryGetProperty("notes", out var notes) ? notes.GetString() ?? "" : "";
        _warehouse = detail.GetProperty("warehouse").GetString() ?? "Bolt";
        _net = detail.GetProperty("net").GetDecimal().ToString("N2", CultureInfo.GetCultureInfo("hu-HU"));
        _gross = detail.GetProperty("gross").GetDecimal().ToString("N2", CultureInfo.GetCultureInfo("hu-HU"));
        var supplierId = detail.GetProperty("supplierId").GetGuid();
        if (Suppliers.All(item => item.Id != supplierId))
            Suppliers.Add(new(supplierId, detail.GetProperty("supplierName").GetString() ?? "", detail.GetProperty("supplierTaxId").GetString() ?? ""));
        _supplier = Suppliers.First(item => item.Id == supplierId);
        Lines.Clear();
        foreach (var row in detail.GetProperty("lines").EnumerateArray())
        {
            var line = new LineEditor(this);
            line.FillQuiet(() =>
            {
                line.Id = row.GetProperty("id").GetGuid();
                line.ProductId = row.TryGetProperty("productId", out var product) && product.ValueKind == JsonValueKind.String ? product.GetGuid() : null;
                line.IsService = row.GetProperty("match").GetString() == "szolgaltatas";
                line.Name = row.GetProperty("name").GetString() ?? "";
                line.Sku = row.GetProperty("sku").GetString() ?? "";
                line.Barcode = row.GetProperty("barcode").GetString() ?? "";
                line.UnitCode = row.GetProperty("unitCode").GetString() ?? "";
                line.QuantityText = row.GetProperty("quantity").GetDecimal().ToString(CultureInfo.GetCultureInfo("hu-HU"));
                line.PriceText = row.GetProperty("netUnitPrice").GetDecimal().ToString(CultureInfo.GetCultureInfo("hu-HU"));
                line.VatText = row.GetProperty("vatRate").GetDecimal().ToString("0", CultureInfo.InvariantCulture);
            });
            line.ShowMatch(row.GetProperty("matchLabel").GetString() ?? "");
            Lines.Add(line);
        }
        _populating = false; IsDirty = false;
        Changed(nameof(Number)); Changed(nameof(IssuedOn)); Changed(nameof(DueOn)); Changed(nameof(Notes)); Changed(nameof(Warehouse));
        Changed(nameof(SelectedSupplier)); Changed(nameof(NetLabel)); Changed(nameof(GrossLabel)); Changed(nameof(EditorTitle));
        Changed(nameof(IsEditable)); Changed(nameof(IsLocked)); Changed(nameof(CanPickSupplier)); Changed(nameof(CanClassify));
    }
    private void BeginManual()
    {
        if (!CanLeave()) return;
        _populating = true;
        _editId = null; _version = 0; _status = "uj"; _number = ""; _issued = DateTime.Now.ToString("yyyy-MM-dd"); _due = ""; _notes = ""; _warehouse = "Bolt"; _supplier = null;
        Lines.Clear(); Lines.Add(new(this));
        _populating = false; IsDirty = false; _mode = "editor"; NotifyMode(); UpdateTotals(); _ = LoadSuppliersAsync();
        Message = "A szállítót a beszállítók közül válaszd, vagy vegyél fel újat. A terméket keresd ki, vagy vegyél fel újat.";
    }
    private void BeginIntake(string mode)
    {
        if (!CanLeave()) return;
        _populating = true; _number = ""; _issued = ""; _due = ""; _supplier = null; _populating = false; IsDirty = false;
        _mode = mode; NotifyMode(); _ = LoadSuppliersAsync();
        Message = IntakeHelp;
    }
    private void Back()
    {
        if (!CanLeave()) return;
        _mode = "home"; NotifyMode(); IsDirty = false; _ = LoadAsync();
    }
    private void AddLine()
    {
        if (!IsEditable) return;
        Lines.Add(new(this)); MarkDirty();
    }
    private bool CanLeave() => !IsDirty || MessageBox.Show("Elveted a nem mentett számlát?", "Nem mentett bevételezés", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    private void NotifyMode()
    {
        Changed(nameof(IsHome)); Changed(nameof(IsNav)); Changed(nameof(IsImport)); Changed(nameof(IsEditor));
        Changed(nameof(IntakeTitle)); Changed(nameof(IntakeHelp)); Changed(nameof(IsEditable)); Changed(nameof(IsLocked)); Changed(nameof(CanClassify)); Changed(nameof(EditorTitle));
    }
    private void UpdateTotals()
    {
        decimal net = 0, gross = 0;
        foreach (var line in Lines)
        {
            if (!TryMoney(line.QuantityText, out var quantity) || !TryMoney(line.PriceText, out var price) || !TryMoney(line.VatText, out var vat)) continue;
            var row = decimal.Round(quantity * price, 2, MidpointRounding.AwayFromZero);
            net += row; gross += row + decimal.Round(row * vat / 100m, 2, MidpointRounding.AwayFromZero);
        }
        _net = net.ToString("N2", CultureInfo.GetCultureInfo("hu-HU"));
        _gross = gross.ToString("N2", CultureInfo.GetCultureInfo("hu-HU"));
        Changed(nameof(NetLabel)); Changed(nameof(GrossLabel));
    }
    private object LinePayload(LineEditor line) => new
    {
        id = line.Id == Guid.Empty ? (Guid?)null : line.Id, productId = line.ProductId, name = line.Name, sku = line.Sku,
        barcode = line.Barcode, unitCode = line.UnitCode, quantity = Money(line.QuantityText, "Mennyiség"),
        netUnitPrice = Money(line.PriceText, "Nettó egységár"), vatRate = Money(line.VatText, "Áfakulcs"), isService = line.IsService
    };
    private Guid CompanyId => _company() ?? throw new InvalidOperationException("Először válassz céget.");
    private async Task<JsonElement> ModuleAsync(string module, string command, object input) =>
        await _connection.SendAsync("module.execute", new ModuleCommandRequest(CompanyId, module, command, JsonSerializer.SerializeToElement(input)), _shutdown);
    private static (string? First, string? Second) Ask(string title, string lead, string firstLabel, string firstValue, string secondLabel, string secondValue)
    {
        var window = new Window { Title = title, Width = 480, Height = 360, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Application.Current.MainWindow };
        window.SetResourceReference(Window.BackgroundProperty, "SurfaceBrush"); window.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = lead, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 14) });
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = firstLabel });
        var first = Box(firstValue); panel.Children.Add(first);
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = secondLabel, Margin = new(0, 8, 0, 0) });
        var second = Box(secondValue); panel.Children.Add(second);
        string? savedFirst = null, savedSecond = null;
        var button = new System.Windows.Controls.Button { Content = "Mentés a törzsbe", IsDefault = true, Margin = new(0, 16, 0, 0) };
        button.Click += (_, _) => { savedFirst = first.Text; savedSecond = second.Text; window.DialogResult = true; };
        panel.Children.Add(button); window.Content = panel;
        return window.ShowDialog() == true ? (savedFirst, savedSecond) : (null, null);
    }
    private static System.Windows.Controls.TextBox Box(string value)
    {
        var box = new System.Windows.Controls.TextBox { Text = value, MinHeight = 36, Margin = new(0, 4, 0, 0), Padding = new(8) };
        box.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "ElevatedBrush");
        box.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextBrush");
        return box;
    }
    internal static bool TryMoney(string text, out decimal value)
    {
        var raw = text.Trim().Replace(" ", "").Replace("\u00a0", "");
        if (raw.Contains(',') && raw.Contains('.')) raw = raw.Replace(".", "").Replace(',', '.');
        else if (raw.Contains(',')) raw = raw.Replace(',', '.');
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
    private static decimal Money(string text, string label) => TryMoney(text, out var value) ? value : throw new ArgumentException(label + ": számot adj meg.");
    private static string ReceivingStatus(string status) => status switch
    {
        "uj" => "Új, besorolásra vár", "bevetelezve" => "Bevételezve", "koltseg" => "Költség", "fuggo" => "Függő", _ => status
    };
}
