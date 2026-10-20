using System.Text.Json;
using PcImpressz.Contracts;

namespace PcImpressz.Modules.Catalog;

public sealed class CatalogModule : IModule
{
    public ValueTask StartAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var repository = new CatalogRepository(context.GetCompanyDataDirectory, context.TimeProvider);
        var importer = new InvenTreeCatalogImport(repository, context.TimeProvider);
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
            var query = CatalogJson.Read<CatalogQuery>(input);
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
            var query = CatalogJson.Read<CatalogQuery>(input);
            var rows = FilterUnits((await repository.Snapshot(query.CompanyId, ct)).Units, query);
            return CatalogJson.Element(new { csv = CatalogRepository.ExportCsv("Név;Kód;Jelölés;Aktív", rows, u =>
                [u.Data.Name, u.Data.Code, u.Data.Symbol, u.Data.IsActive ? "Igen" : "Nem"]) });
        });
        Register(context, "products-list", "catalog.read", CommandAccess.RecoveryReadOnly, async (input, _, ct) =>
        {
            var query = ReadQuery(input);
            var matching = FilterProducts((await repository.Snapshot(query.CompanyId, ct)).Products, query);
            return Page(matching.Skip(Skip(query)).Take(query.PageSize).Select(ProductRow), matching.Length);
        });
        Register(context, "products-save", "catalog.write", CommandAccess.Licensed, async (input, actor, ct) =>
            ProductRow(await repository.SaveProduct(CatalogJson.Read<ProductSave>(input), actor, ct)));
        Register(context, "products-archive", "catalog.write", CommandAccess.Licensed, async (input, actor, ct) =>
            ProductRow(await repository.ArchiveProduct(CatalogJson.Read<CatalogArchive>(input), actor, ct)));
        Register(context, "products-export", "catalog.export", CommandAccess.RecoveryReadOnly, async (input, _, ct) =>
        {
            var query = CatalogJson.Read<CatalogQuery>(input);
            var rows = FilterProducts((await repository.Snapshot(query.CompanyId, ct)).Products, query);
            return CatalogJson.Element(new { csv = CatalogRepository.ExportCsv(
                "Név;Cikkszám;Kategória;Mértékegység;Revízió;Vonalkód;Beszerzés;Eladás;Aktív;Leírás;Paraméterek;Megjegyzés", rows, p =>
                [p.Data.Name, p.Data.Sku, p.Data.CategoryCode, p.Data.UnitCode, p.Data.Revision, p.Data.Barcode,
                 p.Data.IsPurchaseable ? "Igen" : "Nem", p.Data.IsSalable ? "Igen" : "Nem", p.Data.IsActive ? "Igen" : "Nem",
                 p.Data.Description, p.Data.Parameters, p.Data.Notes]) });
        });
        Register(context, "products-import-preview", "catalog.import", CommandAccess.Licensed, async (input, actor, ct) =>
            CatalogJson.Element(await importer.Preview(CatalogJson.Read<CatalogImportOptions>(input), actor, ct)));
        Register(context, "products-import-commit", "catalog.import", CommandAccess.Licensed, async (input, actor, ct) =>
            CatalogJson.Element(new { imported = await importer.Commit(CatalogJson.Read<CatalogImportCommit>(input), actor, ct) }));
        context.RegisterPage(Page("categories", context.ModuleId, "Kategóriák", "Termékcsoportok, egymásba ágyazható kóddal.", "\uE8FD",
            [new("name", "Kategória neve", Required: true), new("code", "Kód", Required: true), new("parentCode", "Szülő kódja"),
             new("description", "Leírás", "multiline"), new("isActive", "Aktív", "boolean", DefaultChecked: true)],
            [new("name", "Kategória", 240), new("code", "Kód", 120), new("parentCode", "Szülő", 120)],
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
            [new("name", "Termék neve", Required: true), new("sku", "Cikkszám", Required: true), new("categoryCode", "Kategória kódja"),
             new("unitCode", "Mértékegység kódja", Required: true), new("revision", "Revízió"), new("barcode", "Vonalkód"),
             new("description", "Leírás", "multiline"), new("parameters", "Paraméterek (név=érték)", "multiline"),
             new("isPurchaseable", "Beszerzésre jelölhető", "boolean"), new("isSalable", "Eladásra jelölhető", "boolean"),
             new("isActive", "Aktív", "boolean", DefaultChecked: true), new("notes", "Megjegyzés", "multiline")],
            [new("name", "Termék", 220), new("sku", "Cikkszám", 140), new("categoryCode", "Kategória", 120), new("unitCode", "Egység", 90)],
            new("products-list", "products-save", "products-archive", "products-export", "products-import-preview", "products-import-commit"),
            new EntityPageLabels
            {
                NewLabel = "Új termék", NewTitle = "Új termék", EditTitle = "Termék szerkesztése", CountNoun = "termék",
                EmptyMessage = "Ebben a nézetben még nincs termék. Az Új termék gombbal vehetsz fel egyet.",
                SavedMessage = "A termék mentése sikerült.", NeedSelection = "Először válassz meglévő terméket.",
                ArchiveQuestion = "A termék inaktív állapotba kerül. Az adatai megmaradnak.",
                ArchiveTitle = "Termék archiválása", ArchivedMessage = "A termék archiválva.",
                ExportFileName = "termekek.csv", ExportedMessage = "A szűrt terméklista exportálva.",
                SearchHint = "Keresés név, cikkszám, vonalkód, kategória vagy mértékegység alapján",
                ImportWindowTitle = "InvenTree-termékek importálása", ImportLead = "Hozd át a meglévő terméktörzset.",
                ImportHelp = "Csak olvasunk az InvenTree-ből. Az import a kiválasztott helyi cégbe kerül, a már meglévő termékeket nem írja felül. Készlet, ár és rendelés nem kerül át.",
                ImportIdle = "A kulcsot nem mentjük el. A kategóriák, mértékegységek és termék-alapadatok kerülnek átvételre.",
                ImportCommitLabel = "Új törzsadatok importálása", ResultNoun = "törzsadat"
            },
            [new("all", "Aktív termékek"), new("purchaseable", "Beszerzésre jelöltek"), new("salable", "Eladásra jelöltek"), new("archived", "Archiváltak")]));
        return ValueTask.CompletedTask;
    }
    public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    private static void Register(IModuleContext context, string name, string permission, CommandAccess access,
        Func<JsonElement, string, CancellationToken, Task<JsonElement>> execute) =>
        context.RegisterCommand(new Handler(name, permission, access, execute));
    private static CatalogQuery ReadQuery(JsonElement input)
    {
        var query = CatalogJson.Read<CatalogQuery>(input);
        if (query.Page < 0 || query.PageSize is < 1 or > 200 || query.Search.Length > 200)
            throw new ArgumentException("Érvénytelen listázási kérés.");
        return query;
    }
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
        source.Where(c => query.Filter == "archived" ? !c.Data.IsActive : c.Data.IsActive)
        .Where(c => string.IsNullOrWhiteSpace(query.Search) || string.Join(" ", c.Data.Name, c.Data.Code, c.Data.ParentCode)
            .Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase))
        .OrderBy(c => c.Data.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("hu-HU"), true)).ToArray();
    private static UnitRecord[] FilterUnits(IEnumerable<UnitRecord> source, CatalogQuery query) =>
        source.Where(u => query.Filter == "archived" ? !u.Data.IsActive : u.Data.IsActive)
        .Where(u => string.IsNullOrWhiteSpace(query.Search) || string.Join(" ", u.Data.Name, u.Data.Code, u.Data.Symbol)
            .Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase))
        .OrderBy(u => u.Data.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("hu-HU"), true)).ToArray();
    private static ProductRecord[] FilterProducts(IEnumerable<ProductRecord> source, CatalogQuery query) =>
        source.Where(p => query.Filter == "archived" ? !p.Data.IsActive : p.Data.IsActive)
        .Where(p => query.Filter switch
        { "purchaseable" => p.Data.IsPurchaseable, "salable" => p.Data.IsSalable, _ => true })
        .Where(p => string.IsNullOrWhiteSpace(query.Search) || string.Join(" ", p.Data.Name, p.Data.Sku, p.Data.Barcode, p.Data.CategoryCode, p.Data.UnitCode)
            .Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase))
        .OrderBy(p => p.Data.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("hu-HU"), true)).ToArray();
    private static JsonElement CategoryRow(CategoryRecord record) => Row(record.Data, record.Id, record.Version);
    private static JsonElement UnitRow(UnitRecord record) => Row(record.Data, record.Id, record.Version);
    private static JsonElement ProductRow(ProductRecord record) => Row(record.Data, record.Id, record.Version);
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
