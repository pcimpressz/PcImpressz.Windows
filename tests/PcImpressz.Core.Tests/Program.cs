namespace PcImpressz.Core.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        var cases = ReceivingAccountingTests.Cases().Concat(ReceivingPreparationTests.Cases()).Concat(WorkspacePanelTests.Cases()).Concat(InvoicingDevelopmentTests.Cases()).Concat(InvoiceWorkspaceTests.Cases()).Concat(NavInvoiceTests.Cases()).Concat(CheckoutTests.Cases()).Concat(DemoDataTests.Cases()).Concat(AdministrationTests.Cases()).Concat(ProductDevelopmentTests.Cases()).Concat(AuditIntegrationTests.Cases()).Concat(LicenseTests.Cases()).Concat(ModuleTests.Cases()).Concat(AppearanceTests.Cases()).Concat(MenuTests.Cases()).Concat(PartnerTests.Cases()).Concat(CatalogTests.Cases()).Concat(ReceivingTests.Cases()).Concat(SettingsFeatureTests.Cases()).Concat(StabilizationTests.Cases()).Concat(HostIntegrationTests.Cases()).ToArray();
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
