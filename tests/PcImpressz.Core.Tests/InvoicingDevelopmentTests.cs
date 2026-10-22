using System.Net;
using System.Net.Http;
using System.Text.Json;
using PcImpressz.Contracts;
using PcImpressz.Core.Companies;
using PcImpressz.Core.Storage;
using PcImpressz.Modules.Receiving;
using PcImpressz.Modules.Sales;

namespace PcImpressz.Core.Tests;
internal static class InvoicingDevelopmentTests
{
    public static IEnumerable<(string,Func<Task>)> Cases()
    {
        yield return ("invoicing settings isolate companies and validate blocks and planned numbering",()=>
        {
            using var f=new Fixture();var settings=new InvoicingSettingsStore(f.Database);var block=new InvoiceBlock(Guid.NewGuid(),"Bolt","bolt",100);
            var saved=settings.Save(f.Company,new("bizonylat",block.Id,[block]));Check.Equal("BOLT",saved.Blocks.Single().Prefix);Check.Equal(100L,saved.Blocks.Single().StartNumber);
            var other=f.Companies.Create("Másik",true).Id;Check.Equal(0,settings.Read(other).Blocks.Length);
            Check.Throws<ArgumentException>(()=>settings.Save(f.Company,new("other",null,[])));
            Check.Throws<ArgumentException>(()=>settings.Save(f.Company,new("billingo",block.Id,[block,block with{Id=Guid.NewGuid()}])));
            Check.Throws<ArgumentException>(()=>settings.Save(f.Company,new("szamlazz",block.Id,[block with{StartNumber=0}])));
            settings.Save(f.Company,new("billingo",block.Id,[block]));Check.Equal("billingo",settings.Read(f.Company).Provider);
            return Task.CompletedTask;
        });
        yield return ("outgoing registry keeps issued data immutable and sums each currency separately",()=>
        {
            using var f=new Fixture();var registry=new OutgoingInvoiceRegistry(f.Database,TimeProvider.System);var a=registry.Upsert(f.Invoice("A-1"),"test");
            registry.Upsert(f.Invoice("B-1") with{Currency="EUR"},"test");
            var list=ReceivingJson.Element(registry.List(new(f.Company)));Check.Equal(2,list.GetProperty("total").GetInt32());Check.Equal(2,list.GetProperty("summary").GetProperty("currencies").GetArrayLength());Check.Equal(JsonValueKind.Null,list.GetProperty("summary").GetProperty("gross").ValueKind);
            Check.Throws<ArgumentException>(()=>registry.Upsert(f.Invoice("A-1") with{Id=a.Id,ProviderInvoiceId=a.ProviderInvoiceId,Net=1000},"test"));
            Check.Throws<ArgumentException>(()=>registry.List(new(f.Company,From:"2026-10-22",To:"2026-10-01")));
            Check.Equal(0,ReceivingJson.Element(registry.List(new(f.Company,Search:"not-found"))).GetProperty("total").GetInt32());
            return Task.CompletedTask;
        });
        yield return ("NAV reconciliation never rewrites invoice amounts and rejects incomplete or modified matches",()=>
        {
            using var f=new Fixture();var registry=new OutgoingInvoiceRegistry(f.Database,TimeProvider.System);var invoice=registry.Upsert(f.Invoice("R-1"),"test");
            var nav=new NavInvoiceDigest("R-1","Own","99999999","2026-10-22","HUF","CREATE",100,CustomerTaxNumber:"11111111",Vat:27,Gross:127);
            Check.Equal(1,registry.Reconcile(f.Company,"2026-10-01","2026-10-22",[nav],"test").Matched);
            Check.Equal(1,registry.Reconcile(f.Company,"2026-10-01","2026-10-22",[nav with{Gross=null}],"test").Mismatched);
            Check.Equal(1,registry.Reconcile(f.Company,"2026-10-01","2026-10-22",[nav with{Currency="EUR"}],"test").Mismatched);
            Check.Equal(1,registry.Reconcile(f.Company,"2026-10-01","2026-10-22",[nav,nav with{Operation="MODIFY"}],"test").Mismatched);
            var after=f.Database.Execute(f.Company,tx=>tx.Read<OutgoingInvoiceRecord>("receiving","outgoing-invoice/"+invoice.Id.ToString("N")))!;
            Check.Equal(127m,after.Gross);Check.Equal(100m,after.Net);
            Check.Equal(1,registry.Reconcile(f.Company,"2026-10-01","2026-10-22",[],"test").MissingAtNav);
            return Task.CompletedTask;
        });
        yield return ("Bizonylat key remains DPAPI protected and company bound",()=>
        {
            if(!OperatingSystem.IsWindows())return Task.CompletedTask;
            using var f=new Fixture();var dir=f.Directory();const string key="bz_live_synthetic_test_key_only";
            BizonylatConnectionStore.Save(dir,f.Company,new(f.Company,key,true,"BOLT"));
            Check.True(!File.ReadAllText(Path.Combine(dir,BizonylatConnectionStore.FileName)).Contains(key));
            Check.True(!JsonSerializer.Serialize(BizonylatConnectionStore.Status(dir,f.Company)).Contains(key));
            Check.Equal(key,BizonylatConnectionStore.Read(dir,f.Company)!.ApiKey);
            Check.Throws<IOException>(()=>BizonylatConnectionStore.Read(dir,Guid.NewGuid()));
            Check.Throws<ArgumentException>(()=>BizonylatConnectionStore.Save(dir,f.Company,new(Guid.NewGuid(),key,true,"X")));
            return Task.CompletedTask;
        });
        yield return ("invoice adapter records success once and reopens same invoice without another HTTP call",async()=>
        {
            if(!OperatingSystem.IsWindows())return;
            using var f=new Fixture();var sale=f.Sale();var calls=0;
            using var client=new HttpClient(new Handler(request=>
            {
                calls++;Check.Equal("pcimpressz-sale-"+sale.Id.ToString("N"),request.Headers.GetValues("Idempotency-Key").Single());
                return Task.FromResult(Response("{\"id\":\"provider-1\",\"invoiceNumber\":\"BOLT-1\",\"netTotal\":100,\"vatTotal\":27,\"grossTotal\":127,\"currency\":\"HUF\",\"paymentStatus\":\"PAID\",\"issueDate\":\"2026-10-22\"}"));
            }));
            var service=new OutgoingInvoiceService(f.Database,TimeProvider.System,_=>f.Directory());var request=new IssueSaleInvoiceRequest(f.Company,sale.Id,"CARD");
            var one=await service.IssueSaleAsync(request,"test",CancellationToken.None,client);var two=await service.IssueSaleAsync(request,"test",CancellationToken.None,client);Check.Equal(one.Id,two.Id);Check.Equal(1,calls);Check.Equal("pending",one.NavStatus);
            await Check.ThrowsAsync<InvalidOperationException>(()=>service.IssueSaleAsync(request,"test",CancellationToken.None));
        });
        yield return ("uncertain invoice attempt survives service restart and forbids blind resend",async()=>
        {
            if(!OperatingSystem.IsWindows())return;
            using var f=new Fixture();var sale=f.Sale();var calls=0;using var client=new HttpClient(new Handler(_=>{calls++;throw new HttpRequestException("synthetic timeout");}));
            var request=new IssueSaleInvoiceRequest(f.Company,sale.Id,"CARD");
            await Check.ThrowsAsync<HttpRequestException>(()=>new OutgoingInvoiceService(f.Database,TimeProvider.System,_=>f.Directory()).IssueSaleAsync(request,"test",CancellationToken.None,client));
            await Check.ThrowsAsync<ArgumentException>(()=>new OutgoingInvoiceService(f.Database,TimeProvider.System,_=>f.Directory()).IssueSaleAsync(request,"test",CancellationToken.None,client));Check.Equal(1,calls);
        });
        yield return ("provider response needs explicit totals and errors never echo remote secret text",async()=>
        {
            var secret=new BizonylatConnectionSecrets("synthetic-secret",true,"");var request=new BizonylatIssueRequest(new("A","","HU","1","B","C"),[new("x",1,100,27)],"HUF","CARD","PAID",null,null,false);
            using(var client=new HttpClient(new Handler(_=>Task.FromResult(Response("{\"id\":\"a\",\"invoiceNumber\":\"b\"}")))))await Check.ThrowsAsync<ArgumentException>(()=>BizonylatInvoiceClient.IssueAsync(secret,"test-key",request,CancellationToken.None,client));
            using var errorClient=new HttpClient(new Handler(_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest){Content=new StringContent("{\"message\":\"synthetic-secret\"}")})));
            try{await BizonylatInvoiceClient.IssueAsync(secret,"test-key",request,CancellationToken.None,errorClient);throw new Exception("Expected error");}catch(ArgumentException ex){Check.True(!ex.Message.Contains("synthetic-secret"));}
        });
    }
    private static HttpResponseMessage Response(string json)=>new(HttpStatusCode.OK){Content=new StringContent(json)};
    private sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> send):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>send(request);}
    private sealed class Fixture:IDisposable
    {
        private readonly TestDirectory _dir=new();public CompanyStore Companies {get;}public CompanyDatabase Database {get;}public Guid Company {get;}
        public Fixture(){Companies=new(_dir.Path);Company=Companies.Create("Számla próba",true).Id;Database=new(Companies.DataDirectory);}
        public string Directory()=>Companies.ModuleDataDirectory(Company,"receiving");
        public OutgoingInvoiceUpsert Invoice(string number)=>new(Company,Guid.NewGuid(),"bizonylat",Guid.NewGuid().ToString("N"),number,null,"Vevő","11111111","2026-10-22","","2026-10-30","CARD","HUF",[new("Egér","DB","M",1,100,27,100,27,127)],100,27,127,"issued");
        public SaleRecord Sale()
        {
            DemoDataSeeder.Seed(id=>Companies.ModuleDataDirectory(id,"receiving"),Database,Company,TimeProvider.System,"test");
            var sale=Database.Execute(Company,tx=>{var customer=CompanyReferenceQueries.BillingCustomers(tx).First(c=>c.IsActive&&c.IsCustomer);var product=CompanyReferenceQueries.Products(tx).First(p=>p.Sku=="DEMO-001");return SalesModule.Post(tx,new(Company,Guid.NewGuid(),customer.Id,"Bolt",[new(product.Id,1,100,27)],"card",true),"test",DateTimeOffset.UtcNow);});
            BizonylatConnectionStore.Save(Directory(),Company,new(Company,"bz_live_synthetic_key_only",true,"BOLT"));return sale;
        }
        public void Dispose()=>_dir.Dispose();
    }
}
