using PcImpressz.Contracts;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace PcImpressz.Modules.Partners;

// A small-company, single-Host transactional document store. No external database server is required.
public sealed class PartnerRepository(Func<Guid, string> companyDirectory, TimeProvider time, ICompanyDatabase? database = null)
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    private async Task<T> Locked<T>(Guid company, Func<ModuleStorageScope, PartnerState, T> action, CancellationToken ct)
    {
        var directory = companyDirectory(company); // Also verifies that the company exists.
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
    public Task<PartnerState> Snapshot(Guid company, CancellationToken ct = default) =>
        Locked(company, (_, state) => state, ct);
    private static PartnerState Read(ModuleStorageScope scope)
    {
        var directory = scope.Directory;
        var body = scope.Read("partners", "partners.json");
        var file = Path.Combine(directory, "partners.json");
        if (body is null) return new(1, 0, [], []);
        try
        {
            var state = JsonSerializer.Deserialize<PartnerState>(body, PartnerJson.Options)
                ?? throw new IOException("Üres adattár.");
            var contacts = state.Contacts ?? [];
            var links = state.Links ?? [];
            var activities = state.Activities ?? [];
            if (state.SchemaVersion != 1 || state.Revision < 0 || state.Partners is null || state.Audit is null
                || state.Partners.Any(p => p.Id == Guid.Empty || p.Version < 1 || p.Data is null)
                || state.Partners.Select(p => p.Id).Distinct().Count() != state.Partners.Length
                || contacts.Any(c => c.Id == Guid.Empty || c.Version < 1 || c.Data is null)
                || contacts.Select(c => c.Id).Distinct().Count() != contacts.Length
                || activities.Any(a => a.Id == Guid.Empty || a.Version < 1 || a.Data is null)
                || activities.Select(a => a.Id).Distinct().Count() != activities.Length
                || links.Any(l => l.ContactId == Guid.Empty || l.PartnerId == Guid.Empty))
                throw new IOException("Sérült adattár.");
            return state with { Contacts = contacts, Links = links, Activities = activities };
        }
        catch (JsonException) { throw new IOException("A partneradatok sérültek. A partners.json.bak mentésből állíthatók helyre."); }
    }
    public Task<PartnerRecord> Save(PartnerSave request, string actor, CancellationToken ct = default, string role = "") =>
        Locked(request.CompanyId, (directory, state) =>
        {
            if (request.Data is null) throw new ArgumentException("Hiányzó partneradat.");
            var existing = request.Id is { } id ? state.Partners.SingleOrDefault(p => p.Id == id) : null;
            if (request.Id is not null && existing is null) throw new PartnerConflictException("A partner már nem található.");
            if (existing is not null && existing.Version != request.Version)
                throw new PartnerConflictException("A partner közben módosult. Frissítsd a listát, majd nyisd meg újra.");
            if (existing is null && request.Version != 0) throw new PartnerConflictException("Érvénytelen új partnerverzió.");
            if (existing is not null && !PartnerCrm.VisiblePartner(state, existing, role, actor))
                throw new ArgumentException("Ehhez a céghez nincs hozzáférésed.");
            var forget = request.Data.Forget;
            var data = forget ? PartnerCrm.AnonymizedCompany() : PartnerCrm.Company(state, existing?.Id, request.Data, role, actor);
            var tax = PartnerValidation.TaxKey(data.TaxId);
            if (tax.Length > 0 && state.Partners.Any(p => p.Id != existing?.Id && p.Data.IsActive && data.IsActive
                && PartnerValidation.TaxKey(p.Data.TaxId) == tax))
                throw new ArgumentException("Ezzel az adószámmal már létezik aktív partner.");
            var eu = PartnerValidation.TaxKey(data.EuTaxId);
            if (eu.Length > 0 && state.Partners.Any(p => p.Id != existing?.Id && p.Data.IsActive && data.IsActive
                && PartnerValidation.TaxKey(p.Data.EuTaxId) == eu))
                throw new ArgumentException("Ezzel az EU adószámmal már létezik aktív partner.");
            var now = time.GetUtcNow();
            var record = new PartnerRecord(existing?.Id ?? Guid.NewGuid(), (existing?.Version ?? 0) + 1,
                data, existing?.CreatedAt ?? now, existing?.CreatedBy ?? actor, now, actor, existing?.Source, existing?.ExternalId);
            var records = existing is null ? state.Partners.Append(record).ToArray()
                : state.Partners.Select(p => p.Id == record.Id ? record : p).ToArray();
            records = records.Select(p => p.Data.ParentId == record.Id ? p with { Data = p.Data with { ParentName = record.Data.Name } } : p).ToArray();
            var contacts = state.Contacts ?? [];
            var links = state.Links ?? [];
            if (forget)
            {
                var owned = links.Where(l => l.PartnerId == record.Id).Select(l => l.ContactId).ToHashSet();
                contacts = contacts.Select(c => owned.Contains(c.Id) && links.Count(l => l.ContactId == c.Id) == 1
                    ? c with { Version = c.Version + 1, Data = PartnerCrm.AnonymizedContact(), UpdatedAt = now, UpdatedBy = actor } : c).ToArray();
                links = links.Where(l => l.PartnerId != record.Id).ToArray();
            }
            Write(directory, state with { Revision = state.Revision + 1, Partners = records, Contacts = contacts, Links = links,
                Activities = state.Activities ?? [],
                Audit = state.Audit.Append(new(record.Id, forget ? "forget" : existing is null ? "create" : "update", record.Version, now, actor)).ToArray() });
            return record;
        }, ct);
    public Task<ContactRecord> SaveContact(ContactSave request, string actor, CancellationToken ct = default, string role = "") =>
        Locked(request.CompanyId, (directory, state) =>
        {
            if (request.Data is null) throw new ArgumentException("Hiányzó kapcsolattartó.");
            var contacts = state.Contacts ?? [];
            var existing = request.Id is { } id ? contacts.SingleOrDefault(c => c.Id == id) : null;
            if (request.Id is not null && existing is null) throw new PartnerConflictException("A kapcsolattartó már nem található.");
            if (existing is not null && existing.Version != request.Version)
                throw new PartnerConflictException("A kapcsolattartó közben módosult. Frissítsd a listát, majd nyisd meg újra.");
            if (existing is null && request.Version != 0) throw new PartnerConflictException("Érvénytelen új kapcsolattartó-verzió.");
            if (existing is not null && !PartnerCrm.VisibleContact(state, existing, role, actor))
                throw new ArgumentException("Ehhez a kapcsolattartóhoz nincs hozzáférésed.");
            var now = time.GetUtcNow();
            var contactId = existing?.Id ?? Guid.NewGuid();
            var data = request.Data.Forget ? PartnerCrm.AnonymizedContact() : PartnerCrm.Contact(request.Data);
            var record = new ContactRecord(contactId, (existing?.Version ?? 0) + 1, data,
                existing?.CreatedAt ?? now, existing?.CreatedBy ?? actor, now, actor);
            var kept = (state.Links ?? []).Where(l => l.ContactId != contactId).ToArray();
            var links = request.Data.Forget ? kept : kept.Concat(PartnerCrm.Links(state, contactId, data.Companies)).ToArray();
            var next = existing is null ? contacts.Append(record).ToArray() : contacts.Select(c => c.Id == contactId ? record : c).ToArray();
            Write(directory, state with { Revision = state.Revision + 1, Contacts = next, Links = links, Activities = state.Activities ?? [],
                Audit = state.Audit.Append(new(contactId, request.Data.Forget ? "forget-contact" : existing is null ? "create-contact" : "update-contact", record.Version, now, actor)).ToArray() });
            return record;
        }, ct);
    public Task<ActivityRecord> SaveActivity(ActivitySave request, string actor, CancellationToken ct = default, string role = "") =>
        Locked(request.CompanyId, (directory, state) =>
        {
            if (request.Data is null) throw new ArgumentException("Hiányzó tevékenység.");
            var activities = state.Activities ?? [];
            var existing = request.Id is { } id ? activities.SingleOrDefault(a => a.Id == id) : null;
            if (request.Id is not null && existing is null) throw new PartnerConflictException("A tevékenység már nem található.");
            if (existing is not null && existing.Version != request.Version)
                throw new PartnerConflictException("A tevékenység közben módosult. Frissítsd a listát, majd nyisd meg újra.");
            if (existing is null && request.Version != 0) throw new PartnerConflictException("Érvénytelen új tevékenységverzió.");
            var data = PartnerCrm.Activity(state, request.Data, time.GetUtcNow());
            if (data.PartnerId is Guid partnerId && state.Partners.FirstOrDefault(p => p.Id == partnerId) is { } partner
                && !PartnerCrm.VisiblePartner(state, partner, role, actor))
                throw new ArgumentException("Ehhez a céghez nincs hozzáférésed.");
            var now = time.GetUtcNow();
            var record = new ActivityRecord(existing?.Id ?? Guid.NewGuid(), (existing?.Version ?? 0) + 1, data,
                existing?.CreatedAt ?? now, existing?.CreatedBy ?? actor, now, actor);
            var next = existing is null ? activities.Append(record).ToArray() : activities.Select(a => a.Id == record.Id ? record : a).ToArray();
            Write(directory, state with { Revision = state.Revision + 1, Contacts = state.Contacts ?? [], Links = state.Links ?? [], Activities = next,
                Audit = state.Audit.Append(new(record.Id, existing is null ? "create-activity" : "update-activity", record.Version, now, actor)).ToArray() });
            return record;
        }, ct);
    public async Task<PartnerRecord> Archive(PartnerArchive request, string actor, CancellationToken ct = default, string role = "")
    {
        var snapshot = await Snapshot(request.CompanyId, ct);
        var partner = snapshot.Partners.SingleOrDefault(p => p.Id == request.Id)
            ?? throw new PartnerConflictException("A partner nem található.");
        return await Save(new(request.CompanyId, request.Id, request.Version, partner.Data with { IsActive = false }), actor, ct, role);
    }
    public async Task<ContactRecord> ArchiveContact(PartnerArchive request, string actor, CancellationToken ct = default, string role = "")
    {
        var snapshot = await Snapshot(request.CompanyId, ct);
        var contact = (snapshot.Contacts ?? []).SingleOrDefault(c => c.Id == request.Id)
            ?? throw new PartnerConflictException("A kapcsolattartó nem található.");
        return await SaveContact(new(request.CompanyId, request.Id, request.Version, contact.Data with { IsActive = false }), actor, ct, role);
    }
    public async Task<ActivityRecord> ArchiveActivity(PartnerArchive request, string actor, CancellationToken ct = default, string role = "")
    {
        var snapshot = await Snapshot(request.CompanyId, ct);
        var activity = (snapshot.Activities ?? []).SingleOrDefault(a => a.Id == request.Id)
            ?? throw new PartnerConflictException("A tevékenység nem található.");
        return await SaveActivity(new(request.CompanyId, request.Id, request.Version, activity.Data with { IsActive = false }), actor, ct, role);
    }
    private static void Write(ModuleStorageScope scope, PartnerState state)
    {
        var directory = scope.Directory;
        if (state.Partners.Length + (state.Contacts?.Length ?? 0) + (state.Activities?.Length ?? 0) > 50000 || state.Audit.Length > 200000)
            throw new IOException("Az első változat adattárkorlátja elérve; archiválási vagy adatbázis-bővítési lépés szükséges.");
        if (scope.Write("partners", JsonSerializer.Serialize(state, PartnerJson.Options))) return;
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
    public static string ExportCsv<T>(string header, IEnumerable<T> records, Func<T, string[]> cells)
    {
        static string Cell(string? value)
        {
            value ??= "";
            if (value.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' || value.StartsWith('\t') || value.StartsWith('\r'))
                value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        var csv = new StringBuilder(header + "\r\n");
        foreach (var record in records) csv.AppendLine(string.Join(';', cells(record).Select(Cell)));
        return csv.ToString();
    }
}
