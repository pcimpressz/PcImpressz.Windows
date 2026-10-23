using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PcImpressz.Modules.Quotes;

// Offline adapter: consumes an already downloaded CompMarket XML snapshot.
// No network requests, credentials, catalog writes or dependency on other modules.
public sealed record CompMarketQuoteItem(string SupplierSku, string Name, decimal PurchaseNet,
    decimal SaleNet, decimal VatRate, string Availability);
public static class CompMarketQuoteImport
{
    public static IReadOnlyList<CompMarketQuoteItem> Parse(string xml, decimal marginPercent, decimal vatRate = 27m)
    {
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > 8_000_000)
            throw new ArgumentException("A CompMarket XML hiányzik vagy túl nagy.");
        if (marginPercent < 0 || marginPercent > 1000 || vatRate is not (0m or 5m or 18m or 27m))
            throw new ArgumentException("Érvénytelen árrés vagy áfakulcs.");
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = 8_000_000, MaxCharactersFromEntities = 0
        });
        var document = XDocument.Load(reader, LoadOptions.None);
        static string Field(XElement node, params string[] keys) => node.Elements()
            .Where(e => keys.Contains(e.Name.LocalName, StringComparer.OrdinalIgnoreCase))
            .Select(e => e.Value.Trim()).FirstOrDefault(s => s.Length > 0) ?? "";
        var items = new List<CompMarketQuoteItem>();
        foreach (var node in document.Descendants().Where(e =>
            e.Elements().Any(c => c.Name.LocalName.Equals("cikkszam", StringComparison.OrdinalIgnoreCase)) &&
            e.Elements().Any(c => c.Name.LocalName.Equals("ar", StringComparison.OrdinalIgnoreCase))))
        {
            var sku = Field(node, "cikkszam");
            var name = Field(node, "nev");
            var raw = Field(node, "ar").Replace(" ", "").Replace('\u00a0', ' ').Trim().Replace(',', '.');
            if (sku.Length == 0 || name.Length == 0 || !decimal.TryParse(raw, NumberStyles.Number,
                    CultureInfo.InvariantCulture, out var purchase) || purchase < 0) continue;
            var sale = decimal.Round(purchase * (1m + marginPercent / 100m), 2, MidpointRounding.AwayFromZero);
            items.Add(new(sku, name, purchase, sale, vatRate, Field(node, "keszletinfo", "keszlet")));
            if (items.Count > 20000) throw new ArgumentException("Túl sok CompMarket termék egy importban.");
        }
        return items;
    }

    public static IReadOnlyList<CompMarketQuoteItem> Search(ReadOnlyMemory<byte> xml, string query, decimal marginPercent, decimal vatRate = 27m, int limit = 40)
    {
        if (xml.Length is 0 or > 8_000_000) throw new ArgumentException("A CompMarket XML hiányzik vagy túl nagy.");
        if (marginPercent < 0 || marginPercent > 1000 || vatRate is not (0m or 5m or 18m or 27m) || limit is < 1 or > 40)
            throw new ArgumentException("Érvénytelen árrés vagy áfakulcs.");
        var terms = Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) throw new ArgumentException("A nagykeres kereséshez legalább 2 karakter kell.");
        using var reader = XmlReader.Create(new MemoryStream(xml.ToArray()), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = 8_000_000, MaxCharactersFromEntities = 0
        });
        var document = XDocument.Load(reader, LoadOptions.None);
        static string Field(XElement node, params string[] keys) => node.Elements()
            .Where(e => keys.Contains(e.Name.LocalName, StringComparer.OrdinalIgnoreCase))
            .Select(e => e.Value.Trim()).FirstOrDefault(s => s.Length > 0) ?? "";
        var items = new List<CompMarketQuoteItem>();
        foreach (var node in document.Descendants().Where(e =>
            e.Elements().Any(c => c.Name.LocalName.Equals("cikkszam", StringComparison.OrdinalIgnoreCase)) &&
            e.Elements().Any(c => c.Name.LocalName.Equals("ar", StringComparison.OrdinalIgnoreCase))))
        {
            var sku = Field(node, "cikkszam");
            var name = Field(node, "nev");
            var extra = Field(node, "gy_cikkszam") + " " + Field(node, "vonalkod");
            if (!terms.All(term => Fold(sku + " " + name + " " + extra).Contains(term, StringComparison.Ordinal))) continue;
            var raw = Field(node, "ar").Replace(" ", "").Replace('\u00a0', ' ').Trim().Replace(',', '.');
            if (sku.Length == 0 || name.Length == 0 || !decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var purchase) || purchase < 0) continue;
            var sale = decimal.Round(purchase * (1m + marginPercent / 100m), 2, MidpointRounding.AwayFromZero);
            items.Add(new(sku, name, purchase, sale, vatRate, Field(node, "keszletinfo", "keszlet")));
            if (items.Count == limit) break;
        }
        return items;
    }

    private static string Fold(string value) => string.Concat(value.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToUpperInvariant();
}
