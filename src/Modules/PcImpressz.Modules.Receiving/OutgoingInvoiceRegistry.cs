using PcImpressz.Contracts;
using System.Globalization;
using System.Text.Json;

namespace PcImpressz.Modules.Receiving;

public sealed record OutgoingInvoiceLine(string Name,string Unit,string Sku,decimal Quantity,decimal NetUnitPrice,decimal VatRate,decimal Net,decimal Vat,decimal Gross);
public sealed record OutgoingInvoiceRecord(Guid Id,long Version,string Provider,string ProviderInvoiceId,string Number,Guid? CustomerId,string CustomerName,string CustomerTaxNumber,
    string IssuedOn,string DeliveryOn,string DueOn,string PaymentMethod,string Currency,OutgoingInvoiceLine[] Lines,decimal Net,decimal Vat,decimal Gross,
    string ProviderStatus,string NavStatus,string FinancialStatus,Guid? SaleId,string PublicUrl,string PublicPdfUrl,DateTimeOffset CreatedAt,DateTimeOffset UpdatedAt,string UpdatedBy);
public sealed record OutgoingInvoiceQuery(Guid CompanyId,string Search="",string PaymentMethod="all",string ProviderStatus="all",string NavStatus="all",string From="",string To="",int Page=0,int PageSize=100);
public sealed record OutgoingInvoiceUpsert(Guid CompanyId,Guid Id,string Provider,string ProviderInvoiceId,string Number,Guid? CustomerId,string CustomerName,string CustomerTaxNumber,
    string IssuedOn,string DeliveryOn,string DueOn,string PaymentMethod,string Currency,OutgoingInvoiceLine[] Lines,decimal Net,decimal Vat,decimal Gross,
    string ProviderStatus,string NavStatus="pending",string FinancialStatus="unknown",Guid? SaleId=null,string PublicUrl="",string PublicPdfUrl="");
public sealed record NavReconcileResult(int LocalCount,int NavCount,int Matched,int MissingAtNav,int NavOnly,int Mismatched,NavReconcileIssue[] Issues);
public sealed record NavReconcileIssue(string Kind,string Number,string Message);

public sealed class OutgoingInvoiceRegistry(ICompanyDatabase database,TimeProvider timeProvider)
{
    private const string Module="receiving";
    private const string Prefix="outgoing-invoice/";

    public object List(OutgoingInvoiceQuery query)
    {
        if(query.Page is <0 or >100000||query.PageSize is <1 or >500||query.Search.Length>200)throw new ArgumentException("Érvénytelen kimenőszámla-lista kérés.");
        if(query.PaymentMethod is not ("all" or "CASH" or "CARD" or "BANK_TRANSFER" or "VOUCHER" or "OTHER"))throw new ArgumentException("Ismeretlen fizetési mód.");
        var from=ParseDate(query.From);var to=ParseDate(query.To);if(from>to)throw new ArgumentException("Fordított időszak.");
        return database.Execute(query.CompanyId,tx=>
        {
            IEnumerable<OutgoingInvoiceRecord> rows=ReadAll(tx);
            if(from is not null)rows=rows.Where(x=>DateOnly.TryParse(x.IssuedOn,out var d)&&d>=from);
            if(to is not null)rows=rows.Where(x=>DateOnly.TryParse(x.IssuedOn,out var d)&&d<=to);
            if(query.PaymentMethod!="all")rows=rows.Where(x=>x.PaymentMethod==query.PaymentMethod);
            if(query.ProviderStatus!="all")rows=rows.Where(x=>x.ProviderStatus.Equals(query.ProviderStatus,StringComparison.OrdinalIgnoreCase));
            if(query.NavStatus!="all")rows=rows.Where(x=>x.NavStatus.Equals(query.NavStatus,StringComparison.OrdinalIgnoreCase));
            if(!string.IsNullOrWhiteSpace(query.Search))
            {
                var needle=ReceivingText.Compact(query.Search);
                rows=rows.Where(x=>ReceivingText.Compact(x.Number+" "+x.CustomerName+" "+x.CustomerTaxNumber+" "+x.ProviderInvoiceId).Contains(needle,StringComparison.Ordinal));
            }
            var ordered=rows.OrderByDescending(x=>x.IssuedOn).ThenByDescending(x=>x.Number).ToArray();
            var page=ordered.Skip(query.Page*query.PageSize).Take(query.PageSize).ToArray();
            var currencies=ordered.GroupBy(x=>x.Currency).OrderBy(g=>g.Key).Select(g=>new{currency=g.Key,count=g.Count(),net=g.Sum(x=>x.Net),vat=g.Sum(x=>x.Vat),gross=g.Sum(x=>x.Gross)}).ToArray();
            var summary=new{count=ordered.Length,currencies,net=currencies.Length<2?(decimal?)ordered.Sum(x=>x.Net):null,vat=currencies.Length<2?(decimal?)ordered.Sum(x=>x.Vat):null,gross=currencies.Length<2?(decimal?)ordered.Sum(x=>x.Gross):null,open=ordered.Count(x=>x.FinancialStatus is "unknown" or "open" or "partial" or "overdue")};
            return new{rows=page,total=ordered.Length,summary};
        });
    }

