namespace PcImpressz.Core.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        var cases = LicenseTests.Cases().Concat(ModuleTests.Cases()).Concat(AppearanceTests.Cases()).Concat(PartnerTests.Cases()).Concat(CatalogTests.Cases()).Concat(HostIntegrationTests.Cases()).ToArray();
        var failures = 0;
        foreach (var (name, test) in cases)
        {
            try { await test(); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex}"); }
        }
        Console.WriteLine($"{cases.Length - failures}/{cases.Length} passed.");
        return failures == 0 ? 0 : 1;
    }
}
