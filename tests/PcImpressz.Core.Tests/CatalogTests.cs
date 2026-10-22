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
        yield return Case("product search keeps a name fragment and the chosen column sort", async f =>
        {
            await f.Unit(f.First.Id, "Darab", "DB");
            await f.Product(f.First.Id, "Alfa csavar", "BBB");
            await f.Product(f.First.Id, "Zeta alátét", "AAA");
            await f.Product(f.First.Id, "Másik", "CCC");
            var products = (await f.Repository.Snapshot(f.First.Id)).Products;
            Check.Equal("Alfa csavar", CatalogListing.Products(products, new(f.First.Id, "alfa")).Single().Data.Name);
            var sorted = CatalogListing.Products(products, new(f.First.Id, Sort: "sku", Descending: true));
            Check.Equal("CCC", sorted[0].Data.Sku);
            Check.Equal("AAA", sorted[^1].Data.Sku);
            Check.Equal(3, CatalogListing.Products(products, new(f.First.Id, Sort: "nincsilyen")).Length);
            Check.Throws<ArgumentException>(() => CatalogListing.Products(products, new(f.First.Id, Sort: "sku;drop")));
        });
        yield return Case("sale prices accept gross entry math, named prices and a partial update", async f =>
        {
            await f.Unit(f.First.Id, "Darab", "DB");
            Check.Equal(1270m, SalePriceMath.GrossFromNet(1000m, 27));
            Check.Equal(1000m, SalePriceMath.NetFromGross(1270m, 27));
            Check.Equal(900m, SalePriceMath.ResolveNet(new("Akciós", "percent", 10), 1000m, 27));
            var saved = await f.Repository.SaveProduct(new(f.First.Id, null, 0, Product("Csavar", "CSV-1") with
            {
                SaleNetUnitPrice = 1000m, SaleVatRate = 27,
                SalePrices = [new("Akciós", "percent", 10), new("Törzsvásárlói", "gross", 1143m)]
            }), "operator");
            Check.Equal("percent", saved.Data.SalePrices![0].Kind);
            Check.Equal(1143m, saved.Data.SalePrices[1].Value);
            var kept = await f.Repository.SaveProduct(new(f.First.Id, saved.Id, saved.Version, saved.Data with { Name = "Csavar plusz", SalePrices = null }), "operator");
            Check.Equal(2, kept.Data.SalePrices!.Length);
            Check.Equal("Csavar plusz", kept.Data.Name);
            var cleared = await f.Repository.SaveProduct(new(f.First.Id, kept.Id, kept.Version, kept.Data with { SalePrices = [] }), "operator");
            Check.Equal(0, cleared.Data.SalePrices!.Length);
            await Check.ThrowsAsync<ArgumentException>(async () => await f.Repository.SaveProduct(
                new(f.First.Id, cleared.Id, cleared.Version, cleared.Data with { SalePrices = [new("Dupla", "percent", 0)] }), "operator"));
            await Check.ThrowsAsync<ArgumentException>(async () => await f.Repository.SaveProduct(
                new(f.First.Id, cleared.Id, cleared.Version, cleared.Data with { SalePrices = [new("Dupla", "percent", 10), new("dupla", "net", 1)] }), "operator"));
        });
        yield return ("product page shows quantity and can hide the sku", () =>
        {
            var probe = new PageProbe("catalog");
            new CatalogModule().StartAsync(probe, default).AsTask().GetAwaiter().GetResult();
            var products = probe.Pages.Single(page => page.Id == "products");
            Check.True(products.Columns.Single(column => column.Id == "quantity").Visible);
            Check.True(products.Columns.Single(column => column.Id == "sku").Visible);
            Check.True(products.Columns.Single(column => column.Id == "saleNet").Visible);
            Check.True(products.Columns.Single(column => column.Id == "saleGross").Visible);
            Check.True(products.Columns.Single(column => column.Id == "barcode").Visible);
            return Task.CompletedTask;
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
        public void Dispose() => Directory.Dispose();
    }
    private sealed class PageProbe(string moduleId) : IModuleContext
    {
        public string ModuleId => moduleId;
        public TimeProvider TimeProvider => TimeProvider.System;
        public List<EntityPageDescriptor> Pages { get; } = [];
        public void RegisterCommand(ICommandHandler handler) { }
        public void RegisterPage(EntityPageDescriptor page) => Pages.Add(page);
        public string GetCompanyDataDirectory(Guid companyId) => throw new NotSupportedException();
    }
}
