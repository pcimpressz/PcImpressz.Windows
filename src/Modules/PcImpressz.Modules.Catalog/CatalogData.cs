using System.Text.Json;

namespace PcImpressz.Modules.Catalog;

public sealed record CategoryData
{
    public string Name { get; init; } = "";
    public string Code { get; init; } = "";
    public string ParentCode { get; init; } = "";
    public string Description { get; init; } = "";
    public bool IsActive { get; init; } = true;
}
public sealed record UnitData
{
    public string Name { get; init; } = "";
    public string Code { get; init; } = "";
    public string Symbol { get; init; } = "";
    public bool IsActive { get; init; } = true;
}
public sealed record ProductData
{
    public string Name { get; init; } = "";
    public string Sku { get; init; } = "";
    public string CategoryCode { get; init; } = "";
    public string UnitCode { get; init; } = "";
    public string Revision { get; init; } = "";
    public string Barcode { get; init; } = "";
    public string Description { get; init; } = "";
    public string Parameters { get; init; } = "";
    public bool IsPurchaseable { get; init; }
    public bool IsSalable { get; init; }
    public bool IsActive { get; init; } = true;
    public string Notes { get; init; } = "";
}
public sealed record CategoryRecord(Guid Id, long Version, CategoryData Data, DateTimeOffset CreatedAt,
    string CreatedBy, DateTimeOffset UpdatedAt, string UpdatedBy, string? Source = null, int? ExternalId = null);
public sealed record UnitRecord(Guid Id, long Version, UnitData Data, DateTimeOffset CreatedAt,
    string CreatedBy, DateTimeOffset UpdatedAt, string UpdatedBy, string? Source = null, int? ExternalId = null);
public sealed record ProductRecord(Guid Id, long Version, ProductData Data, DateTimeOffset CreatedAt,
    string CreatedBy, DateTimeOffset UpdatedAt, string UpdatedBy, string? Source = null, int? ExternalId = null);
public sealed record CatalogAudit(string Entity, Guid EntityId, string Action, long Version, DateTimeOffset At, string Actor);
public sealed record CatalogState(int SchemaVersion, long Revision, CategoryRecord[] Categories, UnitRecord[] Units,
    ProductRecord[] Products, CatalogAudit[] Audit);
public sealed record CategorySave(Guid CompanyId, Guid? Id, long Version, CategoryData Data);
public sealed record UnitSave(Guid CompanyId, Guid? Id, long Version, UnitData Data);
public sealed record ProductSave(Guid CompanyId, Guid? Id, long Version, ProductData Data);
public sealed record CatalogQuery(Guid CompanyId, string Search = "", string Filter = "all", int Page = 0, int PageSize = 50);
public sealed record CatalogArchive(Guid CompanyId, Guid Id, long Version);
public sealed record CatalogImportOptions(Guid CompanyId, string ServerUrl, string ApiToken, bool AllowLocalHttp = false);
public sealed record CatalogImportCommit(Guid CompanyId, Guid PreviewId);
public sealed record CatalogImportPreview(Guid PreviewId, int NewCount, int SkippedCount, int InvalidCount,
    string[] Warnings, ProductData[] Sample);
public sealed record CatalogAdditions(CategoryRecord[] Categories, UnitRecord[] Units, ProductRecord[] Products);
public sealed class CatalogConflictException(string message) : Exception(message);

public static class CatalogJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static T Read<T>(JsonElement input) => input.Deserialize<T>(Options)
        ?? throw new ArgumentException("Hiányzó kérésadat.");
    public static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, Options);
}

public static class CatalogValidation
{
    public static CategoryData NormalizeCategory(CategoryData data) => data with
    {
        Name = Name(data.Name, "A kategória neve"),
        Code = Code(data.Code, 40, "Kategóriakód"),
        ParentCode = OptionalCode(data.ParentCode, 40, "Szülőkategória kódja"),
        Description = Text(data.Description, 4000, "Leírás")
    };
    public static UnitData NormalizeUnit(UnitData data) => data with
    {
        Name = Name(data.Name, "A mértékegység neve", 1),
        Code = Code(data.Code, 20, "Mértékegységkód", "/"),
        Symbol = Text(data.Symbol, 20, "Jelölés")
    };
    public static ProductData NormalizeProduct(ProductData data)
    {
        var normalized = data with
        {
            Name = Name(data.Name, "A termék neve"),
            Sku = Code(data.Sku, 80, "Cikkszám", "/"),
            CategoryCode = OptionalCode(data.CategoryCode, 40, "Kategóriakód"),
            UnitCode = Code(data.UnitCode, 20, "Mértékegységkód", "/"),
            Revision = Text(data.Revision, 40, "Revízió"),
            Barcode = Text(data.Barcode, 80, "Vonalkód"),
            Description = Text(data.Description, 4000, "Leírás"),
            Parameters = Parameters(data.Parameters),
            Notes = Text(data.Notes, 4000, "Megjegyzés")
        };
        if (normalized.UnitCode.Length == 0) throw new ArgumentException("A mértékegység kötelező.");
        return normalized;
    }
    public static string Key(string value) => value.Trim().ToUpperInvariant();
    private static string Name(string? value, string label, int minimum = 2)
    {
        var text = Text(value, 200, label);
        if (text.Length < minimum) throw new ArgumentException(label + " legalább " + minimum + " karakter legyen.");
        return text;
    }
    private static string OptionalCode(string? value, int maximum, string label)
    {
        var text = (value ?? "").Trim();
        return text.Length == 0 ? "" : Code(text, maximum, label);
    }
    private static string Code(string? value, int maximum, string label, string extra = "")
    {
        var text = (value ?? "").Trim().ToUpperInvariant();
        if (text.Length is < 1 || text.Length > maximum
            || text.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '.' && !extra.Contains(c)))
            throw new ArgumentException(label + ": 1–" + maximum + " karakter, betű, szám, kötőjel vagy pont.");
        return text;
    }
    private static string Text(string? value, int maximum, string label)
    {
        value = (value ?? "").Trim();
        if (value.Length > maximum || value.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t'))
            throw new ArgumentException(label + ": érvénytelen vagy túl hosszú érték.");
        return value;
    }
    public static string Parameters(string? value)
    {
        var text = Text(value, 8000, "Paraméterek");
        if (text.Length == 0) return "";
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length > 30) throw new ArgumentException("Legfeljebb 30 alapparaméter adható meg.");
        var normalized = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            var split = line.IndexOfAny(['=', ':']);
            if (split <= 0 || split == line.Length - 1) throw new ArgumentException("A paraméter formátuma: név=érték.");
            var name = line[..split].Trim();
            var parameterValue = line[(split + 1)..].Trim();
            if (name.Length is < 1 or > 80 || parameterValue.Length is < 1 or > 200
                || name.Any(char.IsControl) || parameterValue.Any(char.IsControl))
                throw new ArgumentException("Érvénytelen paraméternév vagy érték.");
            normalized.Add(name + "=" + parameterValue);
        }
        return string.Join('\n', normalized);
    }
}
