using System.Text.Json;
using PcImpressz.Contracts;

namespace PcImpressz.Modules.Receiving;

public sealed class ReceivingModule : IModule
{
    public ValueTask StartAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        Register(context,"seed-demo","receiving.write",CommandAccess.Licensed,(input,actor,ct)=>
        {
            ct.ThrowIfCancellationRequested();
            var request=ReceivingJson.Read<CompanyDemoSeedRequest>(input);
            var report=DemoDataSeeder.Seed(context.GetCompanyDataDirectory,context.Database??throw new InvalidOperationException("A demófeltöltés céges SQLite-adatbázist igényel."),request.CompanyId,context.TimeProvider,actor);
            return Task.FromResult(ReceivingJson.Element(report));
        });
        Register(context,"taxpayer","receiving.read",CommandAccess.Licensed,async(input,actor,ct)=>
        {
            var lookup=ReceivingJson.Read<TaxpayerLookup>(input);
            var secrets=NavConnectionStore.Read(context.GetCompanyDataDirectory(lookup.CompanyId),lookup.CompanyId)??throw new ArgumentException("Az admin előbb állítsa be a cég NAV-kapcsolatát a Beállításokban.");
            return ReceivingJson.Element(await NavTaxpayerClient.QueryAsync(secrets,lookup,ct));
        });
        var repository = new ReceivingRepository(context.GetCompanyDataDirectory, context.TimeProvider, context.Database);
        OutgoingInvoiceRegistry Outgoing()=>new(context.Database??throw new InvalidOperationException("A kimenő számlák céges SQL-adatbázist igényelnek."),context.TimeProvider);
        var invoiceService = new OutgoingInvoiceService(context.Database!, context.TimeProvider, id => context.GetCompanyDataDirectory(id));
        Register(context,"supplier-status","receiving.read",CommandAccess.RecoveryReadOnly,(input,_,ct)=>{ct.ThrowIfCancellationRequested();var id=input.GetProperty("companyId").GetGuid();return Task.FromResult(ReceivingJson.Element(SupplierConnectionStore.Status(context.GetCompanyDataDirectory(id),id)));});
        Register(context,"supplier-save","receiving.write",CommandAccess.Licensed,(input,_,ct)=>{ct.ThrowIfCancellationRequested();var request=ReceivingJson.Read<SupplierConnectionSave>(input);return Task.FromResult(ReceivingJson.Element(SupplierConnectionStore.Save(context.GetCompanyDataDirectory(request.CompanyId),request)));});
        Register(context,"supplier-enrich","receiving.read",CommandAccess.Licensed,async(input,_,ct)=>
        {
            var id=input.GetProperty("companyId").GetGuid();var tax=ReceivingText.TaxKey(input.GetProperty("supplierTaxNumber").GetString());
            if(tax!="23126806")return ReceivingJson.Element(new{configured=false,rows=Array.Empty<SupplierInvoiceLine>()});
            var connection=SupplierConnectionStore.Read(context.GetCompanyDataDirectory(id),id);
            if(connection is null||!connection.Enabled)return ReceivingJson.Element(new{configured=false,rows=Array.Empty<SupplierInvoiceLine>()});
            return ReceivingJson.Element(new{configured=true,rows=await SupplierInvoiceClient.ReadAsync(connection,input.GetProperty("number").GetString()??"",ct)});
        });
        var drafts=new ManualInvoiceDraftStore(context.Database!,context.TimeProvider);
        Register(context,"draft-list","receiving.read",CommandAccess.RecoveryReadOnly,(input,_,ct)=>{ct.ThrowIfCancellationRequested();return Task.FromResult(ReceivingJson.Element(drafts.List(input.GetProperty("companyId").GetGuid())));});
        Register(context,"draft-read","receiving.read",CommandAccess.RecoveryReadOnly,(input,_,ct)=>{ct.ThrowIfCancellationRequested();return Task.FromResult(ReceivingJson.Element(drafts.Read(ReceivingJson.Read<ManualDraftRequest>(input))));});
        Register(context,"draft-save","receiving.write",CommandAccess.Licensed,(input,actor,ct)=>{ct.ThrowIfCancellationRequested();return Task.FromResult(ReceivingJson.Element(drafts.Save(ReceivingJson.Read<ManualDraftSave>(input),actor)));});
        Register(context,"draft-delete","receiving.write",CommandAccess.Licensed,(input,_,ct)=>{ct.ThrowIfCancellationRequested();drafts.Delete(ReceivingJson.Read<ManualDraftRequest>(input));return Task.FromResult(ReceivingJson.Element(new{deleted=true}));});
        var preparations=new InvoicePreparationStore(context.Database!,context.TimeProvider);
        Register(context,"preparation-read","receiving.read",CommandAccess.RecoveryReadOnly,(input,_,ct)=>{ct.ThrowIfCancellationRequested();return Task.FromResult(ReceivingJson.Element(preparations.Read(ReceivingJson.Read<InvoicePreparationRequest>(input))));});
        Register(context,"preparation-save","receiving.write",CommandAccess.Licensed,(input,actor,ct)=>{ct.ThrowIfCancellationRequested();return Task.FromResult(ReceivingJson.Element(preparations.Save(ReceivingJson.Read<InvoicePreparationSave>(input),actor)));});
        Register(context,"nav-list","receiving.read",CommandAccess.Licensed,async(input,_,ct)=>
        {
            var query=ReceivingJson.Read<NavInvoiceQuery>(input);var secret=NavConnectionStore.Read(context.GetCompanyDataDirectory(query.CompanyId),query.CompanyId)??throw new ArgumentException("A Beállításokban előbb add meg a cég NAV-kapcsolatát.");
            var page=await NavInvoiceClient.ListAsync(secret,query,ct);return ReceivingJson.Element(await repository.MarkNavPage(query.CompanyId,page,ct));
        });
        Register(context,"nav-outgoing-list","receiving.read",CommandAccess.Licensed,async(input,_,ct)=>
        {
            var query=ReceivingJson.Read<NavInvoiceQuery>(input);var secret=NavConnectionStore.Read(context.GetCompanyDataDirectory(query.CompanyId),query.CompanyId)??throw new ArgumentException("A Beállításokban előbb add meg a cég NAV-kapcsolatát.");
            return ReceivingJson.Element(await NavInvoiceClient.ListAsync(secret,query,ct,direction:"OUTBOUND"));
        });
        Register(context,"outgoing-local-list","receiving.read",CommandAccess.RecoveryReadOnly,(input,_,ct)=>
        {
            ct.ThrowIfCancellationRequested();return Task.FromResult(ReceivingJson.Element(Outgoing().List(ReceivingJson.Read<OutgoingInvoiceQuery>(input))));
        });
        Register(context,"bizonylat-status","receiving.read",CommandAccess.RecoveryReadOnly,(input,_,ct)=>
        {
            ct.ThrowIfCancellationRequested();var company=input.GetProperty("companyId").GetGuid();return Task.FromResult(ReceivingJson.Element(BizonylatConnectionStore.Status(context.GetCompanyDataDirectory(company),company)));
        });
        Register(context,"bizonylat-save","receiving.write",CommandAccess.Licensed,(input,_,ct)=>
        {
            ct.ThrowIfCancellationRequested();var request=ReceivingJson.Read<BizonylatConnectionSave>(input);return Task.FromResult(ReceivingJson.Element(BizonylatConnectionStore.Save(context.GetCompanyDataDirectory(request.CompanyId),request.CompanyId,request)));
        });
        Register(context,"outgoing-issue-sale","receiving.write",CommandAccess.Licensed,async(input,actor,ct)=>ReceivingJson.Element(await invoiceService.IssueSaleAsync(ReceivingJson.Read<IssueSaleInvoiceRequest>(input),actor,ct)));
        Register(context,"nav-outgoing-reconcile","receiving.write",CommandAccess.Licensed,async(input,actor,ct)=>
        {
            var query=ReceivingJson.Read<NavInvoiceQuery>(input);var secret=NavConnectionStore.Read(context.GetCompanyDataDirectory(query.CompanyId),query.CompanyId)??throw new ArgumentException("A Beállításokban előbb add meg a cég NAV-kapcsolatát.");
            var all=new List<NavInvoiceDigest>();var page=1;var pages=1;
            do{var result=await NavInvoiceClient.ListAsync(secret,query with{Page=page},ct,direction:"OUTBOUND");all.AddRange(result.Rows);pages=result.AvailablePage;page++;if(all.Count>20000)throw new ArgumentException("Túl sok NAV-számla egy egyeztetéshez.");}while(page<=pages);
            return ReceivingJson.Element(all);
        });
        Register(context,"outgoing-reconcile-store","receiving.write",CommandAccess.Licensed,(input,actor,ct)=>
        {
            ct.ThrowIfCancellationRequested();var query=ReceivingJson.Read<NavInvoiceQuery>(input.GetProperty("request"));if(query.CompanyId!=input.GetProperty("companyId").GetGuid())throw new ArgumentException("Eltérő cég.");
            return Task.FromResult(ReceivingJson.Element(Outgoing().Reconcile(query.CompanyId,query.From,query.To,ReceivingJson.Read<NavInvoiceDigest[]>(input.GetProperty("rows")),actor)));
        });
        var billingSettings=new InvoicingSettingsStore(context.Database!);
        Register(context,"invoicing-settings","receiving.read",CommandAccess.RecoveryReadOnly,(input,_,ct)=>
        {
            ct.ThrowIfCancellationRequested();var company=input.GetProperty("companyId").GetGuid();return Task.FromResult(ReceivingJson.Element(new{settings=billingSettings.Read(company),connection=BizonylatConnectionStore.Status(context.GetCompanyDataDirectory(company),company)}));
        });
        Register(context,"invoicing-settings-save","receiving.write",CommandAccess.Licensed,(input,_,ct)=>
        {
            ct.ThrowIfCancellationRequested();var request=ReceivingJson.Read<InvoicingSettingsSave>(input);
            if(!string.IsNullOrWhiteSpace(request.ApiKey)&&(request.ApiKey.Trim().Length is <12 or >300||request.ApiKey.Trim().Any(c=>char.IsWhiteSpace(c)||char.IsControl(c))))throw new ArgumentException("Érvénytelen API-kulcs.");
            var settings=billingSettings.Save(request.CompanyId,request.Settings);var directory=context.GetCompanyDataDirectory(request.CompanyId);
            if(!string.IsNullOrWhiteSpace(request.ApiKey)||BizonylatConnectionStore.Status(directory,request.CompanyId).Configured)
                BizonylatConnectionStore.Save(directory,request.CompanyId,new(request.CompanyId,request.ApiKey,request.TestMode,settings.Blocks.FirstOrDefault(b=>b.Id==settings.DefaultBlockId)?.Prefix??""));
            return Task.FromResult(ReceivingJson.Element(new{settings,connection=BizonylatConnectionStore.Status(directory,request.CompanyId)}));
        });
        Register(context,"pdf-preview","receiving.read",CommandAccess.Licensed,(input,_,ct)=>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(ReceivingJson.Element(new{text=InvoicePdf.ReadText(input.GetProperty("contentBase64").GetString())}));
        });
        Register(context,"nav-preview","receiving.read",CommandAccess.Licensed,async(input,_,ct)=>
        {
            var query=ReceivingJson.Read<NavInvoiceImport>(input);
            var secret=NavConnectionStore.Read(context.GetCompanyDataDirectory(query.CompanyId),query.CompanyId)??throw new ArgumentException("Előbb állítsd be a NAV-kapcsolatot.");
            var document=await NavInvoiceClient.DownloadAsync(secret,query,ct);
            var preview=await repository.PreviewNav(query,document,ct);
            return ReceivingJson.Element(new{document,lines=preview.Lines,receipt=preview.Existing is null?(JsonElement?)null:Detail(preview.Existing)});
        });
        Register(context,"nav-import","receiving.write",CommandAccess.Licensed,async(input,actor,ct)=>
        {
            var query=ReceivingJson.Read<NavInvoiceImport>(input);var secret=NavConnectionStore.Read(context.GetCompanyDataDirectory(query.CompanyId),query.CompanyId)??throw new ArgumentException("A Beállításokban előbb add meg a cég NAV-kapcsolatát.");
            return ReceivingJson.Element(await NavInvoiceClient.DownloadAsync(secret,query,ct));
        });
        Register(context,"nav-store","receiving.write",CommandAccess.Licensed,async(input,actor,ct)=>
        {
            var request=ReceivingJson.Read<NavInvoiceImport>(input.GetProperty("request"));if(request.CompanyId!=input.GetProperty("companyId").GetGuid())throw new ArgumentException("Eltérő importcég.");
            return Detail(await repository.ImportNav(request,ReceivingJson.Read<ImportedDocument>(input.GetProperty("document")),actor,ct));
        });
        Register(context, "list", "receiving.read", CommandAccess.RecoveryReadOnly, async (input, _, ct) =>
        {
            var query = ReadQuery(input);
            var receipts = (await repository.Snapshot(query.CompanyId, ct)).Receipts;
            var counts = new
            {
                uj = receipts.Count(receipt => receipt.Data.Status == "uj"),
                bevetelezve = receipts.Count(receipt => receipt.Data.Status == "bevetelezve"),
                visszaforditva = receipts.Count(receipt => receipt.Data.Status == "visszaforditva"),
                koltseg = receipts.Count(receipt => receipt.Data.Status == "koltseg"),
                fuggo = receipts.Count(receipt => receipt.Data.Status == "fuggo"),
                mind = receipts.Length
            };
            var matching = ReceiptListing.Match(receipts, query);
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
        Register(context, "reverse", "receiving.write", CommandAccess.Licensed, async (input, actor, ct) =>
            Detail(await repository.Reverse(ReceivingJson.Read<ReceiptReverse>(input), actor, ct)));
        Register(context, "import", "receiving.write", CommandAccess.Licensed, async (input, actor, ct) =>
        {
            var request = ReceivingJson.Read<ReceiptImport>(input);
            var extension = Path.GetExtension(request.FileName ?? "").ToLowerInvariant();
            if (request.Source == "pdf" || extension == ".pdf")
                throw new ArgumentException("A PDF-ből a tételsorok kiolvasása a következő kör. Most Excel (xlsx), CSV vagy NAV-számla XML olvasható.");
            request = request with { Source = extension == ".xml" || request.Source == "nav" ? "nav" : "xls" };
            return Detail(await repository.Import(request, ReceivingImport.Read(request.FileName, request.ContentBase64), actor, ct));
        });
        Register(context, "nav-status", "receiving.read", CommandAccess.RecoveryReadOnly, async (input, _, ct) =>
        {
            var query = ReceivingJson.Read<ReceiptQuery>(input);
            var directory = context.GetCompanyDataDirectory(query.CompanyId);
            return ReceivingJson.Element(await Task.Run(() => NavConnectionStore.Status(directory, query.CompanyId), ct));
        });
        Register(context, "nav-save", "receiving.write", CommandAccess.Licensed, async (input, _, ct) =>
        {
            var request = ReceivingJson.Read<NavConnectionSave>(input);
            var directory = context.GetCompanyDataDirectory(request.CompanyId);
            return ReceivingJson.Element(await Task.Run(() => NavConnectionStore.Save(directory, request.CompanyId, request), ct));
        });
        Register(context, "export", "receiving.export", CommandAccess.RecoveryReadOnly, async (input, _, ct) =>
        {
            var query = ReadQuery(input);
            var rows = ReceiptListing.Match((await repository.Snapshot(query.CompanyId, ct)).Receipts, query);
            return ReceivingJson.Element(new { csv = ReceivingRepository.ExportCsv(rows) });
        });
        Register(context, "archive", "receiving.write", CommandAccess.Licensed, (_, _, _) =>
            Task.FromException<JsonElement>(new ArgumentException("A számlát nem archiváljuk. A besorolás dönti el, hogy bevételezés, költség vagy függő.")));
        context.RegisterPage(new("receiving", context.ModuleId, "Számlák",
            "Bejövő és kimenő számlák. NAV-lekérés, kézi bevételezés és PDF alapján történő rögzítés.",
            "\uE7BF",
            [new("number", "Számlaszám", Required: true), new("supplierName", "Szállító", Required: true),
             new("issuedOn", "Kelt"), new("notes", "Megjegyzés", "multiline")],
            [new("number", "Számlaszám", 150), new("supplierName", "Szállító", 220), new("issuedOn", "Kelt", 110),
             new("supplierTaxId","Adószám",120),new("sourceLabel", "Forrás", 110), new("net", "Nettó", 110),new("gross","Bruttó",110), new("statusLabel", "Állapot", 180)],
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
            [new("uj", "Új, besorolásra vár"), new("bevetelezve", "Bevételezve"), new("visszaforditva", "Visszafordítva"), new("koltseg", "Költség"), new("fuggo", "Függő"), new("mind", "Minden számla")]) { ViewKind = "receiving" });
        return ValueTask.CompletedTask;
    }
    public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    private static void Register(IModuleContext context, string name, string permission, CommandAccess access,
        Func<JsonElement, string, CancellationToken, Task<JsonElement>> execute) =>
        context.RegisterCommand(new Handler(name, permission, access, execute));
    private static ReceiptQuery ReadQuery(JsonElement input)
    {
        var query = ReceivingJson.Read<ReceiptQuery>(input);
        ReceiptFilters.Validate(query);
        if (query.Page < 0 || query.PageSize is < 1 or > 200 || (query.Search is null || query.Search.Length > 200) || query.Sort is null || !ReceiptListing.IsSort(query.Sort))
            throw new ArgumentException("Érvénytelen listázási kérés.");
        if (query.Filter is not ("uj" or "bevetelezve" or "visszaforditva" or "koltseg" or "fuggo" or "mind"))
            throw new ArgumentException("Ismeretlen számlaszűrő.");
        return query;
    }
    private static int Skip(ReceiptQuery query) => query.Page * (long)query.PageSize > int.MaxValue ? int.MaxValue : query.Page * query.PageSize;
    private static JsonElement Summary(ReceiptRecord receipt)
    {
        var node = JsonSerializer.SerializeToNode(new
        {
            receipt.Data.Number, receipt.Data.SupplierId, receipt.Data.SupplierName, receipt.Data.SupplierTaxId,receipt.Data.OriginalNavNumber,receipt.Data.OriginalNavSupplierTaxId,
            receipt.Data.IssuedOn, receipt.Data.DueOn, receipt.Data.Source, receipt.Data.Status, receipt.Data.Net,
            receipt.Data.Vat, receipt.Data.Gross, receipt.Data.StockPosted, receipt.Data.Warehouse, lineCount = receipt.Data.Lines.Length,
            statusLabel = ReceivingText.StatusLabel(receipt.Data.Status), sourceLabel = ReceivingText.SourceLabel(receipt.Data.Source)
        }, ReceivingJson.Options)!;
        node["id"] = receipt.Id; node["version"] = receipt.Version;
        return JsonSerializer.SerializeToElement(node);
    }
    private static JsonElement Detail(ReceiptRecord receipt)
    {
        var node = JsonSerializer.SerializeToNode(Summary(receipt))!.AsObject();
        node["notes"] = receipt.Data.Notes;
        node["reversedAt"] = receipt.Data.ReversedAt?.ToString("O"); node["reversedBy"] = receipt.Data.ReversedBy; node["reversalReason"] = receipt.Data.ReversalReason;
        node["receivedAt"] = receipt.Data.ReceivedAt?.ToString("O");
        node["lines"] = JsonSerializer.SerializeToNode(receipt.Data.Lines.Select(line => new
        {
            line.Id, line.ProductId, line.Name, line.Sku, line.Barcode, line.UnitCode, line.Quantity, line.NetUnitPrice,
            line.VatRate, line.StockItem, line.SourceNetAmount,line.SourceVatAmount,line.SourceGrossAmount,line.Match, line.MatchBy, matchLabel = ReceivingText.MatchLabel(line.Match)
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

public static class ReceiptListing
{
    private static readonly StringComparer NameOrder = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("hu-HU"), true);
    public static bool IsSort(string sort) => sort.Length <= 40 && sort.All(char.IsAsciiLetterOrDigit);
    public static ReceiptRecord[] Match(IEnumerable<ReceiptRecord> source, ReceiptQuery query)
    {
        ReceiptFilters.Validate(query);
        if (!IsSort(query.Sort)) throw new ArgumentException("Érvénytelen listázási kérés.");
        var rows = source.OrderBy(receipt => receipt.Id).Where(receipt => query.Filter == "mind" || receipt.Data.Status == query.Filter)
            .Where(receipt => string.IsNullOrWhiteSpace(query.Search) || string.Join(" ", receipt.Data.Number, receipt.Data.SupplierName, receipt.Data.SupplierTaxId)
                .Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase)).Where(r=>ReceiptFilters.Matches(r.Data,query)).ToArray();
        if(query.Sort is "net" or "gross")return (query.Descending?rows.OrderByDescending(r=>query.Sort=="gross"?r.Data.Gross:r.Data.Net):rows.OrderBy(r=>query.Sort=="gross"?r.Data.Gross:r.Data.Net)).ThenBy(r=>r.Id).ToArray();
        if (query.Sort.Length > 0 && Keys.TryGetValue(query.Sort, out var key))
            return TextOrder.By(rows, query.Descending, key);
        return rows.OrderByDescending(receipt => receipt.Data.IssuedOn, StringComparer.Ordinal)
            .ThenBy(receipt => receipt.Data.SupplierName, NameOrder).ToArray();
    }
    private static readonly Dictionary<string, Func<ReceiptRecord, string>> Keys = new(StringComparer.Ordinal)
    {
        ["number"] = receipt => receipt.Data.Number,
        ["supplierName"] = receipt => receipt.Data.SupplierName,
        ["issuedOn"] = receipt => receipt.Data.IssuedOn,
        ["sourceLabel"] = receipt => ReceivingText.SourceLabel(receipt.Data.Source),
        ["net"] = receipt => receipt.Data.Net.ToString("000000000000.00", System.Globalization.CultureInfo.InvariantCulture),
        ["statusLabel"] = receipt => ReceivingText.StatusLabel(receipt.Data.Status)
    };
}
