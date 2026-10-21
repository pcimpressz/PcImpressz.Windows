using System.Text.Json;

namespace PcImpressz.Contracts;

// Host-owned storage. A callback is synchronous and commits as one company transaction.
public interface ICompanyDatabase
{
    T Execute<T>(Guid companyId, Func<ICompanyTransaction, T> action);
}
public interface ICompanyTransaction
{
    string? Get(string module, string key);
    void Put(string module, string key, string json);
    void Delete(string module, string key);
    IReadOnlyList<string> List(string module, string prefix);
}
public static class CompanyDocuments
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static T? Read<T>(this ICompanyTransaction tx, string module, string key) =>
        tx.Get(module, key) is { } body ? JsonSerializer.Deserialize<T>(body, Json) : default;
    public static void Write<T>(this ICompanyTransaction tx, string module, string key, T value) =>
        tx.Put(module, key, JsonSerializer.Serialize(value, Json));
}

// Legacy file access exists only for standalone repository compatibility/tests.
// The production Host always supplies a transaction; it never dual-writes JSON.
public sealed record ModuleStorageScope(string Directory, ICompanyTransaction? Transaction)
{
    public string? Read(string module, string fileName)
    {
        if (Transaction is not null) return Transaction.Get(module, "state");
        var file = Path.Combine(Directory, fileName);
        if (!File.Exists(file)) return null;
        if (new FileInfo(file).Length > 64 * 1024 * 1024) throw new IOException("Az adattár túl nagy.");
        return File.ReadAllText(file);
    }
    public bool Write(string module, string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 64 * 1024 * 1024) throw new IOException("Az adattár túl nagy.");
        if (Transaction is null) return false;
        Transaction.Put(module, "state", json);
        return true;
    }
}

public sealed record PartnerReference(Guid Id, string Name, string TaxId, bool IsSupplier, bool IsCustomer, bool IsActive);
public sealed record IncomingLineReference(string Name, decimal Quantity, decimal NetUnitPrice);
public sealed record IncomingReceiptReference(Guid SupplierId,string Number,string Date,decimal Net,decimal Gross,string Status,IncomingLineReference[] Lines);
public sealed record OutgoingSaleReference(Guid? CustomerId,string Number,string Date,decimal Net,decimal Gross,string Status,IncomingLineReference[] Lines);
public sealed record ProductReference(Guid Id, string Name, string Sku, string Barcode, string UnitCode, bool IsActive, bool IsSalable = false, bool IsPurchaseable = false);

// Version 1 compatibility projection. Consumers depend on these typed contracts.
// Only this adapter knows the original snapshot shape; normalized tables may replace it later.
public static class CompanyReferenceQueries
{
    public static PartnerReference[] Partners(ICompanyTransaction tx) => Rows(tx, "partners", "partners", (id, d) =>
        new PartnerReference(id, Text(d,"name"), Text(d,"taxId"), Flag(d,"isSupplier"), Flag(d,"isCustomer"), Flag(d,"isActive")));
    public static ProductReference[] Products(ICompanyTransaction tx) => Rows(tx, "catalog", "products", (id,d) =>
        new ProductReference(id, Text(d,"name"), Text(d,"sku"), Text(d,"barcode"), Text(d,"unitCode"), Flag(d,"isActive"), Flag(d,"isSalable"), Flag(d,"isPurchaseable")));
    public static IReadOnlyDictionary<Guid,decimal> StockQuantities(ICompanyTransaction tx) => StockRows(tx).GroupBy(row=>row.GetProperty("productId").GetGuid()).ToDictionary(g=>g.Key,g=>g.Sum(row=>row.GetProperty("quantity").GetDecimal()));
    public static decimal StockQuantity(ICompanyTransaction tx, Guid productId) => StockQuantities(tx).GetValueOrDefault(productId);
    public static bool HasStockHistory(ICompanyTransaction tx, Guid productId) => StockRows(tx).Any(row=>row.GetProperty("productId").GetGuid()==productId);
    private static JsonElement[] StockRows(ICompanyTransaction tx) => tx.List("inventory","movement/").Select(body=>JsonSerializer.Deserialize<JsonElement>(body)).ToArray();
    public static IncomingReceiptReference[] Receipts(ICompanyTransaction tx)
    {
        if(tx.Get("receiving","state") is not { } body)return [];
        using var doc=JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("receipts").EnumerateArray().Select(row=>
        {
            var d=row.GetProperty("data");
            return new IncomingReceiptReference(d.GetProperty("supplierId").GetGuid(),Text(d,"number"),Text(d,"issuedOn"),d.GetProperty("net").GetDecimal(),d.GetProperty("gross").GetDecimal(),Text(d,"status"),
                d.GetProperty("lines").EnumerateArray().Select(l=>new IncomingLineReference(Text(l,"name"),l.GetProperty("quantity").GetDecimal(),l.GetProperty("netUnitPrice").GetDecimal())).ToArray());
        }).ToArray();
    }
    public static OutgoingSaleReference[] Sales(ICompanyTransaction tx) => tx.List("sales","sale/").Select(body=>
    {
        using var doc=JsonDocument.Parse(body);var d=doc.RootElement;
        var customer=d.GetProperty("customerId");
        return new OutgoingSaleReference(customer.ValueKind==JsonValueKind.Null?null:customer.GetGuid(),Text(d,"number"),d.GetProperty("at").GetDateTimeOffset().ToString("yyyy-MM-dd"),
            d.GetProperty("net").GetDecimal(),d.GetProperty("gross").GetDecimal(),Text(d,"status"),
            d.GetProperty("lines").EnumerateArray().Select(l=>new IncomingLineReference(Text(l,"name"),l.GetProperty("quantity").GetDecimal(),l.GetProperty("netUnitPrice").GetDecimal())).ToArray());
    }).ToArray();
    private static T[] Rows<T>(ICompanyTransaction tx, string module, string name, Func<Guid,JsonElement,T> map)
    {
        if (tx.Get(module,"state") is not { } body) return [];
        using var doc=JsonDocument.Parse(body);
        return doc.RootElement.GetProperty(name).EnumerateArray().Select(row => map(row.GetProperty("id").GetGuid(),row.GetProperty("data"))).ToArray();
    }
    private static string Text(JsonElement d,string key) => d.TryGetProperty(key,out var v) ? v.GetString() ?? "" : "";
    private static bool Flag(JsonElement d,string key) => d.TryGetProperty(key,out var v) && v.ValueKind==JsonValueKind.True;
}
