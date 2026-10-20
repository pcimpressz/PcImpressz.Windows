using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace PcImpressz.Modules.Receiving;

public sealed class ReceivingRepository(Func<Guid, string> companyDirectory, TimeProvider time)
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    private async Task<T> Locked<T>(Guid company, Func<string, ReceiptState, T> action, CancellationToken ct)
    {
        var directory = companyDirectory(company);
        var gate = _locks.GetOrAdd(company, _ => new(1, 1));
        await gate.WaitAsync(ct);
        try { return action(directory, Read(directory)); }
        finally { gate.Release(); }
    }
    public Task<ReceiptState> Snapshot(Guid company, CancellationToken ct = default) => Locked(company, (_, state) => state, ct);
    public Task<ReceiptRecord> Save(ReceiptSave request, string actor, CancellationToken ct = default) =>
        Locked(request.CompanyId, (directory, state) => WriteManual(directory, state, request, actor), ct);
    public Task<ReceiptRecord> Classify(ReceiptClassify request, string actor, CancellationToken ct = default) =>
        Locked(request.CompanyId, (directory, state) => WriteClassification(directory, state, request, actor), ct);
    public Task<ReceiptRecord> Import(ReceiptImport request, ImportedDocument document, string actor, CancellationToken ct = default) =>
        Locked(request.CompanyId, (directory, state) => WriteImport(directory, state, request, document, actor), ct);

    private ReceiptRecord WriteManual(string directory, ReceiptState state, ReceiptSave request, string actor)
    {
        var draft = request.Draft ?? throw new ArgumentException("Hiányzó számlaadat.");
        var existing = Find(state, request.Id, request.Version);
        if (existing is not null && existing.Data.Status is not ("uj" or "fuggo"))
            throw new ArgumentException("A lezárt számla nem módosítható.");
        var index = MasterIndex.Load(directory);
        var supplier = Supplier(index, draft.SupplierId, null)
            ?? throw new ArgumentException("A szállító nem található az aktív beszállítók között. Vedd fel a partnertörzsben, vagy hozd létre innen.");
        var data = Compose(draft.Number, supplier, draft.IssuedOn, draft.DueOn, draft.Notes, draft.Warehouse,
            existing?.Data.Source ?? "manual", existing?.Data.Status ?? "uj", existing?.Data.ReceivedAt,
            ReceivingMatching.Build(draft.Lines ?? throw new ArgumentException("Hiányzó tételek."), index.Products));
        RejectDuplicate(state, existing?.Id, data);
        return Store(directory, state, existing, data, actor, existing is null ? "create" : "update");
    }
    private ReceiptRecord WriteImport(string directory, ReceiptState state, ReceiptImport request, ImportedDocument document, string actor)
    {
        var source = request.Source is "xls" or "nav" ? request.Source : throw new ArgumentException("Ismeretlen importforrás.");
        var index = MasterIndex.Load(directory);
        var supplier = Supplier(index, request.SupplierId, document.SupplierTaxId)
            ?? throw new ArgumentException(string.IsNullOrWhiteSpace(document.SupplierName)
                ? "Válassz szállítót a partnertörzs beszállítói közül."
                : "Nincs ilyen beszállító a partnertörzsben: " + document.SupplierName.Trim() + ". Vedd fel kézzel, majd olvasd be újra.");
        var number = string.IsNullOrWhiteSpace(request.Number) ? document.Number : request.Number;
        var issued = string.IsNullOrWhiteSpace(request.IssuedOn) ? document.IssuedOn : request.IssuedOn;
        if (string.IsNullOrWhiteSpace(issued)) issued = time.GetUtcNow().ToString("yyyy-MM-dd");
        var due = string.IsNullOrWhiteSpace(request.DueOn) ? document.DueOn : request.DueOn;
        var lines = ReceivingMatching.Build(document.Lines.Select(line => new ReceiptLineDraft(null, null, line.Name, line.Sku, line.Barcode, "DB",
            line.Quantity, line.NetUnitPrice, 27)).ToArray(), index.Products);
        var data = Compose(number, supplier, issued, due, "", "Bolt", source, "uj", null, lines);
        RejectDuplicate(state, null, data);
        return Store(directory, state, null, data, actor, "import");
    }
    private ReceiptRecord WriteClassification(string directory, ReceiptState state, ReceiptClassify request, string actor)
    {
        var existing = Find(state, request.Id, request.Version) ?? throw new ReceivingConflictException("A számla nem található.");
        var next = request.Status ?? "";
        if (next is not ("bevetelezve" or "koltseg" or "fuggo" or "uj"))
            throw new ArgumentException("A besorolás csak bevételezés, költség, függő vagy új lehet.");
        if (existing.Data.Status is not ("uj" or "fuggo"))
            throw new ArgumentException("A lezárt számla besorolása nem változik.");
        if (next == existing.Data.Status) throw new ArgumentException("A számla már ebben az állapotban van.");
        if (next == "uj" && existing.Data.Status != "fuggo") throw new ArgumentException("Új állapotba csak a függő számla tehető vissza.");
        if (next == "bevetelezve") ReceivingMatching.EnsureReceivable(existing.Data.Lines);
        var received = next == "bevetelezve" ? time.GetUtcNow() : (DateTimeOffset?)null;
        var data = existing.Data with { Status = next, ReceivedAt = received };
        return Store(directory, state, existing, data, actor, "classify");
    }
    private static ReceiptData Compose(string? number, SupplierCard supplier, string? issued, string? due, string? notes, string? warehouse,
        string source, string status, DateTimeOffset? received, ReceiptLine[] lines)
    {
        var invoice = Text(number, 80, "Számlaszám");
        if (invoice.Length < 1) throw new ArgumentException("A számlaszám kötelező.");
        var (net, vat, gross) = ReceivingMatching.Totals(lines);
        return new ReceiptData
        {
            Number = invoice, SupplierId = supplier.Id, SupplierName = supplier.Name, SupplierTaxId = supplier.TaxId,
            IssuedOn = ReceivingText.Date(issued, "A számla kelte", true), DueOn = ReceivingText.Date(due, "A fizetési határidő", false),
            Source = source, Status = status, Notes = Text(notes, 4000, "Megjegyzés"),
            Warehouse = Warehouse(warehouse), Net = net, Vat = vat, Gross = gross, ReceivedAt = received, Lines = lines
        };
    }
    private static SupplierCard? Supplier(MasterIndex index, Guid? id, string? taxId)
    {
        var active = index.Suppliers.Where(card => card.IsActive && card.IsSupplier).ToArray();
        if (id is Guid supplierId && supplierId != Guid.Empty)
            return active.SingleOrDefault(card => card.Id == supplierId);
        var key = ReceivingText.TaxKey(taxId);
        if (key.Length == 0) return null;
        var matches = active.Where(card => ReceivingText.TaxKey(card.TaxId) == key).ToArray();
        if (matches.Length > 1) throw new ArgumentException("Több beszállítónak ugyanaz az adószáma. Válaszd ki kézzel.");
        return matches.SingleOrDefault();
    }
    private static void RejectDuplicate(ReceiptState state, Guid? self, ReceiptData data)
    {
        if (state.Receipts.Any(receipt => receipt.Id != self
            && receipt.Data.SupplierId == data.SupplierId
            && string.Equals(receipt.Data.Number, data.Number, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Ezzel a számlaszámmal ennél a szállítónál már van bevételezés.");
    }
    private ReceiptRecord Store(string directory, ReceiptState state, ReceiptRecord? existing, ReceiptData data, string actor, string action)
    {
        var now = time.GetUtcNow();
        var record = new ReceiptRecord(existing?.Id ?? Guid.NewGuid(), (existing?.Version ?? 0) + 1, data,
            existing?.CreatedAt ?? now, existing?.CreatedBy ?? actor, now, actor);
        var receipts = existing is null ? state.Receipts.Append(record).ToArray()
            : state.Receipts.Select(receipt => receipt.Id == record.Id ? record : receipt).ToArray();
        Write(directory, state with { Revision = state.Revision + 1, Receipts = receipts, Audit = state.Audit.Append(new ReceiptAudit(record.Id, action, record.Version, now, actor)).ToArray() });
        return record;
    }
    private static ReceiptRecord? Find(ReceiptState state, Guid? id, long version)
    {
        if (id is null)
        {
            if (version != 0) throw new ReceivingConflictException("Érvénytelen új verzió.");
            return null;
        }
        var existing = state.Receipts.SingleOrDefault(receipt => receipt.Id == id) ?? throw new ReceivingConflictException("A számla nem található.");
        if (existing.Version != version) throw new ReceivingConflictException("A számla közben módosult. Frissítsd a listát, majd nyisd meg újra.");
        return existing;
    }
    private static string Text(string? value, int maximum, string label)
    {
        value = (value ?? "").Trim();
        if (value.Length > maximum || value.Any(character => char.IsControl(character) && character is not '\n' and not '\r' and not '\t'))
            throw new ArgumentException(label + ": érvénytelen vagy túl hosszú érték.");
        return value;
    }
    private static string Warehouse(string? value)
    {
        var text = Text(value, 80, "Raktár");
        return text.Length == 0 ? "Bolt" : text;
    }
    private static ReceiptState Read(string directory)
    {
        var file = Path.Combine(directory, "receipts.json");
        if (!File.Exists(file)) return new(1, 0, [], []);
        try
        {
            if (new FileInfo(file).Length > 64 * 1024 * 1024) throw new IOException("Az adattár túl nagy.");
            var state = JsonSerializer.Deserialize<ReceiptState>(File.ReadAllText(file), ReceivingJson.Options) ?? throw new IOException("Üres adattár.");
            if (state.SchemaVersion != 1 || state.Revision < 0 || state.Receipts is null || state.Audit is null
                || state.Receipts.Any(receipt => receipt.Id == Guid.Empty || receipt.Version < 1 || receipt.Data is null || receipt.Data.Lines is null)
                || state.Receipts.Select(receipt => receipt.Id).Distinct().Count() != state.Receipts.Length)
                throw new IOException("Sérült adattár.");
            return state;
        }
        catch (JsonException) { throw new IOException("A bevételezés sérült. A receipts.json.bak mentésből állítható helyre."); }
    }
    private static void Write(string directory, ReceiptState state)
    {
        if (state.Receipts.Length > 20_000 || state.Audit.Length > 200_000)
            throw new IOException("Az első változat adattárkorlátja elérve.");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "receipts.json");
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, state, ReceivingJson.Options); stream.Flush(true);
            }
            if (File.Exists(file)) File.Replace(temporary, file, file + ".bak");
            else File.Move(temporary, file);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static string ExportCsv(IEnumerable<ReceiptRecord> receipts)
    {
        static string Cell(string? value)
        {
            value ??= "";
            if (value.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' || value.StartsWith('\t') || value.StartsWith('\r'))
                value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        var csv = new StringBuilder("Számlaszám;Szállító;Adószám;Kelt;Forrás;Állapot;Nettó;Bruttó;Tételszám\r\n");
        foreach (var receipt in receipts)
            csv.AppendLine(string.Join(';', new[] { receipt.Data.Number, receipt.Data.SupplierName, receipt.Data.SupplierTaxId, receipt.Data.IssuedOn,
                ReceivingText.SourceLabel(receipt.Data.Source), ReceivingText.StatusLabel(receipt.Data.Status),
                receipt.Data.Net.ToString(System.Globalization.CultureInfo.InvariantCulture),
                receipt.Data.Gross.ToString(System.Globalization.CultureInfo.InvariantCulture), receipt.Data.Lines.Length.ToString() }.Select(Cell)));
        return csv.ToString();
    }
}
