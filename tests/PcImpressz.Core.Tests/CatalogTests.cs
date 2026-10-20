using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PcImpressz.Contracts;
using PcImpressz.Core.Commands;
using PcImpressz.Core.Companies;
using PcImpressz.Core.Modules;
using PcImpressz.Modules.Catalog;

namespace PcImpressz.Core.Tests;

internal static class CatalogTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return Case("new company catalog starts empty", async f =>
        { Check.Equal(0, (await f.Repository.Snapshot(f.First.Id)).Products.Length); });
        yield return Case("catalog data is isolated by company", async f =>
        {
            await f.Unit(f.First.Id, "Darab", "DB");
            await f.Product(f.First.Id, "Csavar", "CSV-1");
            Check.Equal(1, (await f.Repository.Snapshot(f.First.Id)).Products.Length);
            Check.Equal(0, (await f.Repository.Snapshot(f.Second.Id)).Products.Length);
        });
        yield return Case("new company does not create catalog files", async f =>
        {
            var third = f.Companies.Create("Új saját cég", false);
            Check.Equal(0, (await f.Repository.Snapshot(third.Id)).Categories.Length);
            Check.True(!Directory.Exists(f.Companies.ModuleDataDirectory(third.Id, "catalog")));
        });
        yield return Case("unknown company cannot create a catalog directory", async f =>
        { await Check.ThrowsAsync<ArgumentException>(async () => await f.Unit(Guid.NewGuid(), "Darab", "DB")); });
        yield return Case("catalog audit uses the trusted caller", async f =>
        {
            var unit = await f.Unit(f.First.Id, "Darab", "DB", "windows-sid");
            Check.Equal("windows-sid", unit.CreatedBy);
            Check.Equal("windows-sid", (await f.Repository.Snapshot(f.First.Id)).Audit.Single().Actor);
        });
        yield return Case("stale product edit does not overwrite a newer record", async f =>
        {
            await f.Unit(f.First.Id, "Darab", "DB");
            var first = await f.Product(f.First.Id, "Eredeti", "CSV-1");
            await f.Repository.SaveProduct(new(f.First.Id, first.Id, first.Version, Product("Újabb", "CSV-1")), "operator");
            await Check.ThrowsAsync<CatalogConflictException>(async () =>
                await f.Repository.SaveProduct(new(f.First.Id, first.Id, first.Version, Product("Elavult", "CSV-1")), "other"));
            Check.Equal("Újabb", (await f.Repository.Snapshot(f.First.Id)).Products.Single().Data.Name);
        });
        yield return Case("active sku collision is rejected", async f =>
        {
            await f.Unit(f.First.Id, "Darab", "DB");
            await f.Product(f.First.Id, "Első", "csv-1");
            await Check.ThrowsAsync<ArgumentException>(async () => await f.Product(f.First.Id, "Második", "CSV-1"));
            Check.Equal(1, (await f.Repository.Snapshot(f.First.Id)).Products.Length);
        });
        yield return Case("product requires a known active unit and optional category", async f =>
        {
            await Check.ThrowsAsync<ArgumentException>(async () => await f.Product(f.First.Id, "Csavar", "CSV-1"));
            await f.Unit(f.First.Id, "Darab", "DB");
            await f.Category(f.First.Id, "Kötőelem", "KOT");
            var saved = await f.Repository.SaveProduct(new(f.First.Id, null, 0, Product("Csavar", "CSV-1") with { CategoryCode = "kot" }), "operator");
            Check.Equal("KOT", saved.Data.CategoryCode);
            Check.Equal("DB", saved.Data.UnitCode);
            await Check.ThrowsAsync<ArgumentException>(async () => await f.Repository.SaveProduct(
                new(f.First.Id, null, 0, Product("Anyás", "CSV-2") with { CategoryCode = "NINCS" }), "operator"));
        });
        yield return Case("category parent cannot form a cycle", async f =>
        {
            var parent = await f.Category(f.First.Id, "Gyökér", "ROOT");
            await f.Repository.SaveCategory(new(f.First.Id, null, 0, Category("Gyermek", "CHILD") with { ParentCode = "ROOT" }), "operator");
            await Check.ThrowsAsync<ArgumentException>(async () => await f.Repository.SaveCategory(
                new(f.First.Id, parent.Id, parent.Version, Category("Gyökér", "ROOT") with { ParentCode = "CHILD" }), "operator"));
        });
        yield return Case("parameters are normalized and invalid lines are rejected", async f =>
        {
            await f.Unit(f.First.Id, "Darab", "DB");
            var saved = await f.Repository.SaveProduct(new(f.First.Id, null, 0, Product("Csavar", "CSV-1") with { Parameters = " anyag : acél \n" }), "operator");
            Check.Equal("anyag=acél", saved.Data.Parameters);
            await Check.ThrowsAsync<ArgumentException>(async () => await f.Repository.SaveProduct(
                new(f.First.Id, null, 0, Product("Rossz", "CSV-2") with { Parameters = "nincs elválasztó" }), "operator"));
        });
        yield return Case("archive keeps the product and blocks a unit that is still used", async f =>
        {
            var unit = await f.Unit(f.First.Id, "Darab", "DB");
            var product = await f.Product(f.First.Id, "Csavar", "CSV-1");
            await Check.ThrowsAsync<ArgumentException>(async () => await f.Repository.ArchiveUnit(new(f.First.Id, unit.Id, unit.Version), "operator"));
            var archived = await f.Repository.ArchiveProduct(new(f.First.Id, product.Id, product.Version), "operator");
            Check.True(!archived.Data.IsActive);
            var state = await f.Repository.Snapshot(f.First.Id);
            Check.Equal(1, state.Products.Length);
            Check.True(state.Audit.Length >= 3);
            Check.True(File.Exists(Path.Combine(f.Companies.ModuleDataDirectory(f.First.Id, "catalog"), "catalog.json.bak")));
            await f.Repository.ArchiveUnit(new(f.First.Id, unit.Id, unit.Version), "operator");
            Check.True(!(await f.Repository.Snapshot(f.First.Id)).Units.Single().Data.IsActive);
        });
        yield return Case("corrupt catalog file is not silently replaced", async f =>
        {
            var directory = f.Companies.ModuleDataDirectory(f.First.Id, "catalog"); Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, "catalog.json"); File.WriteAllText(file, "corrupt");
            await Check.ThrowsAsync<IOException>(async () => await f.Unit(f.First.Id, "Darab", "DB"));
            Check.Equal("corrupt", File.ReadAllText(file));
        });
        yield return Case("catalog csv neutralizes spreadsheet formulas", async f =>
        {
            var unit = await f.Unit(f.First.Id, "=HYPERLINK(bad)", "DB");
            var csv = CatalogRepository.ExportCsv("Név;Kód", [unit], u => [u.Data.Name, u.Data.Code]);
            Check.True(csv.Contains("\"'=HYPERLINK(bad)\"", StringComparison.Ordinal));
        });
        yield return Case("import preview does not write and commit imports once", async f =>
        {
            var importer = f.Importer();
            var preview = await importer.Preview(f.Options(), "operator", default);
            Check.Equal(3, preview.NewCount);
            Check.Equal(0, (await f.Repository.Snapshot(f.First.Id)).Products.Length);
            Check.Equal(3, await importer.Commit(new(f.First.Id, preview.PreviewId), "operator", default));
            var state = await f.Repository.Snapshot(f.First.Id);
            Check.Equal("CSV-M6", state.Products.Single().Data.Sku);
            Check.Equal("IT-10", state.Products.Single().Data.CategoryCode);
            Check.Equal("DB", state.Products.Single().Data.UnitCode);
            await Check.ThrowsAsync<ArgumentException>(async () => await importer.Commit(new(f.First.Id, preview.PreviewId), "operator", default));
            Check.Equal(1, (await f.Repository.Snapshot(f.First.Id)).Products.Length);
        });
        yield return Case("repeated catalog import skips existing records", async f =>
        {
            var importer = f.Importer();
            await importer.Commit(new(f.First.Id, (await importer.Preview(f.Options(), "operator", default)).PreviewId), "operator", default);
            var repeated = await importer.Preview(f.Options(), "operator", default);
            Check.Equal(0, repeated.NewCount);
            Check.Equal(2, repeated.SkippedCount);
        });
        yield return Case("catalog import preview is bound to company and actor", async f =>
        {
            var importer = f.Importer();
            var preview = await importer.Preview(f.Options(), "operator", default);
            await Check.ThrowsAsync<ArgumentException>(async () => await importer.Commit(new(f.Second.Id, preview.PreviewId), "operator", default));
            await Check.ThrowsAsync<ArgumentException>(async () => await importer.Commit(new(f.First.Id, preview.PreviewId), "other", default));
            Check.Equal(0, (await f.Repository.Snapshot(f.First.Id)).Products.Length);
        });
        yield return Case("expired catalog import preview cannot commit", async f =>
        {
            var importer = f.Importer();
            var preview = await importer.Preview(f.Options(), "operator", default);
            f.Clock.Now = f.Clock.Now.AddMinutes(11);
            await Check.ThrowsAsync<ArgumentException>(async () => await importer.Commit(new(f.First.Id, preview.PreviewId), "operator", default));
        });
        yield return Case("catalog import does not overwrite edits after preview", async f =>
        {
            var importer = f.Importer();
            var preview = await importer.Preview(f.Options(), "operator", default);
            await f.Unit(f.First.Id, "Darab", "DB");
            await Check.ThrowsAsync<CatalogConflictException>(async () => await importer.Commit(new(f.First.Id, preview.PreviewId), "operator", default));
            Check.Equal(0, (await f.Repository.Snapshot(f.First.Id)).Products.Length);
        });
        yield return Case("catalog import token is never persisted", async f =>
        {
            var importer = f.Importer();
            var preview = await importer.Preview(f.Options(), "operator", default);
            await importer.Commit(new(f.First.Id, preview.PreviewId), "operator", default);
            foreach (var file in Directory.EnumerateFiles(f.Directory.Path, "*", SearchOption.AllDirectories))
                Check.True(!File.ReadAllText(file).Contains("TEST-API-TOKEN", StringComparison.Ordinal));
        });
        yield return Case("catalog import does not follow a foreign next link", async f =>
        {
            var calls = 0;
            var importer = new InvenTreeCatalogImport(f.Repository, f.Clock, () => new FakeHandler(_ =>
            { calls++; return "{\"results\":[],\"next\":\"https://other.invalid/api/part/category/\"}"; }));
            await Check.ThrowsAsync<HttpRequestException>(async () => await importer.Preview(f.Options(), "operator", default));
            Check.Equal(1, calls);
        });
        yield return ("catalog HTTP import requires explicit LAN permission", () =>
        {
            Check.Throws<ArgumentException>(() => InvenTreeCatalogImport.ValidateServer("http://192.168.110.71", false));
            Check.Equal("http://192.168.110.71/api/", InvenTreeCatalogImport.ValidateServer("http://192.168.110.71", true).AbsoluteUri);
            Check.Throws<ArgumentException>(() => InvenTreeCatalogImport.ValidateServer("http://public.invalid", true));
            Check.Throws<ArgumentException>(() => InvenTreeCatalogImport.ValidateServer("https://user:secret@host.invalid", false));
            return Task.CompletedTask;
        });
        yield return Case("signed catalog module exposes category, unit and product pages", async f =>
        {
            using var crypto = new CryptoFixture();
            var modules = Path.Combine(f.Directory.Path, "modules");
            var package = Path.Combine(modules, "catalog");
            Directory.CreateDirectory(package);
            var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "PcImpressz.Modules.Catalog.dll"));
            File.WriteAllBytes(Path.Combine(package, "PcImpressz.Modules.Catalog.dll"), bytes);
            var manifest = new ModuleManifest
            {
                SchemaVersion = 1, Id = "catalog", Version = "0.3.0", ApiVersion = PlatformContract.ApiVersion,
                EntryAssembly = "PcImpressz.Modules.Catalog.dll", EntryType = "PcImpressz.Modules.Catalog.CatalogModule",
                Files = new() { ["PcImpressz.Modules.Catalog.dll"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) },
                Dependencies = []
            };
            File.WriteAllText(Path.Combine(package, "manifest.jws"), crypto.Sign(manifest, ModuleManifest.DocumentType));
            var dispatcher = new CommandDispatcher(crypto.Evaluator(), NullLogger<CommandDispatcher>.Instance);
            await using var runtime = new ModuleRuntime(crypto.Verifier, dispatcher, f.Clock, NullLogger<ModuleRuntime>.Instance,
                (company, module) => f.Companies.ModuleDataDirectory(company, module));
            await runtime.StartAsync(modules);
            Check.Equal(ModuleStatus.Active, runtime.Reports.Single().Status);
            Check.Equal(3, runtime.Pages.Count);
            var listed = await dispatcher.ExecuteAsync(new("catalog", "products-list",
                JsonSerializer.SerializeToElement(new { companyId = f.First.Id })), new CallerIdentity("operator", new HashSet<string> { "catalog.read" }));
            Check.Equal(0, listed.GetProperty("total").GetInt32());
            await Check.ThrowsAsync<CommandDeniedException>(async () => await dispatcher.ExecuteAsync(new("catalog", "units-save",
                JsonSerializer.SerializeToElement(new { companyId = f.First.Id, version = 0, data = new { name = "Darab", code = "DB", isActive = true } })),
                new CallerIdentity("operator", new HashSet<string> { "catalog.write" })));
        });
    }

    private static CategoryData Category(string name, string code) => new() { Name = name, Code = code, IsActive = true };
    private static ProductData Product(string name, string sku) => new() { Name = name, Sku = sku, UnitCode = "DB", IsActive = true };
    private static (string, Func<Task>) Case(string name, Func<Fixture, Task> test) => (name, async () => { using var f = new Fixture(); await test(f); });
    private sealed class Fixture : IDisposable
    {
        public readonly TestDirectory Directory = new();
        public readonly TestTime Clock = new();
        public CompanyStore Companies { get; }
        public CompanyInfo First { get; }
        public CompanyInfo Second { get; }
        public CatalogRepository Repository { get; }
        public Fixture()
        {
            Companies = new(Directory.Path);
            First = Companies.Create("Saját cég", false);
            Second = Companies.Create("Üres demó", true);
            Repository = new(id => Companies.ModuleDataDirectory(id, "catalog"), Clock);
        }
        public Task<UnitRecord> Unit(Guid company, string name, string code, string actor = "operator") =>
            Repository.SaveUnit(new(company, null, 0, new() { Name = name, Code = code, IsActive = true }), actor);
        public Task<CategoryRecord> Category(Guid company, string name, string code) =>
            Repository.SaveCategory(new(company, null, 0, CatalogTests.Category(name, code)), "operator");
        public Task<ProductRecord> Product(Guid company, string name, string sku) =>
            Repository.SaveProduct(new(company, null, 0, CatalogTests.Product(name, sku)), "operator");
        public CatalogImportOptions Options() => new(First.Id, "https://inventree.test", "TEST-API-TOKEN");
        public InvenTreeCatalogImport Importer() => new(Repository, Clock, () => new FakeHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("/part/category/", StringComparison.Ordinal)
            ? "{\"results\":[{\"pk\":10,\"name\":\"Csavarok\",\"description\":\"Kötőelem\",\"parent\":null}],\"next\":null}"
            : "{\"results\":[{\"pk\":1,\"name\":\"Hatlapfejű csavar\",\"description\":\"M6\",\"IPN\":\"CSV-M6\",\"category\":10,\"units\":\"db\",\"active\":true,\"purchaseable\":true,\"salable\":false,\"revision\":\"A\"}],\"next\":null}"));
        public void Dispose() => Directory.Dispose();
    }
    private sealed class FakeHandler(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Check.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response(request), Encoding.UTF8, "application/json") });
        }
    }
}
