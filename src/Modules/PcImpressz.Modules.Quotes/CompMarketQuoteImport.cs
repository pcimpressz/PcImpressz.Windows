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
        if (xml.Length is 0 or > 64_000_000) throw new ArgumentException("A CompMarket XML hiányzik vagy túl nagy.");
        if (marginPercent < 0 || marginPercent > 1000 || vatRate is not (0m or 5m or 18m or 27m) || limit is < 1 or > 40)
            throw new ArgumentException("Érvénytelen árrés vagy áfakulcs.");
        var terms = Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) throw new ArgumentException("A nagykeres kereséshez legalább 2 karakter kell.");
        using var reader = XmlReader.Create(new MemoryStream(xml.ToArray()), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = 64_000_000, MaxCharactersFromEntities = 0
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

    // Streaming search: no XDocument and no full XML byte[] allocation.
    public static IReadOnlyList<CompMarketQuoteItem> SearchFile(string path, string query, decimal marginPercent, decimal vatRate = 27m, int limit = 40)
    {
        if (marginPercent is < 0 or > 1000 || vatRate is not (0m or 5m or 18m or 27m) || limit is < 1 or > 40)
            throw new ArgumentException("Érvénytelen árképzés.");
        var terms = Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) throw new ArgumentException("Legalább két karakter szükséges.");
        using var reader = XmlReader.Create(path, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = 512L * 1024 * 1024, MaxCharactersFromEntities = 0,
            IgnoreComments = true, IgnoreWhitespace = true
        });
        var results = new List<CompMarketQuoteItem>();
        var frames = new Stack<(int Depth, Dictionary<string,string> Fields)>();
        var positioned = false;
        while (positioned || reader.Read())
        {
            positioned = false;
            if (reader.NodeType == XmlNodeType.Element)
            {
                if (reader.IsEmptyElement) continue;
                var name = reader.LocalName.ToLowerInvariant();
                if (name is "cikkszam" or "nev" or "ar" or "gy_cikkszam" or "vonalkod" or "keszlet" or "keszletinfo")
                {
                    var depth = reader.Depth;
                    var value = reader.ReadElementContentAsString().Trim();
                    positioned = !reader.EOF;
                    if (frames.Count > 0 && frames.Peek().Depth == depth - 1 && value.Length <= 4096)
                        frames.Peek().Fields[name] = value;
                    continue;
                }
                frames.Push((reader.Depth, new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)));
            }
            else if (reader.NodeType == XmlNodeType.EndElement && frames.Count > 0 && frames.Peek().Depth == reader.Depth)
            {
                var frame = frames.Pop();
                var fields = frame.Fields;
                string Get(string key) => fields.GetValueOrDefault(key) ?? "";
                var sku = Get("cikkszam"); var name = Get("nev");
                if (sku.Length == 0 || name.Length == 0 || Get("ar").Length == 0) continue;
                var searchable = Fold(sku + " " + name + " " + Get("gy_cikkszam") + " " + Get("vonalkod"));
                if (!terms.All(term => searchable.Contains(term, StringComparison.Ordinal))) continue;
                var price = Get("ar").Replace(" ", "").Replace('\u00a0', ' ').Trim().Replace(',', '.');
                if (!decimal.TryParse(price, NumberStyles.Number, CultureInfo.InvariantCulture, out var purchase) || purchase < 0) continue;
                var sale = decimal.Round(purchase * (1m + marginPercent / 100m), 2, MidpointRounding.AwayFromZero);
                results.Add(new(sku, name, purchase, sale, vatRate, Get("keszletinfo").Length > 0 ? Get("keszletinfo") : Get("keszlet")));
                if (results.Count == limit) break;
            }
        }
        return results;
    }

    private static string Fold(string value) => string.Concat(value.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToUpperInvariant();
}
