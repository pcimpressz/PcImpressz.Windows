using System.Text.Json;
using PcImpressz.Contracts;

namespace PcImpressz.Modules.Quotes;

// A supplier snapshot is never written into the company's own catalog.
public sealed record WholesaleCatalogRow(string ProviderId, string ProviderName, string Sku,
    string Name, decimal PurchaseNet, decimal SaleNet, decimal VatRate, string Availability,
    DateTimeOffset SnapshotAt);

public static class WholesaleCatalogEngine
{
    public static IReadOnlyList<WholesaleCatalogRow> SearchCached(string directory, Guid company,
        string query, IReadOnlyCollection<string> providers, decimal markup = 20m, decimal vat = 27m)
    {
        if (query is null || query.Trim().Length < 2 || query.Length > 200)
            throw new ArgumentException("Legalább két karakteres keresőkifejezés szükséges.");
        if (markup is < 0 or > 1000 || vat is not (0m or 5m or 18m or 27m))
            throw new ArgumentException("Érvénytelen árképzés.");
        if (providers.Count > 20 || providers.Any(x => !WholesaleConnections.Providers.Any(p => p.Id == x)))
            throw new ArgumentException("Ismeretlen nagykereskedő.");
        var enabled = WholesaleConnections.EnabledProviders(directory, company);
        var rows = new List<WholesaleCatalogRow>();
        foreach (var id in providers.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!enabled.Contains(id)) continue;
            if (id != "compmarket") continue; // Other adapters must be explicitly implemented and verified.
            var path = Path.Combine(directory, "compmarket-keszlet.xml");
            if (!File.Exists(path))
                throw new ArgumentException("Még nincs letöltött CompMarket XML. A Beállítások, Külső szolgáltatók lapján indítsd a letöltést.");
            var file = new FileInfo(path);
            if (file.Length is <= 0 or > 512L * 1024 * 1024 || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A CompMarket XML pillanatkép érvénytelen.");
            var matches = CompMarketQuoteImport.SearchFile(path, query, markup, vat);
            rows.AddRange(matches.Select(x => new WholesaleCatalogRow(id, "CompMarket", x.SupplierSku,
                x.Name, x.PurchaseNet, x.SaleNet, x.VatRate, x.Availability,
                new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero))));
        }
        return rows.Take(100).ToArray();
    }
}
