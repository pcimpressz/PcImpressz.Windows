using System.Text.Json;
using PcImpressz.Contracts;

namespace PcImpressz.Modules.Catalog;

public sealed partial class CatalogModule : IModule
{
    public ValueTask StartAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        RegisterProductOverview(context);
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
                if (query.Filter is "in-stock" or "out-of-stock" || query.MinStock is not null || query.MaxStock is not null)
                    throw new InvalidOperationException("A készletszűréshez céges adatbázis szükséges.");
                return Page(matching.Skip(Skip(query)).Take(query.PageSize).Select(p => ProductRow(p)), matching.Length);
            }
            return context.Database.Execute(query.CompanyId, tx =>
            {
                var quantities = CompanyReferenceQueries.StockQuantities(tx);
                matching = FilterStock(matching, quantities, query);
                if (query.Sort == "quantity") matching = (query.Descending ? matching.OrderByDescending(p=>quantities.GetValueOrDefault(p.Id)).ThenBy(p=>p.Id) : matching.OrderBy(p=>quantities.GetValueOrDefault(p.Id)).ThenBy(p=>p.Id)).ToArray();
                return Page(matching.Skip(Skip(query)).Take(query.PageSize).Select(p=>ProductRow(p, quantities.GetValueOrDefault(p.Id))), matching.Length);
            });
        });
        Register(context,"products-suggest","catalog.read",CommandAccess.RecoveryReadOnly,async(input,_,ct)=>
        {
            static string Fold(string value)=>new(value.Normalize(System.Text.NormalizationForm.FormD).Where(c=>char.GetUnicodeCategory(c)!=System.Globalization.UnicodeCategory.NonSpacingMark&&char.IsLetterOrDigit(c)).Select(char.ToUpperInvariant).ToArray());
            var name=input.GetProperty("name").GetString()??"";var sku=input.GetProperty("sku").GetString()??"";var barcode=input.GetProperty("barcode").GetString()??"";
            if(name.Length>200||sku.Length>80||barcode.Length>80)throw new ArgumentException("Túl hosszú termékkeresés.");
            var words=name.Split(' ',StringSplitOptions.RemoveEmptyEntries).Select(Fold).Where(w=>w.Length>2).Distinct().ToArray();
            var products=(await repository.Snapshot(input.GetProperty("companyId").GetGuid(),ct)).Products.Where(p=>p.Data.IsActive).Select(p=>
            {
                var exact=barcode.Length>0&&p.Data.Barcode==barcode||sku.Length>0&&(Fold(p.Data.Sku)==Fold(sku)||Fold(p.Data.ManufacturerPartNumber)==Fold(sku))||name.Length>0&&Fold(p.Data.Name)==Fold(name);
                return new{p.Id,p.Data.Name,p.Data.Sku,p.Data.Barcode,p.Data.UnitCode,exact,score=exact?1000:words.Count(w=>Fold(p.Data.Name).Contains(w,StringComparison.Ordinal))};
            }).Where(p=>p.score>0).OrderByDescending(p=>p.score).ThenBy(p=>p.Name,StringComparer.Ordinal).Take(10).ToArray();
            return CatalogJson.Element(new{rows=products});
        });
        Register(context,"barcode-propose","catalog.write",CommandAccess.Licensed,async(input,_,ct)=>
        {
            var codes=(await repository.Snapshot(input.GetProperty("companyId").GetGuid(),ct)).Products.Select(p=>p.Data.Barcode).ToHashSet(StringComparer.Ordinal);
            string code;do{var prefix="20"+System.Security.Cryptography.RandomNumberGenerator.GetInt32(0,1_000_000_000).ToString("D9")+System.Security.Cryptography.RandomNumberGenerator.GetInt32(0,10);var sum=prefix.Select((c,i)=>(c-'0')*(i%2==0?1:3)).Sum();code=prefix+((10-sum%10)%10);}while(codes.Contains(code));
            return CatalogJson.Element(new{barcode=code,kind="internal-ean13"});
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
                var filtered = FilterStock(rows, quantities, query);
                return query.Sort == "quantity" ? (query.Descending ? filtered.OrderByDescending(p=>quantities.GetValueOrDefault(p.Id)).ThenBy(p=>p.Id) : filtered.OrderBy(p=>quantities.GetValueOrDefault(p.Id)).ThenBy(p=>p.Id)).ToArray() : filtered;
            });
            else if (query.Filter is "in-stock" or "out-of-stock" || query.MinStock is not null || query.MaxStock is not null)
                throw new InvalidOperationException("A készletszűréshez céges adatbázis szükséges.");
            return CatalogJson.Element(new { csv = CatalogRepository.ExportCsv(
                "Név;Cikkszám;Gyártói cikkszám;Kategória;Mértékegység;Revízió;Vonalkód;Beszerzés;Eladás;Aktív;Leírás;Paraméterek;Megjegyzés;További eladási árak", rows, p =>
                [p.Data.Name, p.Data.Sku, p.Data.ManufacturerPartNumber, p.Data.CategoryCode, p.Data.UnitCode, p.Data.Revision, p.Data.Barcode,
                 p.Data.IsPurchaseable ? "Igen" : "Nem", p.Data.IsSalable ? "Igen" : "Nem", p.Data.IsActive ? "Igen" : "Nem",
                 p.Data.Description, p.Data.Parameters, p.Data.Notes,
                 string.Join(" | ", (p.Data.SalePrices ?? []).Select(price => SalePriceMath.Describe(price, p.Data.SaleNetUnitPrice, p.Data.SaleVatRate)))]) });
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
        var productPage=Page("products", context.ModuleId, "Terméktörzs", "Cikkszám, kategória, mértékegység és alapparaméterek.", "\uE7B8",
            [new("name", "Termék neve", Required: true), new("sku", "Saját cikkszám", Required: true), new("manufacturerPartNumber", "Gyártói cikkszám"), new("categoryCode", "Kategória kódja"),
             new("unitCode", "Mértékegység kódja", Required: true), new("revision", "Revízió"), new("barcode", "Vonalkód"),
             new("saleNetUnitPrice","Eladási egységár (Ft)","decimal"), new("saleVatRate","Eladási áfa %","decimal",Required:true,DefaultText:"27"),
             new("description", "Leírás", "multiline"), new("parameters", "Paraméterek (név=érték)", "multiline"),
             new("isPurchaseable", "Beszerzésre jelölhető", "boolean"), new("isSalable", "Eladásra jelölhető", "boolean"),
             new("isActive", "Aktív", "boolean", DefaultChecked: true), new("notes", "Megjegyzés", "multiline")],
            [new("name", "Termék", 280), new("sku", "Saját cikkszám", 140), new("quantity", "Saját készlet", 120),
             new("saleNet", "Nettó eladási ár", 140), new("saleGross", "Bruttó eladási ár", 150), new("barcode", "Vonalkód", 140),
             new("categoryCode", "Kategória", 120), new("unitCode", "Egység", 90), new("manufacturerPartNumber", "Gyártói cikkszám", 180, false),
             new("revision", "Revízió", 90, false), new("isPurchaseable", "Beszerzés", 110, false), new("isSalable", "Eladás", 100, false)],
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
            [new("all", "Aktív termékek"), new("in-stock", "Van saját készleten"), new("out-of-stock", "Nincs saját készleten"), new("purchaseable", "Beszerzésre jelöltek"), new("salable", "Eladásra jelöltek"), new("archived", "Archiváltak")]);
        context.RegisterPage(productPage);
        Register(context,"products-schema","catalog.read",CommandAccess.RecoveryReadOnly,(_,_,_)=>Task.FromResult(CatalogJson.Element(productPage.Fields)));
        Register(context,"products-get","catalog.read",CommandAccess.RecoveryReadOnly,async(input,_,ct)=>
        {
            var product=(await repository.Snapshot(input.GetProperty("companyId").GetGuid(),ct)).Products.SingleOrDefault(p=>p.Id==input.GetProperty("id").GetGuid())??throw new ArgumentException("A termék nem található.");
            return ProductRow(product);
        });
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
        if (query.MinPrice is < 0 or > 1_000_000_000 || query.MaxPrice is < 0 or > 1_000_000_000
            || query.MinStock is < -1_000_000 or > 1_000_000_000 || query.MaxStock is < -1_000_000 or > 1_000_000_000
            || query.MinPrice > query.MaxPrice || query.MinStock > query.MaxStock)
            throw new ArgumentException("Érvénytelen árszűrés vagy készletszűrés.");
        if (query.CategoryCodes is { Length: > 40 } || query.CategoryCodes?.Any(code => code is not { Length: > 0 and <= 40 }
            || code.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '.')) == true)
            throw new ArgumentException("Érvénytelen kategóriaszűrés.");
        return query;
    }
    public static ProductRecord[] FilterStock(ProductRecord[] products, IReadOnlyDictionary<Guid, decimal> quantities, string filter) =>
        FilterStock(products, quantities, new CatalogQuery(Guid.Empty, Filter: filter));
    public static ProductRecord[] FilterStock(ProductRecord[] products, IReadOnlyDictionary<Guid, decimal> quantities, CatalogQuery query)
    {
        if (query.MinStock > query.MaxStock) throw new ArgumentException("Érvénytelen készletszűrés.");
        return products.Where(p =>
        {
            var quantity = quantities.GetValueOrDefault(p.Id);
            if (query.Filter == "in-stock" && quantity <= 0 || query.Filter == "out-of-stock" && quantity > 0) return false;
            if (query.MinStock is { } min && quantity < min || query.MaxStock is { } max && quantity > max) return false;
            return true;
        }).ToArray();
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
        node["saleNet"] = JsonSerializer.SerializeToNode(Money(record.Data.SaleNetUnitPrice));
        node["saleGross"] = JsonSerializer.SerializeToNode(record.Data.SaleNetUnitPrice is null ? "—" : Money(decimal.Round(record.Data.SaleNetUnitPrice.Value * (1 + record.Data.SaleVatRate / 100m), 2, MidpointRounding.AwayFromZero)));
        return JsonSerializer.SerializeToElement(node);
    }
    private static string Money(decimal? value)
    {
        if (value is null) return "—";
        var culture = System.Globalization.CultureInfo.GetCultureInfo("hu-HU");
        return value.Value.ToString(value.Value == decimal.Round(value.Value, 0) ? "N0" : "N2", culture) + " Ft";
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
        if (query.MinPrice > query.MaxPrice) throw new ArgumentException("Érvénytelen árszűrés.");
        var categories = query.CategoryCodes?.Select(code => code.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
        var rows = source.OrderBy(p => p.Id).Where(p => query.Filter == "archived" ? !p.Data.IsActive : p.Data.IsActive)
            .Where(p => query.Filter switch
            { "purchaseable" => p.Data.IsPurchaseable, "salable" => p.Data.IsSalable, _ => true })
            .Where(p => !query.NoCategoryMatch && (categories is null || categories.Count == 0 || categories.Contains(p.Data.CategoryCode)))
            .Where(p => query.MinPrice is null && query.MaxPrice is null || p.Data.SaleNetUnitPrice is { } price
                && (query.MinPrice is null || price >= query.MinPrice) && (query.MaxPrice is null || price <= query.MaxPrice))
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
        ["quantity"] = _ => "—", ["saleNet"] = p => (p.Data.SaleNetUnitPrice ?? -1m).ToString("0000000000.0000", System.Globalization.CultureInfo.InvariantCulture),
        ["saleGross"] = p => (p.Data.SaleNetUnitPrice is null ? -1m : decimal.Round(p.Data.SaleNetUnitPrice.Value * (1 + p.Data.SaleVatRate / 100m), 2, MidpointRounding.AwayFromZero)).ToString("0000000000.0000", System.Globalization.CultureInfo.InvariantCulture),
        ["isPurchaseable"] = p => Yes(p.Data.IsPurchaseable), ["isSalable"] = p => Yes(p.Data.IsSalable)
    };
}
