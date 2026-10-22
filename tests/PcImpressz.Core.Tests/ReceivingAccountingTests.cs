using PcImpressz.Business;
using PcImpressz.Contracts;
using PcImpressz.Core.Companies;
using PcImpressz.Core.Storage;
using PcImpressz.Modules.Receiving;

namespace PcImpressz.Core.Tests;
internal static class ReceivingAccountingTests
{
    public static IEnumerable<(string,Func<Task>)> Cases()
    {
        yield return ("NAV exact discounted line amounts include non-stock freight in invoice totals across save and posting",async()=>
        {
            using var f=new Fixture();var tax=ReceivingText.TaxKey(f.Supplier.TaxId);
            var xml=$"<InvoiceData><invoiceNumber>FREIGHT-1</invoiceNumber><invoiceIssueDate>2026-10-22</invoiceIssueDate><currencyCode>HUF</currencyCode><supplierName>Szállító</supplierName><supplierTaxNumber>{tax}</supplierTaxNumber><line><lineDescription>{f.Product.Name}</lineDescription><productCode><productCodeCategory>OWN</productCodeCategory><productCodeValue>{f.Product.Sku}</productCodeValue></productCode><quantity>2</quantity><unitPrice>100.1234567890</unitPrice><unitOfMeasure>PIECE</unitOfMeasure><vatPercentage>0.27</vatPercentage><lineNetAmount>190</lineNetAmount><lineVatAmount>51.3</lineVatAmount><lineGrossAmountNormal>241.3</lineGrossAmountNormal></line><line><lineDescription>Szállítási költség</lineDescription><quantity>1</quantity><unitPrice>10</unitPrice><vatPercentage>0.27</vatPercentage><unitOfMeasure>PIECE</unitOfMeasure><lineNetAmount>10</lineNetAmount><lineVatAmount>2.7</lineVatAmount><lineGrossAmountNormal>12.7</lineGrossAmountNormal></line><invoiceNetAmount>200</invoiceNetAmount><invoiceVatAmount>54</invoiceVatAmount><invoiceGrossAmount>254</invoiceGrossAmount></InvoiceData>";
            var doc=ReceivingImport.FromNav(xml);Check.Equal(190m,doc.Lines[0].SourceNetAmount!.Value);
            var preview=await f.Repo.PreviewNav(new(f.Company,"FREIGHT-1",tax),doc);Check.Equal(190m,preview.Lines[0].SourceNetAmount!.Value);
            var saved=await f.Repo.ImportNav(new(f.Company,"FREIGHT-1",tax,f.Supplier.Id),doc,"test");
            var lines=saved.Data.Lines.Select((l,i)=>new ReceiptLineDraft(l.Id,l.ProductId,l.Name,l.Sku,l.Barcode,l.UnitCode,l.Quantity,l.NetUnitPrice,l.VatRate,false,i==0)).ToArray();
            saved=await f.Repo.Save(new(f.Company,saved.Id,saved.Version,new(saved.Data.Number,f.Supplier.Id,saved.Data.IssuedOn,"","","Bolt",lines)),"test");
            Check.Equal(200m,saved.Data.Net);Check.Equal(54m,saved.Data.Vat);Check.Equal(254m,saved.Data.Gross);Check.Equal(190m,saved.Data.Lines[0].SourceNetAmount!.Value);
            var before=f.Stock();var posted=await f.Repo.Classify(new(f.Company,saved.Id,saved.Version,"bevetelezve"),"test");Check.Equal(before+2,f.Stock());
            var reopened=(await f.Repo.Snapshot(f.Company)).Receipts.Single(r=>r.Id==posted.Id);Check.True(!reopened.Data.Lines[1].StockItem);Check.Equal("Szállítási költség",reopened.Data.Lines[1].Name);Check.Equal(254m,reopened.Data.Gross);
            Check.Equal(1,f.Db.Execute(f.Company,tx=>StockLedger.Movements(tx).Count(m=>m.SourceId==posted.Id)));
            await Check.ThrowsAsync<ArgumentException>(()=>f.Repo.ImportNav(new(f.Company,"BAD-TOTAL",tax,f.Supplier.Id),doc with{Number="BAD-TOTAL",ExpectedGross=999},"test"));
        });
        yield return ("signed manual and NAV correction receipts decrease stock and reverse safely without duplicate movements",async()=>
        {
            using var f=new Fixture();var before=f.Stock();Check.True(before>=2);
            var manual=await f.Repo.Save(new(f.Company,null,0,new("MANUAL-CREDIT",f.Supplier.Id,"2026-10-22","","","Bolt",[new(null,f.Product.Id,f.Product.Name,f.Product.Sku,"","DB",-2,100,27)])),"test");
            Check.Equal(-254m,manual.Data.Gross);manual=await f.Repo.Classify(new(f.Company,manual.Id,manual.Version,"bevetelezve"),"test");Check.Equal(before-2,f.Stock());
            await f.Repo.Reverse(new(f.Company,manual.Id,manual.Version,"Jóváírás visszafordítása"),"test");Check.Equal(before,f.Stock());
            var doc=new ImportedDocument("NAV-CREDIT","2026-10-22",null,f.Supplier.Name,f.Supplier.TaxId,[new(f.Product.Name,f.Product.Sku,"",-1,100,27,"DB",false,-100,-27,-127)],ExpectedNet:-100,ExpectedVat:-27,ExpectedGross:-127,Operation:"STORNO",RelatedInvoiceNumber:"ORIGINAL-1");
            var nav=await f.Repo.ImportNav(new(f.Company,doc.Number!,f.Supplier.TaxId,f.Supplier.Id),doc,"test");Check.Equal("STORNO",nav.Data.NavOperation);Check.Equal("ORIGINAL-1",nav.Data.RelatedInvoiceNumber);
            nav=await f.Repo.Classify(new(f.Company,nav.Id,nav.Version,"bevetelezve"),"test");Check.Equal(before-1,f.Stock());
            var again=await f.Repo.ImportNav(new(f.Company,doc.Number!,f.Supplier.TaxId,f.Supplier.Id),doc,"test");Check.Equal(nav.Id,again.Id);Check.Equal(before-1,f.Stock());
            var failed=await f.Repo.Save(new(f.Company,null,0,new("OVER-CREDIT",f.Supplier.Id,"2026-10-22","","","Bolt",[new(null,f.Product.Id,f.Product.Name,f.Product.Sku,"","DB",-(before+1),100,27)])),"test");
            await Check.ThrowsAsync<ArgumentException>(()=>f.Repo.Classify(new(f.Company,failed.Id,failed.Version,"bevetelezve"),"test"));Check.Equal(before-1,f.Stock());
        });
        yield return ("incoming invoice filters combine supplier dates signed amount column search and numeric ordering",()=>
        {
            ReceiptRecord Row(string number,string supplier,string date,decimal gross)=>new(Guid.NewGuid(),1,new(){Number=number,SupplierName=supplier,SupplierTaxId="12345678",IssuedOn=date,Status="bevetelezve",Net=gross,Gross=gross},DateTimeOffset.UtcNow,"test",DateTimeOffset.UtcNow,"test");
            var rows=new[]{Row("A","SMA Rendszerház","2026-10-22",-127),Row("B","Orink","2026-10-22",1000),Row("C","SMA Rendszerház","2026-10-21",125000)};
            var query=new ReceiptQuery(Guid.NewGuid(),Filter:"mind",Supplier:"rendszerhaz",DateFrom:"2026-10-21",DateTo:"2026-10-22",MinGross:100000,MaxGross:130000,Columns:new(){{"number","C"}});
            Check.Equal("C",ReceiptListing.Match(rows,query).Single().Data.Number);Check.Equal("A",ReceiptListing.Match(rows,query with{MinGross=-200,MaxGross=-1,Columns=null}).Single().Data.Number);
            Check.Equal("A",ReceiptListing.Match(rows,new(Guid.NewGuid(),Filter:"mind",Sort:"gross")).First().Data.Number);
            Check.Throws<ArgumentException>(()=>ReceiptListing.Match(rows,query with{MinGross=200000,MaxGross=100}));Check.Throws<ArgumentException>(()=>ReceiptListing.Match(rows,query with{DateFrom="bad"}));Check.Throws<ArgumentException>(()=>ReceiptListing.Match(rows,query with{Columns=new(){{"unknown","x"}}}));return Task.CompletedTask;
        });
    }
    private sealed class Fixture:IDisposable
    {
        private readonly TestDirectory _directory=new();public Guid Company{get;}public CompanyDatabase Db{get;}public ReceivingRepository Repo{get;}public PartnerReference Supplier{get;}public ProductReference Product{get;}
        public Fixture(){var companies=new CompanyStore(_directory.Path);Company=companies.Create("Számla teszt",true).Id;Db=new(companies.DataDirectory);DemoDataSeeder.Seed(id=>companies.ModuleDataDirectory(id,"receiving"),Db,Company,TimeProvider.System,"test");Supplier=Db.Execute(Company,tx=>CompanyReferenceQueries.Partners(tx).First(p=>p.IsSupplier));Product=Db.Execute(Company,tx=>CompanyReferenceQueries.Products(tx).Single(p=>p.Sku=="DEMO-001"));Repo=new(id=>companies.ModuleDataDirectory(id,"receiving"),TimeProvider.System,Db);}
        public decimal Stock()=>Db.Execute(Company,tx=>StockLedger.Balances(tx).Where(b=>b.ProductId==Product.Id&&b.Warehouse=="Bolt").Sum(b=>b.Quantity));public void Dispose()=>_directory.Dispose();
    }
}
