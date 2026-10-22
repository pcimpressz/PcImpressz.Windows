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
        Net = MoneyDisplay.Format(json.GetProperty("net").GetDecimal());
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

internal sealed partial class LineEditor : Observable
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
    private Guid? _productId;
    public bool HasNoProduct=>ProductId is null;
    public Guid? ProductId {get=>_productId;set{_productId=value;Changed();Changed(nameof(HasNoProduct));}}
    public bool IsService { get => _isService; set { _isService = value; if (value) ProductId = null; Changed(); Touch(); } }
    public string Name { get => _name; set { _name = value; Changed(); Touch(); } }
    public string Sku { get => _sku; set { _sku = value; Changed(); Touch();SyncProductField("sku",value); } }
    public string Barcode { get => _barcode; set { _barcode = value; Changed(); Touch();SyncProductField("barcode",value); } }
    public string UnitCode { get => _unit; set { _unit = value; Changed(); Touch(); } }
    public string QuantityText { get => _quantity; set { _quantity = value; Changed(); Changed(nameof(SuggestedSale)); Touch(); } }
    public string PriceText { get => _price; set { _price = value; Changed(); Changed(nameof(SuggestedSale)); Touch(); } }
    public string VatText { get => _vat; set { _vat = value; Changed(); Changed(nameof(SuggestedSale)); Touch(); } }
    public string SearchText { get => _search; set { _search = value; Changed(); } }
    public string MatchLabel { get; private set; } = "";
    public string SuggestedSale => "Árjavaslat: nettó beszerzés × (1 + haszonkulcs / 100) × (1 + eladási áfa / 100).";
    public ObservableCollection<ProductOption> Matches { get; } = [];
    public ProductOption? SelectedMatch
    {
        get => _match;
        set
        {
            _match = value; Changed();
            if (value is null || _filling) return;
            _filling = true;
            if(!_page.CanEditInvoiceFacts&&!UnitCode.Equals(value.UnitCode,StringComparison.OrdinalIgnoreCase)){_filling=false;_page.ProductMessage("Eltérő mértékegység: ezt a párosítást kézi bevételezésként kell rendezni.");_match=null;Changed(nameof(SelectedMatch));return;}
            IsService = false; ProductId = value.Id; if(_page.CanEditInvoiceFacts)Name = value.Name; Sku = value.Sku; Barcode = value.Barcode;if(_page.CanEditInvoiceFacts)UnitCode = value.UnitCode;
            _filling = false;
            _page.MarkDirty(); _= _page.LoadLineProductAsync(this);
        }
    }
    public ICommand SearchCommand { get; }
    public ICommand NewProductCommand { get; }
    public ICommand RemoveCommand { get; }
    public void ShowMatch(string label) { MatchLabel = label; Changed(nameof(MatchLabel)); }
    public void FillQuiet(Action fill) { _filling = true; fill(); _filling = false; }
    private void Touch() { if (!_filling) _page.MarkDirty(); }
}

