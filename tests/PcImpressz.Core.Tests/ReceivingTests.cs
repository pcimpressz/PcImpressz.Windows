using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PcImpressz.Contracts;
using PcImpressz.Core.Commands;
using PcImpressz.Core.Companies;
using PcImpressz.Core.Modules;
using PcImpressz.Modules.Catalog;
using PcImpressz.Modules.Partners;
using PcImpressz.Modules.Receiving;

namespace PcImpressz.Core.Tests;

internal static class ReceivingTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return Case("new company has no receipts and creates no file", async f =>
        {
            Check.Equal(0, (await f.Receipts.Snapshot(f.First.Id)).Receipts.Length);
            Check.True(!File.Exists(Path.Combine(f.Companies.ModuleDataDirectory(f.First.Id, "receiving"), "receipts.json")));
        });
        yield return Case("manual invoice needs an active supplier and keeps company data apart", async f =>
        {
            var supplier = await f.Supplier("Nagyker Kft.", "12345678-2-42");
            var saved = await f.Manual(supplier.Id, "2026/1", Line("Szállítási díj", 1, 1500));
            Check.Equal("manual", saved.Data.Source);
            Check.Equal("uj", saved.Data.Status);
            Check.Equal("szolgaltatas", saved.Data.Lines.Single().Match);
            Check.Equal(1905m, saved.Data.Gross);
            Check.Equal(0, (await f.Receipts.Snapshot(f.Second.Id)).Receipts.Length);
            await Check.ThrowsAsync<ArgumentException>(async () => await f.Manual(Guid.NewGuid(), "2026/2", Line("Szállítási díj", 1, 100)));
            var customer = await f.Partners.Save(new(f.First.Id, null, 0, new() { Name = "Vevő", IsCustomer = true, IsActive = true }), "operator");
            await Check.ThrowsAsync<ArgumentException>(async () => await f.Manual(customer.Id, "2026/3", Line("Szállítási díj", 1, 100)));
        });
        yield return Case("selected product is paired and an unknown stock line cannot be received", async f =>
        {
            var supplier = await f.Supplier("Nagyker Kft.", "11111111-2-42");
            await f.Unit();
            var product = await f.Product("Egér", "EGR-1", "5991234567890");
            var paired = await f.Manual(supplier.Id, "2026/10", new ReceiptLineDraft(null, product.Id, "Egér a számlán", "", "", "", 2, 1000, 27));
            Check.Equal("megvan", paired.Data.Lines.Single().Match);
            Check.Equal(product.Id, paired.Data.Lines.Single().ProductId);
            Check.Equal(2540m, paired.Data.Gross);
            var open = await f.Manual(supplier.Id, "2026/11", Line("Ismeretlen kábel", 1, 800));
            Check.Equal("fuggo", open.Data.Lines.Single().Match);
            await Check.ThrowsAsync<ArgumentException>(async () => await f.Receipts.Classify(new(f.First.Id, open.Id, open.Version, "bevetelezve"), "operator"));
            var expense = await f.Receipts.Classify(new(f.First.Id, open.Id, open.Version, "koltseg"), "operator");
            Check.Equal("koltseg", expense.Data.Status);
            await Check.ThrowsAsync<ArgumentException>(async () => await f.Receipts.Save(new(f.First.Id, expense.Id, expense.Version, Draft(supplier.Id, "2026/11", Line("Ismeretlen kábel", 1, 800))), "operator"));
        });
        yield return Case("barcode match can be received and a duplicate invoice number is rejected", async f =>
        {
            var supplier = await f.Supplier("Nagyker Kft.", "22222222-2-42");
            await f.Unit();
            var product = await f.Product("Egér", "EGR-1", "5991234567890");
            var saved = await f.Manual(supplier.Id, "2026/12", new ReceiptLineDraft(null, null, "Egér", "", "5991234567890", "DB", 1, 1000, 27));
            Check.Equal("EAN", saved.Data.Lines.Single().MatchBy);
            Check.Equal(product.Id, saved.Data.Lines.Single().ProductId);
            var received = await f.Receipts.Classify(new(f.First.Id, saved.Id, saved.Version, "bevetelezve"), "operator");
            Check.True(received.Data.ReceivedAt is not null);
            await Check.ThrowsAsync<ArgumentException>(async () => await f.Manual(supplier.Id, "2026/12", Line("Szállítási díj", 1, 100)));
            var stale = saved.Version;
            await Check.ThrowsAsync<ReceivingConflictException>(async () =>
                await f.Receipts.Classify(new(f.First.Id, saved.Id, stale, "fuggo"), "operator"));
        });
        yield return Case("csv and nav xml become new invoices and the dossier reads them", async f =>
        {
            var supplier = await f.Supplier("Nagyker Kft.", "12345678-2-42");
            await f.Unit();
            await f.Product("Egér", "EGR-1", "5991234567890");
            var csv = "termék neve;mennyiség;nettó egységár\r\nSzállítási díj;1;1500\r\n";
            var csvBody = Convert.ToBase64String(Encoding.UTF8.GetBytes(csv));
            var imported = await f.Receipts.Import(new(f.First.Id, supplier.Id, "XLS-1", null, null, "xls", "szamla.csv", csvBody),
                ReceivingImport.Read("szamla.csv", csvBody), "operator");
            Check.Equal("xls", imported.Data.Source);
            Check.Equal("szolgaltatas", imported.Data.Lines.Single().Match);
            var xml = """
                <InvoiceData>
                  <invoiceNumber>NAV-1</invoiceNumber>
                  <invoiceIssueDate>2026-10-01</invoiceIssueDate>
                  <supplierTaxNumber>12345678-2-42</supplierTaxNumber>
                  <line><lineDescription>Egér</lineDescription><quantity>2</quantity><unitPrice>1000</unitPrice>
                    <productCode><productCodeCategory>EAN</productCodeCategory><productCodeValue>5991234567890</productCodeValue></productCode>
                  </line>
                </InvoiceData>
                """;
            var nav = await f.Receipts.Import(new(f.First.Id, null, null, null, null, "nav", "nav.xml", Convert.ToBase64String(Encoding.UTF8.GetBytes(xml))),
                ReceivingImport.Read("nav.xml", Convert.ToBase64String(Encoding.UTF8.GetBytes(xml))), "operator");
            Check.Equal("NAV-1", nav.Data.Number);
            Check.Equal(supplier.Id, nav.Data.SupplierId);
            Check.Equal("megvan", nav.Data.Lines.Single().Match);
            Check.Throws<ArgumentException>(() => ReceivingImport.Read("szamla.pdf", Convert.ToBase64String("pdf"u8.ToArray())));
            var dossier = PartnerDossier.FromReceipts(f.Companies.ModuleDataDirectory(f.First.Id, "partners"), supplier.Id);
            Check.Equal(2, dossier!.Invoices.Length);
            Check.Equal(0m, dossier.PurchaseGross);
            var received = await f.Receipts.Classify(new(f.First.Id, nav.Id, nav.Version, "bevetelezve"), "operator");
            dossier = PartnerDossier.FromReceipts(f.Companies.ModuleDataDirectory(f.First.Id, "partners"), supplier.Id);
            Check.Equal(received.Data.Gross, dossier!.PurchaseGross);
            Check.Equal("Egér", dossier.Frequent.Single().Name);
        });
        yield return Case("nav connection encrypts secrets for one company and one windows user", async f =>
        {
            Check.Equal(false, NavConnectionStore.Status(f.Companies.ModuleDataDirectory(f.Second.Id, "receiving"), f.Second.Id).Configured);
            var directory = f.Companies.ModuleDataDirectory(f.First.Id, "receiving");
            var saved = NavConnectionStore.Save(directory, f.First.Id, new(f.First.Id, "12345678-2-42", "technikai", "TitokJelszo99", "AlairoKulcs1234", "CsereKulcs12345", true));
            Check.Equal("12345678", saved.TaxNumber);
            Check.True(saved.TestMode);
            var file = File.ReadAllBytes(Path.Combine(directory, NavConnectionStore.FileName));
            var text = Encoding.UTF8.GetString(file);
            Check.True(!text.Contains("TitokJelszo99", StringComparison.Ordinal));
            Check.True(!text.Contains("AlairoKulcs1234", StringComparison.Ordinal));
            Check.True(!text.Contains("CsereKulcs12345", StringComparison.Ordinal));
            Check.True(!text.Contains("technikai", StringComparison.Ordinal));
            var opened = NavConnectionStore.Read(directory, f.First.Id);
            Check.Equal("TitokJelszo99", opened!.Password);
            var kept = NavConnectionStore.Save(directory, f.First.Id, new(f.First.Id, "12345678", "technikai", "", "", "", false));
            Check.True(!kept.TestMode);
            Check.Equal("TitokJelszo99", NavConnectionStore.Read(directory, f.First.Id)!.Password);
            var other = f.Companies.ModuleDataDirectory(f.Second.Id, "receiving");
            Directory.CreateDirectory(other);
            File.Copy(Path.Combine(directory, NavConnectionStore.FileName), Path.Combine(other, NavConnectionStore.FileName));
            await Check.ThrowsAsync<IOException>(async () => await Task.Run(() => NavConnectionStore.Read(other, f.Second.Id)));
        });
        yield return Case("export prefixes a formula and the signed module lists one page", async f =>
        {
            var supplier = await f.Supplier("=Nagyker", "33333333-2-42");
            var saved = await f.Manual(supplier.Id, "2026/20", Line("Szállítási díj", 1, 100));
            var csv = ReceivingRepository.ExportCsv([saved]);
            Check.True(csv.Contains("\"'=Nagyker\"", StringComparison.Ordinal));
            using var crypto = new CryptoFixture();
            var now = crypto.Clock.Now.ToUnixTimeSeconds();
            crypto.License = crypto.License with
            {
                Modules =
                [
                    new() { ModuleId = "partners", Capabilities = ["partners.write"], ExpiresAt = now + 86400 },
                    new() { ModuleId = "catalog", Capabilities = ["catalog.write"], ExpiresAt = now + 86400 },
                    new() { ModuleId = "receiving", Capabilities = ["receiving.write"], ExpiresAt = now + 86400 }
                ]
            };
            crypto.Refresh();
            var modules = Path.Combine(f.Directory.Path, "packages");
            Install(crypto, modules, "partners", "PcImpressz.Modules.Partners.dll", "PcImpressz.Modules.Partners.PartnersModule");
            Install(crypto, modules, "catalog", "PcImpressz.Modules.Catalog.dll", "PcImpressz.Modules.Catalog.CatalogModule");
            Install(crypto, modules, "receiving", "PcImpressz.Modules.Receiving.dll", "PcImpressz.Modules.Receiving.ReceivingModule",
            [
                new() { ModuleId = "partners", MinimumVersion = "0.3.0", MaximumVersionExclusive = "1.0.0" },
                new() { ModuleId = "catalog", MinimumVersion = "0.3.0", MaximumVersionExclusive = "1.0.0" }
            ]);
            var dispatcher = new CommandDispatcher(crypto.Evaluator(), NullLogger<CommandDispatcher>.Instance);
            await using var runtime = new ModuleRuntime(crypto.Verifier, dispatcher, crypto.Clock, NullLogger<ModuleRuntime>.Instance,
                (company, module) => f.Companies.ModuleDataDirectory(company, module));
            await runtime.StartAsync(modules);
            Check.Equal(ModuleStatus.Active, runtime.Reports.Single(report => report.Id == "receiving").Status);
            Check.Equal(1, runtime.Pages.Count(page => page.ModuleId == "receiving"));
            var listed = await dispatcher.ExecuteAsync(new("receiving", "list", JsonSerializer.SerializeToElement(new { companyId = f.First.Id })),
                new CallerIdentity("operator", new HashSet<string> { "receiving.read" }));
            Check.Equal(1, listed.GetProperty("total").GetInt32());
        });
    }

    private static ReceiptLineDraft Line(string name, decimal quantity, decimal price) => new(null, null, name, "", "", "DB", quantity, price, 27);
    private static ReceiptDraft Draft(Guid supplier, string number, params ReceiptLineDraft[] lines) =>
        new(number, supplier, "2026-10-20", "", "", "Bolt", lines);
    private static (string, Func<Task>) Case(string name, Func<Fixture, Task> test) => (name, async () => { using var f = new Fixture(); await test(f); });
    private static void Install(CryptoFixture crypto, string modules, string id, string fileName, string entryType, ModuleDependency[]? dependencies = null)
    {
        var package = Path.Combine(modules, id);
        Directory.CreateDirectory(package);
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, fileName));
        File.WriteAllBytes(Path.Combine(package, fileName), bytes);
        var manifest = new ModuleManifest
        {
            SchemaVersion = 1, Id = id, Version = "0.3.0", ApiVersion = PlatformContract.ApiVersion,
            EntryAssembly = fileName, EntryType = entryType,
            Files = new() { [fileName] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) },
            Dependencies = dependencies ?? []
        };
        File.WriteAllText(Path.Combine(package, "manifest.jws"), crypto.Sign(manifest, ModuleManifest.DocumentType));
    }
    private sealed class Fixture : IDisposable
    {
        public readonly TestDirectory Directory = new();
        public readonly TestTime Clock = new();
        public CompanyStore Companies { get; }
        public CompanyInfo First { get; }
        public CompanyInfo Second { get; }
        public PartnerRepository Partners { get; }
        public CatalogRepository Catalog { get; }
        public ReceivingRepository Receipts { get; }
        public Fixture()
        {
            Companies = new(Directory.Path);
            First = Companies.Create("Saját cég", false);
            Second = Companies.Create("Üres demó", true);
            Partners = new(id => Companies.ModuleDataDirectory(id, "partners"), Clock);
            Catalog = new(id => Companies.ModuleDataDirectory(id, "catalog"), Clock);
            Receipts = new(id => Companies.ModuleDataDirectory(id, "receiving"), Clock);
        }
        public Task<PartnerRecord> Supplier(string name, string taxId) =>
            Partners.Save(new(First.Id, null, 0, new() { Name = name, TaxId = taxId, IsSupplier = true, IsActive = true }), "operator");
        public Task<UnitRecord> Unit() => Catalog.SaveUnit(new(First.Id, null, 0, new() { Name = "Darab", Code = "DB", IsActive = true }), "operator");
        public Task<ProductRecord> Product(string name, string sku, string barcode) =>
            Catalog.SaveProduct(new(First.Id, null, 0, new() { Name = name, Sku = sku, Barcode = barcode, UnitCode = "DB", IsPurchaseable = true, IsActive = true }), "operator");
        public Task<ReceiptRecord> Manual(Guid supplier, string number, params ReceiptLineDraft[] lines) =>
            Receipts.Save(new(First.Id, null, 0, Draft(supplier, number, lines)), "operator");
        public void Dispose() => Directory.Dispose();
    }
}
