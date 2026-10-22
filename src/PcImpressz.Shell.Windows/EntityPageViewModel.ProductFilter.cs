using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using PcImpressz.Shell.Customization;

namespace PcImpressz.Shell.Windows;

public sealed record CategoryPick(string Code, string Name, string ParentCode)
{
    public string Label => string.IsNullOrWhiteSpace(ParentCode) ? Name : Name + " · " + Code;
}

public sealed partial class EntityPageViewModel
{
    private readonly Dictionary<string, string> _columnText = new(StringComparer.Ordinal);
    private readonly List<CategoryPick> _categories = [];
    private string[]? _categoryCodes;
    private bool _noCategoryMatch;
    private string _categoryLabel = "";
    private TypedSearch? _columnTyping;
    private TypedSearch? _categoryTyping;
    private string _pendingColumn = "";
    private string _pendingText = "";
    private string _categoryLookup = "";
    private decimal? _minPrice, _maxPrice, _minStock, _maxStock;
    private string _priceFrom = "", _priceTo = "", _stockFrom = "", _stockTo = "";
    private bool _filterOpen;
    private int _columnFilterRevision;
    public ObservableCollection<CategoryPick> CategoryChoices { get; } = [];
    public int ColumnFilterRevision => _columnFilterRevision;
    public bool FilterPanelOpen { get => _filterOpen; set { _filterOpen = value; Changed(); } }
    public string PriceFromText { get => _priceFrom; set { _priceFrom = value; Changed(); } }
    public string PriceToText { get => _priceTo; set { _priceTo = value; Changed(); } }
    public string StockFromText { get => _stockFrom; set { _stockFrom = value; Changed(); } }
    public string StockToText { get => _stockTo; set { _stockTo = value; Changed(); } }
    public string CategoryLookup { get => _categoryLookup; set { _categoryLookup = value; Changed(); RefreshCategoryChoices(); if (ListSearch.Live) ScheduleCategory(); } }
    public ICommand ToggleFilterPanelCommand => new RelayCommand(() => FilterPanelOpen = !FilterPanelOpen);
    public ICommand ApplyDetailFilterCommand => new RelayCommand(ApplyDetailFilter);
    public ICommand ClearProductFiltersCommand => new RelayCommand(() => ClearProductFilters(reload: true));
    public bool IsColumnFiltered(string id) => id == "categoryCode" ? _categoryCodes is { Length: > 0 } : _columnText.TryGetValue(id, out var text) && text.Length > 0;
    public string ColumnText(string id) => _columnText.GetValueOrDefault(id) ?? "";
    public string ActiveFiltersLabel
    {
        get
        {
            if (!IsProductMaster) return "";
            var parts = new List<string>();
            foreach (var (id, text) in _columnText.Where(pair => pair.Value.Length > 0))
                parts.Add(ColumnTitle(id) + ": " + text);
            if (_categoryLabel.Length > 0) parts.Add("Kategória: " + _categoryLabel);
            if (_minPrice is not null || _maxPrice is not null) parts.Add("Ár: " + Bound(_minPrice) + "–" + Bound(_maxPrice));
            if (_minStock is not null || _maxStock is not null) parts.Add("Készlet: " + Bound(_minStock) + "–" + Bound(_maxStock));
            var status = Filters.FirstOrDefault(option => option.Value == Filter);
            if (Filter != "all" && status is not null) parts.Add(status.Label);
            return parts.Count == 0 ? "" : string.Join(" · ", parts);
        }
    }

    public void ScheduleColumnText(string id, string text)
    {
        if (!ListSearch.Live) return;
        _pendingColumn = id; _pendingText = text;
        (_columnTyping ??= new TypedSearch(() => ApplyColumnText(_pendingColumn, _pendingText, refreshHeaders: false), _shutdown)).Schedule();
    }

    public void RefreshFilterHeaders() => TouchFilters(headers: true, reload: false);

    public void ApplyColumnText(string id, string text, bool refreshHeaders = true)
    {
        text = text.Trim();
        if (text.Length > 80 || text.Any(char.IsControl) || text.Contains('"'))
        { Message = "A keresőszó legfeljebb 80 karakter, idézőjel nélkül."; return; }
        var current = _columnText.GetValueOrDefault(id) ?? "";
        if (current == text) return;
        if (text.Length == 0) _columnText.Remove(id);
        else _columnText[id] = text;
        ReloadFilters(refreshHeaders);
    }

    public void ClearCategory()
    {
        _categoryCodes = null; _categoryLabel = ""; _categoryLookup = "";
        Changed(nameof(CategoryLookup));
        RefreshCategoryChoices();
        ReloadFilters();
    }

    public void ApplyCategory(CategoryPick? category)
    {
        if (category is null) { Message = "Válassz egy kategóriát a listából."; return; }
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { category.Code };
        var grew = true;
        while (grew)
        {
            grew = false;
            foreach (var item in _categories)
                if (codes.Contains(item.ParentCode) && codes.Add(item.Code)) grew = true;
        }
        _categoryCodes = codes.Select(code => code.ToUpperInvariant()).ToArray();
        _noCategoryMatch = false;
        _categoryLabel = category.Name;
        ReloadFilters();
    }

    private void ScheduleCategory() =>
        (_categoryTyping ??= new TypedSearch(ApplyTypedCategory, _shutdown)).Schedule();

