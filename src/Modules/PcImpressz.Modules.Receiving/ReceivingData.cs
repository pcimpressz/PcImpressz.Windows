using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PcImpressz.Modules.Receiving;

public sealed record ReceiptLine
{
    public Guid Id { get; init; }
    public Guid? ProductId { get; init; }
    public string Name { get; init; } = "";
    public string Sku { get; init; } = "";
    public string Barcode { get; init; } = "";
    public string UnitCode { get; init; } = "";
    public decimal Quantity { get; init; }
    public decimal NetUnitPrice { get; init; }
    public decimal VatRate { get; init; }
    public string Match { get; init; } = "";
    public string MatchBy { get; init; } = "";
}

public sealed record ReceiptData
{
    public string Number { get; init; } = "";
    public Guid SupplierId { get; init; }
    public string SupplierName { get; init; } = "";
    public string SupplierTaxId { get; init; } = "";
    public string IssuedOn { get; init; } = "";
    public string DueOn { get; init; } = "";
    public string Source { get; init; } = "manual";
    public string Status { get; init; } = "uj";
    public string Notes { get; init; } = "";
    public string Warehouse { get; init; } = "Bolt";
    public decimal Net { get; init; }
    public decimal Vat { get; init; }
    public decimal Gross { get; init; }
    public DateTimeOffset? ReceivedAt { get; init; }
    public ReceiptLine[] Lines { get; init; } = [];
}

public sealed record ReceiptRecord(Guid Id, long Version, ReceiptData Data, DateTimeOffset CreatedAt,
    string CreatedBy, DateTimeOffset UpdatedAt, string UpdatedBy);
public sealed record ReceiptAudit(Guid ReceiptId, string Action, long Version, DateTimeOffset At, string Actor);
public sealed record ReceiptState(int SchemaVersion, long Revision, ReceiptRecord[] Receipts, ReceiptAudit[] Audit);
public sealed record ReceiptLineDraft(Guid? Id, Guid? ProductId, string? Name, string? Sku, string? Barcode,
    string? UnitCode, decimal Quantity, decimal NetUnitPrice, decimal VatRate);
public sealed record ReceiptDraft(string? Number, Guid SupplierId, string? IssuedOn, string? DueOn, string? Notes,
    string? Warehouse, ReceiptLineDraft[]? Lines);
public sealed record ReceiptSave(Guid CompanyId, Guid? Id, long Version, ReceiptDraft? Draft);
public sealed record ReceiptQuery(Guid CompanyId, string Search = "", string Filter = "uj", int Page = 0, int PageSize = 50);
public sealed record ReceiptGet(Guid CompanyId, Guid Id);
public sealed record ReceiptClassify(Guid CompanyId, Guid Id, long Version, string? Status);
public sealed record ReceiptImport(Guid CompanyId, Guid? SupplierId, string? Number, string? IssuedOn, string? DueOn,
    string? Source, string? FileName, string? ContentBase64);
public sealed record ImportedLine(string Name, string Sku, string Barcode, decimal Quantity, decimal NetUnitPrice);
public sealed record ImportedDocument(string? Number, string? IssuedOn, string? DueOn, string? SupplierName,
    string? SupplierTaxId, ImportedLine[] Lines);
public sealed class ReceivingConflictException(string message) : Exception(message);

public static class ReceivingJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static T Read<T>(JsonElement input) => input.Deserialize<T>(Options)
        ?? throw new ArgumentException("Hiányzó kérésadat.");
    public static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, Options);
}

public static class ReceivingText
{
    public static string StatusLabel(string status) => status switch
    {
        "uj" => "Új, besorolásra vár",
        "bevetelezve" => "Bevételezve",
        "koltseg" => "Költség",
        "fuggo" => "Függő",
        _ => status
    };
    public static string SourceLabel(string source) => source switch
    {
        "manual" => "Kézi",
        "nav" => "NAV",
        "xls" => "Excel/CSV",
        "pdf" => "PDF",
        _ => source
    };
    public static string MatchLabel(string match) => match switch
    {
        "megvan" => "Párosítva",
        "uj" => "Nincs a törzsben",
        "fuggo" => "Döntésre vár",
        "szolgaltatas" => "Szolgáltatás",
        _ => match
    };
    public static string Fold(string? value)
    {
        var form = (value ?? "").Normalize(NormalizationForm.FormD);
        var buffer = new StringBuilder(form.Length);
        foreach (var character in form)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            buffer.Append(char.ToLowerInvariant(character));
        }
        return buffer.ToString();
    }
    public static string Compact(string? value)
    {
        var folded = Fold(value);
        var buffer = new char[folded.Length];
        var count = 0;
        var space = false;
        foreach (var character in folded)
        {
            if (char.IsAsciiLetterOrDigit(character)) { buffer[count++] = character; space = false; }
            else if (!space && count > 0) { buffer[count++] = ' '; space = true; }
        }
        return new string(buffer, 0, space && count > 0 ? count - 1 : count);
    }
    public static string TaxKey(string? value) => string.Concat((value ?? "").Where(char.IsAsciiLetterOrDigit)).ToUpperInvariant();
    public static string Digits(string? value) => string.Concat((value ?? "").Where(char.IsAsciiDigit));
    public static string Date(string? value, string label, bool required)
    {
        var text = (value ?? "").Trim().Replace('.', '-').Replace('/', '-');
        if (text.Length == 0)
        {
            if (required) throw new ArgumentException(label + " kötelező (éééé-hh-nn).");
            return "";
        }
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ArgumentException(label + ": éééé-hh-nn formátum kell.");
        return text;
    }
    public static decimal Round(decimal value, int digits) => decimal.Round(value, digits, MidpointRounding.AwayFromZero);
}

