using PcImpressz.Contracts;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace PcImpressz.Modules.Catalog;

// One company, one Host, one file for categories, units and products so a reference check stays atomic.
public sealed class CatalogRepository(Func<Guid, string> companyDirectory, TimeProvider time, ICompanyDatabase? database = null)
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    private async Task<T> Locked<T>(Guid company, Func<ModuleStorageScope, CatalogState, T> action, CancellationToken ct)
    {
        var directory = companyDirectory(company);
        var gate = _locks.GetOrAdd(company, _ => new(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (database is not null) return database.Execute(company, tx =>
            {
                var scope = new ModuleStorageScope(directory, tx);
                return action(scope, Read(scope));
            });
            var legacy = new ModuleStorageScope(directory, null);
            return action(legacy, Read(legacy));
        }
        finally { gate.Release(); }
    }
    public Task<CatalogState> Snapshot(Guid company, CancellationToken ct = default) => Locked(company, (_, state) => state, ct);
    private static CatalogState Read(ModuleStorageScope scope)
    {
        var directory = scope.Directory;
        var body = scope.Read("catalog", "catalog.json");
        var file = Path.Combine(directory, "catalog.json");
        if (body is null) return new(1, 0, [], [], [], []);
        try
        {
            var state = JsonSerializer.Deserialize<CatalogState>(body, CatalogJson.Options)
                ?? throw new IOException("Üres adattár.");
            if (state.SchemaVersion != 1 || state.Revision < 0 || state.Categories is null || state.Units is null
                || state.Products is null || state.Audit is null
                || Broken(state.Categories, c => c.Id, c => c.Version, c => c.Data)
                || Broken(state.Units, u => u.Id, u => u.Version, u => u.Data)
                || Broken(state.Products, p => p.Id, p => p.Version, p => p.Data)
                || Duplicates(state.Categories.Where(c => c.Data.IsActive).Select(c => CatalogValidation.Key(c.Data.Code)))
                || Duplicates(state.Units.Where(u => u.Data.IsActive).Select(u => CatalogValidation.Key(u.Data.Code)))
                || Duplicates(state.Products.Where(p => p.Data.IsActive).Select(p => CatalogValidation.Key(p.Data.Sku))))
                throw new IOException("Sérült adattár.");
            return state;
        }
        catch (JsonException) { throw new IOException("A terméktörzs sérült. A catalog.json.bak mentésből állítható helyre."); }
    }
    private static bool Broken<T>(T[] rows, Func<T, Guid> id, Func<T, long> version, Func<T, object?> data) =>
        rows.Any(r => id(r) == Guid.Empty || version(r) < 1 || data(r) is null) || rows.Select(id).Distinct().Count() != rows.Length;
    private static bool Duplicates(IEnumerable<string> keys)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return keys.Any(key => key.Length > 0 && !seen.Add(key));
    }
    public Task<CategoryRecord> SaveCategory(CategorySave request, string actor, CancellationToken ct = default) =>
        Locked(request.CompanyId, (directory, state) => WriteCategory(directory, state, request, actor), ct);
    public Task<UnitRecord> SaveUnit(UnitSave request, string actor, CancellationToken ct = default) =>
        Locked(request.CompanyId, (directory, state) => WriteUnit(directory, state, request, actor), ct);
    public Task<ProductRecord> SaveProduct(ProductSave request, string actor, CancellationToken ct = default) =>
        Locked(request.CompanyId, (directory, state) => WriteProduct(directory, state, request, actor), ct);
    public Task<CategoryRecord> ArchiveCategory(CatalogArchive request, string actor, CancellationToken ct = default) =>
        Locked(request.CompanyId, (directory, state) =>
        {
            var existing = Find(state.Categories, request.Id, request.Version, c => c.Id, c => c.Version)
                ?? throw new CatalogConflictException("A kategória nem található.");
            if (ReferencedCategory(state, existing.Data.Code))
                throw new ArgumentException("A kategóriát még aktív alkategória vagy termék használja.");
            return WriteCategory(directory, state, new(request.CompanyId, request.Id, request.Version, existing.Data with { IsActive = false }), actor);
        }, ct);
    public Task<UnitRecord> ArchiveUnit(CatalogArchive request, string actor, CancellationToken ct = default) =>
        Locked(request.CompanyId, (directory, state) =>
        {
            var existing = Find(state.Units, request.Id, request.Version, u => u.Id, u => u.Version)
                ?? throw new CatalogConflictException("A mértékegység nem található.");
            if (ReferencedUnit(state, existing.Data.Code))
                throw new ArgumentException("A mértékegységet még aktív termék használja.");
            return WriteUnit(directory, state, new(request.CompanyId, request.Id, request.Version, existing.Data with { IsActive = false }), actor);
        }, ct);
    public async Task<ProductRecord> ArchiveProduct(CatalogArchive request, string actor, CancellationToken ct = default)
    {
        var snapshot = await Snapshot(request.CompanyId, ct);
        var product = snapshot.Products.SingleOrDefault(p => p.Id == request.Id)
            ?? throw new CatalogConflictException("A termék nem található.");
        if (product.Version != request.Version) throw new CatalogConflictException("A termék közben módosult. Frissítsd a listát, majd nyisd meg újra.");
        return await SaveProduct(new(request.CompanyId, request.Id, request.Version, product.Data with { IsActive = false }), actor, ct);
    }
    private CategoryRecord WriteCategory(ModuleStorageScope scope, CatalogState state, CategorySave request, string actor)
    {
        var data = CatalogValidation.NormalizeCategory(request.Data ?? throw new ArgumentException("Hiányzó kategóriaadat."));
        var existing = Find(state.Categories, request.Id, request.Version, c => c.Id, c => c.Version);
        if (data.ParentCode.Length > 0 && data.ParentCode == data.Code)
            throw new ArgumentException("A kategória nem lehet a saját szülője.");
        EnsureParent(state, data, existing?.Id);
        if (ActiveCodeTaken(state.Categories, existing?.Id, c => c.Id, c => c.Data.IsActive, c => c.Data.Code, data.Code, data.IsActive))
            throw new ArgumentException("Ezzel a kóddal már létezik aktív kategória.");
        RejectCodeChange(existing?.Data.Code, data.Code, ReferencedCategory(state, existing?.Data.Code));
        var now = time.GetUtcNow();
        var record = new CategoryRecord(existing?.Id ?? Guid.NewGuid(), (existing?.Version ?? 0) + 1, data,
            existing?.CreatedAt ?? now, existing?.CreatedBy ?? actor, now, actor, existing?.Source, existing?.ExternalId);
        Write(scope, state with
        {
            Revision = state.Revision + 1,
            Categories = Upsert(state.Categories, record, r => r.Id),
            Audit = Append(state.Audit, "category", record.Id, existing is null ? "create" : "update", record.Version, now, actor)
        });
        return record;
    }
    private UnitRecord WriteUnit(ModuleStorageScope scope, CatalogState state, UnitSave request, string actor)
    {
        var data = CatalogValidation.NormalizeUnit(request.Data ?? throw new ArgumentException("Hiányzó mértékegység."));
        var existing = Find(state.Units, request.Id, request.Version, u => u.Id, u => u.Version);
        if (ActiveCodeTaken(state.Units, existing?.Id, u => u.Id, u => u.Data.IsActive, u => u.Data.Code, data.Code, data.IsActive))
            throw new ArgumentException("Ezzel a kóddal már létezik aktív mértékegység.");
        RejectCodeChange(existing?.Data.Code, data.Code, ReferencedUnit(state, existing?.Data.Code));
        var now = time.GetUtcNow();
        var record = new UnitRecord(existing?.Id ?? Guid.NewGuid(), (existing?.Version ?? 0) + 1, data,
            existing?.CreatedAt ?? now, existing?.CreatedBy ?? actor, now, actor, existing?.Source, existing?.ExternalId);
        Write(scope, state with
        {
            Revision = state.Revision + 1,
            Units = Upsert(state.Units, record, r => r.Id),
            Audit = Append(state.Audit, "unit", record.Id, existing is null ? "create" : "update", record.Version, now, actor)
        });
        return record;
    }
    private ProductRecord WriteProduct(ModuleStorageScope scope, CatalogState state, ProductSave request, string actor)
    {
        var data = CatalogValidation.NormalizeProduct(request.Data ?? throw new ArgumentException("Hiányzó termékadat."));
        var existing = Find(state.Products, request.Id, request.Version, p => p.Id, p => p.Version);
        EnsureProductLinks(state, data);
        if (existing is not null && existing.Data.UnitCode != data.UnitCode && scope.Transaction is { } transaction
            && CompanyReferenceQueries.HasStockHistory(transaction, existing.Id))
            throw new ArgumentException("Készletmozgással rendelkező termék mértékegysége nem módosítható; külön terméket vagy egységátváltást használj.");
        if (ActiveCodeTaken(state.Products, existing?.Id, p => p.Id, p => p.Data.IsActive, p => p.Data.Sku, data.Sku, data.IsActive))
            throw new ArgumentException("Ezzel a cikkszámmal már létezik aktív termék.");
        var now = time.GetUtcNow();
        var record = new ProductRecord(existing?.Id ?? Guid.NewGuid(), (existing?.Version ?? 0) + 1, data,
            existing?.CreatedAt ?? now, existing?.CreatedBy ?? actor, now, actor, existing?.Source, existing?.ExternalId);
        Write(scope, state with
        {
            Revision = state.Revision + 1,
            Products = Upsert(state.Products, record, r => r.Id),
            Audit = Append(state.Audit, "product", record.Id, existing is null ? "create" : "update", record.Version, now, actor)
        });
        return record;
    }
    private static T? Find<T>(T[] rows, Guid? id, long version, Func<T, Guid> key, Func<T, long> rowVersion) where T : class
    {
        if (id is null)
        {
            if (version != 0) throw new CatalogConflictException("Érvénytelen új verzió.");
            return null;
        }
        var existing = rows.SingleOrDefault(r => key(r) == id) ?? throw new CatalogConflictException("A rekord már nem található.");
        if (rowVersion(existing) != version) throw new CatalogConflictException("A rekord közben módosult. Frissítsd a listát, majd nyisd meg újra.");
        return existing;
    }
    private static bool ActiveCodeTaken<T>(T[] rows, Guid? self, Func<T, Guid> id, Func<T, bool> active, Func<T, string> code, string candidate, bool candidateActive) =>
        candidateActive && rows.Any(r => id(r) != self && active(r) && CatalogValidation.Key(code(r)) == CatalogValidation.Key(candidate));
    private static void RejectCodeChange(string? previous, string next, bool referenced)
    {
        if (previous is not null && referenced && CatalogValidation.Key(previous) != CatalogValidation.Key(next))
            throw new ArgumentException("A kód nem módosítható, amíg másik aktív rekord hivatkozik rá.");
    }
    private static void EnsureParent(CatalogState state, CategoryData data, Guid? self)
    {
        if (data.ParentCode.Length == 0) return;
        var parent = state.Categories.SingleOrDefault(c => c.Id != self && CatalogValidation.Key(c.Data.Code) == data.ParentCode);
        if (parent is null) throw new ArgumentException("A szülőkategória nem található.");
        if (data.IsActive && !parent.Data.IsActive) throw new ArgumentException("Aktív kategória csak aktív szülő alá tehető.");
        var cursor = parent.Data.ParentCode;
        var guard = 0;
        while (cursor.Length > 0)
        {
            if (CatalogValidation.Key(cursor) == data.Code) throw new ArgumentException("A kategória körbe hivatkozna.");
            var next = state.Categories.FirstOrDefault(c => CatalogValidation.Key(c.Data.Code) == CatalogValidation.Key(cursor));
            if (next is null) break;
            cursor = next.Data.ParentCode;
            if (++guard > 50) throw new ArgumentException("A kategória körbe hivatkozna.");
        }
    }
    private static void EnsureProductLinks(CatalogState state, ProductData data)
    {
        var unit = state.Units.SingleOrDefault(u => CatalogValidation.Key(u.Data.Code) == data.UnitCode);
        if (unit is null) throw new ArgumentException("A mértékegység nem található. Előbb vedd fel a mértékegységet.");
        if (data.IsActive && !unit.Data.IsActive) throw new ArgumentException("Aktív termékhez aktív mértékegység kell.");
        if (data.CategoryCode.Length == 0) return;
        var category = state.Categories.SingleOrDefault(c => CatalogValidation.Key(c.Data.Code) == data.CategoryCode);
        if (category is null) throw new ArgumentException("A kategória nem található.");
        if (data.IsActive && !category.Data.IsActive) throw new ArgumentException("Aktív termékhez aktív kategória kell.");
    }
    private static bool ReferencedCategory(CatalogState state, string? code) => code is not null && (
        state.Categories.Any(c => c.Data.IsActive && CatalogValidation.Key(c.Data.ParentCode) == CatalogValidation.Key(code))
        || state.Products.Any(p => p.Data.IsActive && CatalogValidation.Key(p.Data.CategoryCode) == CatalogValidation.Key(code)));
    private static bool ReferencedUnit(CatalogState state, string? code) => code is not null
        && state.Products.Any(p => p.Data.IsActive && CatalogValidation.Key(p.Data.UnitCode) == CatalogValidation.Key(code));
    private static T[] Upsert<T>(T[] rows, T record, Func<T, Guid> id) =>
        rows.Any(r => id(r) == id(record)) ? rows.Select(r => id(r) == id(record) ? record : r).ToArray() : rows.Append(record).ToArray();
    private static CatalogAudit[] Append(CatalogAudit[] audit, string entity, Guid id, string action, long version, DateTimeOffset at, string actor) =>
        audit.Append(new(entity, id, action, version, at, actor)).ToArray();
    private static void Write(ModuleStorageScope scope, CatalogState state)
    {
        var directory = scope.Directory;
        if (state.Products.Length > 50_000 || state.Categories.Length > 10_000 || state.Units.Length > 500 || state.Audit.Length > 200_000)
            throw new IOException("Az első változat adattárkorlátja elérve; archiválási vagy adatbázis-bővítési lépés szükséges.");
        if (scope.Write("catalog", JsonSerializer.Serialize(state, CatalogJson.Options))) return;
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "catalog.json");
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, state, CatalogJson.Options); stream.Flush(true);
            }
            if (File.Exists(file)) File.Replace(temporary, file, file + ".bak");
            else File.Move(temporary, file);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static string ExportCsv<T>(string header, IEnumerable<T> records, Func<T, string?[]> cells)
    {
        static string Cell(string? value)
        {
            value ??= "";
            if (value.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' || value.StartsWith('\t') || value.StartsWith('\r'))
                value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        var csv = new StringBuilder(header).Append("\r\n");
        foreach (var record in records) csv.AppendLine(string.Join(';', cells(record).Select(Cell)));
        return csv.ToString();
    }
}