internal sealed partial class ReceivingPageViewModel : Observable, IWorkspacePage, IColumnList
{
    private readonly HostConnection _connection;
    private readonly Func<Guid?> _company;
    private readonly CancellationToken _shutdown;
    private readonly ColumnList _columns;
    private readonly TypedSearch _typed;
    private readonly ListLoader _loader;
    private object? _appliedListQuery;
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
        InitializeReversal();
        ManualCommand = new RelayCommand(BeginManual);
        NavCommand = new RelayCommand(() => IsNav = !IsNav);
        ImportCommand = new RelayCommand(() => BeginIntake("import"));
        BackCommand = new RelayCommand(()=>_=ClosePreparedEditorAsync());
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
        SearchCommand = new RelayCommand(() => { _receiptPage=0;_typed.CancelPending(); _ = LoadAsync(); });
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
    public bool IsNav { get => _navExpanded; set { if(IsBusy)return; _navExpanded=value;Changed();Changed(nameof(IsReceiptListVisible));Changed(nameof(IntakeHelp));if(value&&Suppliers.Count==0)_=LoadSuppliersAsync(); } }
    public bool IsImport => _mode == "import";
    public bool IsReceiptListVisible=>!IsNav&&!IsEditor;
    public bool IsEditor => _mode == "editor";
    public bool IsBusy { get => _busy; private set { _busy = value; Changed(); Changed(nameof(IsNotBusy)); Changed(nameof(CanPickSupplier)); } }
    public bool IsNotBusy => !IsBusy;
    public bool IsDirty { get => _dirty; private set { _dirty = value; Changed(); Changed(nameof(EditorTitle)); } }
    public bool IsEditable => _editId is null || _status is "uj" or "fuggo";
    public bool CanPickSupplier => !IsBusy && (IsImport || IsEditable);
    public bool IsLocked => !IsEditable;
    public bool CanClassify => (_editId is not null||_preparationIdentity is not null) && _status is "uj" or "fuggo";
    public string Message { get => _message; private set { _message = value; Changed(); } }
    public string CountsLabel { get => _counts; private set { _counts = value; Changed(); } }
    public string MatchCount => _matches + " db találat";
    public string Search { get => _search; set { if (_search == value) return; _search = value;_receiptPage=0; Changed(); _typed.Schedule(); } }
    public string Filter { get => _filter; set { if (string.IsNullOrEmpty(value) || _filter == value) return; _filter = value;_receiptPage=0; Changed(); if (!_populating) _ = LoadAsync(); } }
    public string Number { get => _number; set { if(!_populating&&IsNavReceipt&&_number!=value)ConvertToManual();_number = value; Changed(); MarkDirty(); Changed(nameof(EditorTitle)); } }
    public string IssuedOn { get => _issued; set { _issued = value; Changed(); MarkDirty(); } }
    public string DueOn { get => _due; set { _due = value; Changed(); MarkDirty(); } }
    public string Notes { get => _notes; set { _notes = value; Changed(); MarkDirty(); } }
    public string Warehouse { get => _warehouse; set { _warehouse = value; Changed(); MarkDirty(); } }
    public string NetLabel => "Nettó: " + _net + " Ft";
    public string GrossLabel => "Bruttó: " + _gross + " Ft";
    public string EditorTitle => (_editId is null&&_navPreview is null ? "Új kézi számla" : Number) + " · " + ReceivingStatus(_status) + (IsDirty ? " · nincs mentve" : "");
    public string IntakeTitle => !IsImport ? "NAV-számla besorolása" : "Számla importálása";
    public string IntakeHelp => !IsImport
        ? "Dátum szerint kérd le a bejövő NAV-számlákat, majd kattints egy számlára. A tételek alatta megjelennek; a hiányzó szállítót innen is felveheted. A tételek betöltése után te döntesz a bevételezésről, költségről vagy függő állapotról. A cég NAV-kapcsolatát a Beállításokban adhatod meg."
        : "Excel (xlsx) vagy CSV: termék neve, mennyiség, nettó egységár. A saját cikkszám, gyártói cikkszám és vonalkód segít a párosításban. PDF-hez a külön PDF alapján rögzítés gombot használd.";
    public SupplierOption? SelectedSupplier { get => _supplier; set { if(!_populating&&IsNavReceipt&&value is null&&_supplier is not null)return;if(!_populating&&IsNavReceipt&&_supplier is not null&&_supplier.Id!=value?.Id)ConvertToManual();_supplier = value; Changed();Changed(nameof(NavSupplierHint)); MarkDirty(); } }
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

