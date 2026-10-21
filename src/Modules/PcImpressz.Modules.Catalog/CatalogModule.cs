using System.Text.Json;
using PcImpressz.Contracts;

namespace PcImpressz.Modules.Catalog;

public sealed class CatalogModule : IModule
{
    public ValueTask StartAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var repository = new CatalogRepository(context.GetCompanyDataDirectory, context.TimeProvider, context.Database);
        Register(context, "categories-list", "catalog.read", CommandAccess.RecoveryReadOnly, async (input, _, ct) =>
        {
            var query = ReadQuery(input);
            var matching = FilterCategories((await repository.Snapshot(query.CompanyId, ct)).Categories, query);
            return Page(matching.Skip(Skip(query)).Take(query.PageSize).Select(CategoryRow), matching.Length);
        });
        Register(context, "categories-save", "catalog.write", CommandAccess.Licensed, async (input, actor, ct) =>
            CategoryRow(await repository.SaveCategory(CatalogJson.Read<CategorySave>(input), actor, ct)));
        Register(context, "categories-archive", "catalog.write", CommandAccess.Licensed, async (input, actor, ct) =>
            CategoryRow(await repository.ArchiveCategory(CatalogJson.Read<CatalogArchive>(input), actor, ct)));
        Register(context, "categories-export", "catalog.export", CommandAccess.RecoveryReadOnly, async (input, _, ct) =>
        {
            var query = ReadQuery(input);
            var rows = FilterCategories((await repository.Snapshot(query.CompanyId, ct)).Categories, query);
            return CatalogJson.Element(new { csv = CatalogRepository.ExportCsv("Név;Kód;Szülő;Leírás;Aktív", rows, c =>
                [c.Data.Name, c.Data.Code, c.Data.ParentCode, c.Data.Description, c.Data.IsActive ? "Igen" : "Nem"]) });
        });
        Register(context, "units-list", "catalog.read", CommandAccess.RecoveryReadOnly, async (input, _, ct) =>
        {
            var query = ReadQuery(input);
            var matching = FilterUnits((await repository.Snapshot(query.CompanyId, ct)).Units, query);
            return Page(matching.Skip(Skip(query)).Take(query.PageSize).Select(UnitRow), matching.Length);
        });
        Register(context, "units-save", "catalog.write", CommandAccess.Licensed, async (input, actor, ct) =>
            UnitRow(await repository.SaveUnit(CatalogJson.Read<UnitSave>(input), actor, ct)));
        Register(context, "units-archive", "catalog.write", CommandAccess.Licensed, async (input, actor, ct) =>
            UnitRow(await repository.ArchiveUnit(CatalogJson.Read<CatalogArchive>(input), actor, ct)));
        Register(context, "units-export", "catalog.export", CommandAccess.RecoveryReadOnly, async (input, _, ct) =>
        {
            var query = ReadQuery(input);
            var rows = FilterUnits((await repository.Snapshot(query.CompanyId, ct)).Units, query);
            return CatalogJson.Element(new { csv = CatalogRepository.ExportCsv("Név;Kód;Jelölés;Aktív", rows, u =>
                [u.Data.Name, u.Data.Code, u.Data.Symbol, u.Data.IsActive ? "Igen" : "Nem"]) });
        });
        Register(context, "products-list", "catalog.read", CommandAccess.RecoveryReadOnly, async (input, _, ct) =>
        {
            var query = ReadQuery(input);
            var matching = FilterProducts((await repository.Snapshot(query.CompanyId, ct)).Products, query);
            if (context.Database is null)
            {
                if (query.Filter is "in-stock" or "out-of-stock") throw new InvalidOperationException("A készletszűréshez céges adatbázis szükséges.");
                return Page(matching.Skip(Skip(query)).Take(query.PageSize).Select(p => ProductRow(p)), matching.Length);
            }
            return context.Database.Execute(query.CompanyId, tx =>
            {
                var quantities = CompanyReferenceQueries.StockQuantities(tx);
                matching = FilterStock(matching, quantities, query.Filter);
                if (query.Sort == "quantity") matching = (query.Descending ? matching.OrderByDescending(p=>quantities.GetValueOrDefault(p.Id)).ThenBy(p=>p.Id) : matching.OrderBy(p=>quantities.GetValueOrDefault(p.Id)).ThenBy(p=>p.Id)).ToArray();
                return Page(matching.Skip(Skip(query)).Take(query.PageSize).Select(p=>ProductRow(p, quantities.GetValueOrDefault(p.Id))), matching.Length);
            });
        });
        Register(context, "products-save", "catalog.write", CommandAccess.Licensed, async (input, actor, ct) =>
            ProductRow(await repository.SaveProduct(CatalogJson.Read<ProductSave>(input), actor, ct)));
        Register(context, "products-archive", "catalog.write", CommandAccess.Licensed, async (input, actor, ct) =>
            ProductRow(await repository.ArchiveProduct(CatalogJson.Read<CatalogArchive>(input), actor, ct)));
        Register(context, "products-export", "catalog.export", CommandAccess.RecoveryReadOnly, async (input, _, ct) =>
        {
            var query = ReadQuery(input);
            var rows = FilterProducts((await repository.Snapshot(query.CompanyId, ct)).Products, query);
            if (context.Database is not null) rows = context.Database.Execute(query.CompanyId, tx =>
            {
                var quantities = CompanyReferenceQueries.StockQuantities(tx);
                var filtered = FilterStock(rows, quantities, query.Filter);
                return query.Sort == "quantity" ? (query.Descending ? filtered.OrderByDescending(p=>quantities.GetValueOrDefault(p.Id)).ThenBy(p=>p.Id) : filtered.OrderBy(p=>quantities.GetValueOrDefault(p.Id)).ThenBy(p=>p.Id)).ToArray() : filtered;
            });
            else if (query.Filter is "in-stock" or "out-of-stock") throw new InvalidOperationException("A készletszűréshez céges adatbázis szükséges.");
            return CatalogJson.Element(new { csv = CatalogRepository.ExportCsv(
                "Név;Cikkszám;Gyártói cikkszám;Kategória;Mértékegység;Revízió;Vonalkód;Beszerzés;Eladás;Aktív;Leírás;Paraméterek;Megjegyzés", rows, p =>
                [p.Data.Name, p.Data.Sku, p.Data.ManufacturerPartNumber, p.Data.CategoryCode, p.Data.UnitCode, p.Data.Revision, p.Data.Barcode,
                 p.Data.IsPurchaseable ? "Igen" : "Nem", p.Data.IsSalable ? "Igen" : "Nem", p.Data.IsActive ? "Igen" : "Nem",
                 p.Data.Description, p.Data.Parameters, p.Data.Notes]) });
        });
        context.RegisterPage(Page("categories", context.ModuleId, "Kategóriák", "Termékcsoportok, egymásba ágyazható kóddal.", "\uE8FD",
            [new("name", "Kategória neve", Required: true), new("code", "Kód", Required: true), new("parentCode", "Szülő kódja"),
             new("description", "Leírás", "multiline"), new("isActive", "Aktív", "boolean", DefaultChecked: true)],
            [new("name", "Kategória", 240), new("code", "Kód", 120), new("parentCode", "Szülő", 120), new("description", "Leírás", 220, false)],
            new("categories-list", "categories-save", "categories-archive", "categories-export"),
            Labels("kategória", "Kategória", "kategóriát", "kategoriak.csv", "Keresés név vagy kód alapján"),
            [new("all", "Aktív kategóriák"), new("archived", "Archiváltak")]));
        context.RegisterPage(Page("units", context.ModuleId, "Mértékegységek", "Darab, kilogramm és a saját egységek.", "\uE8EF",
            [new("name", "Megnevezés", Required: true), new("code", "Kód", Required: true), new("symbol", "Jelölés"),
             new("isActive", "Aktív", "boolean", DefaultChecked: true)],
            [new("name", "Mértékegység", 220), new("code", "Kód", 120), new("symbol", "Jelölés", 120)],
            new("units-list", "units-save", "units-archive", "units-export"),
            Labels("mértékegység", "Mértékegység", "mértékegységet", "mertekegysegek.csv", "Keresés név, kód vagy jelölés alapján"),
            [new("all", "Aktív mértékegységek"), new("archived", "Archiváltak")]));
        context.RegisterPage(Page("products", context.ModuleId, "Terméktörzs", "Cikkszám, kategória, mértékegység és alapparaméterek.", "\uE7B8",
            [new("name", "Termék neve", Required: true), new("sku", "Saját cikkszám", Required: true), new("manufacturerPartNumber", "Gyártói cikkszám"), new("categoryCode", "Kategória kódja"),
             new("unitCode", "Mértékegység kódja", Required: true), new("revision", "Revízió"), new("barcode", "Vonalkód"),
             new("saleNetUnitPrice","Nettó eladási egységár (Ft)","decimal"), new("saleVatRate","Eladási áfa %","decimal",Required:true,DefaultText:"27"),
             new("description", "Leírás", "multiline"), new("parameters", "Paraméterek (név=érték)", "multiline"),
             new("isPurchaseable", "Beszerzésre jelölhető", "boolean"), new("isSalable", "Eladásra jelölhető", "boolean"),
             new("isActive", "Aktív", "boolean", DefaultChecked: true), new("notes", "Megjegyzés", "multiline")],
            [new("name", "Termék", 340), new("sku", "Saját cikkszám", 180), new("manufacturerPartNumber", "Gyártói cikkszám", 210), new("categoryCode", "Kategória", 120), new("unitCode", "Egység", 90),
             new("quantity", "Saját készlet", 110), new("revision", "Revízió", 90, false), new("barcode", "Vonalkód", 140, false),
             new("isPurchaseable", "Beszerzés", 110, false), new("isSalable", "Eladás", 100, false)],
            new("products-list", "products-save", "products-archive", "products-export"),
            new EntityPageLabels
            {
                NewLabel = "Új termék", NewTitle = "Új termék", EditTitle = "Termék szerkesztése", CountNoun = "termék",
                EmptyMessage = "Ebben a nézetben még nincs termék. Az Új termék gombbal vehetsz fel egyet.",
                SavedMessage = "A termék mentése sikerült.", NeedSelection = "Először válassz meglévő terméket.",
                ArchiveQuestion = "A termék inaktív állapotba kerül. Az adatai megmaradnak.",
                ArchiveTitle = "Termék archiválása", ArchivedMessage = "A termék archiválva.",
                ExportFileName = "termekek.csv", ExportedMessage = "A szűrt terméklista exportálva.",
                SearchHint = "Több szó, ékezet nélkül is. Példa: vezetek nelkuli eger · gyartoi:=ABC-123 · sku:=MOUSE-1 · vonalkod:599 · nev:\"vezeték nélküli\""
            },
            [new("all", "Aktív termékek"), new("in-stock", "Van saját készleten"), new("out-of-stock", "Nincs saját készleten"), new("purchaseable", "Beszerzésre jelöltek"), new("salable", "Eladásra jelöltek"), new("archived", "Archiváltak")]));
        return ValueTask.CompletedTask;
    }
    public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    private static void Register(IModuleContext context, string name, string permission, CommandAccess access,
        Func<JsonElement, string, CancellationToken, Task<JsonElement>> execute) =>
        context.RegisterCommand(new Handler(name, permission, access, execute));
    private static CatalogQuery ReadQuery(JsonElement input)
    {
        var query = CatalogJson.Read<CatalogQuery>(input);
        if (query.Page < 0 || query.PageSize is < 1 or > 200 || (query.Search is null || query.Search.Length > 200) || query.Filter is null || query.Sort is null || !IsSort(query.Sort))
            throw new ArgumentException("Érvénytelen listázási kérés.");
        return query;
    }
    public static ProductRecord[] FilterStock(ProductRecord[] products, IReadOnlyDictionary<Guid, decimal> quantities, string filter) =>
        products.Where(p => filter switch
        { "in-stock" => quantities.GetValueOrDefault(p.Id) > 0, "out-of-stock" => quantities.GetValueOrDefault(p.Id) <= 0, _ => true }).ToArray();
    private static int Skip(CatalogQuery query) => query.Page * (long)query.PageSize > int.MaxValue ? int.MaxValue : query.Page * query.PageSize;
    private static JsonElement Page(IEnumerable<JsonElement> rows, int total) => CatalogJson.Element(new { rows, total });
    private static EntityPageDescriptor Page(string id, string moduleId, string title, string description, string icon,
        EntityField[] fields, EntityColumn[] columns, EntityPageCommands commands, EntityPageLabels labels, EntityFilterOption[] filters) =>
        new(id, moduleId, title, description, icon, fields, columns, commands, labels, filters);
    private static EntityPageLabels Labels(string noun, string capital, string accusative, string file, string search) => new()
    {
        NewLabel = "Új " + noun, NewTitle = "Új " + noun, EditTitle = capital + " szerkesztése", CountNoun = noun,
        EmptyMessage = "Ebben a nézetben még nincs " + noun + ". Az Új " + noun + " gombbal vehetsz fel egyet.",
        SavedMessage = "A " + noun + " mentése sikerült.", NeedSelection = "Először válassz meglévő " + accusative + ".",
        ArchiveQuestion = "A " + noun + " inaktív állapotba kerül. Az adatai megmaradnak.",
        ArchiveTitle = capital + " archiválása", ArchivedMessage = "A " + noun + " archiválva.",
        ExportFileName = file, ExportedMessage = "A szűrt lista exportálva.", SearchHint = search
    };
    private static CategoryRecord[] FilterCategories(IEnumerable<CategoryRecord> source, CatalogQuery query) =>
        CatalogListing.Categories(source, query);
    private static UnitRecord[] FilterUnits(IEnumerable<UnitRecord> source, CatalogQuery query) =>
        CatalogListing.Units(source, query);
    private static ProductRecord[] FilterProducts(IEnumerable<ProductRecord> source, CatalogQuery query) =>
        CatalogListing.Products(source, query);
    private static bool IsSort(string sort) => sort.Length <= 40 && sort.All(char.IsAsciiLetterOrDigit);
    private static JsonElement CategoryRow(CategoryRecord record) => Row(record.Data, record.Id, record.Version);
    private static JsonElement UnitRow(UnitRecord record) => Row(record.Data, record.Id, record.Version);
    private static JsonElement ProductRow(ProductRecord record) => ProductRow(record, null);
    private static JsonElement ProductRow(ProductRecord record, decimal? quantity)
    {
        var node = JsonSerializer.SerializeToNode(record.Data, CatalogJson.Options)!;
        node["id"] = record.Id; node["version"] = record.Version; node["quantity"] = quantity is null ? JsonSerializer.SerializeToNode("—") : JsonSerializer.SerializeToNode(quantity.Value);
        return JsonSerializer.SerializeToElement(node);
    }
    private static JsonElement Row<T>(T data, Guid id, long version)
    {
        var node = JsonSerializer.SerializeToNode(data, CatalogJson.Options)!;
        node["id"] = id; node["version"] = version;
        return JsonSerializer.SerializeToElement(node);
    }
    private sealed class Handler(string name, string permission, CommandAccess access,
        Func<JsonElement, string, CancellationToken, Task<JsonElement>> execute) : IContextualCommandHandler
    {
        public CommandDescriptor Descriptor { get; } = new(name, permission, access);
        public ValueTask<JsonElement> ExecuteAsync(JsonElement input, CommandCallContext caller, CancellationToken ct) =>
            new(execute(input, caller.ActorId, ct));
    }
}