internal sealed record SupplierCard(Guid Id, string Name, string TaxId, bool IsSupplier, bool IsActive);
internal sealed record ProductCard(Guid Id, string Name, string Sku, string Barcode, string UnitCode, bool IsActive);

internal sealed class MasterIndex
{
    public SupplierCard[] Suppliers { get; private set; } = [];
    public ProductCard[] Products { get; private set; } = [];
    public static MasterIndex Load(string receivingDirectory)
    {
        var modules = Directory.GetParent(receivingDirectory)?.FullName
            ?? throw new IOException("A céges modulmappa nem olvasható.");
        var index = new MasterIndex();
        index.Suppliers = Read(Path.Combine(modules, "partners", "partners.json"), "partnertörzs", static (JsonElement root) =>
        {
            if (!root.TryGetProperty("partners", out var rows) || rows.ValueKind != JsonValueKind.Array) return [];
            return rows.EnumerateArray().Select(row =>
            {
                var data = row.TryGetProperty("data", out var body) ? body : default;
                return new SupplierCard(ReadGuid(row), Text(data, "name"), Text(data, "taxId"),
                    Flag(data, "isSupplier"), Flag(data, "isActive"));
            }).Where(card => card.Id != Guid.Empty).ToArray();
        });
        index.Products = Read(Path.Combine(modules, "catalog", "catalog.json"), "terméktörzs", static (JsonElement root) =>
        {
            if (!root.TryGetProperty("products", out var rows) || rows.ValueKind != JsonValueKind.Array) return [];
            return rows.EnumerateArray().Select(row =>
            {
                var data = row.TryGetProperty("data", out var body) ? body : default;
                return new ProductCard(ReadGuid(row), Text(data, "name"), Text(data, "sku"), Text(data, "barcode"),
                    Text(data, "unitCode"), Flag(data, "isActive"));
            }).Where(card => card.Id != Guid.Empty).ToArray();
        });
        return index;
    }
    private static T[] Read<T>(string path, string label, Func<JsonElement, T[]> map)
    {
        if (!File.Exists(path)) return [];
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("A " + label + " hivatkozás, ezért nem olvasható.");
        if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new IOException("A " + label + " túl nagy.");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return map(document.RootElement);
        }
        catch (JsonException) { throw new IOException("A " + label + " sérült, a bevételezés nem tudja olvasni."); }
    }
    private static Guid ReadGuid(JsonElement row) =>
        row.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out var parsed) ? parsed : Guid.Empty;
    private static string Text(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";
    private static bool Flag(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}

public static class ReceivingMatching
{
    private static readonly string[] ServiceWords = ["dij", "szallitas", "futar", "logisztik", "csomagol", "kezeles", "szolgaltatas", "postakoltseg", "utanvet"];
    private static readonly decimal[] VatRates = [0, 5, 18, 27];

    internal static ReceiptLine[] Build(ReceiptLineDraft[] drafts, ProductCard[] products)
    {
        if (drafts.Length is < 1 or > 400) throw new ArgumentException("A számlán 1–400 tétel lehet.");
        var lines = drafts.Select(draft => BuildLine(draft, products)).ToArray();
        if (lines.Select(line => line.Id).Distinct().Count() != lines.Length)
            throw new ArgumentException("A tételek azonosítója ütközik.");
        return lines;
    }
    public static (decimal Net, decimal Vat, decimal Gross) Totals(IEnumerable<ReceiptLine> lines)
    {
        decimal net = 0, vat = 0;
        foreach (var line in lines)
        {
            var row = ReceivingText.Round(line.Quantity * line.NetUnitPrice, 2);
            net += row;
            vat += ReceivingText.Round(row * line.VatRate / 100m, 2);
        }
        return (net, vat, net + vat);
    }
    public static void EnsureReceivable(ReceiptLine[] lines)
    {
        var blocked = lines.Where(line => line.Match != "szolgaltatas" && line.ProductId is null).Select(line => line.Name).ToArray();
        if (blocked.Length > 0)
            throw new ArgumentException("Bevételezés előtt párosítsd a készletes tételeket a terméktörzshöz, vagy vegyél fel új terméket: " + string.Join(", ", blocked));
    }
    private static ReceiptLine BuildLine(ReceiptLineDraft draft, ProductCard[] products)
    {
        var name = Clean(draft.Name);
        var sku = CleanCode(draft.Sku);
        var barcode = ReceivingText.Digits(draft.Barcode);
        var unit = CleanCode(draft.UnitCode);
        var quantity = ReceivingText.Round(draft.Quantity, 4);
        var price = ReceivingText.Round(draft.NetUnitPrice, 4);
        var vat = ReceivingText.Round(draft.VatRate, 0);
        if (quantity is <= 0 or > 1_000_000) throw new ArgumentException(name + ": a mennyiség legyen nagyobb nullánál.");
        if (price is < 0 or > 1_000_000_000) throw new ArgumentException(name + ": érvénytelen nettó egységár.");
        if (!VatRates.Contains(vat)) throw new ArgumentException(name + ": az áfakulcs 0, 5, 18 vagy 27 százalék lehet.");
        if (draft.ProductId is Guid selected)
        {
            var product = products.SingleOrDefault(card => card.Id == selected && card.IsActive)
                ?? throw new ArgumentException("A kiválasztott termék nem található az aktív terméktörzsben.");
            return new ReceiptLine
            {
                Id = draft.Id is null || draft.Id == Guid.Empty ? Guid.NewGuid() : draft.Id.Value,
                ProductId = product.Id,
                Name = name.Length > 0 ? name : product.Name,
                Sku = sku.Length > 0 ? sku : product.Sku,
                Barcode = barcode.Length > 0 ? barcode : ReceivingText.Digits(product.Barcode),
                UnitCode = unit.Length > 0 ? unit : product.UnitCode,
                Quantity = quantity, NetUnitPrice = price, VatRate = vat, Match = "megvan", MatchBy = "KEZI"
            };
        }
        if (name.Length < 2) throw new ArgumentException("A tétel neve legalább 2 karakter legyen.");
        if (IsService(name, barcode))
            return Line(draft, null, name, sku, barcode, unit, quantity, price, vat, "szolgaltatas", "SZOLGALTATAS");
        var byBarcode = barcode.Length == 0 ? [] : products.Where(card => card.IsActive && ReceivingText.Digits(card.Barcode) == barcode).ToArray();
        if (byBarcode.Length == 1) return FromProduct(draft, byBarcode[0], name, quantity, price, vat, "EAN");
        var bySku = sku.Length == 0 ? [] : products.Where(card => card.IsActive && ReceivingText.Fold(card.Sku) == ReceivingText.Fold(sku)).ToArray();
        if (bySku.Length == 1) return FromProduct(draft, bySku[0], name, quantity, price, vat, "SKU");
        if (barcode.Length == 0 && sku.Length == 0)
            return Line(draft, null, name, sku, barcode, unit, quantity, price, vat, "fuggo", "NINCS_AZONOSITO");
        var byName = products.Where(card => card.IsActive && ReceivingText.Compact(card.Name) == ReceivingText.Compact(name)).ToArray();
        if (byName.Length == 1) return FromProduct(draft, byName[0], name, quantity, price, vat, "NEV");
        if (byBarcode.Length > 1 || bySku.Length > 1 || byName.Length > 1)
            return Line(draft, null, name, sku, barcode, unit, quantity, price, vat, "fuggo", "TOBB");
        return Line(draft, null, name, sku, barcode, unit, quantity, price, vat, "uj", "");
    }
    private static ReceiptLine FromProduct(ReceiptLineDraft draft, ProductCard product, string name, decimal quantity, decimal price, decimal vat, string how) =>
        Line(draft, product.Id, name.Length > 0 ? name : product.Name, product.Sku, ReceivingText.Digits(product.Barcode), product.UnitCode, quantity, price, vat, "megvan", how);
    private static ReceiptLine Line(ReceiptLineDraft draft, Guid? productId, string name, string sku, string barcode, string unit,
        decimal quantity, decimal price, decimal vat, string match, string how) => new()
    {
        Id = draft.Id is null || draft.Id == Guid.Empty ? Guid.NewGuid() : draft.Id.Value,
        ProductId = productId, Name = name, Sku = sku, Barcode = barcode, UnitCode = unit,
        Quantity = quantity, NetUnitPrice = price, VatRate = vat, Match = match, MatchBy = how
    };
    private static bool IsService(string name, string barcode) =>
        barcode.Length == 0 && ServiceWords.Any(word => ReceivingText.Compact(name).Contains(word, StringComparison.Ordinal));
    private static string Clean(string? value)
    {
        var text = (value ?? "").Replace('<', ' ').Replace('>', ' ').Trim();
        if (text.Length > 200 || text.Any(character => char.IsControl(character)))
            throw new ArgumentException("A tétel neve érvénytelen vagy túl hosszú.");
        return text;
    }
    private static string CleanCode(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length > 80 || text.Any(char.IsControl)) throw new ArgumentException("Érvénytelen cikkszám, vonalkód vagy mértékegység.");
        return text;
    }
}
