using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using PcImpressz.Contracts;

namespace PcImpressz.Shell.Windows;

public sealed record SourcePriceRow(string Name, string Source, string Net);

public sealed partial class EntityPageViewModel
{
    private static readonly CultureInfo Hungary = CultureInfo.GetCultureInfo("hu-HU");
    private bool _sourceOwn = true, _sourceCompMarket = true, _sourceJtc, _sourceCedrus, _sourceOrink, _sourceCaseking;
    private string _sourceNote = "A saját készlet a cikktörzsből jön. A CompMarket a Beállításokban letöltött XML-ből. A többi nagyker még nincs bekötve.";
    public ObservableCollection<SourcePriceRow> SourcePrices { get; } = [];
    public bool SourceOwn { get => _sourceOwn; set { if (_sourceOwn == value) return; _sourceOwn = value; Changed(); } }
    public bool SourceCompMarket { get => _sourceCompMarket; set { if (_sourceCompMarket == value) return; _sourceCompMarket = value; Changed(); } }
    public bool SourceJtc { get => _sourceJtc; set { if (_sourceJtc == value) return; _sourceJtc = value; Changed(); } }
    public bool SourceCedrus { get => _sourceCedrus; set { if (_sourceCedrus == value) return; _sourceCedrus = value; Changed(); } }
    public bool SourceOrink { get => _sourceOrink; set { if (_sourceOrink == value) return; _sourceOrink = value; Changed(); } }
    public bool SourceCaseking { get => _sourceCaseking; set { if (_sourceCaseking == value) return; _sourceCaseking = value; Changed(); } }
    public string SourceNote { get => _sourceNote; private set { if (_sourceNote == value) return; _sourceNote = value; Changed(); } }
    public ICommand SearchSourcesCommand => new RelayCommand(() => _ = SearchSourcesAsync());

    private async Task SearchSourcesAsync()
    {
        if (!IsProductMaster || _company() is not Guid company) return;
        var query = Search.Trim();
        if (query.Length < 2) { SourceNote = "A forráskereséshez legalább két karakter kell."; SourcePrices.Clear(); return; }
        SourcePrices.Clear();
        var notes = new List<string>();
        var planned = new (bool On, string Name)[] { (SourceJtc, "JTC"), (SourceCedrus, "Cédrus"), (SourceOrink, "Orink"), (SourceCaseking, "Caseking") };
        foreach (var item in planned) if (item.On) notes.Add(item.Name + " még nincs bekötve.");
        if (SourceOwn)
        {
            try
            {
                var rows = await _connection.SendAsync("module.execute", new ModuleCommandRequest(company, "catalog", "products-list",
                    JsonSerializer.SerializeToElement(new { search = query, filter = "all", page = 0, pageSize = 20 })), _shutdown);
                foreach (var row in rows.GetProperty("rows").EnumerateArray())
                {
                    var name = row.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var net = 0m;
                    if (row.TryGetProperty("saleNetUnitPrice", out var price))
                    {
                        if (price.ValueKind == JsonValueKind.Number) net = price.GetDecimal();
                        else decimal.TryParse((price.GetString() ?? "").Replace(" ", "").Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out net);
                    }
                    SourcePrices.Add(new(name, "Saját", Money(net)));
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { notes.Add("Saját készlet: " + ex.Message); }
        }
        if (SourceCompMarket)
        {
            try
            {
                var rows = await _connection.SendAsync("module.execute", new ModuleCommandRequest(company, "quotes", "wholesale-search-cached",
                    JsonSerializer.SerializeToElement(new { query, context = "catalog", providers = new[] { "compmarket" }, marginPercent = 0m, vatRate = 27m })), _shutdown);
                foreach (var row in rows.EnumerateArray())
                    SourcePrices.Add(new(row.GetProperty("name").GetString() ?? "", "CompMarket", Money(row.GetProperty("purchaseNet").GetDecimal())));
                if (rows.GetArrayLength() == 0) notes.Add("A CompMarket nem adott találatot. A terméktörzs kapcsolója ki lehet kapcsolva, vagy nincs ilyen tétel az XML-ben.");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { notes.Add(ex.Message); }
        }
        SourceNote = SourcePrices.Count + " sor. " + (notes.Count == 0 ? "Sajátnál az eladási nettó, CompMarketnél a beszerzési nettó látszik." : string.Join(" ", notes));
    }

    private static string Money(decimal value) => value.ToString(value == decimal.Truncate(value) ? "N0" : "N2", Hungary) + " Ft";
}
