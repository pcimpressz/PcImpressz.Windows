using System.Text.Json;
using PcImpressz.Business;
using PcImpressz.Contracts;
using PcImpressz.Core.Companies;
using PcImpressz.Core.Storage;
using PcImpressz.Modules.Catalog;
using PcImpressz.Modules.Inventory;
using PcImpressz.Shell.Customization;

namespace PcImpressz.Core.Tests;
internal static class ProductDevelopmentTests
{
    private static ProductRecord Record(string name, string sku, string mpn = "", string barcode = "") =>
        new(Guid.NewGuid(), 1, new ProductData { Name = name, Sku = sku, ManufacturerPartNumber = mpn, Barcode = barcode, UnitCode = "DB" },
            DateTimeOffset.UtcNow, "test", DateTimeOffset.UtcNow, "test");
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return Test("product search combines unordered accent-insensitive words and exact fields", () =>
        {
            var p = Record("Vezeték nélküli egér", "MOUSE-1", "ABC-123", "599123");
            Check.True(new ProductSearch("eger vezetek").Matches(p.Data));
            Check.True(new ProductSearch("gyartoi:=abc-123 nev:\"vezeték nélküli\"").Matches(p.Data));
            Check.True(new ProductSearch("vonalkod:599 sku:MOUSE").Matches(p.Data));
            Check.True(!new ProductSearch("sku:=MOUSE").Matches(p.Data));
            Check.True(!new ProductSearch("eger billentyuzet").Matches(p.Data));
            Check.Throws<ArgumentException>(() => new ProductSearch("nev:\"nyitott"));
            Check.Throws<ArgumentException>(() => new ProductSearch("ismeretlen:ertek"));
            Check.Throws<ArgumentException>(() => new ProductSearch("sku:"));
            Check.Throws<ArgumentException>(() => PcImpressz.Modules.Partners.PartnerListing.Validate(new(Guid.NewGuid(), Search: null!)));
            Check.Throws<ArgumentException>(() => PcImpressz.Modules.Partners.PartnerListing.Validate(new(Guid.NewGuid(), Filter: "unknown")));
        });
        yield return Test("exact product identifiers rank before partial matches with stable ties", () =>
        {
            var exact = Record("Záró termék", "ABC"); var partial = Record("Alma ABC tartozék", "OTHER");
            Check.Equal(exact.Id, CatalogListing.Products([partial, exact], new(Guid.NewGuid(), "abc"))[0].Id);
            var a = Record("Azonos", "A"); var b = Record("Azonos", "B");
            var first = CatalogListing.Products([a, b], new(Guid.NewGuid(), Sort: "name"));
            var reverse = CatalogListing.Products([b, a], new(Guid.NewGuid(), Sort: "name"));
            Check.True(first.Select(p => p.Id).SequenceEqual(reverse.Select(p => p.Id)));
        });
        yield return ("manufacturer part numbers persist in SQLite and old products remain readable", async () =>
        {
            using var dir = new TestDirectory(); var companies = new CompanyStore(dir.Path); var company = companies.Create("Próba", true);
            var database = new CompanyDatabase(companies.DataDirectory);
            var repository = new CatalogRepository(id => companies.ModuleDataDirectory(id, "catalog"), TimeProvider.System, database);
            await repository.SaveUnit(new(company.Id, null, 0, new UnitData { Name = "Darab", Code = "DB" }), "test");
            var saved = await repository.SaveProduct(new(company.Id, null, 0, new ProductData { Name = "Egér", Sku = "MOUSE-1", UnitCode = "DB", ManufacturerPartNumber = "  AB / 123  " }), "test");
            Check.Equal("AB / 123", (await repository.Snapshot(company.Id)).Products.Single().Data.ManufacturerPartNumber);
            Check.Equal("", JsonSerializer.Deserialize<ProductData>("{\"name\":\"Régi\",\"sku\":\"OLD\"}", CatalogJson.Options)!.ManufacturerPartNumber);
            Check.Throws<ArgumentException>(() => CatalogValidation.NormalizeProduct(saved.Data with { ManufacturerPartNumber = new string('a', 101) }));
        });
        yield return Test("product stock filters distinguish own stock and preserve zero-stock products", () =>
        {
            var a = Record("Készletes", "A"); var b = Record("Üres", "B");
            var quantities = new Dictionary<Guid, decimal> { [a.Id] = 2 };
            Check.Equal(a.Id, CatalogModule.FilterStock([a, b], quantities, "in-stock").Single().Id);
            Check.Equal(b.Id, CatalogModule.FilterStock([a, b], quantities, "out-of-stock").Single().Id);
        });
        yield return Test("inventory is embedded in product navigation with a standalone fallback", () =>
        {
            var products = new EntityPageDescriptor("products", "catalog", "Terméktörzs", "", "", [], [], new("list", "save", "archive", "export"));
            var inventory = products with { Id = "inventory", ModuleId = "inventory", Title = "Készlet", ViewKind = "inventory" };
            Check.True(WorkspaceMenu.Compose([products, inventory]).All(p => p.PageKey != "inventory.inventory"));
            Check.True(WorkspaceMenu.Compose([products, inventory], new HashSet<string> { "catalog" }).Any(p => p.PageKey == "inventory.inventory"));
        });
        yield return ("selected product inventory list history references and export cannot mix products", async () =>
        {
            using var dir = new TestDirectory(); var companies = new CompanyStore(dir.Path); var company = companies.Create("Próba", true);
            var db = new CompanyDatabase(companies.DataDirectory);
            var repo = new CatalogRepository(id => companies.ModuleDataDirectory(id, "catalog"), TimeProvider.System, db);
            await repo.SaveUnit(new(company.Id, null, 0, new UnitData { Name = "Darab", Code = "DB" }), "test");
            var a = await repo.SaveProduct(new(company.Id, null, 0, new ProductData { Name = "Egér", Sku = "A", UnitCode = "DB" }), "test");
            var b = await repo.SaveProduct(new(company.Id, null, 0, new ProductData { Name = "Monitor", Sku = "B", UnitCode = "DB" }), "test");
            db.Execute(company.Id, tx => StockLedger.Post(tx, "test", "inventory", Guid.NewGuid(),
                [new(a.Id, a.Data.Name, "DB", "Bolt", 2), new(b.Id, b.Data.Name, "DB", "Bolt", 3)], "Nyitás", "test", DateTimeOffset.UtcNow));
            var probe = new Probe(companies, db); await new InventoryModule().StartAsync(probe, default);
            async Task<JsonElement> Run(string command) => await ((IContextualCommandHandler)probe.Commands[command]).ExecuteAsync(
                JsonSerializer.SerializeToElement(new { companyId = company.Id, productId = a.Id }), new("test"), default);
            var list = await Run("list"); Check.Equal(1, list.GetProperty("rows").GetArrayLength());
            Check.True(list.GetProperty("movements").EnumerateArray().All(m => m.GetProperty("productId").GetGuid() == a.Id));
            Check.Equal(1, (await Run("references")).GetProperty("products").GetArrayLength());
            var csv = (await Run("export")).GetProperty("csv").GetString()!;
            Check.True(csv.Contains("Egér") && !csv.Contains("Monitor"));
        });
    }
    private static (string, Func<Task>) Test(string name, Action action) => (name, () => { action(); return Task.CompletedTask; });
    private sealed class Probe(CompanyStore companies, ICompanyDatabase database) : IModuleContext
    {
        public string ModuleId => "inventory";
        public TimeProvider TimeProvider => TimeProvider.System;
        public ICompanyDatabase Database => database;
        public Dictionary<string, ICommandHandler> Commands { get; } = [];
        public void RegisterCommand(ICommandHandler handler) => Commands.Add(handler.Descriptor.Name, handler);
        public void RegisterPage(EntityPageDescriptor page) { }
        public string GetCompanyDataDirectory(Guid companyId) => companies.ModuleDataDirectory(companyId, "inventory");
    }
}
