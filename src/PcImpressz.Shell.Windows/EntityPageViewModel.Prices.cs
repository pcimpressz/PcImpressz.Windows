using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using PcImpressz.Contracts;

namespace PcImpressz.Shell.Windows;

public sealed record SalePriceKindOption(string Value, string Label);

public sealed class SalePriceLine : Observable
{
    private readonly Action _edited;
    private bool _quiet;
    private string _name = "";
    private string _kind = "gross";
    private string _value = "";
    private string _hint = "";
    public SalePriceLine(Action edited, Action<SalePriceLine> remove)
    {
        _edited = edited;
        RemoveCommand = new RelayCommand(() => remove(this));
    }
    public SalePriceKindOption[] KindChoices => Kinds;
    public static SalePriceKindOption[] Kinds { get; } =
    [
        new("gross", "Bruttó ár (Ft)"),
        new("net", "Nettó ár (Ft)"),
        new("percent", "Kedvezmény %")
    ];
    public string Name { get => _name; set { if (_name == value) return; _name = value; Changed(); Touch(); } }
    public string Kind { get => _kind; set { if (_kind == value) return; _kind = value; Changed(); Touch(); } }
    public string ValueText { get => _value; set { if (_value == value) return; _value = value; Changed(); Touch(); } }
    public string Hint { get => _hint; set { if (_hint == value) return; _hint = value; Changed(); } }
    public ICommand RemoveCommand { get; }
    public void Fill(string name, string kind, string value)
    {
        _quiet = true;
        Name = name;
        Kind = kind;
        ValueText = value;
        _quiet = false;
    }
    private void Touch() { if (!_quiet) _edited(); }
}

