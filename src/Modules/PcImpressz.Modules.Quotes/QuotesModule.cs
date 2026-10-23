using System.Text.Json;
using PcImpressz.Business;
using PcImpressz.Contracts;

namespace PcImpressz.Modules.Quotes;

public sealed record QuoteLine(Guid ProductId, string Description, decimal Quantity, decimal NetUnitPrice, decimal VatRate);
public sealed record QuoteDocument(Guid Id, long Version, Guid? CustomerId, string Number, DateOnly ValidUntil,
    string Notes, string Status, QuoteLine[] Lines, decimal Net, decimal Vat, decimal Gross,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string Actor,BillingCustomer? Customer=null);
public sealed record QuoteSave(Guid CompanyId, Guid RequestId, Guid Id, long Version, Guid? CustomerId,
    DateOnly ValidUntil, string Notes, QuoteLine[] Lines,BillingCustomer? Customer=null);
public sealed record CompMarketImportRequest(string Xml, decimal MarginPercent, decimal VatRate = 27m);
public sealed record QuoteAction(Guid CompanyId, Guid RequestId, Guid Id, long Version);

// Standalone signed module. No host-side special case and no dependency on future module grouping.
public sealed class QuotesModule : IModule
{
    private const string Store = "quotes";
    private sealed record RequestResult(string Fingerprint,QuoteDocument Document);
    private static string Fingerprint<T>(string action,T request)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new{action,request},CompanyDocuments.Json)));
    private static QuoteDocument? Replay<T>(ICompanyTransaction tx,Guid id,string action,T request)
    {
        if(id==Guid.Empty)throw new ArgumentException("Hiányzó műveletazonosító.");
        var previous=tx.Read<RequestResult>(Store,"request/"+id.ToString("N"));
        if(previous is null)return null;if(previous.Fingerprint!=Fingerprint(action,request))throw new ArgumentException("Eltérő művelet ugyanazzal az azonosítóval.");return previous.Document;
    }
    private static void Remember<T>(ICompanyTransaction tx,Guid id,string action,T request,QuoteDocument document)=>tx.Write(Store,"request/"+id.ToString("N"),new RequestResult(Fingerprint(action,request),document));
    private static string Key(Guid id) => "quote/" + id.ToString("N");
    public ValueTask StartAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        Register(context, "compmarket-status", "quotes.read", CommandAccess.RecoveryReadOnly, (input, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            var id = input.GetProperty("companyId").GetGuid();
            return Task.FromResult(DomainCommands.Json(CompMarketQuoteConnectionStore.Status(context.GetCompanyDataDirectory(id), id)));
        });
        Register(context, "compmarket-save", "quotes.write", CommandAccess.Licensed, (input, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            var request = DomainCommands.Read<CompMarketConnectionSave>(input);
            return Task.FromResult(DomainCommands.Json(CompMarketQuoteConnectionStore.Save(context.GetCompanyDataDirectory(request.CompanyId), request)));
        });
        Register(context, "compmarket-search", "quotes.read", CommandAccess.RecoveryReadOnly, async (input, _, ct) =>
        {
            var id = input.GetProperty("companyId").GetGuid();
            var directory = context.GetCompanyDataDirectory(id);
            var connection = CompMarketQuoteConnectionStore.Read(directory, id) ?? throw new ArgumentException("Nincs CompMarket-kapcsolat. A Beállítások CompMarket kártyáján add meg a proxyt, a pid-et és a kódot.");
            var query = DomainCommands.Search(input, "query");
            var margin = input.TryGetProperty("marginPercent", out var marginValue) && marginValue.TryGetDecimal(out var parsed) ? parsed : 20m;
            var vat = input.TryGetProperty("vatRate", out var vatValue) && vatValue.TryGetDecimal(out var parsedVat) ? parsedVat : 27m;
            return DomainCommands.Json(await CompMarketQuoteFeed.SearchAsync(directory, connection, query, margin, vat, ct));
        });
        DomainCommands.Register(context, "list", "quotes.read", CommandAccess.RecoveryReadOnly, (tx, input, _) =>
        {
            var search = DomainCommands.Search(input);
            var rows = tx.List(Store, "quote/").Select(s => System.Text.Json.JsonSerializer.Deserialize<QuoteDocument>(s, CompanyDocuments.Json)!)
                .Where(q => DomainCommands.Matches(q.Number + " " + q.Notes, search))
                .OrderByDescending(q => q.UpdatedAt).Take(200).ToArray();
            return DomainCommands.Json(rows);
        });
        DomainCommands.Register(context, "save", "quotes.write", CommandAccess.Licensed, (tx, input, actor) =>
        {
            var request = DomainCommands.Read<QuoteSave>(input);
            if(Replay(tx,request.RequestId,"save",request) is {} replay)return DomainCommands.Json(replay);
            if (request.RequestId == Guid.Empty) throw new ArgumentException("Hiányzó művelet- vagy partnerazonosító.");
            if (request.Lines is not { Length: > 0 and <= 200 }) throw new ArgumentException("Legalább egy, legfeljebb 200 ajánlati sor szükséges.");
            if (request.Notes is null || request.Notes.Length > 2000) throw new ArgumentException("Túl hosszú megjegyzés.");
            if (request.Lines.Any(l => string.IsNullOrWhiteSpace(l.Description) || l.Description.Length > 300 || l.Quantity <= 0 || l.NetUnitPrice < 0 || !VatMath.IsSupportedRate(l.VatRate)))
                throw new ArgumentException("Érvénytelen ajánlati sor.");
            var id = request.Id == Guid.Empty ? request.RequestId : request.Id;
            var existing = tx.Read<QuoteDocument>(Store, Key(id));
            if (existing is null && request.Version != 0 || existing is not null && (existing.Version != request.Version || existing.Status != "draft"))
                throw new InvalidOperationException("Az ajánlat időközben módosult vagy már nem szerkeszthető.");
            var amounts=request.Lines.Select(l=>VatMath.Line(l.Quantity,l.NetUnitPrice,l.VatRate)).ToArray();
            var net=amounts.Sum(a=>a.Net);var vat=amounts.Sum(a=>a.Vat);
            var customer=request.Customer;
            if(request.CustomerId is {} customerId&&customer is null)customer=CompanyReferenceQueries.BillingCustomers(tx).SingleOrDefault(c=>c.Id==customerId&&c.IsActive)??throw new ArgumentException("A partnertörzs nem használható; adj meg kézi vevőadatot.");
            if(customer is not null&&(string.IsNullOrWhiteSpace(customer.Name)||customer.Name.Length>200))throw new ArgumentException("Érvénytelen vevőnév.");
            var now = context.TimeProvider.GetUtcNow();
            var result = new QuoteDocument(id, (existing?.Version ?? 0) + 1, request.CustomerId,
                existing?.Number ?? "AJ-" + now.ToString("yyyyMMdd") + "-" + id.ToString("N")[..8].ToUpperInvariant(),
                request.ValidUntil, request.Notes, "draft", request.Lines, net, vat, net + vat,
                existing?.CreatedAt ?? now, now, actor,customer);
            tx.Write(Store, Key(id), result);
            Remember(tx,request.RequestId,"save",request,result);
            return DomainCommands.Json(result);
        });
        DomainCommands.Register(context, "accept", "quotes.write", CommandAccess.Licensed, (tx, input, actor) =>
        {
            var request = DomainCommands.Read<QuoteAction>(input);
            if(Replay(tx,request.RequestId,"accept",request) is {} replay)return DomainCommands.Json(replay);
            if (request.RequestId == Guid.Empty || request.Id == Guid.Empty) throw new ArgumentException("Hiányzó műveletazonosító.");
            var quote = tx.Read<QuoteDocument>(Store, Key(request.Id)) ?? throw new ArgumentException("Az ajánlat nem található.");
            if (quote.Status == "accepted" && quote.Version == request.Version) return DomainCommands.Json(quote);
            if (quote.Status != "draft" || quote.Version != request.Version) throw new InvalidOperationException("Az ajánlat állapota vagy verziója megváltozott.");
            if (quote.ValidUntil < DateOnly.FromDateTime(context.TimeProvider.GetUtcNow().UtcDateTime)) throw new InvalidOperationException("Az ajánlat lejárt.");
            var updated = quote with { Status = "accepted", Version = quote.Version + 1, UpdatedAt = context.TimeProvider.GetUtcNow(), Actor = actor };
            tx.Write(Store, Key(request.Id), updated);
            Remember(tx,request.RequestId,"accept",request,updated);
            return DomainCommands.Json(updated);
        });
        DomainCommands.Register(context, "compmarket-xml-preview", "quotes.read", CommandAccess.RecoveryReadOnly, (tx, input, _) =>
        {
            var request = DomainCommands.Read<CompMarketImportRequest>(input);
            return DomainCommands.Json(CompMarketQuoteImport.Parse(request.Xml, request.MarginPercent, request.VatRate));
        });
        context.RegisterPage(new("quotes", context.ModuleId, "Árajánlatok", "Árajánlatok mentése és elfogadása.", "\uE8A5",
            [new("number","Ajánlatszám")], [new("number", "Ajánlatszám"), new("status", "Állapot"), new("gross", "Bruttó")], new("list", "save", "accept", "list")){ViewKind="quotes"});
        return ValueTask.CompletedTask;
    }
    public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    private static void Register(IModuleContext context, string name, string permission, CommandAccess access,
        Func<JsonElement, string, CancellationToken, Task<JsonElement>> execute) =>
        context.RegisterCommand(new Handler(name, permission, access, execute));
    private sealed class Handler(string name, string permission, CommandAccess access,
        Func<JsonElement, string, CancellationToken, Task<JsonElement>> execute) : IContextualCommandHandler
    {
        public CommandDescriptor Descriptor { get; } = new(name, permission, access);
        public ValueTask<JsonElement> ExecuteAsync(JsonElement input, CommandCallContext caller, CancellationToken ct) => new(execute(input, caller.ActorId, ct));
    }
}
