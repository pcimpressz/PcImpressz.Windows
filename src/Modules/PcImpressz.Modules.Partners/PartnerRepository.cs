using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace PcImpressz.Modules.Partners;

// A small-company, single-Host transactional document store. No external database server is required.
public sealed class PartnerRepository(Func<Guid, string> companyDirectory, TimeProvider time)
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    private async Task<T> Locked<T>(Guid company, Func<string, PartnerState, T> action, CancellationToken ct)
    {
        var directory = companyDirectory(company); // Also verifies that the company exists.
        var gate = _locks.GetOrAdd(company, _ => new(1, 1));
        await gate.WaitAsync(ct);
        try { return action(directory, Read(directory)); }
        finally { gate.Release(); }
    }
    public Task<PartnerState> Snapshot(Guid company, CancellationToken ct = default) =>
        Locked(company, (_, state) => state, ct);
    private static PartnerState Read(string directory)
    {
        var file = Path.Combine(directory, "partners.json");
        if (!File.Exists(file)) return new(1, 0, [], []);
        try
        {
            if (new FileInfo(file).Length > 64 * 1024 * 1024) throw new IOException("Az adattár túl nagy.");
            var state = JsonSerializer.Deserialize<PartnerState>(File.ReadAllText(file), PartnerJson.Options)
                ?? throw new IOException("Üres adattár.");
            if (state.SchemaVersion != 1 || state.Revision < 0 || state.Partners is null || state.Audit is null
                || state.Partners.Any(p => p.Id == Guid.Empty || p.Version < 1 || p.Data is null)
                || state.Partners.Select(p => p.Id).Distinct().Count() != state.Partners.Length)
                throw new IOException("Sérült adattár.");
            return state;
        }
        catch (JsonException) { throw new IOException("A partneradatok sérültek. A partners.json.bak mentésből állíthatók helyre."); }
    }
    public Task<PartnerRecord> Save(PartnerSave request, string actor, CancellationToken ct = default) =>
        Locked(request.CompanyId, (directory, state) =>
        {
            var data = PartnerValidation.Normalize(request.Data ?? throw new ArgumentException("Hiányzó partneradat."));
            var existing = request.Id is { } id ? state.Partners.SingleOrDefault(p => p.Id == id) : null;
            if (request.Id is not null && existing is null) throw new PartnerConflictException("A partner már nem található.");
            if (existing is not null && existing.Version != request.Version)
                throw new PartnerConflictException("A partner közben módosult. Frissítsd a listát, majd nyisd meg újra.");
            if (existing is null && request.Version != 0) throw new PartnerConflictException("Érvénytelen új partnerverzió.");
            var tax = PartnerValidation.TaxKey(data.TaxId);
            if (tax.Length > 0 && state.Partners.Any(p => p.Id != existing?.Id && p.Data.IsActive && data.IsActive
                && PartnerValidation.TaxKey(p.Data.TaxId) == tax))
                throw new ArgumentException("Ezzel az adószámmal már létezik aktív partner.");
            var now = time.GetUtcNow();
            var record = new PartnerRecord(existing?.Id ?? Guid.NewGuid(), (existing?.Version ?? 0) + 1,
                data, existing?.CreatedAt ?? now, existing?.CreatedBy ?? actor, now, actor, existing?.Source, existing?.ExternalId);
            var records = existing is null ? state.Partners.Append(record).ToArray()
                : state.Partners.Select(p => p.Id == record.Id ? record : p).ToArray();
            Write(directory, state with { Revision = state.Revision + 1, Partners = records,
                Audit = state.Audit.Append(new(record.Id, existing is null ? "create" : "update", record.Version, now, actor)).ToArray() });
            return record;
        }, ct);
    public async Task<PartnerRecord> Archive(PartnerArchive request, string actor, CancellationToken ct = default)
    {
        var snapshot = await Snapshot(request.CompanyId, ct);
        var partner = snapshot.Partners.SingleOrDefault(p => p.Id == request.Id)
            ?? throw new PartnerConflictException("A partner nem található.");
        return await Save(new(request.CompanyId, request.Id, request.Version, partner.Data with { IsActive = false }), actor, ct);
    }
    public Task<int> Import(Guid company, PartnerRecord[] additions, long expectedRevision, string actor, CancellationToken ct) =>
        Locked(company, (directory, state) =>
        {
            if (state.Revision != expectedRevision)
                throw new PartnerConflictException("Az adatok az előnézet óta változtak. Készíts új importelőnézetet.");
            if (additions.Length == 0) return 0;
            var now = time.GetUtcNow();
            Write(directory, state with { Revision = state.Revision + 1, Partners = state.Partners.Concat(additions).ToArray(),
                Audit = state.Audit.Concat(additions.Select(p => new PartnerAudit(p.Id, "import", 1, now, actor))).ToArray() });
            return additions.Length;
        }, ct);
    private static void Write(string directory, PartnerState state)
    {
        if (state.Partners.Length > 50000 || state.Audit.Length > 200000)
            throw new IOException("Az első változat adattárkorlátja elérve; archiválási vagy adatbázis-bővítési lépés szükséges.");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "partners.json");
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, state, PartnerJson.Options); stream.Flush(true);
            }
            if (File.Exists(file)) File.Replace(temporary, file, file + ".bak");
            else File.Move(temporary, file);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static string ExportCsv(IEnumerable<PartnerRecord> records)
    {
        static string Cell(string? value)
        {
            value ??= "";
            if (value.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' || value.StartsWith('\t') || value.StartsWith('\r'))
                value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        var csv = new StringBuilder("Név;Adószám;E-mail;Telefon;Ország;Irányítószám;Település;Cím;Vevő;Szállító;Gyártó;Aktív;Megjegyzés\r\n");
        foreach (var partner in records)
        {
            var p = partner.Data;
            csv.AppendLine(string.Join(';', new[] { p.Name, p.TaxId, p.Email, p.Phone, p.Country, p.PostalCode, p.City,
                p.Address, p.IsCustomer ? "Igen" : "Nem", p.IsSupplier ? "Igen" : "Nem", p.IsManufacturer ? "Igen" : "Nem",
                p.IsActive ? "Igen" : "Nem", p.Notes }.Select(Cell)));
        }
        return csv.ToString();
    }
}
