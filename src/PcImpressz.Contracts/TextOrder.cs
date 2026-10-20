namespace PcImpressz.Contracts;

public static class TextOrder
{
    private static readonly StringComparer Compare = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("hu-HU"), true);

    public static T[] By<T>(IEnumerable<T> source, bool descending, Func<T, string> key) =>
        (descending ? source.OrderByDescending(key, Compare) : source.OrderBy(key, Compare)).ToArray();
}
