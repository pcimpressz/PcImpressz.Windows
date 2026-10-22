using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PcImpressz.Business;
using PcImpressz.Contracts;
using PcImpressz.Modules.Partners;
namespace PcImpressz.Modules.Sales;
public sealed partial class SalesModule
{
    private static string Fingerprint(SaleRequest r)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(r.ReturnOf is null?(object)new{r.OperationId,r.CustomerId,r.Warehouse,r.Lines,r.PaymentMethod,r.InvoiceRequested}:new{r.OperationId,r.CustomerId,r.Warehouse,r.Lines,r.PaymentMethod,r.InvoiceRequested,r.ReturnOf},CompanyDocuments.Json))));
    private static string Fold(string value)=>string.Concat(value.Normalize(NormalizationForm.FormD).Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)).ToUpperInvariant();
    private static bool Match(string value,string search)=>Fold(search).Split(' ',StringSplitOptions.RemoveEmptyEntries).All(term=>Fold(value).Contains(term,StringComparison.Ordinal));
    private static void RegisterCheckout(IModuleContext context)
    {
        DomainCommands.Register(context,"checkout-info","sales.read",CommandAccess.RecoveryReadOnly,(tx,input,actor)=>DomainCommands.Json(new{version=2}));
        DomainCommands.Register(context,"references","sales.read",CommandAccess.RecoveryReadOnly,(tx,input,_)=>
        {
            var warehouse=input.TryGetProperty("warehouse",out var wh)?StockLedger.Warehouse(wh.GetString()):"Bolt";
            var quantities=StockLedger.Balances(tx).Where(b=>b.Warehouse==warehouse).ToDictionary(b=>b.ProductId,b=>b.Quantity);
            var search=DomainCommands.Search(input);var products=CompanyReferenceQueries.SearchProducts(tx,search,"salable",checkout:true);
            var exact=search.Length>0?products.Where(p=>p.Barcode.Length>0&&p.Barcode.Equals(search,StringComparison.OrdinalIgnoreCase)).ToArray():[];
            var matching=exact.Length>0?exact:products.Where(p=>Match(p.Name+" "+p.Sku+" "+p.Barcode+" "+p.ManufacturerPartNumber,search)).ToArray();
            var demo=tx.Get("demo",DemoDataset.Marker) is not null||tx.Get("demo",DemoDataset.LegacyMarker) is not null;
            return DomainCommands.Json(new{
                products=matching.Take(200).Select(p=>
                {
                    var price=p.SaleNetUnitPrice;var source=price is not null?"Terméktörzs":"Nincs megadott ár";
                    if(price is null&&demo&&p.Sku.StartsWith("DEMO-",StringComparison.Ordinal))
                    {
                        var entry=p.Parameters.Split('\n').Select(line=>line.Split('=',2)).FirstOrDefault(pair=>pair.Length==2&&pair[0].Trim()=="névleges tesztár");
                        if(entry is not null&&decimal.TryParse(entry[1].Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var demoPrice)&&demoPrice is >=0 and <=1_000_000_000){price=demoPrice;source="Demó tesztár";}
                    }
                    return new{p.Id,p.Name,p.Sku,p.Barcode,p.ManufacturerPartNumber,p.UnitCode,availableQuantity=StockReservations.Free(tx,p.Id,warehouse,context.TimeProvider.GetUtcNow()),physicalQuantity=quantities.GetValueOrDefault(p.Id),reservedQuantity=StockReservations.Quantity(tx,p.Id,warehouse,context.TimeProvider.GetUtcNow()),netUnitPrice=price,vatRate=p.SaleVatRate,priceSource=source};
                }),
                customers=CompanyReferenceQueries.BillingCustomers(tx).Where(p=>p.IsActive&&(p.IsCustomer||input.TryGetProperty("allPartners",out var all)&&all.ValueKind==JsonValueKind.True)&&Match(p.Name+" "+p.TaxId,DomainCommands.Search(input,"customerSearch"))).Take(50)});
        });
        DomainCommands.Register(context,"customer-save","sales.write",CommandAccess.Licensed,(tx,input,actor)=>
        {
            var request=DomainCommands.Read<CheckoutCustomerSave>(input);
            var data=PartnerValidation.Normalize(new PartnerData{Name=request.Name,TaxId=request.TaxId,Country=request.Country,PostalCode=request.PostalCode,City=request.City,Address=request.Address,IsCustomer=true,IsActive=true});
            if(new[]{data.Name,data.Country,data.PostalCode,data.City,data.Address}.Any(string.IsNullOrWhiteSpace))throw new ArgumentException("A vevő neve és teljes címe szükséges.");
            var repository=new PartnerRepository(context.GetCompanyDataDirectory,context.TimeProvider,new TransactionDatabase(request.CompanyId,tx));
            var partners=repository.Snapshot(request.CompanyId).GetAwaiter().GetResult().Partners;
            var tax=TaxKey(data.TaxId,data.Country);
            var matches=partners.Where(p=>p.Data.IsActive&&(tax.Length>0?TaxKey(p.Data.TaxId,p.Data.Country)==tax:p.Data.Name.Equals(data.Name,StringComparison.OrdinalIgnoreCase)&&p.Data.Country==data.Country&&p.Data.PostalCode==data.PostalCode&&p.Data.City==data.City&&p.Data.Address==data.Address)).ToArray();
            if(matches.Length>1)throw new ArgumentException("Több partner tartozik ehhez az adószámhoz. Válassz meglévő vevőt.");
            if(matches.FirstOrDefault() is {} existing)
            {
                data=existing.Data with{IsCustomer=true,Country=Fill(existing.Data.Country,data.Country),PostalCode=Fill(existing.Data.PostalCode,data.PostalCode),City=Fill(existing.Data.City,data.City),Address=Fill(existing.Data.Address,data.Address)};
                if(data!=existing.Data)repository.Save(new(request.CompanyId,existing.Id,existing.Version,data),actor).GetAwaiter().GetResult();
                return DomainCommands.Json(CompanyReferenceQueries.BillingCustomers(tx).Single(p=>p.Id==existing.Id));
            }
            var saved=repository.Save(new(request.CompanyId,null,0,data),actor).GetAwaiter().GetResult();
            return DomainCommands.Json(CompanyReferenceQueries.BillingCustomers(tx).Single(p=>p.Id==saved.Id));
        });
    }
    private static string Fill(string current,string incoming)=>string.IsNullOrWhiteSpace(current)?incoming:current;
    private static string TaxKey(string tax,string country)
    {
        var key=PartnerValidation.TaxKey(tax);return country=="HU"&&(key.Length is 8 or 11)&&key.All(char.IsAsciiDigit)?key[..8]:key;
    }
    private sealed class TransactionDatabase(Guid company,ICompanyTransaction tx):ICompanyDatabase
    {
        public T Execute<T>(Guid id,Func<ICompanyTransaction,T> action)=>id==company?action(tx):throw new ArgumentException("Eltérő cég.");
    }
}