    public OutgoingInvoiceRecord Upsert(OutgoingInvoiceUpsert request,string actor)
    {
        Validate(request);
        return database.Execute(request.CompanyId,tx=>
        {
            var key=Prefix+request.Id.ToString("N");
            var existing=tx.Read<OutgoingInvoiceRecord>(Module,key);
            if(existing is not null && (!existing.Provider.Equals(request.Provider,StringComparison.OrdinalIgnoreCase)||(!string.IsNullOrWhiteSpace(existing.ProviderInvoiceId)&&existing.ProviderInvoiceId!=request.ProviderInvoiceId)))
                throw new ArgumentException("Az azonosítóhoz már másik számla tartozik.");
            if(existing is not null)
            {
                if(existing.Number!=request.Number||existing.Net!=request.Net||existing.Vat!=request.Vat||existing.Gross!=request.Gross||existing.Currency!=request.Currency||existing.CustomerName!=request.CustomerName||existing.CustomerTaxNumber!=request.CustomerTaxNumber||existing.SaleId!=request.SaleId||JsonSerializer.Serialize(existing.Lines)!=JsonSerializer.Serialize(request.Lines))throw new ArgumentException("A kiállított számla adatai nem írhatók át.");
                return existing;
            }
            if(ReadAll(tx).Any(x=>x.Id!=request.Id&&x.Number.Length>0&&x.Number.Equals(request.Number,StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("Ez a számlaszám már szerepel a saját nyilvántartásban.");
            var now=timeProvider.GetUtcNow();
            var record=new OutgoingInvoiceRecord(request.Id,(existing?.Version??0)+1,request.Provider.Trim(),request.ProviderInvoiceId.Trim(),request.Number.Trim(),request.CustomerId,
                request.CustomerName.Trim(),request.CustomerTaxNumber.Trim(),request.IssuedOn,request.DeliveryOn,request.DueOn,request.PaymentMethod,request.Currency.Trim().ToUpperInvariant(),request.Lines,
                request.Net,request.Vat,request.Gross,request.ProviderStatus.Trim(),request.NavStatus.Trim(),request.FinancialStatus.Trim(),request.SaleId,request.PublicUrl.Trim(),request.PublicPdfUrl.Trim(),existing?.CreatedAt??now,now,actor);
            tx.Write(Module,key,record);return record;
        });
    }

    public NavReconcileResult Reconcile(Guid companyId,string from,string to,IReadOnlyList<NavInvoiceDigest> navRows,string actor)
    {
        var fromDate=ParseDate(from)??throw new ArgumentException("Kezdő dátum szükséges.");
        var toDate=ParseDate(to)??throw new ArgumentException("Záró dátum szükséges.");
        if(fromDate>toDate||toDate.DayNumber-fromDate.DayNumber>34)throw new ArgumentException("Az egyeztetéshez legfeljebb 35 nap kell.");
        return database.Execute(companyId,tx=>
        {
            var local=ReadAll(tx).Where(x=>DateOnly.TryParse(x.IssuedOn,out var d)&&d>=fromDate&&d<=toDate).ToArray();
            var navByNumber=navRows.GroupBy(x=>x.Number,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.OrdinalIgnoreCase);
            var issues=new List<NavReconcileIssue>();var matched=0;var missing=0;var mismatch=0;
            foreach(var invoice in local)
            {
                if(!navByNumber.TryGetValue(invoice.Number,out var found)||found.Length==0)
                {
                    missing++;issues.Add(new("missing-at-nav",invoice.Number,"A saját nyilvántartásban szerepel, de a NAV-listában még nem található."));
                    SaveNavStatus(tx,invoice,"missing",actor);continue;
                }
                var nav=found[0];var netOk=nav.Net is not null&&Math.Abs(nav.Net.Value-invoice.Net)<=0.02m;var grossOk=nav.Gross is not null&&Math.Abs(nav.Gross.Value-invoice.Gross)<=0.02m;
                if(found.Length!=1||nav.Operation!="CREATE"||nav.Currency!=invoice.Currency||(!string.IsNullOrWhiteSpace(invoice.CustomerTaxNumber)&&ReceivingText.TaxKey(nav.CustomerTaxNumber)!=ReceivingText.TaxKey(invoice.CustomerTaxNumber))||!netOk||!grossOk||(!string.IsNullOrWhiteSpace(nav.PaymentMethod)&&!NormalizePayment(nav.PaymentMethod).Equals(invoice.PaymentMethod,StringComparison.OrdinalIgnoreCase)))
                {
                    mismatch++;issues.Add(new("mismatch",invoice.Number,"A NAV és a saját számlaadatok között eltérés van."));SaveNavStatus(tx,invoice,"mismatch",actor);
                }
                else{matched++;SaveNavStatus(tx,invoice,"matched",actor);}
            }
            var localNumbers=local.Select(x=>x.Number).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach(var nav in navRows.Where(x=>!localNumbers.Contains(x.Number)))issues.Add(new("nav-only",nav.Number,"A NAV-ban szerepel, de a saját nyilvántartásban nincs ilyen számla."));
            return new NavReconcileResult(local.Length,navRows.Count,matched,missing,navRows.Count(x=>!localNumbers.Contains(x.Number)),mismatch,issues.ToArray());
        });
    }

    public static string NormalizePayment(string? value)=>value?.Trim().ToUpperInvariant() switch
    {
        "TRANSFER" or "BANK_TRANSFER"=>"BANK_TRANSFER",
        "CASH"=>"CASH",
        "CARD"=>"CARD",
        "VOUCHER"=>"VOUCHER",
        _=>"OTHER"
    };

    private static OutgoingInvoiceRecord[] ReadAll(ICompanyTransaction tx)=>tx.List(Module,Prefix).Select(x=>JsonSerializer.Deserialize<OutgoingInvoiceRecord>(x,CompanyDocuments.Json)??throw new IOException("Sérült kimenő számla.")).ToArray();
    private void SaveNavStatus(ICompanyTransaction tx,OutgoingInvoiceRecord current,string status,string actor)=>tx.Write(Module,Prefix+current.Id.ToString("N"),current with{Version=current.Version+1,NavStatus=status,UpdatedAt=timeProvider.GetUtcNow(),UpdatedBy=actor});
    private static DateOnly? ParseDate(string? value){if(string.IsNullOrWhiteSpace(value))return null;if(!DateOnly.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d))throw new ArgumentException("A dátum formátuma éééé-hh-nn legyen.");return d;}
    private static void Validate(OutgoingInvoiceUpsert r)
    {
        if(r.Id==Guid.Empty||string.IsNullOrWhiteSpace(r.Provider)||string.IsNullOrWhiteSpace(r.ProviderInvoiceId)||string.IsNullOrWhiteSpace(r.Number))throw new ArgumentException("Hiányos kimenő számlaazonosító.");
        _=ParseDate(r.IssuedOn)??throw new ArgumentException("A keltezés kötelező.");
        if(r.DeliveryOn.Length>0)_=ParseDate(r.DeliveryOn);if(r.DueOn.Length>0)_=ParseDate(r.DueOn);
        if(r.Lines is null||r.Lines.Length is <1 or >400)throw new ArgumentException("A számlán 1–400 tétel lehet.");
        if(r.Net<0||r.Vat<0||r.Gross<0||Math.Abs((r.Net+r.Vat)-r.Gross)>0.05m)throw new ArgumentException("Érvénytelen számlaösszeg.");
        if(r.Provider.Length>40||r.ProviderInvoiceId.Length>160||r.Number.Length>100||r.CustomerName.Length>200||r.CustomerTaxNumber.Length>40||r.Currency.Length!=3||r.Lines.Any(l=>string.IsNullOrWhiteSpace(l.Name)||l.Name.Length>500||l.Quantity<=0||l.Quantity>1000000||l.NetUnitPrice<0||l.NetUnitPrice>1000000000||l.VatRate is not (0 or 5 or 18 or 27)||l.Net<0||l.Vat<0||l.Gross!=l.Net+l.Vat)||r.Lines.Sum(l=>l.Net)!=r.Net||r.Lines.Sum(l=>l.Vat)!=r.Vat||r.Lines.Sum(l=>l.Gross)!=r.Gross)throw new ArgumentException("A számla tételei és összesítői eltérnek.");
        if(r.PaymentMethod is not ("CASH" or "CARD" or "BANK_TRANSFER" or "VOUCHER" or "OTHER"))throw new ArgumentException("Érvénytelen fizetési mód.");
    }
}
