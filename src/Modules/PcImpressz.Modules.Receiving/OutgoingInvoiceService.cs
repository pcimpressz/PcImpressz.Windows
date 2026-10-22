using PcImpressz.Contracts;
using PcImpressz.Modules.Sales;

namespace PcImpressz.Modules.Receiving;

public sealed record IssueSaleInvoiceRequest(Guid CompanyId,Guid SaleId,string PaymentMethod,string? DueDate=null,string? DeliveryDate=null,bool SendEmail=false);

public sealed class OutgoingInvoiceService(ICompanyDatabase database,TimeProvider timeProvider,Func<Guid,string> receivingDirectory)
{
    public async Task<OutgoingInvoiceRecord> IssueSaleAsync(IssueSaleInvoiceRequest request,string actor,CancellationToken ct,HttpClient? client=null)
    {
        if(client is null)throw new InvalidOperationException("A számlakiállító fejlesztési adapter még nem élesíthető. A szolgáltatói API-referencia és a tesztcéges próba szükséges; nem történt számlakiállítás.");
        if(request.SendEmail)throw new ArgumentException("E-mail-küldés ebben a fejlesztési adapterben még nem támogatott.");
        var existing=database.Execute(request.CompanyId,tx=>tx.Read<OutgoingInvoiceRecord>("receiving","outgoing-invoice/"+request.SaleId.ToString("N")));
        if(existing is not null)return existing;
        var prepared=database.Execute(request.CompanyId,tx=>
        {
            var sale=SalesModule.Records(tx).SingleOrDefault(x=>x.Id==request.SaleId)??throw new ArgumentException("Az eladás nem található.");
            if(sale.Status!="vegleges"||sale.ReturnOf is not null)throw new ArgumentException("Csak végleges normál eladás számlázható.");
            var customer=sale.InvoiceCustomer??(sale.CustomerId is Guid id?CompanyReferenceQueries.BillingCustomers(tx).SingleOrDefault(x=>x.Id==id):null)
                ??throw new ArgumentException("A számlához teljes számlázási vevő szükséges.");
            if(new[]{customer.Name,customer.Country,customer.PostalCode,customer.City,customer.Address}.Any(string.IsNullOrWhiteSpace))throw new ArgumentException("A vevő számlázási címe hiányos.");
            var lines=sale.Lines.Select(x=>new BizonylatLine(x.Name,x.Quantity,x.NetUnitPrice,x.VatRate)).ToArray();
            return (sale,customer,lines);
        });
        var secrets=BizonylatConnectionStore.Read(receivingDirectory(request.CompanyId),request.CompanyId)??throw new ArgumentException("A Beállításokban előbb add meg a Bizonylat API-kapcsolatot.");
        var settings=new InvoicingSettingsStore(database).Read(request.CompanyId);
        if(settings.Provider!="bizonylat")throw new ArgumentException("A kiválasztott számlázó integrációja még nincs bekötve.");
        var block=settings.Blocks.FirstOrDefault(b=>b.Id==settings.DefaultBlockId);
        if(block is not null&&block.StartNumber!=1)throw new ArgumentException("A tervezett kezdősorszám szolgáltatói támogatása még ellenőrzendő; nem történt kiállítás.");
        var payment=OutgoingInvoiceRegistry.NormalizePayment(request.PaymentMethod);
        if(payment=="OTHER")throw new ArgumentException("A számlázáshoz válassz készpénz, kártya vagy banki átutalás fizetési módot.");
        var status=payment is "CASH" or "CARD"?"PAID":"UNPAID";
        var apiPayment=payment;
        var issue=new BizonylatIssueRequest(new(prepared.customer.Name,prepared.customer.TaxId,prepared.customer.Country,prepared.customer.PostalCode,prepared.customer.City,prepared.customer.Address),prepared.lines,prepared.sale.Currency,apiPayment,status,request.DueDate,request.DeliveryDate,request.SendEmail,block?.Prefix??secrets.InvoicePrefix,"PC-Impressz eladás: "+prepared.sale.Number);
        var fingerprint=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new{issue,secrets.TestMode,keyHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secrets.ApiKey)))})));
        database.Execute(request.CompanyId,tx=>
        {
            var key="outgoing-attempt/"+request.SaleId.ToString("N");
            if(tx.Read<InvoiceAttempt>("receiving",key) is not null)throw new ArgumentException("Ehhez az eladáshoz már indult számlakiállítás. Az eredmény egyeztetendő; új API-hívás nem indult.");
            tx.Write("receiving",key,new InvoiceAttempt(fingerprint,"unknown",timeProvider.GetUtcNow(),actor));return true;
        });
        var result=await BizonylatInvoiceClient.IssueAsync(secrets,"pcimpressz-sale-"+prepared.sale.Id.ToString("N"),issue,ct,client);
        if(result.NetTotal!=prepared.sale.Net||result.VatTotal!=prepared.sale.Vat||result.GrossTotal!=prepared.sale.Gross||result.Currency!=prepared.sale.Currency||result.PaymentStatus!=status)
            throw new ArgumentException("A szolgáltatói számla összege vagy pénzneme eltér az eladástól. Az eredmény külön egyeztetendő; új kiállítás nem indulhat.");
        var lines=prepared.sale.Lines.Select(x=>new OutgoingInvoiceLine(x.Name,x.UnitCode,"",x.Quantity,x.NetUnitPrice,x.VatRate,x.Net,x.Vat,x.Net+x.Vat)).ToArray();
        var local=new OutgoingInvoiceUpsert(request.CompanyId,prepared.sale.Id,"bizonylat",result.Id,result.InvoiceNumber,prepared.sale.CustomerId,prepared.customer.Name,prepared.customer.TaxId,
            string.IsNullOrWhiteSpace(result.IssueDate)?timeProvider.GetUtcNow().ToString("yyyy-MM-dd"):result.IssueDate,request.DeliveryDate??"",string.IsNullOrWhiteSpace(result.DueDate)?request.DueDate??"":result.DueDate,payment,result.Currency.Length>0?result.Currency:prepared.sale.Currency,
            lines,result.NetTotal,result.VatTotal,result.GrossTotal,"issued","pending",status=="PAID"?"paid":"open",prepared.sale.Id,result.PublicUrl,result.PublicPdfUrl);
        return new OutgoingInvoiceRegistry(database,timeProvider).Upsert(local,actor);
    }
}

internal sealed record InvoiceAttempt(string Fingerprint,string State,DateTimeOffset At,string Actor);
