using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
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
            var csv = "termék neve;mennyiség;nettó egységár;áfa;szolgáltatás\r\nSzállítási díj;1;1500;27;igen\r\n";
            var csvBody = Convert.ToBase64String(Encoding.UTF8.GetBytes(csv));
            var imported = await f.Receipts.Import(new(f.First.Id, supplier.Id, "XLS-1", null, null, "xls", "szamla.csv", csvBody),
                ReceivingImport.Read("szamla.csv", csvBody), "operator");
            Check.Equal("xls", imported.Data.Source);
            Check.Equal("szolgaltatas", imported.Data.Lines.Single().Match);
            var xml = """
                <InvoiceData>
                  <invoiceNumber>NAV-1</invoiceNumber><currencyCode>HUF</currencyCode>
                  <invoiceIssueDate>2026-10-01</invoiceIssueDate>
                  <supplierTaxNumber>12345678-2-42</supplierTaxNumber>
                  <line><lineDescription>Egér</lineDescription><quantity>2</quantity><unitPrice>1000</unitPrice><lineVatRate><vatPercentage>0.27</vatPercentage></lineVatRate>
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
        yield return ("receipt list narrows by supplier fragment and sorts by net", () =>
        {
            var now = DateTimeOffset.UnixEpoch;
            ReceiptRecord Row(string number, string supplier, string issued, decimal net) =>
                new(Guid.NewGuid(), 1, new() { Number = number, SupplierName = supplier, IssuedOn = issued, Status = "uj", Net = net, Source = "manual" }, now, "a", now, "a");
            var rows = new[] { Row("B-2", "Nagyker", "2026-02-01", 10), Row("A-1", "Kisker", "2026-03-01", 50), Row("C-3", "Másik", "2026-01-01", 5) };
            Check.Equal("A-1", ReceiptListing.Match(rows, new(Guid.NewGuid(), "kis")).Single().Data.Number);
            Check.Equal("A-1", ReceiptListing.Match(rows, new(Guid.NewGuid(), Filter: "uj"))[0].Data.Number);
            var byNet = ReceiptListing.Match(rows, new(Guid.NewGuid(), Filter: "mind", Sort: "net", Descending: true));
            Check.Equal(50m, byNet[0].Data.Net);
            Check.Equal(5m, byNet[^1].Data.Net);
            return Task.CompletedTask;
        });
        yield return Case("nav digest request is signed and a stub pull imports a new inbound invoice", async f =>
        {
            var secrets = new NavConnectionSecrets("87654321", "technikai", "jelszo", "alairas", "csere", true);
            var at = new DateTimeOffset(2026, 10, 21, 8, 15, 30, TimeSpan.Zero);
            var signed = NavInvoiceClient.DigestRequest(secrets, new DateOnly(2026, 9, 21), new DateOnly(2026, 10, 21), 1, at, "REQNAV1").ToString();
            Check.True(signed.Contains("INBOUND", StringComparison.Ordinal) && signed.Contains("2026-09-21", StringComparison.Ordinal) && signed.Contains("2026-10-21", StringComparison.Ordinal));
            Check.True(signed.Contains("SHA3-512", StringComparison.Ordinal) && signed.Contains("SHA-512", StringComparison.Ordinal));
            Check.True(signed.Contains(Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes("jelszo"))), StringComparison.Ordinal));
            Check.True(signed.Contains(Convert.ToHexString(NavSha3.Hash(Encoding.UTF8.GetBytes("REQNAV1" + "20261021081530" + "alairas"))), StringComparison.Ordinal));
            var digest = NavInvoiceClient.ParseDigest(DigestBody("NAV-1"));
            Check.Equal("NAV-1", digest.Invoices.Single().Number);
            Check.Equal("CREATE", digest.Invoices.Single().Operation);
            var gzipped = ReceivingImport.FromNav(NavInvoiceClient.InvoiceXml(DataBody(InvoiceBody("GZIP-1", "5990000000001"), gzip: true)));
            Check.Equal("GZIP-1", gzipped.Number);
            Check.Equal("5990000000001", gzipped.Lines.Single().Barcode);
            var merged = SupplierXmlLinks.MergeMissing(ReceivingImport.FromNav(InvoiceBody("M-1", "")), ReceivingImport.FromNav(InvoiceBody("M-1", "5991234567890")));
            Check.Equal("5991234567890", merged.Lines.Single().Barcode);
            Check.Equal(1m, merged.Lines.Single().Quantity);
            Check.Equal("https://nagyker.example/szamla", SupplierXmlLinks.Parse("xml-szamla=https://nagyker.example/szamla")!.Endpoint);
            Check.True(SupplierXmlLinks.Find("""{"partners":[{"data":{"taxId":"12345678-2-42","isActive":true,"customFields":"xml-szamla=igen"}}]}""", "12345678") is { HasEndpoint: false });
            var blocked = 0;
            using (var idle = new HttpClient(new ScriptedHandler(_ => { blocked++; return Xml("nem"); })))
                await Check.ThrowsAsync<ArgumentException>(async () => await NavIntake.PullAsync(f.Receipts, secrets, f.First.Id, "2020-01-01", "2026-01-01", new DateOnly(2026, 10, 21), "operator", CancellationToken.None, idle));
            Check.Equal(0, blocked);
            await f.Supplier("Nagyker Kft.", "12345678-2-42");
            using var http = new HttpClient(new ScriptedHandler(request =>
                request.RequestUri!.AbsolutePath.EndsWith("queryInvoiceDigest", StringComparison.Ordinal)
                    ? Xml(DigestBody("NAV-PULL-1"))
                    : request.RequestUri.AbsolutePath.EndsWith("queryInvoiceData", StringComparison.Ordinal)
                        ? Xml(DataBody(InvoiceBody("NAV-PULL-1", "")))
                        : throw new InvalidOperationException(request.RequestUri.ToString())));
            var pulled = await NavIntake.PullAsync(f.Receipts, secrets, f.First.Id, "2026-09-21", "2026-10-21", new DateOnly(2026, 10, 21), "operator", CancellationToken.None, http);
            Check.Equal(1, pulled.Imported.Length);
            Check.Equal(0, pulled.Errors.Length);
            Check.Equal(0, pulled.UnknownSuppliers.Length);
            var saved = (await f.Receipts.Snapshot(f.First.Id)).Receipts.Single();
            Check.Equal("uj", saved.Data.Status);
            Check.Equal("nav", saved.Data.Source);
            Check.Equal("NAV-PULL-1", saved.Data.Number);
        });
        yield return Case("supplier xml feed and a separate file fill only the missing invoice identifiers", async f =>
        {
            var supplier = await f.Supplier("Nagyker Kft.", "12345678-2-42");
            await f.Partners.Save(new(f.First.Id, supplier.Id, supplier.Version, supplier.Data with { CustomFields = "xml-szamla=https://nagyker.example/szamla" }), "operator");
            await f.Unit();
            await f.Product("Egér", "EGR-1", "5991234567890");
            var secrets = new NavConnectionSecrets("87654321", "technikai", "jelszo", "alairas", "csere", true);
            using var http = new HttpClient(new ScriptedHandler(request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("queryInvoiceDigest", StringComparison.Ordinal)) return Xml(DigestBody("NAV-PULL-2"));
                if (request.RequestUri.AbsolutePath.EndsWith("queryInvoiceData", StringComparison.Ordinal)) return Xml(DataBody(InvoiceBody("NAV-PULL-2", "")));
                Check.Equal("nagyker.example", request.RequestUri.Host);
                Check.True(request.RequestUri.Query.Contains("invoiceNumber=NAV-PULL-2", StringComparison.Ordinal));
                return Xml(InvoiceBody("NAV-PULL-2", "5991234567890"));
            }));
            var pulled = await NavIntake.PullAsync(f.Receipts, secrets, f.First.Id, "2026-09-21", "2026-10-21", new DateOnly(2026, 10, 21), "operator", CancellationToken.None, http);
            Check.Equal(1, pulled.XmlFeeds.Length);
            var saved = (await f.Receipts.Snapshot(f.First.Id)).Receipts.Single();
            Check.Equal("5991234567890", saved.Data.Lines.Single().Barcode);
            Check.Equal("megvan", saved.Data.Lines.Single().Match);
            var bare = InvoiceBody("NAV-SUP-1", "");
            var imported = await f.Receipts.Import(new(f.First.Id, null, null, null, null, "nav", "nav.xml", Convert.ToBase64String(Encoding.UTF8.GetBytes(bare))),
                ReceivingImport.Read("nav.xml", Convert.ToBase64String(Encoding.UTF8.GetBytes(bare))), "operator");
            Check.Equal("fuggo", imported.Data.Lines.Single().Match);
            var filled = InvoiceBody("NAV-SUP-1", "5991234567890");
            var body = Convert.ToBase64String(Encoding.UTF8.GetBytes(filled));
            var supplemented = await f.Receipts.Supplement(new(f.First.Id, imported.Id, imported.Version, "potlas.xml", body), ReceivingImport.ReadSupplement("potlas.xml", body), "operator");
            Check.Equal("megvan", supplemented.Data.Lines.Single().Match);
            var pdf = Encoding.Latin1.GetBytes("%PDF-1.4\n" + InvoiceBody("NAV-PDF-1", "5991234567890") + "\n%%EOF");
            Check.Equal("NAV-PDF-1", ReceivingImport.ReadSupplement("szamla.pdf", Convert.ToBase64String(pdf)).Number);
            Check.Throws<ArgumentException>(() => ReceivingImport.Read("szamla.pdf", Convert.ToBase64String(pdf)));
        });
    }

    private static string InvoiceBody(string number, string barcode)
    {
        var code = barcode.Length == 0 ? "" : "<productCode><productCodeCategory>EAN</productCodeCategory><productCodeValue>" + barcode + "</productCodeValue></productCode>";
        return "<InvoiceData><invoiceNumber>" + number + "</invoiceNumber><currencyCode>HUF</currencyCode><invoiceIssueDate>2026-10-01</invoiceIssueDate><supplierName>Nagyker Kft.</supplierName><supplierTaxNumber>12345678-2-42</supplierTaxNumber><invoiceNetAmount>1000</invoiceNetAmount><invoiceVatAmount>270</invoiceVatAmount><invoiceGrossAmount>1270</invoiceGrossAmount><line><lineDescription>Egér</lineDescription><quantity>1</quantity><unitPrice>1000</unitPrice><lineVatRate><vatPercentage>0.27</vatPercentage></lineVatRate>" + code + "</line></InvoiceData>";
    }
    private static string DigestBody(string number) =>
        "<QueryInvoiceDigestResponse xmlns=\"http://schemas.nav.gov.hu/OSA/3.0/api\" xmlns:common=\"http://schemas.nav.gov.hu/NTCA/1.0/common\"><common:result><common:funcCode>OK</common:funcCode></common:result><invoiceDigestResult><currentPage>1</currentPage><availablePage>1</availablePage><invoiceDigest><invoiceNumber>" + number + "</invoiceNumber><supplierTaxNumber>12345678</supplierTaxNumber><supplierName>Nagyker Kft.</supplierName><invoiceIssueDate>2026-10-01</invoiceIssueDate><invoiceOperation>CREATE</invoiceOperation></invoiceDigest></invoiceDigestResult></QueryInvoiceDigestResponse>";
    private static string DataBody(string invoice, bool gzip = false)
    {
        var raw = Encoding.UTF8.GetBytes(invoice);
        if (gzip)
        {
            using var output = new MemoryStream();
            using (var stream = new GZipStream(output, CompressionLevel.SmallestSize, true)) stream.Write(raw);
            raw = output.ToArray();
        }
        return "<QueryInvoiceDataResponse xmlns=\"http://schemas.nav.gov.hu/OSA/3.0/api\"><result><funcCode>OK</funcCode></result><invoiceData>" + Convert.ToBase64String(raw) + "</invoiceData><compressedContentIndicator>" + (gzip ? "true" : "false") + "</compressedContentIndicator></QueryInvoiceDataResponse>";
    }
    private static HttpResponseMessage Xml(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/xml") };
    private static ReceiptLineDraft Line(string name, decimal quantity, decimal price) => new(null, null, name, "", "", "DB", quantity, price, 27, name == "Szállítási díj");
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
    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> next) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(next(request));
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