public static class CatalogListing
{
    private static readonly StringComparer NameOrder = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("hu-HU"), true);
    public static CategoryRecord[] Categories(IEnumerable<CategoryRecord> source, CatalogQuery query)
    {
        RequireSort(query.Sort);
        var rows = source.Where(c => query.Filter == "archived" ? !c.Data.IsActive : c.Data.IsActive)
            .Where(c => Contains(query.Search, c.Data.Name, c.Data.Code, c.Data.ParentCode)).ToArray();
        return Order(rows, query, CategoryKeys, c => c.Data.Name);
    }
    public static UnitRecord[] Units(IEnumerable<UnitRecord> source, CatalogQuery query)
    {
        RequireSort(query.Sort);
        var rows = source.Where(u => query.Filter == "archived" ? !u.Data.IsActive : u.Data.IsActive)
            .Where(u => Contains(query.Search, u.Data.Name, u.Data.Code, u.Data.Symbol)).ToArray();
        return Order(rows, query, UnitKeys, u => u.Data.Name);
    }
    public static ProductRecord[] Products(IEnumerable<ProductRecord> source, CatalogQuery query)
    {
        RequireSort(query.Sort);
        var search = new ProductSearch(query.Search);
        var rows = source.OrderBy(p => p.Id).Where(p => query.Filter == "archived" ? !p.Data.IsActive : p.Data.IsActive)
            .Where(p => query.Filter switch
            { "purchaseable" => p.Data.IsPurchaseable, "salable" => p.Data.IsSalable, _ => true })
            .Where(p => search.Matches(p.Data)).ToArray();
        return query.Sort.Length == 0 ? rows.OrderBy(p => search.Rank(p.Data)).ThenBy(p => p.Data.Name, NameOrder).ThenBy(p => p.Id).ToArray() : Order(rows, query, ProductKeys, p => p.Data.Name);
    }
    private static T[] Order<T>(T[] rows, CatalogQuery query, Dictionary<string, Func<T, string>> keys, Func<T, string> name) =>
        query.Sort.Length > 0 && keys.TryGetValue(query.Sort, out var key)
            ? TextOrder.By(rows, query.Descending, key)
            : rows.OrderBy(name, NameOrder).ToArray();
    private static bool Contains(string search, params string[] parts) =>
        string.IsNullOrWhiteSpace(search) || string.Join(" ", parts).Contains(search.Trim(), StringComparison.OrdinalIgnoreCase);
    private static void RequireSort(string sort)
    {
        if (sort.Length > 40 || sort.Any(c => !char.IsAsciiLetterOrDigit(c)))
            throw new ArgumentException("Érvénytelen listázási kérés.");
    }
    private static string Yes(bool value) => value ? "Igen" : "Nem";
    private static readonly Dictionary<string, Func<CategoryRecord, string>> CategoryKeys = new(StringComparer.Ordinal)
    {
        ["name"] = c => c.Data.Name, ["code"] = c => c.Data.Code, ["parentCode"] = c => c.Data.ParentCode, ["description"] = c => c.Data.Description
    };
    private static readonly Dictionary<string, Func<UnitRecord, string>> UnitKeys = new(StringComparer.Ordinal)
    {
        ["name"] = u => u.Data.Name, ["code"] = u => u.Data.Code, ["symbol"] = u => u.Data.Symbol
    };
    private static readonly Dictionary<string, Func<ProductRecord, string>> ProductKeys = new(StringComparer.Ordinal)
    {
        ["name"] = p => p.Data.Name, ["sku"] = p => p.Data.Sku, ["manufacturerPartNumber"] = p => p.Data.ManufacturerPartNumber, ["categoryCode"] = p => p.Data.CategoryCode,
        ["unitCode"] = p => p.Data.UnitCode, ["revision"] = p => p.Data.Revision, ["barcode"] = p => p.Data.Barcode,
        ["quantity"] = _ => "—", ["isPurchaseable"] = p => Yes(p.Data.IsPurchaseable), ["isSalable"] = p => Yes(p.Data.IsSalable)
    };
}
