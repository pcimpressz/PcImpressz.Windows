namespace PcImpressz.Contracts;

public static class QuotePricing
{
    public static decimal GrossFromNet(decimal net, decimal vatRate) => VatMath.Line(1, net, vatRate).Gross;

    public static decimal NetFromGross(decimal gross, decimal vatRate)
    {
        if (gross < 0 || !VatMath.IsSupportedRate(vatRate))
            throw new ArgumentException("Érvénytelen bruttó ár vagy áfakulcs.");
        if (vatRate == 0) return VatMath.Round(gross);
        var guess = VatMath.Round(gross / (1 + vatRate / 100m));
        var best = guess;
        var bestDiff = decimal.MaxValue;
        for (var step = -30; step <= 30; step++)
        {
            var candidate = guess + step * 0.01m;
            if (candidate < 0) continue;
            var diff = Math.Abs(GrossFromNet(candidate, vatRate) - gross);
            if (diff < bestDiff || diff == bestDiff && candidate < best)
            {
                best = candidate;
                bestDiff = diff;
                if (diff == 0) break;
            }
        }
        return best;
    }

    public static decimal SaleNet(decimal purchase, decimal marginPercent)
    {
        if (purchase < 0 || marginPercent is < -100 or > 1000)
            throw new ArgumentException("Érvénytelen beszerzési ár vagy haszonkulcs.");
        return VatMath.Round(purchase * (1 + marginPercent / 100m));
    }

    public static decimal? Margin(decimal purchase, decimal saleNet)
    {
        if (purchase <= 0 || saleNet < 0) return null;
        return VatMath.Round((saleNet / purchase - 1m) * 100m);
    }
}
