using System.Net;
using System.Net.Http;
using System.Text.Json;
using PcImpressz.Contracts;
using PcImpressz.Core.Companies;
using PcImpressz.Core.Storage;
using PcImpressz.Modules.Receiving;
using PcImpressz.Shell.Customization;
namespace PcImpressz.Core.Tests;
internal static class ReceivingPreparationTests
{
    public static IEnumerable<(string,Func<Task>)> Cases()
    {
        yield return ("partial manual invoice drafts persist without stock, isolate companies and reject stale updates and deletion",()=>
        {
            using var dir=new TestDirectory();var companies=new CompanyStore(dir.Path);var company=companies.Create("Piszkozat",true).Id;var other=companies.Create("Másik",true).Id;var db=new CompanyDatabase(companies.DataDirectory);var store=new ManualInvoiceDraftStore(db,TimeProvider.System);var id=Guid.NewGuid();
            var data=JsonSerializer.SerializeToElement(new{number="",supplierId=(Guid?)null,lines=new[]{new{name="Félbehagyott sor",quantityText="",priceText="még nincs ár",category="TONER"}}});
            var saved=store.Save(new(company,id,0,data),"raktaros");Check.Equal(1L,saved.Version);Check.Equal(1,store.List(company).Length);Check.Equal(0,store.List(other).Length);
            var reopened=new ManualInvoiceDraftStore(db,TimeProvider.System).Read(new(company,id));Check.Equal("még nincs ár",reopened.Data.GetProperty("lines")[0].GetProperty("priceText").GetString());
            Check.Throws<ArgumentException>(()=>store.Read(new(other,id)));Check.Throws<ReceivingConflictException>(()=>store.Save(new(company,id,0,data),"masik"));Check.Throws<ReceivingConflictException>(()=>store.Delete(new(company,id,0)));
            Check.Equal(0,db.Execute(company,tx=>CompanyReferenceQueries.Receipts(tx).Length));Check.Equal(0,db.Execute(company,tx=>CompanyReferenceQueries.StockQuantities(tx).Count));store.Delete(new(company,id,1));Check.Equal(0,store.List(company).Length);return Task.CompletedTask;
        });
        yield return ("invoice preparation persists across reopen without receipt stock and rejects stale writers",()=>
        {
            using var dir=new TestDirectory();var companies=new CompanyStore(dir.Path);var company=companies.Create("Előkészítés",true).Id;var other=companies.Create("Másik cég",true).Id;var db=new CompanyDatabase(companies.DataDirectory);var store=new InvoicePreparationStore(db,TimeProvider.System);
            var saved=store.Save(new(company,"NAV-1","11111111",0,JsonSerializer.SerializeToElement(new{lines=new[]{new{retailPrice="12345",stockItem=false}}})),"worker");Check.Equal(1L,saved.Version);
            var reopened=new InvoicePreparationStore(db,TimeProvider.System).Read(new(company,"NAV-1","11111111-2-42"));Check.Equal("12345",reopened!.Data.GetProperty("lines")[0].GetProperty("retailPrice").GetString());
            Check.True(store.Read(new(other,"NAV-1","11111111")) is null);Check.Equal(0,db.Execute(company,tx=>CompanyReferenceQueries.Receipts(tx).Length));Check.Equal(0,db.Execute(company,tx=>CompanyReferenceQueries.StockQuantities(tx).Count));
            Check.Throws<ReceivingConflictException>(()=>store.Save(new(company,"NAV-1","11111111",0,JsonSerializer.SerializeToElement(new{})),"other"));return Task.CompletedTask;
        });
        yield return ("NAV facts stay immutable while per-line expense selection preserves invoice totals",async()=>
        {
            using var dir=new TestDirectory();var companies=new CompanyStore(dir.Path);var company=companies.Create("Sorok",true).Id;var db=new CompanyDatabase(companies.DataDirectory);DemoDataSeeder.Seed(id=>companies.ModuleDataDirectory(id,"receiving"),db,company,TimeProvider.System,"test");
            var supplier=db.Execute(company,tx=>CompanyReferenceQueries.Partners(tx).First(p=>p.IsSupplier));var product=db.Execute(company,tx=>CompanyReferenceQueries.Products(tx).First());var before=db.Execute(company,tx=>CompanyReferenceQueries.StockQuantity(tx,product.Id));
            var repo=new ReceivingRepository(id=>companies.ModuleDataDirectory(id,"receiving"),TimeProvider.System,db);
            var doc=new ImportedDocument("NAV-MIX","2026-10-22",null,supplier.Name,supplier.TaxId,[new(product.Name,"","",2,100,27,"DB"),new("Kávé saját felhasználásra","","",1,50,27,"DB")],ExpectedNet:250,ExpectedVat:67.5m,ExpectedGross:317.5m);
            var record=await repo.ImportNav(new(company,doc.Number!,supplier.TaxId,supplier.Id),doc,"test");Check.Equal(product.Id,record.Data.Lines[0].ProductId!.Value);
            ReceiptLineDraft Draft(ReceiptLine l,bool stock)=>new(l.Id,l.ProductId,l.Name,l.Sku,l.Barcode,l.UnitCode,l.Quantity,l.NetUnitPrice,l.VatRate,false,stock);
            var draft=new ReceiptDraft(record.Data.Number,supplier.Id,record.Data.IssuedOn,"","","Bolt",[Draft(record.Data.Lines[0],true),Draft(record.Data.Lines[1],false)]);
            await Check.ThrowsAsync<ArgumentException>(()=>repo.Save(new(company,record.Id,record.Version,draft with{Lines=[draft.Lines![0] with{Quantity=99},draft.Lines[1]]}),"test"));
            var saved=await repo.Save(new(company,record.Id,record.Version,draft),"test");Check.Equal(317.5m,saved.Data.Gross);var posted=await repo.Classify(new(company,saved.Id,saved.Version,"bevetelezve"),"test");Check.True(posted.Data.StockPosted);Check.Equal(before+2,db.Execute(company,tx=>CompanyReferenceQueries.StockQuantity(tx,product.Id)));
            var second=await repo.ImportNav(new(company,"NAV-MANUAL",supplier.TaxId,supplier.Id),doc with{Number="NAV-MANUAL"},"test");
            var manual=await repo.Save(new(company,second.Id,second.Version,draft with{Number="MANUAL-EDIT",Lines=[Draft(second.Data.Lines[0],true) with{Quantity=3},Draft(second.Data.Lines[1],false)],ConvertToManual=true}),"test");Check.Equal("manual",manual.Data.Source);Check.True(manual.Data.SourceGross is null);
            Check.Equal(manual.Id,(await repo.ImportNav(new(company,"NAV-MANUAL",supplier.TaxId,supplier.Id),doc with{Number="NAV-MANUAL"},"test")).Id);
            Check.Equal(manual.Id,(await repo.MarkNavPage(company,new(1,1,[new("NAV-MANUAL",supplier.Name,supplier.TaxId,"2026-10-22","HUF","CREATE",250)]))).Rows.Single().ReceiptId!.Value);
        });
        yield return ("internal barcode checksum and compact Code128 symbols are valid",()=>
        {
            Check.Equal("4006381333931",InternalBarcodes.Complete("400638133393"));var codes=Enumerable.Range(0,100).Select(_=>InternalBarcodes.Generate()).ToArray();Check.Equal(100,codes.Distinct().Count());Check.True(codes.All(c=>c.Length==13&&c.StartsWith("20")&&InternalBarcodes.Complete(c[..12])==c));
            Check.Equal(101,Barcode128.Pattern("123456789012").Length);Check.True(Barcode128.Pattern("1234567890123").EndsWith("1100011101011",StringComparison.Ordinal));Check.Throws<ArgumentException>(()=>Barcode128.Pattern("árvíz"));return Task.CompletedTask;
        });
        yield return ("CompMarket read-only request validates invoice identity and parses identifiers without invoice pricing",async()=>
        {
            using var client=new HttpClient(new Handler(request=>
            {
                Check.Equal(HttpMethod.Get,request.Method);Check.Equal("compmarket.hu",request.RequestUri!.Host);Check.Equal("/xml/szamla.php",request.RequestUri.AbsolutePath);Check.True(request.RequestUri.Query.Contains("szamlaszam=2026%2F1",StringComparison.OrdinalIgnoreCase));
                return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("<szamlak><szamla><szamlaszam>2026/1</szamlaszam><tetel><nev>Egér</nev><ean>5991234567890</ean><cikkszam>CM-1</cikkszam><gyartoi_cikkszam>MPN-1</gyartoi_cikkszam><kategoria>Egerek</kategoria><menny>999</menny><eladar>1</eladar></tetel></szamla></szamlak>")};
            }));
            var rows=await SupplierInvoiceClient.ReadAsync(new("test-id","test-code","http://localhost:8888",true),"2026/1",CancellationToken.None,client);Check.Equal("5991234567890",rows.Single().Barcode);Check.Equal("MPN-1",rows.Single().ManufacturerPartNumber);Check.Equal("Egerek",rows.Single().Category);
            Check.Throws<ArgumentException>(()=>SupplierInvoiceClient.Parse(System.Text.Encoding.UTF8.GetBytes("<szamla><szamlaszam>OTHER</szamlaszam></szamla>"),"2026/1"));
        });
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(send(request));}
}