    public void PermissionsChanged(){Changed(nameof(IsNotBusy));}
    public void CompanyChanged()
    {
        ClearPreparation();_navPreview=null;
        ResetInvoices();
        ResetNavList();
        _appliedListQuery=null;
        _mode = "home"; NotifyMode(); Rows.Clear(); Lines.Clear(); Suppliers.Clear();
        ClearPdfPreview();
        _editId = null; _version = 0; _status = "uj"; _selected = null; IsDirty = false; _ = LoadAsync();
    }
    public void MarkDirty() { if (!_populating && IsEditor) { IsDirty = true; UpdateTotals();SchedulePreparation(); } }
    public void RemoveLine(LineEditor line)
    {
        if (!CanEditInvoiceFacts || Lines.Count < 2) { Message = "Legalább egy tétel maradjon a számlán."; return; }
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
    public Task CreateProductAsync(LineEditor line) => SaveLineProductAsync(line);

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
            var query = new
            {
                search = Search, filter = Filter, page = _receiptPage, pageSize = InvoicePageSize,
                sort = SortColumn, descending = SortDescending
            };
            var result = await ModuleAsync("receiving", "list", query);
            if (!_loader.IsCurrent(generation)) return;
            _appliedListQuery=query;
            Rows.Clear();
            foreach (var row in result.GetProperty("rows").EnumerateArray()) Rows.Add(new(row));
            var counts = result.GetProperty("counts");
            CountsLabel = counts.GetProperty("uj").GetInt32() + " új · " + counts.GetProperty("bevetelezve").GetInt32()
                + " bevételezve · " + counts.GetProperty("koltseg").GetInt32() + " költség · " + counts.GetProperty("fuggo").GetInt32() + " függő";
            _matches = result.GetProperty("total").GetInt32();
            Changed(nameof(MatchCount));Changed(nameof(ReceiptPageLabel));
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
            ClearPreparation();Fill(detail);
            _mode = "editor"; NotifyMode();
            if(_company() is Guid company)await LoadLineProductsAsync(company);
            Message = "A szállító a partnertörzs beszállítója, a termék a terméktörzsből jön.";
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { IsBusy = false; }
    }
    internal async Task<bool> SaveAsync()
    {
        if (IsBusy || !IsEditable) return false;
        if (SelectedSupplier is null) { Message = "Válassz szállítót, vagy vegyél fel újat."; return false; }
        if(_company() is not Guid savingCompany)return false;
        IsBusy = true;
        try
        {
            foreach(var line in Lines.Where(l=>l.ProductDirty).ToArray())await SaveLineProductAsync(line,true);
            if(_company()!=savingCompany)return false;
            if(_navPreview is {} preview)
            {
                if(!_convertToManual&&Tax(SelectedSupplier.TaxId)!=Tax(preview.SupplierTaxNumber))throw new ArgumentException("A szállító adószáma nem egyezik a NAV-számlával.");
                var imported=await ModuleAsync("receiving","nav-import",new{number=preview.Number,supplierTaxNumber=preview.SupplierTaxNumber,supplierId=Suppliers.FirstOrDefault(s=>Tax(s.TaxId)==Tax(preview.SupplierTaxNumber))?.Id,batchIndex=preview.BatchIndex});
                if(_company()!=savingCompany)return false;
                _editId=imported.GetProperty("id").GetGuid();_version=imported.GetProperty("version").GetInt64();
                _status=Get(imported,"status");_receiptSource="nav";_navPreview=null;
                var originalLines=imported.GetProperty("lines").EnumerateArray().ToArray();for(var i=0;i<Math.Min(Lines.Count,originalLines.Length);i++)Lines[i].Id=originalLines[i].GetProperty("id").GetGuid();
                if(_status is not ("uj" or "fuggo")){Fill(imported);throw new ArgumentException("A számlát közben már lezárták; a friss állapot látható.");}
            }
            var saved = await ModuleAsync("receiving", "save", new
            {
                id = _editId, version = _version,
                draft = new
                {
                    number = Number, supplierId = SelectedSupplier.Id, issuedOn = IssuedOn, dueOn = DueOn, notes = Notes, warehouse = Warehouse, source = _draftSource, convertToManual=_convertToManual,
                    lines = Lines.Select(LinePayload).ToArray()
                }
            });
            if(_company()!=savingCompany)return false;
            Fill(saved,true);
            if(_company() is Guid company)await LoadLineProductsAsync(company);
            _ = LoadAsync();
            Message = "A számla mentve, besorolásra vár. Bevételezheted, költségként hagyhatod, vagy függőben tarthatod.";
            IsDirty = false;return true;
        }
        catch (Exception ex) { Message = ex.Message;return false; }
        finally { IsBusy = false; }
    }
    private async Task ClassifyAsync(string status)
    {
        if(_preparationIdentity is not null&&!await SaveAsync())return;
        Guid id; long version;
        if (IsEditor && _editId is Guid open)
        {
            if (IsDirty&&!await SaveAsync())return;
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
            await RefreshNavReceiptFlagsAsync(saved);
            classifiedMessage = status switch
            {
                "bevetelezve" => "Bevételezve. A készletes termékek mennyisége a számlán megadott raktárban megnőtt.",
                "koltseg" => "Költségként rögzítve, készletre nem kerül.",
                "fuggo" => "Függőben hagyva. Később még bevételezhető vagy költségként lezárható.",
                _ => "A számla visszakerült az új tételek közé."
            };
            classified = true;
            Message = classifiedMessage;
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { IsBusy = false; }
        if (classified)
        {
            await LoadAsync();
            Message = classifiedMessage;
        }
    }
    private async Task ImportFileAsync()
    {
        if (IsBusy) return;
        if (IsImport && SelectedSupplier is null) { Message = "Az importhoz válassz szállítót, vagy vegyél fel újat."; return; }
        if(!CanLeave())return;
        var navFile = IsNav && !IsImport;
        var dialog = new OpenFileDialog { Title = navFile ? "NAV-számla XML" : "Számla importálása", CheckFileExists = true,
            Filter = navFile ? "NAV XML (*.xml)|*.xml" : "Excel, CSV vagy XML (*.xlsx;*.csv;*.xml)|*.xlsx;*.csv;*.xml" };
        if (dialog.ShowDialog() != true) return;
        IsBusy = true;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 1_500_000) throw new IOException("A fájl nagyobb 1,5 MB-nál.");
            var bytes = await File.ReadAllBytesAsync(dialog.FileName, _shutdown);
            var saved = await ModuleAsync("receiving", "import", new
            {
                supplierId = navFile ? SelectedNavSupplier?.Id : SelectedSupplier?.Id, number = navFile ? "" : Number, issuedOn = navFile ? "" : IssuedOn, dueOn = navFile ? "" : DueOn,
                source = navFile ? "nav" : "xls", fileName = Path.GetFileName(dialog.FileName), contentBase64 = Convert.ToBase64String(bytes)
            });
            await LoadSuppliersAsync();
            Fill(saved);
            _mode = "editor"; NotifyMode(); IsDirty = false; _ = LoadAsync();
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
            var result = await ModuleAsync("receiving", "export", _appliedListQuery ?? throw new InvalidOperationException("Először töltsd be az exportálandó listát."));
            await AtomicExport.WriteAsync(dialog.FileName, result.GetProperty("csv").GetString() ?? "", _shutdown);
            Message = "A szűrt számlalista exportálva.";
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { IsBusy = false; }
    }
    private async Task CreateSupplierAsync(bool forNav=false)
    {
        if (IsBusy || !IsEditable && !IsImport && !IsNav) return;
        var (name, tax) = Ask("Új szállító", "Beszállítóként kerül a partnertörzsbe, és azonnal kiválasztható.", "Szállító neve", (forNav||_navPreview is not null)?(_navPreview??SelectedNavInvoice)?.SupplierName??"":"", "Adószám", (forNav||_navPreview is not null)?(_navPreview??SelectedNavInvoice)?.SupplierTaxNumber??"":"");
        if (name is null || tax is null) return;
        IsBusy = true;
        try
        {
            var saved = await ModuleAsync("partners", "save", new { id = (Guid?)null, version = 0, data = new { name, taxId = tax, country = "HU", isSupplier = true, isActive = true } });
            await LoadSuppliersAsync();
            var id = saved.GetProperty("id").GetGuid();
            if(forNav)SelectedNavSupplier=Suppliers.FirstOrDefault(item=>item.Id==id);
            if(!forNav||_navPreview is not null)SelectedSupplier=Suppliers.FirstOrDefault(item=>item.Id==id);
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
        var navSelected = _navSupplier?.Id;
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
        SelectedNavSupplier=Suppliers.FirstOrDefault(item=>item.Id==navSelected)??(SelectedNavInvoice is {} invoice?Suppliers.FirstOrDefault(item=>Tax(item.TaxId)==Tax(invoice.SupplierTaxNumber)):null);
    }
    private void Fill(JsonElement detail, bool preservePdf=false)
    {
        _populating = true;
        if(!preservePdf)ClearPdfPreview();
        _navPreview=null;_receiptSource=Get(detail,"source");
        _editId = detail.GetProperty("id").GetGuid();
        _version = detail.GetProperty("version").GetInt64();
        _status = detail.GetProperty("status").GetString() ?? "uj";
        _number = detail.GetProperty("number").GetString() ?? "";
        _issued = detail.GetProperty("issuedOn").GetString() ?? "";
        _due = detail.GetProperty("dueOn").GetString() ?? "";
        _notes = detail.TryGetProperty("notes", out var notes) ? notes.GetString() ?? "" : "";
        _warehouse = detail.GetProperty("warehouse").GetString() ?? "Bolt";
        _net = MoneyDisplay.Format(detail.GetProperty("net").GetDecimal());
        _gross = MoneyDisplay.Format(detail.GetProperty("gross").GetDecimal());
        var supplierId = detail.GetProperty("supplierId").GetGuid();
        if (supplierId!=Guid.Empty && Suppliers.All(item => item.Id != supplierId))
            Suppliers.Add(new(supplierId, detail.GetProperty("supplierName").GetString() ?? "", detail.GetProperty("supplierTaxId").GetString() ?? ""));
        _supplier = Suppliers.FirstOrDefault(item => item.Id == supplierId);
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
            line.StockItem=!row.TryGetProperty("stockItem",out var stock)||stock.GetBoolean();
            line.ShowMatch(row.TryGetProperty("matchLabel",out var label)?label.GetString()??"":Get(row,"match"));
            Lines.Add(line);
        }
        _populating = false; IsDirty = false;
        Changed(nameof(Number)); Changed(nameof(IssuedOn)); Changed(nameof(DueOn)); Changed(nameof(Notes)); Changed(nameof(Warehouse));
        Changed(nameof(SelectedSupplier)); Changed(nameof(NavSupplierHint)); Changed(nameof(NetLabel)); Changed(nameof(GrossLabel)); Changed(nameof(EditorTitle));
        Changed(nameof(IsNavReceipt));Changed(nameof(CanEditInvoiceFacts));Changed(nameof(CanAddInvoiceLine));Changed(nameof(IsEditable)); Changed(nameof(IsLocked)); Changed(nameof(CanPickSupplier)); Changed(nameof(CanClassify));
    }
    private void BeginManual()
    {
        if (IsBusy || !CanLeave()) return;
        ClearPreparation();
        _populating = true;
        ClearPdfPreview();
        _navPreview=null;
        _editId = null; _version = 0; _status = "uj"; _number = ""; _issued = DateTime.Now.ToString("yyyy-MM-dd"); _due = ""; _notes = ""; _warehouse = "Bolt"; _supplier = null;
        Lines.Clear(); Lines.Add(new(this));
        _populating = false; IsDirty = false; _mode = "editor"; NotifyMode(); UpdateTotals(); _ = LoadSuppliersAsync();
        Message = "A szállítót a beszállítók közül válaszd, vagy vegyél fel újat. A terméket keresd ki, vagy vegyél fel újat.";
    }
    private void BeginIntake(string mode)
    {
        if (IsBusy || !CanLeave()) return;
        ClearPreparation();_navPreview=null;
        _populating = true; _number = ""; _issued = ""; _due = ""; _supplier = null; _populating = false; IsDirty = false;
        if(mode=="nav")_navExpanded=true;
        _mode = mode; NotifyMode(); _ = LoadSuppliersAsync();
        Message = IntakeHelp;
    }
    private void Back()
    {
        if (IsBusy || !CanLeave()) return;
        ClearPreparation();_navPreview=null;ClearPdfPreview(); _mode = "home"; NotifyMode(); IsDirty = false; _ = LoadAsync();
    }
    private void AddLine()
    {
        if (!CanAddInvoiceLine) return;
        Lines.Add(new(this)); MarkDirty();
    }
    private bool CanLeave() => !IsDirty || MessageBox.Show("Elveted a nem mentett számlát?", "Nem mentett bevételezés", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    private void NotifyMode()
    {
        Changed(nameof(IsReceiptListVisible));Changed(nameof(IsHome)); Changed(nameof(IsNav)); Changed(nameof(IsImport)); Changed(nameof(IsEditor));
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
        _net = MoneyDisplay.Format(net);
        _gross = MoneyDisplay.Format(gross);
        Changed(nameof(NetLabel)); Changed(nameof(GrossLabel));
    }
    private object LinePayload(LineEditor line) => new
    {
        id = line.Id == Guid.Empty ? (Guid?)null : line.Id, productId = line.ProductId, name = line.Name, sku = line.Sku,
        barcode = line.Barcode, unitCode = line.UnitCode, quantity = Money(line.QuantityText, "Mennyiség"),
        netUnitPrice = Money(line.PriceText, "Nettó egységár"), vatRate = Money(line.VatText, "Áfakulcs"), isService = line.IsService,stockItem=line.StockItem
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
