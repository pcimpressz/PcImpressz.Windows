using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using PcImpressz.Business;
using PcImpressz.Contracts;
using PcImpressz.Core.Companies;
using PcImpressz.Core.Storage;
using PcImpressz.Modules.Receiving;
namespace PcImpressz.Core.Tests;
internal static class NavInvoiceTests
{
    private static readonly NavConnectionSecrets Secrets=new("99999999","test-user","test-password","test-sign-key","test-exchange",true);
    public static IEnumerable<(string,Func<Task>)> Cases()
    {
        yield return ("NAV inbound digest signs request and preserves pagination",async()=>
        {
            using var client=new HttpClient(new Handler(async request=>
            {
                var xml=XDocument.Parse(await request.Content!.ReadAsStringAsync());Check.Equal("QueryInvoiceDigestRequest",xml.Root!.Name.LocalName);Check.Equal("INBOUND",Value(xml,"invoiceDirection"));Check.Equal("2",Value(xml,"page"));Check.Equal("2026-10-01",Value(xml,"dateFrom"));Check.True(Value(xml,"requestSignature").Length==128);
                return Response("QueryInvoiceDigestResponse",Value(xml,"requestId"),"<invoiceDigestResult><currentPage>2</currentPage><availablePage>3</availablePage><invoiceDigest><invoiceNumber>NAV-1</invoiceNumber><invoiceOperation>CREATE</invoiceOperation><supplierName>Teszt Szállító</supplierName><supplierTaxNumber>11111111</supplierTaxNumber><invoiceIssueDate>2026-10-21</invoiceIssueDate><currency>HUF</currency><invoiceNetAmount>200</invoiceNetAmount></invoiceDigest></invoiceDigestResult>");
            }));
            var page=await NavInvoiceClient.ListAsync(Secrets,new(Guid.NewGuid(),"2026-10-01","2026-10-21",2),CancellationToken.None,client);Check.Equal(2,page.CurrentPage);Check.Equal(3,page.AvailablePage);Check.Equal("NAV-1",page.Rows.Single().Number);
            Check.Throws<ArgumentException>(()=>NavInvoiceClient.Validate(new(Guid.NewGuid(),"2026-01-01","2026-10-21")));
        });
        yield return ("NAV invoice download handles gzip and verifies invoice supplier buyer and correlation",async()=>
        {
            foreach(var compressed in new[]{false,true})
            {
                using var client=DataClient(Invoice(),compressed);var doc=await NavInvoiceClient.DownloadAsync(Secrets,new(Guid.NewGuid(),"NAV-1","11111111"),CancellationToken.None,client);Check.Equal("NAV-1",doc.Number);Check.Equal(2m,doc.Lines.Single().Quantity);Check.Equal(254m,doc.ExpectedGross!.Value);
            }
            using(var wrongBuyer=DataClient(Invoice().Replace("99999999","88888888"),false))await Check.ThrowsAsync<ArgumentException>(()=>NavInvoiceClient.DownloadAsync(Secrets,new(Guid.NewGuid(),"NAV-1","11111111"),CancellationToken.None,wrongBuyer));
            using(var wrongSupplier=DataClient(Invoice(),false))await Check.ThrowsAsync<ArgumentException>(()=>NavInvoiceClient.DownloadAsync(Secrets,new(Guid.NewGuid(),"NAV-1","22222222"),CancellationToken.None,wrongSupplier));
            using(var wrongNumber=DataClient(Invoice(),false))await Check.ThrowsAsync<ArgumentException>(()=>NavInvoiceClient.DownloadAsync(Secrets,new(Guid.NewGuid(),"OTHER","11111111"),CancellationToken.None,wrongNumber));
            using(var wrongId=DataClient(Invoice(),false,true))await Check.ThrowsAsync<ArgumentException>(()=>NavInvoiceClient.DownloadAsync(Secrets,new(Guid.NewGuid(),"NAV-1","11111111"),CancellationToken.None,wrongId));
            using(var modified=DataClient(Invoice().Replace("<invoiceMain>","<invoiceReference><originalInvoiceNumber>OLD</originalInvoiceNumber></invoiceReference><invoiceMain>"),false))Check.Equal("MODIFY",(await NavInvoiceClient.DownloadAsync(Secrets,new(Guid.NewGuid(),"NAV-1","11111111"),CancellationToken.None,modified)).Operation);
            using(var bomb=DataClient(new string('x',5*1024*1024),true))await Check.ThrowsAsync<ArgumentException>(()=>NavInvoiceClient.DownloadAsync(Secrets,new(Guid.NewGuid(),"NAV-1","11111111"),CancellationToken.None,bomb));
            using(var xxe=new HttpClient(new Handler(_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///not-read'>]><x>&e;</x>")}))))await Check.ThrowsAsync<XmlException>(()=>NavInvoiceClient.ListAsync(Secrets,new(Guid.NewGuid(),"2026-10-01","2026-10-21"),CancellationToken.None,xxe));
        });
        yield return ("NAV import remains new idempotent and never changes stock before classification",async()=>
        {
            using var dir=new TestDirectory();var companies=new CompanyStore(dir.Path);var company=companies.Create("NAV próba",true).Id;var database=new CompanyDatabase(companies.DataDirectory);DemoDataSeeder.Seed(id=>companies.ModuleDataDirectory(id,"receiving"),database,company,TimeProvider.System,"test");
            var supplier=database.Execute(company,tx=>CompanyReferenceQueries.Partners(tx).First(p=>p.IsSupplier));var product=database.Execute(company,tx=>CompanyReferenceQueries.Products(tx).Single(p=>p.Sku=="DEMO-001"));var before=database.Execute(company,tx=>CompanyReferenceQueries.StockQuantity(tx,product.Id));
            var doc=new ImportedDocument("NAV-UNIQUE","2026-10-21",null,supplier.Name,supplier.TaxId,[new(product.Name,product.Sku,"",2,100,27,"DB")],ExpectedNet:200,ExpectedVat:54,ExpectedGross:254);
            var repo=new ReceivingRepository(id=>companies.ModuleDataDirectory(id,"receiving"),TimeProvider.System,database);var request=new NavInvoiceImport(company,"NAV-UNIQUE",supplier.TaxId,supplier.Id);
            var preview=await repo.PreviewNav(request with{SupplierId=null},doc);
            Check.True(preview.Existing is null);Check.Equal(product.Id,preview.Lines.Single().ProductId!.Value);
            var unknown=doc with{Number="UNKNOWN",SupplierTaxId="55555555"};
            var missing=await repo.PreviewNav(new(company,"UNKNOWN","55555555"),unknown);Check.True(missing.Existing is null);Check.Equal(1,missing.Lines.Length);
            Check.True(!(await repo.Snapshot(company)).Receipts.Any(r=>r.Data.Number is "UNKNOWN" or "NAV-UNIQUE"));
            Check.Equal(before,database.Execute(company,tx=>CompanyReferenceQueries.StockQuantity(tx,product.Id)));
            var digest=new NavInvoicePage(1,1,[new("NAV-UNIQUE",supplier.Name,supplier.TaxId,"2026-10-21","HUF","CREATE",200)]);
            Check.True(!(await repo.MarkNavPage(company,digest)).Rows.Single().Received);
            var first=await repo.ImportNav(request,doc,"test");var again=await repo.ImportNav(request,doc,"test");Check.Equal(first.Id,again.Id);Check.Equal("uj",first.Data.Status);Check.True(!first.Data.StockPosted);Check.Equal(before,database.Execute(company,tx=>CompanyReferenceQueries.StockQuantity(tx,product.Id)));
            var posted=await repo.Classify(new(company,first.Id,first.Version,"bevetelezve"),"test");Check.Equal(before+2,database.Execute(company,tx=>CompanyReferenceQueries.StockQuantity(tx,product.Id)));
            var marked=(await repo.MarkNavPage(company,digest)).Rows.Single();Check.True(marked.Received);Check.Equal(posted.Id,marked.ReceiptId!.Value);
            Check.True(!(await repo.MarkNavPage(company,digest with{Rows=[digest.Rows[0] with{SupplierTaxNumber="55555555"}]})).Rows.Single().Received);
            var third=await repo.ImportNav(request,doc,"test");Check.Equal(posted.Id,third.Id);Check.Equal("bevetelezve",third.Data.Status);Check.Equal(before+2,database.Execute(company,tx=>CompanyReferenceQueries.StockQuantity(tx,product.Id)));
            await repo.Reverse(new(company,posted.Id,posted.Version,"Teszt visszavétel"),"test");Check.True(!(await repo.MarkNavPage(company,digest)).Rows.Single().Received);
        });
    }
    private static string Invoice()=>"<InvoiceData xmlns='http://schemas.nav.gov.hu/OSA/3.0/data'><invoiceNumber>NAV-1</invoiceNumber><invoiceIssueDate>2026-10-21</invoiceIssueDate><invoiceMain><invoice><invoiceHead><supplierInfo><supplierTaxNumber><taxpayerId>11111111</taxpayerId><vatCode>2</vatCode><countyCode>42</countyCode></supplierTaxNumber><supplierName>Teszt Szállító</supplierName></supplierInfo><customerInfo><customerTaxNumber><taxpayerId>99999999</taxpayerId></customerTaxNumber></customerInfo><invoiceDetail><currencyCode>HUF</currencyCode></invoiceDetail></invoiceHead><invoiceLines><line><lineDescription>USB egér</lineDescription><quantity>2</quantity><unitPrice>100</unitPrice><unitOfMeasure>PIECE</unitOfMeasure><vatPercentage>0.27</vatPercentage></line></invoiceLines><invoiceSummary><invoiceNetAmount>200</invoiceNetAmount><invoiceVatAmount>54</invoiceVatAmount><invoiceGrossAmount>254</invoiceGrossAmount></invoiceSummary></invoice></invoiceMain></InvoiceData>";
    private static HttpClient DataClient(string invoice,bool compressed,bool wrongId=false)=>new(new Handler(async request=>
    {
        var xml=XDocument.Parse(await request.Content!.ReadAsStringAsync());Check.Equal("INBOUND",Value(xml,"invoiceDirection"));var bytes=Encoding.UTF8.GetBytes(invoice);if(compressed){using var packed=new MemoryStream();using(var gzip=new GZipStream(packed,CompressionLevel.Optimal,true))gzip.Write(bytes);bytes=packed.ToArray();}
        return Response("QueryInvoiceDataResponse",wrongId?"OTHER":Value(xml,"requestId"),"<invoiceDataResult><invoiceData>"+Convert.ToBase64String(bytes)+"</invoiceData><compressedContentIndicator>"+(compressed?"true":"false")+"</compressedContentIndicator></invoiceDataResult>");
    }));
    private static HttpResponseMessage Response(string name,string id,string body)=>new(HttpStatusCode.OK){Content=new StringContent("<"+name+" xmlns='http://schemas.nav.gov.hu/OSA/3.0/api'><header><requestId>"+id+"</requestId></header><result><funcCode>OK</funcCode></result>"+body+"</"+name+">")};
    private static string Value(XContainer xml,string key)=>xml.Descendants().First(e=>e.Name.LocalName==key).Value;
    private sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> send):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>send(request);}
}
