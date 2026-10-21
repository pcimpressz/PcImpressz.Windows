using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using PcImpressz.Business;
using PcImpressz.Contracts;
using PcImpressz.Core.Companies;
using PcImpressz.Core.Storage;
using PcImpressz.Modules.Partners;
using PcImpressz.Modules.Receiving;
using PcImpressz.Modules.Sales;
namespace PcImpressz.Core.Tests;
internal static class CheckoutTests
{
    public static IEnumerable<(string,Func<Task>)> Cases()
    {
        yield return ("cash rounding boundaries and card exact amount",()=>
        {
            foreach(var item in new (decimal Input,decimal Expected)[]{(0,0),(1,0),(2.49m,0),(2.5m,5),(4.99m,5),(5,5),(7.49m,5),(7.5m,10),(9.99m,10),(127,125),(128,130)})
            {Check.Equal(item.Expected,PaymentRounding.Payable(item.Input,"cash"));Check.Equal(item.Input,PaymentRounding.Payable(item.Input,"card"));}
            using var f=new Fixture();var product=f.Product();
            var sale=f.Database.Execute(f.Company,tx=>SalesModule.Post(tx,new(f.Company,Guid.NewGuid(),null,"Bolt",[new(product.Id,1,100,27)],"cash"),"test",DateTimeOffset.UtcNow));
            Check.Equal(127m,sale.Gross);Check.Equal(-2m,sale.Rounding);Check.Equal(125m,sale.Payable!.Value);
            var reversed=f.Database.Execute(f.Company,tx=>SalesModule.Reverse(tx,new(f.Company,sale.Id,1),"test",DateTimeOffset.UtcNow));Check.Equal(sale.Payable,reversed.Payable);Check.Equal(sale.Rounding,reversed.Rounding);
            return Task.CompletedTask;
        });
        yield return ("checkout stores payment invoice request and immutable billing snapshot and rejects changed retry",()=>
        {
            using var f=new Fixture();var customer=f.Database.Execute(f.Company,tx=>CompanyReferenceQueries.Partners(tx).First(p=>p.IsCustomer));var product=f.Product();
            var request=new SaleRequest(f.Company,Guid.NewGuid(),customer.Id,"Bolt",[new(product.Id,1,100,27)],"card",true);
            var sale=f.Database.Execute(f.Company,tx=>SalesModule.Post(tx,request,"test/seller",DateTimeOffset.UtcNow));Check.Equal("card",sale.PaymentMethod);Check.True(sale.InvoiceRequested);Check.Equal(customer.Name,sale.InvoiceCustomer!.Name);
            var partners=new PartnerRepository(id=>f.Companies.ModuleDataDirectory(id,"partners"),TimeProvider.System,f.Database);var old=partners.Snapshot(f.Company).Result.Partners.Single(p=>p.Id==customer.Id);partners.Save(new(f.Company,old.Id,old.Version,old.Data with{Name="Megváltozott név"}),"test").GetAwaiter().GetResult();
            var retry=f.Database.Execute(f.Company,tx=>SalesModule.Post(tx,request,"test/seller",DateTimeOffset.UtcNow));Check.Equal(sale.InvoiceCustomer,retry.InvoiceCustomer);
            Check.Throws<ArgumentException>(()=>f.Database.Execute(f.Company,tx=>SalesModule.Post(tx,request with{PaymentMethod="cash"},"test",DateTimeOffset.UtcNow)));
            Check.Throws<ArgumentException>(()=>f.Database.Execute(f.Company,tx=>SalesModule.Post(tx,request with{OperationId=Guid.NewGuid(),CustomerId=null},"test",DateTimeOffset.UtcNow)));
            Check.Throws<ArgumentException>(()=>f.Database.Execute(f.Company,tx=>SalesModule.Post(tx,request with{OperationId=Guid.NewGuid(),PaymentMethod="unknown"},"test",DateTimeOffset.UtcNow)));
            return Task.CompletedTask;
        });
        yield return ("legacy sale retry remains readable without inventing historical payment",()=>
        {
            using var f=new Fixture();var r=new SaleRequest(f.Company,Guid.NewGuid(),null,"Bolt",[new(f.Product().Id,1,100,27)]);
            var sale=f.Database.Execute(f.Company,tx=>SalesModule.Post(tx,r,"test",DateTimeOffset.UtcNow));
            var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{r.OperationId,r.CustomerId,r.Warehouse,r.Lines},CompanyDocuments.Json))));
            f.Database.Execute(f.Company,tx=>{tx.Write("sales","sale/"+r.OperationId.ToString("N"),sale with{PaymentMethod=null,Fingerprint=fingerprint});return 0;});
            var retry=f.Database.Execute(f.Company,tx=>SalesModule.Post(tx,r,"test",DateTimeOffset.UtcNow));Check.True(retry.PaymentMethod is null);Check.Equal(sale.Id,retry.Id);return Task.CompletedTask;
        });
        yield return ("checkout search folds accents prioritizes exact barcode and exposes catalog price",async()=>
        {
            using var f=new Fixture();await f.Module.StartAsync(f.Context,CancellationToken.None);
            var first=f.Product();var named=await f.Execute("references",new{search="eger",warehouse="Bolt"});Check.True(named.GetProperty("products").GetArrayLength()>0);
            var exact=await f.Execute("references",new{search=first.Barcode,warehouse="Bolt"});Check.Equal(1,exact.GetProperty("products").GetArrayLength());Check.Equal(first.SaleNetUnitPrice!.Value,exact.GetProperty("products")[0].GetProperty("netUnitPrice").GetDecimal());
        });
        yield return ("checkout customer creation deduplicates tax root and only adds customer role to supplier",async()=>
        {
            using var f=new Fixture();await f.Module.StartAsync(f.Context,CancellationToken.None);
            var input=new CheckoutCustomerSave(f.Company,"Vevő Teszt","11111111-2-42","HU","1111","Budapest","Minta utca 1.");
            var first=await f.Execute("customer-save",input);var again=await f.Execute("customer-save",input with{TaxId="11111111",Name="NAV-ból új név"});Check.Equal(first.GetProperty("id").GetGuid(),again.GetProperty("id").GetGuid());Check.Equal("Vevő Teszt",again.GetProperty("name").GetString());
            var repo=new PartnerRepository(id=>f.Companies.ModuleDataDirectory(id,"partners"),TimeProvider.System,f.Database);var supplier=await repo.Save(new(f.Company,null,0,new(){Name="Szállító Teszt",TaxId="22222222-2-42",IsSupplier=true,IsActive=true,Country="HU",PostalCode="2222",City="Szeged",Address="Eredeti cím"}),"test");
            var promoted=await f.Execute("customer-save",input with{TaxId="22222222",Name="Ne írja felül",Address="Ne változzon"});Check.Equal(supplier.Id,promoted.GetProperty("id").GetGuid());var after=(await repo.Snapshot(f.Company)).Partners.Single(p=>p.Id==supplier.Id);Check.True(after.Data.IsCustomer&&after.Data.IsSupplier);Check.Equal("Eredeti cím",after.Data.Address);
        });
        yield return ("NAV SHA3 fallback matches FIPS vectors and native hashes across block boundaries",()=>
        {
            Check.Equal("A69F73CCA23A9AC5C8B567DC185A756E97C982164FE25859E0D1DCC1475C80A615B2123AF1F5F94C11E3E9402C3AC558F500199D95B6D3E301758586281DCD26",Convert.ToHexString(NavSha3.Hash([])));
            Check.Equal("B751850B1A57168A5693CD924B6B096E08F621827444F70D884F5D0240D2712E10E116E9192AF3C91A7EC57647E3934057340B4CF408D5A56592F8274EEC53F0",Convert.ToHexString(NavSha3.Hash("abc"u8)));
            if(SHA3_512.IsSupported)foreach(var n in new[]{1,71,72,73,144,1000}){var bytes=Enumerable.Range(0,n).Select(i=>(byte)i).ToArray();Check.True(SHA3_512.HashData(bytes).SequenceEqual(NavSha3.Hash(bytes)));}return Task.CompletedTask;
        });
        yield return ("NAV taxpayer client signs real protocol request parses HQ and rejects invalid taxpayer and XML entities",async()=>
        {
            var company=Guid.NewGuid();var secrets=new NavConnectionSecrets("99999999","test-user","test-password<&","test-sign-key","unused-exchange",true);
            using var client=new HttpClient(new NavHandler(async request=>
            {
                Check.Equal("https://api-test.onlineszamla.nav.gov.hu/invoiceService/v3/queryTaxpayer",request.RequestUri!.ToString());
                var text=await request.Content!.ReadAsStringAsync();Check.True(!text.Contains(secrets.Password)&&!text.Contains(secrets.SignKey));
                var xml=System.Xml.Linq.XDocument.Parse(text);var id=xml.Descendants().Single(e=>e.Name.LocalName=="requestId").Value;
                return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Response(id,"true"))};
            }));
            var result=await NavTaxpayerClient.QueryAsync(secrets,new(company,"11111111-2-42"),CancellationToken.None,client);Check.Equal("NAV Teszt Kft.",result.Name);Check.Equal("11111111-2-42",result.TaxId);Check.Equal("Budapest",result.City);Check.True(result.Address.Contains("Minta"));
            Check.Throws<ArgumentException>(()=>NavTaxpayerClient.Parse(Response("id","false"),company,"11111111","id"));Check.Throws<ArgumentException>(()=>NavTaxpayerClient.Parse(Response("other","true"),company,"11111111","id"));Check.Throws<ArgumentException>(()=>NavTaxpayerClient.Parse(Response("id","true"),company,"22222222","id"));
            Check.Throws<XmlException>(()=>NavTaxpayerClient.Parse("<!DOCTYPE x [<!ENTITY a SYSTEM 'file:///not-read'>]><x>&a;</x>",company,"11111111"));
        });
    }
    private static string Response(string id,string valid)=>$"<QueryTaxpayerResponse xmlns='http://schemas.nav.gov.hu/OSA/3.0/api' xmlns:c='http://schemas.nav.gov.hu/NTCA/1.0/common' xmlns:b='http://schemas.nav.gov.hu/OSA/3.0/base'><c:header><c:requestId>{id}</c:requestId></c:header><c:result><c:funcCode>OK</c:funcCode></c:result><taxpayerValidity>{valid}</taxpayerValidity><taxpayerData><taxpayerName>NAV Teszt Kft.</taxpayerName><taxNumberDetail><b:taxpayerId>11111111</b:taxpayerId><b:vatCode>2</b:vatCode><b:countyCode>42</b:countyCode></taxNumberDetail><taxpayerAddressList><taxpayerAddressItem><taxpayerAddressType>HQ</taxpayerAddressType><taxpayerAddress><b:countryCode>HU</b:countryCode><b:postalCode>1111</b:postalCode><b:city>Budapest</b:city><b:streetName>Minta</b:streetName><b:publicPlaceCategory>utca</b:publicPlaceCategory><b:number>1.</b:number></taxpayerAddress></taxpayerAddressItem></taxpayerAddressList></taxpayerData></QueryTaxpayerResponse>";
    private sealed class NavHandler(Func<HttpRequestMessage,Task<HttpResponseMessage>> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>send(request);}
    private sealed class Fixture:IDisposable
    {
        private readonly TestDirectory _dir=new();public CompanyStore Companies{get;}public CompanyDatabase Database{get;}public Guid Company{get;}public SalesModule Module{get;}=new();public Context Context{get;}
        public Fixture(){Companies=new(_dir.Path);Company=Companies.Create("Próba",true).Id;Database=new(Companies.DataDirectory);DemoDataSeeder.Seed(id=>Companies.ModuleDataDirectory(id,"receiving"),Database,Company,TimeProvider.System,"test");Context=new(Database,id=>Companies.ModuleDataDirectory(id,"sales"));}
        public ProductReference Product()=>Database.Execute(Company,tx=>CompanyReferenceQueries.Products(tx).Single(p=>p.Sku=="DEMO-001"));
        public Task<JsonElement> Execute(string command,object data)
        {
            var node=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(data,CompanyDocuments.Json))!.AsObject();node["companyId"]=Company;
            return ((IContextualCommandHandler)Context.Handlers[command]).ExecuteAsync(JsonSerializer.SerializeToElement(node),new("test/seller"),CancellationToken.None).AsTask();
        }
        public void Dispose()=>_dir.Dispose();
    }
    private sealed class Context(ICompanyDatabase database,Func<Guid,string> directory):IModuleContext
    {
        public Dictionary<string,ICommandHandler> Handlers{get;}=[];public ICompanyDatabase Database=>database;public string ModuleId=>"sales";public TimeProvider TimeProvider=>TimeProvider.System;
        public void RegisterCommand(ICommandHandler handler)=>Handlers.Add(handler.Descriptor.Name,handler);public void RegisterPage(EntityPageDescriptor page){}public string GetCompanyDataDirectory(Guid id)=>directory(id);
    }
}
