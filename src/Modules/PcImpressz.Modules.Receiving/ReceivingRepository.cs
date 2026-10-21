using PcImpressz.Business;
using PcImpressz.Contracts;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace PcImpressz.Modules.Receiving;

public sealed class ReceivingRepository(Func<Guid, string> companyDirectory, TimeProvider time, ICompanyDatabase? database = null)
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    private async Task<T> Locked<T>(Guid company, Func<ModuleStorageScope, ReceiptState, T> action, CancellationToken ct)
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
    public Task<ReceiptState> Snapshot(Guid company, CancellationToken ct = default) => Locked(company, (_, state) => state, ct);
    public Task<ReceiptRecord> Save(ReceiptSave request, string actor, CancellationToken ct = default) =>
        Locked(request.CompanyId, (directory, state) => WriteManual(directory, state, request, actor), ct);
    public Task<ReceiptRecord> Classify(ReceiptClassify request, string actor, CancellationToken ct = default) =>
        Locked(request.CompanyId, (directory, state) => WriteClassification(directory, state, request, actor), ct);
    public Task<ReceiptRecord> Import(ReceiptImport request, ImportedDocument document, string actor, CancellationToken ct = default) =>
        Locked(request.CompanyId, (directory, state) => WriteImport(directory, state, request, document, actor), ct);
    public Task<ReceiptRecord> Supplement(ReceiptSupplement request, ImportedDocument document, string actor, CancellationToken ct = default) =>
        Locked(request.CompanyId, (scope, state) => WriteSupplement(scope, state, request, document, actor), ct);
    public Task<bool> KnowsSupplier(Guid company, string taxId, CancellationToken ct = default) =>
        Locked(company, (scope, _) => Supplier(MasterIndex.Load(scope.Directory, scope.Transaction), null, taxId) is not null, ct);
    public Task<string?> ReadPartnersAsync(Guid company, CancellationToken ct = default) =>
        Locked(company, (scope, _) => SupplierXmlLinks.ReadPartners(scope.Directory, scope.Transaction), ct);

    public Task<ReceiptRecord> Reverse(ReceiptReverse request, string actor, CancellationToken ct = default) =>
        Locked(request.CompanyId, (scope, state) =>
        {
            var reason = Text(request.Reason, 200, "Visszafordítás oka");
            if (reason.Length < 3) throw new ArgumentException("A visszafordítás okát legalább 3 karakterrel add meg.");
            var existing = state.Receipts.SingleOrDefault(r => r.Id == request.Id) ?? throw new ReceivingConflictException("A számla nem található.");
            if (existing.Data.Status == "visszaforditva") return existing;
            if (existing.Version != request.Version) throw new ReceivingConflictException("A számla közben változott; frissítsd a listát.");
            if (existing.Data.Status != "bevetelezve" || !existing.Data.StockPosted || scope.Transaction is null)
                throw new ArgumentException("Csak a ténylegesen készletre könyvelt bevételezés fordítható vissza.");
            var original = scope.Transaction.Read<StockOperation>("inventory", "operation/receipt:" + existing.Id.ToString("N"))
                ?? throw new IOException("A bevételezés készletművelete hiányzik; ellenőrzés szükséges.");
            StockLedger.Post(scope.Transaction, "receipt-reverse:" + existing.Id.ToString("N"), "receiving", existing.Id,
                original.Movements.Select(m => new StockChange(m.ProductId, m.ProductName, m.UnitCode, m.Warehouse, -m.Quantity)).ToArray(),
                "Bevételezés visszafordítása " + existing.Data.Number + ": " + reason, actor, time.GetUtcNow());
            return Store(scope, state, existing, existing.Data with { Status = "visszaforditva", StockPosted = false,
                ReversedAt = time.GetUtcNow(), ReversedBy = actor, ReversalReason = reason }, actor, "reverse");
        }, ct);

    private ReceiptRecord WriteManual(ModuleStorageScope scope, ReceiptState state, ReceiptSave request, string actor)
    {
        var draft = request.Draft ?? throw new ArgumentException("Hiányzó számlaadat.");
        var existing = Find(state, request.Id, request.Version);
        if (existing is not null && existing.Data.Status is not ("uj" or "fuggo"))
            throw new ArgumentException("A lezárt számla nem módosítható.");
        var index = MasterIndex.Load(scope.Directory, scope.Transaction);
        var supplier = Supplier(index, draft.SupplierId, null)
            ?? throw new ArgumentException("A szállító nem található az aktív beszállítók között. Vedd fel a partnertörzsben, vagy hozd létre innen.");
        var data = Compose(draft.Number, supplier, draft.IssuedOn, draft.DueOn, draft.Notes, draft.Warehouse,
            existing?.Data.Source ?? "manual", existing?.Data.Status ?? "uj", existing?.Data.ReceivedAt,
            ReceivingMatching.Build(draft.Lines ?? throw new ArgumentException("Hiányzó tételek."), index.Products));
        data = data with { SourceNet=existing?.Data.SourceNet, SourceVat=existing?.Data.SourceVat, SourceGross=existing?.Data.SourceGross };
        RejectDuplicate(state, existing?.Id, data);
        return Store(scope, state, existing, data, actor, existing is null ? "create" : "update");
    }
    private ReceiptRecord WriteImport(ModuleStorageScope scope, ReceiptState state, ReceiptImport request, ImportedDocument document, string actor)
    {
        var source = request.Source is "xls" or "nav" ? request.Source : throw new ArgumentException("Ismeretlen importforrás.");
        if (document.Currency != "HUF") throw new ArgumentException("Most csak HUF számla importálható; a devizás összeget nem számítjuk át automatikusan.");
        var index = MasterIndex.Load(scope.Directory, scope.Transaction);
        var supplier = Supplier(index, request.SupplierId, document.SupplierTaxId)
            ?? throw new ReceivingUnknownSupplierException(string.IsNullOrWhiteSpace(document.SupplierName)
                ? "Válassz szállítót a partnertörzs beszállítói közül."
                : "Nincs ilyen beszállító a partnertörzsben: " + document.SupplierName.Trim() + ". Vedd fel kézzel, majd olvasd be újra.");
        if (!string.IsNullOrWhiteSpace(document.SupplierTaxId) && ReceivingText.TaxKey(supplier.TaxId) != ReceivingText.TaxKey(document.SupplierTaxId))
            throw new ArgumentException("A kiválasztott szállító adószáma eltér az importált számlától.");
        var number = string.IsNullOrWhiteSpace(request.Number) ? document.Number : request.Number;
        var issued = string.IsNullOrWhiteSpace(request.IssuedOn) ? document.IssuedOn : request.IssuedOn;
        if (string.IsNullOrWhiteSpace(issued)) issued = time.GetUtcNow().ToString("yyyy-MM-dd");
        var due = string.IsNullOrWhiteSpace(request.DueOn) ? document.DueOn : request.DueOn;
        var lines = ReceivingMatching.Build(document.Lines.Select(line => new ReceiptLineDraft(null, null, line.Name, line.Sku, line.Barcode, line.UnitCode,
            line.Quantity, line.NetUnitPrice, line.VatRate ?? throw new ArgumentException("Hiányzó áfakulcs: add meg az importban."), line.IsService)).ToArray(), index.Products);
        var data = Compose(number, supplier, issued, due, request.Notes, "Bolt", source, "uj", null, lines);
        data = data with { SourceNet=document.ExpectedNet, SourceVat=document.ExpectedVat, SourceGross=document.ExpectedGross };
        CheckSourceTotals(data);
        RejectDuplicate(state, null, data);
        return Store(scope, state, null, data, actor, "import");
    }
    private ReceiptRecord WriteSupplement(ModuleStorageScope scope, ReceiptState state, ReceiptSupplement request, ImportedDocument document, string actor)
    {
        var existing = Find(state, request.Id, request.Version) ?? throw new ReceivingConflictException("A számla nem található.");
        if (existing.Data.Status is not ("uj" or "fuggo"))
            throw new ArgumentException("A lezárt számla adatai nem pótolhatók.");
        if (document.Currency != "HUF") throw new ArgumentException("Most csak HUF számla adata pótolható.");
        if (!string.IsNullOrWhiteSpace(document.SupplierTaxId) && ReceivingText.TaxKey(existing.Data.SupplierTaxId) != ReceivingText.TaxKey(document.SupplierTaxId))
            throw new ArgumentException("A pótló fájl adószáma nem ehhez a szállítóhoz tartozik.");
        if (!string.IsNullOrWhiteSpace(document.Number) && !document.Number.Trim().Equals(existing.Data.Number, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A pótló fájl számlaszáma nem egyezik a megnyitott számlával.");
        var changed = false;
        var due = existing.Data.DueOn;
        if (due.Length == 0 && !string.IsNullOrWhiteSpace(document.DueOn)) { due = document.DueOn; changed = true; }
        var index = MasterIndex.Load(scope.Directory, scope.Transaction);
        ReceiptLine[] lines;
        if (existing.Data.Lines.Length == 0)
        {
            lines = ReceivingMatching.Build(document.Lines.Select(line => new ReceiptLineDraft(null, null, line.Name, line.Sku, line.Barcode, line.UnitCode,
                line.Quantity, line.NetUnitPrice, line.VatRate ?? throw new ArgumentException("Hiányzó áfakulcs a pótló fájlban."), line.IsService)).ToArray(), index.Products);
            changed = true;
        }
        else
        {
            var drafts = existing.Data.Lines.Select(line =>
            {
                var extra = document.Lines.FirstOrDefault(candidate => ReceivingText.Compact(candidate.Name) == ReceivingText.Compact(line.Name));
                var sku = line.Sku;
                var barcode = line.Barcode;
                if (extra is not null && sku.Length == 0 && extra.Sku.Length > 0) { sku = extra.Sku; changed = true; }
                if (extra is not null && barcode.Length == 0 && extra.Barcode.Length > 0) { barcode = extra.Barcode; changed = true; }
                return new ReceiptLineDraft(line.Id, barcode != line.Barcode || sku != line.Sku ? null : line.ProductId, line.Name, sku, barcode, line.UnitCode,
                    line.Quantity, line.NetUnitPrice, line.VatRate, line.Match == "szolgaltatas");
            }).ToArray();
            lines = ReceivingMatching.Build(drafts, index.Products);
        }
        if (!changed) throw new ArgumentException("A fájl nem tartalmazott új, hiányzó adatot ehhez a számlához.");
        var (net, vat, gross) = ReceivingMatching.Totals(lines);
        var sourceName = string.IsNullOrWhiteSpace(request.FileName) ? "fájl" : Path.GetFileName(request.FileName);
        var data = existing.Data with
        {
            DueOn = ReceivingText.Date(due, "A fizetési határidő", false),
            Lines = lines, Net = net, Vat = vat, Gross = gross,
            Notes = JoinNote(existing.Data.Notes, "Hiányzó adatok pótolva: " + sourceName),
            SourceNet = existing.Data.SourceNet ?? document.ExpectedNet,
            SourceVat = existing.Data.SourceVat ?? document.ExpectedVat,
            SourceGross = existing.Data.SourceGross ?? document.ExpectedGross
        };
        CheckSourceTotals(data);
        return Store(scope, state, existing, data, actor, "supplement");
    }
    private static string JoinNote(string existing, string extra)
    {
        var text = string.IsNullOrWhiteSpace(existing) ? extra : existing.TrimEnd() + "\n" + extra;
        return text.Length <= 4000 ? text : text[^4000..];
    }
    private ReceiptRecord WriteClassification(ModuleStorageScope scope, ReceiptState state, ReceiptClassify request, string actor)
    {
        var existing = Find(state, request.Id, request.Version) ?? throw new ReceivingConflictException("A számla nem található.");
        var next = request.Status ?? "";
        if (next is not ("bevetelezve" or "koltseg" or "fuggo" or "uj"))
            throw new ArgumentException("A besorolás csak bevételezés, költség, függő vagy új lehet.");
        if (existing.Data.Status is not ("uj" or "fuggo"))
            throw new ArgumentException("A lezárt számla besorolása nem változik.");
        if (next == existing.Data.Status) throw new ArgumentException("A számla már ebben az állapotban van.");
        if (next == "uj" && existing.Data.Status != "fuggo") throw new ArgumentException("Új állapotba csak a függő számla tehető vissza.");
        if (next == "bevetelezve")
        {
            CheckSourceTotals(existing.Data);
            ReceivingMatching.EnsureReceivable(existing.Data.Lines);
            if (scope.Transaction is { } transaction)
            {
                var products = CompanyReferenceQueries.Products(transaction);
                foreach (var line in existing.Data.Lines.Where(l => l.ProductId is not null))
                    if (!products.Any(p => p.Id == line.ProductId && p.IsActive && p.UnitCode == line.UnitCode))
                        throw new ArgumentException("A termék vagy a mértékegysége közben megváltozott; ellenőrizd a számlát.");
                var changes = existing.Data.Lines.Where(l => l.ProductId is not null)
                    .Select(l => new StockChange(l.ProductId!.Value,l.Name,l.UnitCode,existing.Data.Warehouse,l.Quantity)).ToArray();
                StockLedger.Post(transaction,"receipt:"+existing.Id.ToString("N"),"receiving",existing.Id,changes,
                    "Bevételezés "+existing.Data.Number,actor,time.GetUtcNow());
            }
        }
        var received = next == "bevetelezve" ? time.GetUtcNow() : (DateTimeOffset?)null;
        var data = existing.Data with { Status = next, ReceivedAt = received, StockPosted = next == "bevetelezve" && scope.Transaction is not null };
        return Store(scope, state, existing, data, actor, "classify");
    }
    private static void CheckSourceTotals(ReceiptData data)
    {
        if ((data.SourceNet is { } net && net != data.Net) || (data.SourceVat is { } vat && vat != data.Vat) || (data.SourceGross is { } gross && gross != data.Gross))
            throw new ArgumentException("Az importált számla eredeti összege és a tételsorok számított összege eltér. Ellenőrizd az áfát, mennyiséget, egységárat és kerekítést; a számla nem véglegesíthető.");
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
            throw new ReceivingDuplicateException("Ezzel a számlaszámmal ennél a szállítónál már van bevételezés.");
    }
    private ReceiptRecord Store(ModuleStorageScope scope, ReceiptState state, ReceiptRecord? existing, ReceiptData data, string actor, string action)
    {
        var now = time.GetUtcNow();
        var record = new ReceiptRecord(existing?.Id ?? Guid.NewGuid(), (existing?.Version ?? 0) + 1, data,
            existing?.CreatedAt ?? now, existing?.CreatedBy ?? actor, now, actor);
        var receipts = existing is null ? state.Receipts.Append(record).ToArray()
            : state.Receipts.Select(receipt => receipt.Id == record.Id ? record : receipt).ToArray();
        Write(scope, state with { Revision = state.Revision + 1, Receipts = receipts, Audit = state.Audit.Append(new ReceiptAudit(record.Id, action, record.Version, now, actor)).ToArray() });
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
    private static ReceiptState Read(ModuleStorageScope scope)
    {
        var directory = scope.Directory;
        var body = scope.Read("receiving", "receipts.json");
        var file = Path.Combine(directory, "receipts.json");
        if (body is null) return new(1, 0, [], []);
        try
        {
            var state = JsonSerializer.Deserialize<ReceiptState>(body, ReceivingJson.Options) ?? throw new IOException("Üres adattár.");
            if (state.SchemaVersion != 1 || state.Revision < 0 || state.Receipts is null || state.Audit is null
                || state.Receipts.Any(receipt => receipt.Id == Guid.Empty || receipt.Version < 1 || receipt.Data is null || receipt.Data.Lines is null)
                || state.Receipts.Select(receipt => receipt.Id).Distinct().Count() != state.Receipts.Length)
                throw new IOException("Sérült adattár.");
            return state;
        }
        catch (JsonException) { throw new IOException("A bevételezés sérült. A receipts.json.bak mentésből állítható helyre."); }
    }
    private static void Write(ModuleStorageScope scope, ReceiptState state)
    {
        var directory = scope.Directory;
        if (state.Receipts.Length > 20_000 || state.Audit.Length > 200_000)
            throw new IOException("Az első változat adattárkorlátja elérve.");
        if (scope.Write("receiving", JsonSerializer.Serialize(state, ReceivingJson.Options))) return;
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "receipts.json");
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, state, ReceivingJson.Options); stream.Flush(true);
            }
            if (new FileInfo(temporary).Length > 64 * 1024 * 1024) throw new IOException("Az adattár túl nagy; a korábbi mentés változatlan maradt.");
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
