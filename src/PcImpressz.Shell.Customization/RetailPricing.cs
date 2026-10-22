namespace PcImpressz.Shell.Customization;

public static class RetailPricing
{
    public static decimal Suggest(decimal purchaseNet,decimal markupPercent,decimal vatPercent)
    {
        if(purchaseNet is <0 or >1_000_000_000||markupPercent is <0 or >10000||vatPercent is not (0 or 5 or 18 or 27))throw new ArgumentException("Érvénytelen árképzési adat.");
        var gross=purchaseNet*(1+markupPercent/100m)*(1+vatPercent/100m);
        return decimal.Round(gross/5m,0,MidpointRounding.AwayFromZero)*5m;
    }
}
