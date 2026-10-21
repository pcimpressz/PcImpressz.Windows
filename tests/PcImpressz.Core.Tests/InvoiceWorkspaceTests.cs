using PcImpressz.Contracts;
using System.Net;
using System.Net.Http;
using System.Xml.Linq;
using PcImpressz.Modules.Receiving;
using PcImpressz.Core.Companies;
using PcImpressz.Core.Storage;
using PcImpressz.Business;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Core;

namespace PcImpressz.Core.Tests;
internal static class InvoiceWorkspaceTests
{
    public static IEnumerable<(string,Func<Task>)> Cases()
    {
        yield return ("outgoing NAV digest uses OUTBOUND and verifies own issuer",async()=>
        {
            var secret=new NavConnectionSecrets("99999999","test","password","sign","exchange",true);
            foreach(var own in new[]{true,false})
            {
                using var client=new HttpClient(new Handler(async request=>
                {
                    var xml=XDocument.Parse(await request.Content!.ReadAsStringAsync());
                    Check.Equal("OUTBOUND",xml.Descendants().First(e=>e.Name.LocalName=="invoiceDirection").Value);
                    var id=xml.Descendants().First(e=>e.Name.LocalName=="requestId").Value;
                    return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent($"<QueryInvoiceDigestResponse xmlns='http://schemas.nav.gov.hu/OSA/3.0/api'><header><requestId>{id}</requestId></header><result><funcCode>OK</funcCode></result><invoiceDigestResult><currentPage>1</currentPage><availablePage>1</availablePage><invoiceDigest><invoiceNumber>OUT-1</invoiceNumber><supplierTaxNumber>{(own?"99999999":"11111111")}</supplierTaxNumber><customerName>Buyer</customerName><customerTaxNumber>22222222</customerTaxNumber><invoiceIssueDate>2026-10-21</invoiceIssueDate><currency>HUF</currency><invoiceOperation>CREATE</invoiceOperation><invoiceNetAmount>100</invoiceNetAmount></invoiceDigest></invoiceDigestResult></QueryInvoiceDigestResponse>")};
                }));
                if(own){var result=await NavInvoiceClient.ListAsync(secret,new(Guid.NewGuid(),"2026-10-01","2026-10-21"),CancellationToken.None,client,"OUTBOUND");Check.Equal("Buyer",result.Rows.Single().CustomerName);}
                else await Check.ThrowsAsync<ArgumentException>(()=>NavInvoiceClient.ListAsync(secret,new(Guid.NewGuid(),"2026-10-01","2026-10-21"),CancellationToken.None,client,"OUTBOUND"));
            }
        });
        yield return ("PDF assisted input extracts text and rejects invalid scanned and oversized documents",()=>
        {
            var builder=new PdfDocumentBuilder();var page=builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
            var font=builder.AddStandard14Font(Standard14Font.Helvetica);page.AddText("Invoice TEST-1, USB mouse 2 pcs",12,new PdfPoint(30,700),font);
            Check.True(InvoicePdf.ReadText(Convert.ToBase64String(builder.Build())).Contains("TEST-1"));
            Check.Throws<ArgumentException>(()=>InvoicePdf.ReadText("!invalid"));
            Check.Throws<ArgumentException>(()=>InvoicePdf.ReadText(Convert.ToBase64String("hello"u8.ToArray())));
            Check.Throws<ArgumentException>(()=>InvoicePdf.ReadText(new string('a',2_000_005)));
            var empty=new PdfDocumentBuilder();empty.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
            Check.Throws<ArgumentException>(()=>InvoicePdf.ReadText(Convert.ToBase64String(empty.Build())));
            return Task.CompletedTask;
        });
        yield return ("PDF based receipt remains a draft without stock movement",async()=>
        {
            using var dir=new TestDirectory();var companies=new CompanyStore(dir.Path);var company=companies.Create("PDF próba",true).Id;var db=new CompanyDatabase(companies.DataDirectory);
            DemoDataSeeder.Seed(id=>companies.ModuleDataDirectory(id,"receiving"),db,company,TimeProvider.System,"test");
            var supplier=db.Execute(company,tx=>CompanyReferenceQueries.Partners(tx).First(p=>p.IsSupplier));var product=db.Execute(company,tx=>CompanyReferenceQueries.Products(tx).First(p=>p.Sku=="DEMO-001"));
            var before=db.Execute(company,tx=>CompanyReferenceQueries.StockQuantity(tx,product.Id));
            var repo=new ReceivingRepository(id=>companies.ModuleDataDirectory(id,"receiving"),TimeProvider.System,db);
            var record=await repo.Save(new(company,null,0,new("PDF-1",supplier.Id,"2026-10-21",null,"PDF alapján", "Bolt",[new(null,product.Id,product.Name,product.Sku,"","DB",2,100,27)],"pdf")),"test");
            Check.Equal("pdf",record.Data.Source);Check.Equal("uj",record.Data.Status);Check.True(!record.Data.StockPosted);Check.Equal(before,db.Execute(company,tx=>CompanyReferenceQueries.StockQuantity(tx,product.Id)));
        });
    }
    private sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>send(request);}
}
