using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PcImpressz.Business;
using PcImpressz.Contracts;

namespace PcImpressz.Modules.Sales;
public sealed record SaleLineDraft(Guid ProductId,decimal Quantity,decimal NetUnitPrice,decimal VatRate);
public sealed record SaleRequest(Guid CompanyId,Guid OperationId,Guid? CustomerId,string Warehouse,SaleLineDraft[] Lines);
public sealed record SaleReverse(Guid CompanyId,Guid Id,long Version);
public sealed record SaleLine(Guid ProductId,string Name,string UnitCode,decimal Quantity,decimal NetUnitPrice,decimal VatRate,decimal Net,decimal Vat);
public sealed record SaleRecord(Guid Id,long Version,string Number,Guid? CustomerId,string CustomerName,string Warehouse,string Currency,
    SaleLine[] Lines,decimal Net,decimal Vat,decimal Gross,string Status,DateTimeOffset At,string Actor,string Fingerprint,DateTimeOffset? ReversedAt=null,string? ReversedBy=null);
public sealed record SaleCounter(int Value);
public sealed class SalesModule:IModule
{
    public ValueTask StartAsync(IModuleContext context,CancellationToken cancellationToken)
    {
        DomainCommands.Register(context,"references","sales.read",CommandAccess.RecoveryReadOnly,(tx,input,_)=>DomainCommands.Json(new {
            products=CompanyReferenceQueries.Products(tx).Where(p=>p.IsActive&&p.IsSalable&&DomainCommands.Matches(p.Name+" "+p.Sku,DomainCommands.Search(input))).Take(200),
            customers=CompanyReferenceQueries.Partners(tx).Where(p=>p.IsCustomer&&p.IsActive&&DomainCommands.Matches(p.Name+" "+p.TaxId,DomainCommands.Search(input,"customerSearch"))).Take(200) }));
        DomainCommands.Register(context,"list","sales.read",CommandAccess.RecoveryReadOnly,(tx,input,_)=>
            DomainCommands.Json(new {rows=Records(tx).Where(s=>DomainCommands.Matches(s.Number+" "+s.CustomerName,DomainCommands.Search(input,"recordSearch"))).OrderByDescending(s=>s.At).Take(200)
                .Select(s=>new{s.Id,s.Version,s.Number,s.CustomerName,s.Warehouse,s.Currency,s.Net,s.Vat,s.Gross,s.Status,statusLabel=s.Status=="vegleges"?"Végleges":"Visszafordítva",s.At})}));
        DomainCommands.Register(context,"get","sales.read",CommandAccess.RecoveryReadOnly,(tx,input,_)=>
            DomainCommands.Json(tx.Read<SaleRecord>("sales","sale/"+input.GetProperty("id").GetGuid().ToString("N")) ?? throw new ArgumentException("Az eladás nem található.")));
        DomainCommands.Register(context,"save","sales.write",CommandAccess.Licensed,(tx,input,actor)=>
            DomainCommands.Json(Post(tx,DomainCommands.Read<SaleRequest>(input),actor,context.TimeProvider.GetUtcNow())));
        DomainCommands.Register(context,"reverse","sales.write",CommandAccess.Licensed,(tx,input,actor)=>
            DomainCommands.Json(Reverse(tx,DomainCommands.Read<SaleReverse>(input),actor,context.TimeProvider.GetUtcNow())));
        DomainCommands.Register(context,"export","sales.export",CommandAccess.RecoveryReadOnly,(tx,_,_)=>DomainCommands.Json(new {
            csv=DomainCommands.Csv(new[]{new[]{"Bizonylat","Vevő","Nettó","Áfa","Bruttó","Pénznem","Állapot"}}.Concat(Records(tx).Select(s=>new[]{s.Number,s.CustomerName,s.Net.ToString(CultureInfo.InvariantCulture),s.Vat.ToString(CultureInfo.InvariantCulture),s.Gross.ToString(CultureInfo.InvariantCulture),s.Currency,s.Status})))}));
        context.RegisterPage(new("sales",context.ModuleId,"Eladás","Helyi eladási bizonylat és készletcsökkentés. Nem adóügyi számla.","\uE719",
            [new("number","Bizonylat")],[new("number","Bizonylat"),new("customerName","Vevő"),new("gross","Bruttó"),new("statusLabel","Állapot")],new("list","save","reverse","export")) {ViewKind="sales"});
        return ValueTask.CompletedTask;
    }
    public ValueTask StopAsync(CancellationToken cancellationToken)=>ValueTask.CompletedTask;
    public static SaleRecord[] Records(ICompanyTransaction tx)=>tx.List("sales","sale/").Select(s=>JsonSerializer.Deserialize<SaleRecord>(s,CompanyDocuments.Json) ?? throw new IOException("Sérült eladás.")).ToArray();
    public static SaleRecord Post(ICompanyTransaction tx,SaleRequest request,string actor,DateTimeOffset at)
    {
        if(request.OperationId==Guid.Empty || request.Lines is null || request.Lines.Length is <1 or >400 || request.Lines.Any(l=>l is null))throw new ArgumentException("Egy eladáshoz 1–400 tétel és műveletazonosító kell.");
        var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {request.OperationId,request.CustomerId,request.Warehouse,request.Lines},CompanyDocuments.Json))));
        var key="sale/"+request.OperationId.ToString("N");
        if(tx.Read<SaleRecord>("sales",key) is { } existing)
        {if(existing.Fingerprint!=fingerprint)throw new ArgumentException("Ezzel az azonosítóval másik eladás készült.");return existing;}
        var customer=request.CustomerId is null ? null : CompanyReferenceQueries.Partners(tx).SingleOrDefault(p=>p.Id==request.CustomerId&&p.IsActive&&p.IsCustomer)
            ?? throw new ArgumentException("A kiválasztott vevő nem aktív.");
        var warehouse=StockLedger.Warehouse(request.Warehouse);
        var products=CompanyReferenceQueries.Products(tx);
        var lines=request.Lines.Select(line=>
        {
            var product=products.SingleOrDefault(p=>p.Id==line.ProductId&&p.IsActive&&p.IsSalable) ?? throw new ArgumentException("A termék nem aktív.");
            if(StockLedger.Quantity(line.Quantity)<=0 || line.NetUnitPrice is <0 or >1_000_000_000 || decimal.Round(line.NetUnitPrice,4)!=line.NetUnitPrice
                || !new decimal[]{0,5,18,27}.Contains(line.VatRate))throw new ArgumentException("Érvénytelen mennyiség, ár vagy áfakulcs.");
            var net=decimal.Round(line.Quantity*line.NetUnitPrice,2,MidpointRounding.AwayFromZero);
            return new SaleLine(product.Id,product.Name,product.UnitCode,line.Quantity,line.NetUnitPrice,line.VatRate,net,decimal.Round(net*line.VatRate/100m,2,MidpointRounding.AwayFromZero));
        }).ToArray();
        var year=at.Year.ToString(CultureInfo.InvariantCulture);
        var counter=tx.Read<SaleCounter>("sales","counter/"+year)?.Value ?? 0;
        if(counter>=999999)throw new ArgumentException("Az éves bizonylatszámtartomány megtelt.");
        var number="EL-"+year+"-"+(counter+1).ToString("D6",CultureInfo.InvariantCulture);
        StockLedger.Post(tx,"sale:"+request.OperationId.ToString("N"),"sales",request.OperationId,
            lines.Select(l=>new StockChange(l.ProductId,l.Name,l.UnitCode,warehouse,-l.Quantity)).ToArray(),"Eladás "+number,actor,at);
        var netTotal=lines.Sum(l=>l.Net);var vatTotal=lines.Sum(l=>l.Vat);
        var record=new SaleRecord(request.OperationId,1,number,customer?.Id,customer?.Name??"Alkalmi vevő",warehouse,"HUF",lines,netTotal,vatTotal,netTotal+vatTotal,"vegleges",at,actor,fingerprint);
        tx.Write("sales",key,record);tx.Write("sales","counter/"+year,new SaleCounter(counter+1));return record;
    }
    public static SaleRecord Reverse(ICompanyTransaction tx,SaleReverse request,string actor,DateTimeOffset at)
    {
        var key="sale/"+request.Id.ToString("N");
        var existing=tx.Read<SaleRecord>("sales",key) ?? throw new ArgumentException("Az eladás nem található.");
        if(existing.Status=="visszaforditva")return existing;
        if(existing.Version!=request.Version)throw new ArgumentException("Az eladás közben megváltozott; frissítsd a listát.");
        StockLedger.Post(tx,"sale-reverse:"+request.Id.ToString("N"),"sales",request.Id,
            existing.Lines.Select(l=>new StockChange(l.ProductId,l.Name,l.UnitCode,existing.Warehouse,l.Quantity)).ToArray(),"Eladás visszafordítása "+existing.Number,actor,at);
        var updated=existing with {Version=existing.Version+1,Status="visszaforditva",ReversedAt=at,ReversedBy=actor};tx.Write("sales",key,updated);return updated;
    }
}