    private void ApplyTypedCategory()
    {
        var needle = _categoryLookup.Trim();
        if (needle.Length == 0)
        {
            if (_categoryCodes is null && !_noCategoryMatch) return;
            _categoryCodes = null; _noCategoryMatch = false; _categoryLabel = "";
            ReloadFilters(refreshHeaders: false);
            return;
        }
        var matches = _categories.Where(item => item.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || item.Code.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0)
        {
            if (_noCategoryMatch && _categoryLabel == needle) return;
            _categoryCodes = null; _noCategoryMatch = true; _categoryLabel = needle;
            ReloadFilters(refreshHeaders: false);
            return;
        }
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var match in matches) codes.Add(match.Code);
        var grew = true;
        while (grew)
        {
            grew = false;
            foreach (var item in _categories)
                if (codes.Contains(item.ParentCode) && codes.Add(item.Code)) grew = true;
        }
        var next = codes.Select(code => code.ToUpperInvariant()).OrderBy(code => code, StringComparer.Ordinal).ToArray();
        var label = matches.Length == 1 ? matches[0].Name : needle;
        if (!_noCategoryMatch && _categoryLabel == label && _categoryCodes is not null && _categoryCodes.SequenceEqual(next)) return;
        _categoryCodes = next; _noCategoryMatch = false; _categoryLabel = label;
        ReloadFilters(refreshHeaders: false);
    }

    public async Task LoadCategoryChoicesAsync()
    {
        if (!IsProductMaster) return;
        try
        {
            _categories.Clear();
            for (var page = 0; page < 20; page++)
            {
                var result = await ExecuteAsync("categories-list", new { search = "", filter = "all", page, pageSize = 200 });
                var count = 0;
                foreach (var row in result.GetProperty("rows").EnumerateArray())
                {
                    count++;
                    _categories.Add(new(Text(row, "code"), Text(row, "name"), Text(row, "parentCode")));
                }
                if (count < 200) break;
            }
            RefreshCategoryChoices();
        }
        catch (Exception ex) { Message = ex.Message; }
    }

    private void ApplyDetailFilter()
    {
        if (!TryBound(PriceFromText, out var minPrice) || !TryBound(PriceToText, out var maxPrice)
            || !TryBound(StockFromText, out var minStock) || !TryBound(StockToText, out var maxStock))
        { Message = "Az ár és a készlet tól–ig mezője üres vagy nemnegatív szám legyen."; return; }
        if (minPrice > maxPrice || minStock > maxStock) { Message = "A tól érték nem lehet nagyobb, mint az ig."; return; }
        _minPrice = minPrice; _maxPrice = maxPrice; _minStock = minStock; _maxStock = maxStock;
        ReloadFilters();
    }

    private void ClearProductFilters(bool reload = false)
    {
        _columnText.Clear(); _categoryCodes = null; _noCategoryMatch = false; _categoryLabel = ""; _categoryLookup = "";
        _minPrice = _maxPrice = _minStock = _maxStock = null;
        _priceFrom = _priceTo = _stockFrom = _stockTo = "";
        if (_filter != "all") { _filter = "all"; Changed(nameof(Filter)); }
        Changed(nameof(PriceFromText)); Changed(nameof(PriceToText)); Changed(nameof(StockFromText)); Changed(nameof(StockToText)); Changed(nameof(CategoryLookup));
        RefreshCategoryChoices();
        if (reload) ReloadFilters();
        else TouchFilters(headers: true, reload: false);
    }

    private void ReloadFilters(bool refreshHeaders = true)
    {
        TouchFilters(refreshHeaders, reload: false);
        _page = 0;
        _ = LoadAsync();
    }

    private void TouchFilters(bool headers, bool reload)
    {
        if (headers) { _columnFilterRevision++; Changed(nameof(ColumnFilterRevision)); }
        Changed(nameof(ActiveFiltersLabel));
        Changed(nameof(AppliedSearchLabel));
        if (reload) { _page = 0; _ = LoadAsync(); }
    }

    private string ComposeColumnSearch()
    {
        var parts = new List<string>();
        foreach (var (id, text) in _columnText)
        {
            if (text.Length == 0 || FieldPrefix(id) is not { Length: > 0 } prefix) continue;
            parts.Add(text.Any(char.IsWhiteSpace) || text.Contains(':') ? prefix + ":\"" + text + "\"" : prefix + ":" + text);
        }
        return string.Join(' ', parts);
    }

    private void RefreshCategoryChoices()
    {
        CategoryChoices.Clear();
        var needle = _categoryLookup.Trim();
        foreach (var category in _categories.Where(item => needle.Length == 0
            || item.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || item.Code.Contains(needle, StringComparison.OrdinalIgnoreCase)).Take(30))
            CategoryChoices.Add(category);
    }

    private static bool TryBound(string text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!decimal.TryParse(text.Replace(" ", "").Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            || parsed < 0 || parsed > 1_000_000_000) return false;
        value = parsed;
        return true;
    }

    private static string Bound(decimal? value) => value?.ToString("0.##", CultureInfo.GetCultureInfo("hu-HU")) ?? "…";
    private static string ColumnTitle(string id) => id switch
    {
        "name" => "Név", "sku" => "Saját cikkszám", "manufacturerPartNumber" => "Gyártói cikkszám",
        "barcode" => "Vonalkód", "unitCode" => "Egység", "revision" => "Revízió", _ => id
    };
    private static string FieldPrefix(string id) => id switch
    {
        "name" => "nev", "sku" => "sku", "manufacturerPartNumber" => "gyartoi",
        "barcode" => "vonalkod", "unitCode" => "egyseg", "revision" => "revizio", _ => ""
    };
}
