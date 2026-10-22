using System.Globalization;

namespace PcImpressz.Contracts;

public sealed record ProductSalePrice(string Name, string Kind, decimal Value);

public static class SalePriceMath
{
    public static decimal GrossFromNet(decimal net, decimal vat) =>
        decimal.Round(net * (1 + vat / 100m), 2, MidpointRounding.AwayFromZero);

    public static decimal NetFromGross(decimal gross, decimal vat) =>
        decimal.Round(gross / (1 + vat / 100m), 4, MidpointRounding.AwayFromZero);

    public static decimal? ResolveNet(ProductSalePrice price, decimal? baseNet, decimal vat) => price.Kind switch
    {
        "percent" when baseNet is decimal net => decimal.Round(net * (1 - price.Value / 100m), 4, MidpointRounding.AwayFromZero),
        "net" => price.Value,
        "gross" => NetFromGross(price.Value, vat),
        _ => null
    };

    public static string Describe(ProductSalePrice price, decimal? baseNet, decimal vat)
    {
        var culture = CultureInfo.GetCultureInfo("hu-HU");
        string Ft(decimal amount) => amount.ToString(amount == decimal.Round(amount, 0) ? "N0" : "N2", culture) + " Ft";
        if (price.Kind == "percent")
        {
            var text = price.Name + ": " + price.Value.ToString("0.##", culture) + "% kedvezmény";
            if (ResolveNet(price, baseNet, vat) is decimal net)
                text += " · " + Ft(GrossFromNet(net, vat)) + " bruttó";
            return text;
        }
        if (price.Kind == "gross") return price.Name + ": " + Ft(price.Value) + " bruttó";
        if (price.Kind == "net" && ResolveNet(price, baseNet, vat) is decimal resolved)
            return price.Name + ": " + Ft(GrossFromNet(resolved, vat)) + " bruttó";
        return price.Name;
    }
}
