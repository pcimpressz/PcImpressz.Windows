using System.Text.Json;
using PcImpressz.Contracts;

namespace PcImpressz.Modules.Receiving;

public sealed class ReceivingModule : IModule
{
    public ValueTask StartAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var repository = new ReceivingRepository(context.GetCompanyDataDirectory, context.TimeProvider);
        Register(context, "list", "receiving.read", CommandAccess.RecoveryReadOnly, async (input, _, ct) =>
        {
            var query = ReadQuery(input);
            var receipts = (await repository.Snapshot(query.CompanyId, ct)).Receipts;
            var counts = new
            {
                uj = receipts.Count(receipt => receipt.Data.Status == "uj"),
                bevetelezve = receipts.Count(receipt => receipt.Data.Status == "bevetelezve"),
                koltseg = receipts.Count(receipt => receipt.Data.Status == "koltseg"),
                fuggo = receipts.Count(receipt => receipt.Data.Status == "fuggo"),
                mind = receipts.Length
            };
            var matching = Filter(receipts, query);
            return ReceivingJson.Element(new { rows = matching.Skip(Skip(query)).Take(query.PageSize).Select(Summary), total = matching.Length, counts });
        });
        Register(context, "get", "receiving.read", CommandAccess.RecoveryReadOnly, async (input, _, ct) =>
        {
            var query = ReceivingJson.Read<ReceiptGet>(input);
            var receipt = (await repository.Snapshot(query.CompanyId, ct)).Receipts.SingleOrDefault(item => item.Id == query.Id)
                ?? throw new ReceivingConflictException("A számla nem található.");
            return Detail(receipt);
        });
        Register(context, "save", "receiving.write", CommandAccess.Licensed, async (input, actor, ct) =>
            Detail(await repository.Save(ReceivingJson.Read<ReceiptSave>(input), actor, ct)));
        Register(context, "classify", "receiving.write", CommandAccess.Licensed, async (input, actor, ct) =>
            Detail(await repository.Classify(ReceivingJson.Read<ReceiptClassify>(input), actor, ct)));
        Register(context, "import", "receiving.write", CommandAccess.Licensed, async (input, actor, ct) =>
        {
            var request = ReceivingJson.Read<ReceiptImport>(input);
            var extension = Path.GetExtension(request.FileName ?? "").ToLowerInvariant();
            if (request.Source == "pdf" || extension == ".pdf")
                throw new ArgumentException("A PDF-ből a tételsorok kiolvasása a következő kör. Most Excel (xlsx), CSV vagy NAV-számla XML olvasható.");
            request = request with { Source = extension == ".xml" || request.Source == "nav" ? "nav" : "xls" };
            return Detail(await repository.Import(request, ReceivingImport.Read(request.FileName, request.ContentBase64), actor, ct));
        });
        Register(context, "export", "receiving.export", CommandAccess.RecoveryReadOnly, async (input, _, ct) =>
        {
            var query = ReceivingJson.Read<ReceiptQuery>(input);
            var rows = Filter((await repository.Snapshot(query.CompanyId, ct)).Receipts, query);
            return ReceivingJson.Element(new { csv = ReceivingRepository.ExportCsv(rows) });
        });
        Register(context, "archive", "receiving.write", CommandAccess.Licensed, (_, _, _) =>
            Task.FromException<JsonElement>(new ArgumentException("A számlát nem archiváljuk. A besorolás dönti el, hogy bevételezés, költség vagy függő.")));
        context.RegisterPage(new("receiving", context.ModuleId, "Bevételezés",
            "Kézi számla, NAV-besorolás vagy Excel/CSV/XML import. A szállító a partnertörzsből, a termék a terméktörzsből jön.",
            "\uE7BF",
            [new("number", "Számlaszám", Required: true), new("supplierName", "Szállító", Required: true),
             new("issuedOn", "Kelt"), new("notes", "Megjegyzés", "multiline")],
            [new("number", "Számlaszám", 160), new("supplierName", "Szállító", 220), new("issuedOn", "Kelt", 120), new("statusLabel", "Állapot", 180)],
            new("list", "save", "archive", "export"),
            new EntityPageLabels
            {
                NewLabel = "Kézi számla", NewTitle = "Kézi bevételezés", EditTitle = "Számla", CountNoun = "számla",
                EmptyMessage = "Ebben a nézetben még nincs számla.",
                SavedMessage = "A számla mentése sikerült.", NeedSelection = "Először válassz számlát.",
                ArchiveQuestion = "A számlát a besorolás zárja le.", ArchiveTitle = "Bevételezés",
                ArchivedMessage = "A besorolás megmaradt.", ExportFileName = "bevetelezes.csv",
                ExportedMessage = "A szűrt számlalista exportálva.",
                SearchHint = "Keresés számlaszám vagy szállító alapján"
            },
            [new("uj", "Új, besorolásra vár"), new("bevetelezve", "Bevételezve"), new("koltseg", "Költség"), new("fuggo", "Függő"), new("mind", "Minden számla")]));
        return ValueTask.CompletedTask;
    }
    public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    private static void Register(IModuleContext context, string name, string permission, CommandAccess access,
        Func<JsonElement, string, CancellationToken, Task<JsonElement>> execute) =>
        context.RegisterCommand(new Handler(name, permission, access, execute));
    private static ReceiptQuery ReadQuery(JsonElement input)
    {
        var query = ReceivingJson.Read<ReceiptQuery>(input);
        if (query.Page < 0 || query.PageSize is < 1 or > 200 || query.Search.Length > 200)
            throw new ArgumentException("Érvénytelen listázási kérés.");
        if (query.Filter is not ("uj" or "bevetelezve" or "koltseg" or "fuggo" or "mind"))
            throw new ArgumentException("Ismeretlen számlaszűrő.");
        return query;
    }
    private static int Skip(ReceiptQuery query) => query.Page * (long)query.PageSize > int.MaxValue ? int.MaxValue : query.Page * query.PageSize;
    private static ReceiptRecord[] Filter(IEnumerable<ReceiptRecord> source, ReceiptQuery query) =>
        source.Where(receipt => query.Filter == "mind" || receipt.Data.Status == query.Filter)
        .Where(receipt => string.IsNullOrWhiteSpace(query.Search) || string.Join(" ", receipt.Data.Number, receipt.Data.SupplierName, receipt.Data.SupplierTaxId)
            .Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(receipt => receipt.Data.IssuedOn, StringComparer.Ordinal)
        .ThenBy(receipt => receipt.Data.SupplierName, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("hu-HU"), true))
        .ToArray();
    private static JsonElement Summary(ReceiptRecord receipt)
    {
        var node = JsonSerializer.SerializeToNode(new
        {
            receipt.Data.Number, receipt.Data.SupplierId, receipt.Data.SupplierName, receipt.Data.SupplierTaxId,
            receipt.Data.IssuedOn, receipt.Data.DueOn, receipt.Data.Source, receipt.Data.Status, receipt.Data.Net,
            receipt.Data.Vat, receipt.Data.Gross, receipt.Data.Warehouse, lineCount = receipt.Data.Lines.Length,
            statusLabel = ReceivingText.StatusLabel(receipt.Data.Status), sourceLabel = ReceivingText.SourceLabel(receipt.Data.Source)
        }, ReceivingJson.Options)!;
        node["id"] = receipt.Id; node["version"] = receipt.Version;
        return JsonSerializer.SerializeToElement(node);
    }
    private static JsonElement Detail(ReceiptRecord receipt)
    {
        var node = JsonSerializer.SerializeToNode(Summary(receipt))!.AsObject();
        node["notes"] = receipt.Data.Notes;
        node["receivedAt"] = receipt.Data.ReceivedAt?.ToString("O");
        node["lines"] = JsonSerializer.SerializeToNode(receipt.Data.Lines.Select(line => new
        {
            line.Id, line.ProductId, line.Name, line.Sku, line.Barcode, line.UnitCode, line.Quantity, line.NetUnitPrice,
            line.VatRate, line.Match, line.MatchBy, matchLabel = ReceivingText.MatchLabel(line.Match)
        }), ReceivingJson.Options);
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
