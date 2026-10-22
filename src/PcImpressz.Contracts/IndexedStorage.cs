using System.Globalization;
using System.Text;

namespace PcImpressz.Contracts;

// Additive capability: old/test storage implementations keep their existing behavior.
public interface IIndexedCompanyTransaction
{
    string? CatalogMetadata();
    string? Product(Guid id);
    bool ActiveSkuExists(string sku, Guid? except);
    void SaveProduct(string record, string audit);
    ProductIndexPage Products(ProductIndexQuery query);
    IndexedStockBalance[] StockBalances(Guid? productId = null);
    bool HasStockHistory(Guid productId);
    decimal ReservedQuantity(Guid productId,string warehouse,DateTimeOffset at);
    OrderIndexPage Orders(OrderQuery query,DateTimeOffset at);
}
public sealed record SearchTerm(string Field, string Value, bool Exact);
public sealed record ProductIndexQuery(SearchTerm[] Terms, string Filter, string Sort, bool Descending,
    int Offset, int Limit, decimal? MinPrice = null, decimal? MaxPrice = null,
    decimal? MinStock = null, decimal? MaxStock = null, string[]? Categories = null, bool NoCategoryMatch = false);
public sealed record ProductIndexPage(string[] Records, int Total, IReadOnlyDictionary<Guid, decimal> Quantities);
public sealed record IndexedStockBalance(Guid ProductId, string ProductName, string UnitCode, string Warehouse, decimal Quantity);

public static class SearchText
{
    public static string Fold(string? text) => string.Concat((text ?? "").Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
        .Normalize(NormalizationForm.FormC).ToUpperInvariant();
}

public static class VatMath
{
    public static bool IsSupportedRate(decimal rate) => rate is 0 or 5 or 18 or 27;
    public static decimal Round(decimal value, int digits = 2) => decimal.Round(value, digits, MidpointRounding.AwayFromZero);
    public static (decimal Net, decimal Vat, decimal Gross) Line(decimal quantity, decimal netPrice, decimal rate,
        decimal? sourceNet = null, decimal? sourceVat = null, decimal? sourceGross = null)
    {
        if (!IsSupportedRate(rate)) throw new ArgumentException("Nem támogatott áfakulcs.");
        var net = sourceNet ?? Round(checked(quantity * netPrice));
        var vat = sourceVat ?? Round(checked(net * rate / 100m));
        return (net, vat, sourceGross ?? checked(net + vat));
    }
}