public sealed partial class EntityPageViewModel
{
    private static readonly CultureInfo PriceCulture = CultureInfo.GetCultureInfo("hu-HU");
    private bool _priceAsGross;
    private bool _convertingPrice;
    private string _grossSnapshot = "";
    private string _netTextSnapshot = "";
    public ObservableCollection<SalePriceLine> SalePrices { get; } = [];
    public ICommand AddSalePriceCommand { get; private set; } = new RelayCommand(() => { });
    public string PriceEntryLabel => _priceAsGross ? "Bruttó eladási egységár (Ft)" : "Nettó eladási egységár (Ft)";
    public bool PriceAsGross
    {
        get => _priceAsGross;
        set
        {
            if (_priceAsGross == value) return;
            _priceAsGross = value;
            if (!_populating) ApplyPriceMode(value);
            Changed();
            Changed(nameof(PriceEntryLabel));
            RefreshPricePresentation();
            if (!_populating) IsDirty = true;
        }
    }
    public string PriceCounterpart
    {
        get
        {
            var price = PriceField;
            if (price is null || string.IsNullOrWhiteSpace(price.Text))
                return _priceAsGross ? "A nettó a beírt bruttóból és az áfából készül." : "A bruttó a nettó árból és az áfából számolódik.";
            if (!TryAmount(price.Text, out var amount) || !TryVat(out var vat))
                return "Az árhoz szám kell, az áfa 0, 5, 18 vagy 27% lehet.";
            if (_priceAsGross)
            {
                var net = _grossSnapshot.Length > 0 && SameAmount(price.Text, _grossSnapshot) && TryAmount(_netTextSnapshot, out var original)
                    ? original : SalePriceMath.NetFromGross(amount, vat);
                return "Nettó: " + net.ToString("0.####", PriceCulture) + " Ft";
            }
            return "Bruttó: " + SalePriceMath.GrossFromNet(amount, vat).ToString("N2", PriceCulture) + " Ft";
        }
    }
    private EntityFieldViewModel? PriceField => Fields.FirstOrDefault(item => item.Definition.Id == "saleNetUnitPrice");
    private void InitializeProductPrices()
    {
        if (!IsProductMaster) return;
        AddSalePriceCommand = new RelayCommand(AddSalePrice);
        var price = PriceField;
        if (price is not null)
        {
            price.Span = 2;
            var edited = price.Edited;
            price.Edited = () =>
            {
                edited?.Invoke();
                if (_populating || _convertingPrice) return;
                if (_priceAsGross) _grossSnapshot = "";
                RefreshPricePresentation();
            };
        }
        var vat = Fields.FirstOrDefault(field => field.Definition.Id == "saleVatRate");
        if (vat is not null)
        {
            var edited = vat.Edited;
            vat.Edited = () =>
            {
                edited?.Invoke();
                if (_populating || _convertingPrice) return;
                if (_priceAsGross) _grossSnapshot = "";
                RefreshPricePresentation();
            };
        }
    }
    private void LoadProductPrices(EntityRow? row)
    {
        if (!IsProductMaster) return;
        _priceAsGross = false;
        _grossSnapshot = "";
        _netTextSnapshot = "";
        Changed(nameof(PriceAsGross));
        Changed(nameof(PriceEntryLabel));
        SalePrices.Clear();
        if (row is not null && row.Json.TryGetProperty("salePrices", out var prices) && prices.ValueKind == JsonValueKind.Array)
        {
            foreach (var price in prices.EnumerateArray())
            {
                if (price.ValueKind != JsonValueKind.Object) continue;
                var line = new SalePriceLine(OnSalePriceEdited, RemoveSalePrice);
                var kind = price.TryGetProperty("kind", out var kindValue) ? kindValue.GetString() ?? "gross" : "gross";
                if (kind is not ("net" or "gross" or "percent")) kind = "gross";
                var value = price.TryGetProperty("value", out var amount) && amount.ValueKind == JsonValueKind.Number
                    ? amount.GetDecimal().ToString("0.####", PriceCulture) : "";
                var name = price.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? "" : "";
                line.Fill(name, kind, value);
                SalePrices.Add(line);
            }
        }
        RefreshPricePresentation();
    }
    private void AddSalePrice()
    {
        if (SalePrices.Count >= 12) { Message = "Legfeljebb 12 további eladási ár adható meg."; return; }
        SalePrices.Add(new SalePriceLine(OnSalePriceEdited, RemoveSalePrice));
        if (!_populating) IsDirty = true;
    }
    private void RemoveSalePrice(SalePriceLine line)
    {
        if (!SalePrices.Remove(line) || _populating) return;
        IsDirty = true;
        RefreshPricePresentation();
    }
    private void OnSalePriceEdited()
    {
        if (_populating) return;
        IsDirty = true;
        RefreshSalePriceHints();
    }
    private void ApplyPriceMode(bool gross)
    {
        var field = PriceField;
        if (field is null) return;
        if (gross)
        {
            _netTextSnapshot = field.Text;
            if (TryAmount(field.Text, out var net) && TryVat(out var vat))
            {
                _grossSnapshot = SalePriceMath.GrossFromNet(net, vat).ToString("0.##", PriceCulture);
                SetPriceText(_grossSnapshot);
            }
            else _grossSnapshot = "";
            return;
        }
        if (_grossSnapshot.Length > 0 && SameAmount(field.Text, _grossSnapshot)) SetPriceText(_netTextSnapshot);
        else if (TryAmount(field.Text, out var typed) && TryVat(out var rate)) SetPriceText(SalePriceMath.NetFromGross(typed, rate).ToString("0.####", PriceCulture));
        _grossSnapshot = "";
    }
    private void SetPriceText(string text)
    {
        _convertingPrice = true;
        try { if (PriceField is { } field) field.Text = text; }
        finally { _convertingPrice = false; }
    }
    private void WriteProductPrices(JsonObject data)
    {
        if (!IsProductMaster) return;
        if (_priceAsGross && data["saleNetUnitPrice"] is JsonValue grossNode && grossNode.TryGetValue<decimal>(out var gross))
        {
            if (!TryVat(out var vat)) throw new ArgumentException("Az eladási áfa 0, 5, 18 vagy 27% lehet.");
            var net = _grossSnapshot.Length > 0 && SameAmount(PriceField?.Text ?? "", _grossSnapshot) && TryAmount(_netTextSnapshot, out var original)
                ? original : SalePriceMath.NetFromGross(gross, vat);
            data["saleNetUnitPrice"] = JsonValue.Create(net);
        }
        var prices = new JsonArray();
        foreach (var line in SalePrices)
        {
            var name = line.Name.Trim();
            var valueText = line.ValueText.Trim();
            if (name.Length == 0 && valueText.Length == 0) continue;
            if (name.Length == 0 || valueText.Length == 0) throw new ArgumentException("A további eladási árhoz név és érték is kell.");
            if (!TryAmount(valueText, out var value)) throw new ArgumentException(name + ": számot adj meg.");
            prices.Add(new JsonObject { ["name"] = name, ["kind"] = line.Kind, ["value"] = JsonValue.Create(value) });
        }
        data["salePrices"] = prices;
    }
    private void RefreshPricePresentation()
    {
        Changed(nameof(PriceCounterpart));
        RefreshSalePriceHints();
    }
    private void RefreshSalePriceHints()
    {
        if (!TryVat(out var vat)) vat = 27;
        var basis = CurrentBaseNet();
        foreach (var line in SalePrices)
        {
            if (string.IsNullOrWhiteSpace(line.ValueText) || !TryAmount(line.ValueText, out var value)) { line.Hint = ""; continue; }
            var name = line.Name.Trim();
            line.Hint = SalePriceMath.Describe(new ProductSalePrice(name.Length == 0 ? "Ár" : name, line.Kind, value), basis, vat);
        }
    }
    private decimal? CurrentBaseNet()
    {
        var field = PriceField;
        if (field is null || string.IsNullOrWhiteSpace(field.Text) || !TryAmount(field.Text, out var amount) || !TryVat(out var vat)) return null;
        if (!_priceAsGross) return amount;
        if (_grossSnapshot.Length > 0 && SameAmount(field.Text, _grossSnapshot) && TryAmount(_netTextSnapshot, out var original)) return original;
        return SalePriceMath.NetFromGross(amount, vat);
    }
    private bool TryVat(out decimal vat)
    {
        vat = 27;
        var field = Fields.FirstOrDefault(item => item.Definition.Id == "saleVatRate");
        return field is not null && TryAmount(field.Text, out vat) && vat is 0 or 5 or 18 or 27;
    }
    private static bool SameAmount(string left, string right) => TryAmount(left, out var a) && TryAmount(right, out var b) && a == b;
    private static bool TryAmount(string text, out decimal amount) =>
        decimal.TryParse((text ?? "").Replace(" ", "").Replace(",", "."), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount);
}
