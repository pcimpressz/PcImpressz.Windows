using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PcImpressz.Business;
using PcImpressz.Contracts;
using PcImpressz.Core.Companies;
using PcImpressz.Core.Storage;
using PcImpressz.Modules.Catalog;
using PcImpressz.Modules.Partners;
using PcImpressz.Modules.Receiving;
using PcImpressz.Modules.Sales;

namespace PcImpressz.Core.Tests;
internal static class StabilizationTests
{
    public static IEnumerable<(string,Func<Task>)> Cases()
    {
        yield return Case("NAV import preserves 0, 5, 18 and 27 percent VAT",async f=>
        {
            foreach(var vat in new[]{0,5,18,27})
            {
                var xml=$"<InvoiceData><invoiceNumber>VAT-{vat}</invoiceNumber><currencyCode>HUF</currencyCode><line><lineDescription>Egér</lineDescription><quantity>1</quantity><unitPrice>100</unitPrice><vatPercentage>{(vat/100m).ToString(System.Globalization.CultureInfo.InvariantCulture)}</vatPercentage></line></InvoiceData>";
                var result=await f.Receipts.Import(new(f.Company.Id,f.Supplier.Id,null,null,null,"nav","v.xml",null),ReceivingImport.FromNav(xml),"operator");
                Check.Equal((decimal)vat,result.Data.Lines[0].VatRate);Check.Equal(100m+vat,result.Data.Gross);
            }
            Check.Throws<ArgumentException>(()=>ReceivingImport.FromNav("<InvoiceData><currencyCode>EUR</currencyCode></InvoiceData>"));
            Check.Throws<ArgumentException>(()=>ReceivingImport.FromTable([["termék neve","mennyiség","nettó egységár"],["Egér","1","100"]]));
            Check.Throws<ArgumentException>(()=>ReceivingImport.FromNav("<InvoiceData><currencyCode>HUF</currencyCode><line><lineDescription>Egér</lineDescription><quantity>1</quantity><unitPrice>100</unitPrice></line></InvoiceData>"));
        });
        yield return Case("imported source totals and supplier tax cannot be silently changed",async f=>
        {
            var document=new ImportedDocument("CHECK-1","2026-10-20",null,"Partner","12345678",[new("Egér",f.Product.Data.Sku,"",1,100,5)],"HUF",100,5,105);
            var partner=await f.Partners.Save(new(f.Company.Id,f.Supplier.Id,f.Supplier.Version,f.Supplier.Data with{TaxId="12345678-2-42"}),"operator");
            var request=new ReceiptImport(f.Company.Id,partner.Id,null,null,null,"nav","x.xml",null);
            var saved=await f.Receipts.Import(request,document,"operator");Check.Equal(105m,saved.Data.SourceGross);
            await Check.ThrowsAsync<ArgumentException>(async()=>await f.Receipts.Import(request,document with{Number="CHECK-2",ExpectedGross=999},"operator"));
            await Check.ThrowsAsync<ArgumentException>(async()=>await f.Receipts.Import(request,document with{Number="CHECK-3",SupplierTaxId="87654321"},"operator"));
            await Check.ThrowsAsync<ArgumentException>(async()=>await f.Receipts.Save(new(f.Company.Id,saved.Id,saved.Version,new(saved.Data.Number,partner.Id,"2026-10-20",null,null,"Bolt",[new(saved.Data.Lines[0].Id,f.Product.Id,"Egér","","","DB",1,200,5)])),"operator"));
            Check.Equal(100m,(await f.Receipts.Snapshot(f.Company.Id)).Receipts.Single(r=>r.Id==saved.Id).Data.Net);
        });
        yield return Case("service is explicit and SKU wins over name fragments",async f=>
        {
            var receipt=await f.Receipt("Díjnyertes egér",sku:f.Product.Data.Sku);
            Check.Equal(f.Product.Id,receipt.Data.Lines[0].ProductId);Check.Equal("megvan",receipt.Data.Lines[0].Match);
            var unknown=await f.Receipt("Szállításra kész doboz");Check.True(unknown.Data.Lines[0].ProductId is null);
            await Check.ThrowsAsync<ArgumentException>(async()=>await f.Receipts.Classify(new(f.Company.Id,unknown.Id,unknown.Version,"bevetelezve"),"operator"));
            var service=await f.Receipt("Futárdíj",service:true);Check.Equal("szolgaltatas",service.Data.Lines[0].Match);
        });
        yield return ("dossier period preserves received-only totals and unknown payment",()=>
        {
            var id=Guid.NewGuid();var invoice=new PartnerDossierInvoice("R1","2026-10-01",100,127,null,"Bevételezve",[]);
            var state=new PartnerDossierSnapshot(id,100,127,"2026-10-01","2026-10-02",[],[invoice,invoice with{Number="R2",Net=200,Gross=254,Status="Költség"}]);
            var filtered=PartnerDossier.Within(state,new DateOnly(2026,10,1),new DateOnly(2026,10,31));Check.Equal(127m,filtered.PurchaseGross);
            var view=new PartnerDossierView(id,"","",false,"",[],[new("incoming","","","","",[])]);
            Check.True(PartnerDossier.Apply(view,state).Sections[0].Lines[0].Detail.Contains("nem ismert",StringComparison.Ordinal));return Task.CompletedTask;
        });
        yield return ("bounded XLSX rejects compressed expansion and malformed CSV",()=>
        {
            using var bytes=new MemoryStream();using(var zip=new ZipArchive(bytes,ZipArchiveMode.Create,true))
            {using var writer=new StreamWriter(zip.CreateEntry("xl/worksheets/sheet1.xml").Open());writer.Write(new string(' ',9*1024*1024));}
            Check.True(bytes.Length<1_500_000);Check.Throws<ArgumentException>(()=>ReceivingImport.Read("bomb.xlsx",Convert.ToBase64String(bytes.ToArray())));
            Check.Throws<ArgumentException>(()=>ReceivingImport.Read("broken.csv",Convert.ToBase64String(Encoding.UTF8.GetBytes("termék neve;mennyiség;nettó egységár;áfa\n\"unfinished;1;10;27"))));return Task.CompletedTask;
        });
        yield return Case("SQLite transactions roll back and isolate company data",f=>
        {
            Check.Throws<InvalidOperationException>(()=>f.Database.Execute(f.Company.Id,tx=>{tx.Write("probe","record",new{value=1});return FailTransaction();}));
            Check.True(f.Database.Execute(f.Company.Id,tx=>tx.Get("probe","record")) is null);
            var second=f.Companies.Create("Másik cég",true);
            Check.Equal(0,f.Database.Execute(second.Id,tx=>CompanyReferenceQueries.Products(tx).Length));
            Check.Equal(1,f.Database.Execute(f.Company.Id,tx=>CompanyReferenceQueries.Products(tx).Length));return Task.CompletedTask;
        });
        yield return ("legacy JSON migrates once and source survives without dual writes",async()=>
        {
            using var dir=new TestDirectory();var companies=new CompanyStore(dir.Path);var company=companies.Create("Régi cég",false);
            var legacy=new PartnerRepository(id=>companies.ModuleDataDirectory(id,"partners"),TimeProvider.System);
            var p=await legacy.Save(new(company.Id,null,0,new(){Name="Eredeti partner",IsCustomer=true,IsActive=true}),"old");
            var source=Path.Combine(companies.ModuleDataDirectory(company.Id,"partners"),"partners.json");var original=File.ReadAllBytes(source);
            var db=new CompanyDatabase(companies.DataDirectory);var migrated=new PartnerRepository(id=>companies.ModuleDataDirectory(id,"partners"),TimeProvider.System,db);
            Check.Equal(1,(await migrated.Snapshot(company.Id)).Partners.Length);
            await migrated.Save(new(company.Id,p.Id,p.Version,p.Data with{Name="Új név"}),"new");
            Check.True(original.SequenceEqual(File.ReadAllBytes(source)));Check.Equal("Új név",(await migrated.Snapshot(company.Id)).Partners[0].Data.Name);
            File.WriteAllText(source,"broken old copy");Check.Equal(1,(await migrated.Snapshot(company.Id)).Partners.Length);
        });
        yield return ("invalid legacy data aborts migration without an empty company",()=>
        {
            using var dir=new TestDirectory();var companies=new CompanyStore(dir.Path);var company=companies.Create("Hibás cég",true);
            var folder=companies.ModuleDataDirectory(company.Id,"partners");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"partners.json"),"{}");
            var db=new CompanyDatabase(companies.DataDirectory);Check.Throws<IOException>(()=>db.Execute(company.Id,_=>true));
            using var connection=new SqliteConnection("Data Source="+db.DatabasePath(company.Id)+";Pooling=False");connection.Open();using var cmd=connection.CreateCommand();cmd.CommandText="PRAGMA user_version";Check.Equal(0L,Convert.ToInt64(cmd.ExecuteScalar()));return Task.CompletedTask;
        });
        yield return Case("receipt and stock commit together and cannot post twice",async f=>
        {
            var receipt=await f.Receipt("Egér",sku:f.Product.Data.Sku,quantity:3);
            var done=await f.Receipts.Classify(new(f.Company.Id,receipt.Id,receipt.Version,"bevetelezve"),"operator");Check.True(done.Data.StockPosted);Check.Equal(3m,f.Balance());
            await Check.ThrowsAsync<ReceivingConflictException>(async()=>await f.Receipts.Classify(new(f.Company.Id,receipt.Id,receipt.Version,"bevetelezve"),"operator"));Check.Equal(3m,f.Balance());
        });
        yield return Case("sales reject insufficient stock without partial documents",f=>
        {
            var request=f.Sale(2);
            Check.Throws<ArgumentException>(()=>f.Database.Execute(f.Company.Id,tx=>SalesModule.Post(tx,request,"cashier",f.Clock.Now)));
            Check.Equal(0,f.Database.Execute(f.Company.Id,tx=>SalesModule.Records(tx).Length));Check.Equal(0m,f.Balance());return Task.CompletedTask;
        });
        yield return Case("sales retry, reversal and exact fractional quantities are atomic",f=>
        {
            f.Opening(5.125m);var request=f.Sale(2.125m);
            var saved=f.Database.Execute(f.Company.Id,tx=>SalesModule.Post(tx,request,"cashier",f.Clock.Now));Check.Equal(3m,f.Balance());Check.Equal("EL-2026-000001",saved.Number);
            var same=f.Database.Execute(f.Company.Id,tx=>SalesModule.Post(tx,request,"cashier",f.Clock.Now));Check.Equal(saved.Id,same.Id);Check.Equal(3m,f.Balance());
            Check.Throws<ArgumentException>(()=>f.Database.Execute(f.Company.Id,tx=>SalesModule.Post(tx,request with{Lines=[new(f.Product.Id,1,100,27)]},"cashier",f.Clock.Now)));
            f.Database.Execute(f.Company.Id,tx=>SalesModule.Reverse(tx,new(f.Company.Id,saved.Id,1),"cashier",f.Clock.Now));Check.Equal(5.125m,f.Balance());
            f.Database.Execute(f.Company.Id,tx=>SalesModule.Reverse(tx,new(f.Company.Id,saved.Id,1),"cashier",f.Clock.Now));Check.Equal(5.125m,f.Balance());
            Check.Equal(3,f.Database.Execute(f.Company.Id,tx=>StockLedger.Movements(tx).Length));return Task.CompletedTask;
        });
        yield return Case("concurrent sales serialize and cannot oversell",async f=>
        {
            f.Opening(1);
            async Task<bool> Attempt(){return await Task.Run(()=>{try{f.Database.Execute(f.Company.Id,tx=>SalesModule.Post(tx,f.Sale(1),"cashier",f.Clock.Now));return true;}catch(ArgumentException){return false;}});}
            var results=await Task.WhenAll(Attempt(),Attempt());Check.Equal(1,results.Count(x=>x));Check.Equal(0m,f.Balance());
        });
        yield return Case("company backup restores business data without secrets and preserves source",async f=>
        {
            f.Opening(10);f.Database.Execute(f.Company.Id,tx=>SalesModule.Post(tx,f.Sale(2),"cashier",f.Clock.Now));
            var secret=Path.Combine(f.Companies.ModuleDataDirectory(f.Company.Id,"receiving"),"nav-connection.bin");Directory.CreateDirectory(Path.GetDirectoryName(secret)!);File.WriteAllText(secret,"secret-not-for-backup");
            var backups=new CompanyBackupService(f.Companies,f.Database);var file=Path.Combine(f.Directory.Path,"company.pcibak");backups.Create(f.Company.Id,file);
            using(var zip=ZipFile.OpenRead(file)){Check.Equal(2,zip.Entries.Count);Check.True(zip.Entries.All(e=>!e.FullName.Contains("nav",StringComparison.Ordinal)));}
            var restored=backups.Restore(file,"Visszaállított cég",false);
            Check.Equal(8m,f.Database.Execute(restored.Id,tx=>StockLedger.Balances(tx).Single().Quantity));
            Check.Equal(8m,f.Balance());Check.True(!File.Exists(Path.Combine(f.Companies.ModuleDataDirectory(restored.Id,"receiving"),"nav-connection.bin")));
            Check.Equal(1,f.Database.Execute(restored.Id,tx=>SalesModule.Records(tx).Length));
            var partners=new PartnerRepository(id=>f.Companies.ModuleDataDirectory(id,"partners"),f.Clock,f.Database);Check.Equal(1,(await partners.Snapshot(restored.Id)).Partners.Length);
            Check.Throws<IOException>(()=>backups.Create(f.Company.Id,file));
            using(var zip=ZipFile.Open(file,ZipArchiveMode.Update)){using var writer=new StreamWriter(zip.CreateEntry("unexpected.txt").Open());writer.Write("bad");}
            var count=f.Companies.List().Length;Check.Throws<IOException>(()=>backups.Restore(file,"Hibás visszaállítás",true));Check.Equal(count,f.Companies.List().Length);
        });
        yield return Case("anonymous sale is allowed and non-salable products are rejected",async f=>
        {
            f.Opening(4);
            var anonymous=f.Database.Execute(f.Company.Id,tx=>SalesModule.Post(tx,f.Sale(1) with{CustomerId=null},"cashier",f.Clock.Now));Check.Equal("Alkalmi vevő",anonymous.CustomerName);
            await f.Catalog.SaveProduct(new(f.Company.Id,f.Product.Id,f.Product.Version,f.Product.Data with{IsSalable=false}),"operator");
            Check.Throws<ArgumentException>(()=>f.Database.Execute(f.Company.Id,tx=>SalesModule.Post(tx,f.Sale(1),"cashier",f.Clock.Now)));Check.Equal(3m,f.Balance());
        });
        yield return Case("product unit cannot reinterpret existing stock history",async f=>
        {
            f.Opening(2);await f.Catalog.SaveUnit(new(f.Company.Id,null,0,new(){Name="Kilogramm",Code="KG",IsActive=true}),"operator");
            await Check.ThrowsAsync<ArgumentException>(async()=>await f.Catalog.SaveProduct(new(f.Company.Id,f.Product.Id,f.Product.Version,f.Product.Data with{UnitCode="KG"}),"operator"));Check.Equal(2m,f.Balance());
        });
        yield return Case("receipt rejects silent unit conversion and fractional VAT",async f=>
        {
            async Task Save(string unit,decimal vat,decimal quantity)=>await f.Receipts.Save(new(f.Company.Id,null,0,new(Guid.NewGuid().ToString("N"),f.Supplier.Id,"2026-10-20",null,null,"Bolt",[new(null,f.Product.Id,"Egér","","",unit,quantity,100,vat)])),"operator");
            await Check.ThrowsAsync<ArgumentException>(()=>Save("KG",27,1));
            await Check.ThrowsAsync<ArgumentException>(()=>Save("DB",26.8m,1));
            await Check.ThrowsAsync<ArgumentException>(()=>Save("DB",27,1.00001m));
        });
        yield return Case("one failed posting rolls back every movement in the batch",f=>
        {
            f.Opening(2);var before=f.Database.Execute(f.Company.Id,tx=>StockLedger.Movements(tx).Length);
            Check.Throws<ArgumentException>(()=>f.Database.Execute(f.Company.Id,tx=>StockLedger.Post(tx,"batch","inventory",Guid.NewGuid(),
                [new(f.Product.Id,f.Product.Data.Name,"DB","Bolt",1),new(f.Product.Id,f.Product.Data.Name,"DB","Másik",-1)],"Próba","operator",f.Clock.Now)));
            Check.Equal(2m,f.Balance());Check.Equal(before,f.Database.Execute(f.Company.Id,tx=>StockLedger.Movements(tx).Length));return Task.CompletedTask;
        });
        yield return Case("receipt sale and both reversals form one safe end-to-end stock workflow",async f=>
        {
            var receipt=await f.Receipt("Egér",f.Product.Data.Sku,10);
            receipt=await f.Receipts.Classify(new(f.Company.Id,receipt.Id,receipt.Version,"bevetelezve"),"receiver");
            Check.Equal(10m,f.Balance());
            var sale=f.Database.Execute(f.Company.Id,tx=>SalesModule.Post(tx,f.Sale(2),"seller",f.Clock.Now));
            Check.Equal(8m,f.Balance());
            await Check.ThrowsAsync<ArgumentException>(async()=>await f.Receipts.Reverse(new(f.Company.Id,receipt.Id,receipt.Version,"Téves bevételezés"),"receiver"));
            Check.Equal("bevetelezve",(await f.Receipts.Snapshot(f.Company.Id)).Receipts.Single().Data.Status);Check.Equal(8m,f.Balance());
            f.Database.Execute(f.Company.Id,tx=>SalesModule.Reverse(tx,new(f.Company.Id,sale.Id,1),"seller",f.Clock.Now));
            Check.Equal(10m,f.Balance());
            var reversed=await f.Receipts.Reverse(new(f.Company.Id,receipt.Id,receipt.Version,"Téves bevételezés"),"receiver");
            Check.Equal(0m,f.Balance());Check.Equal("visszaforditva",reversed.Data.Status);Check.True(!reversed.Data.StockPosted);Check.Equal("receiver",reversed.Data.ReversedBy);
            var repeated=await f.Receipts.Reverse(new(f.Company.Id,receipt.Id,receipt.Version,"Téves bevételezés"),"receiver");
            Check.Equal(reversed.Version,repeated.Version);Check.Equal(0m,f.Balance());
            Check.Equal(1,(await f.Receipts.Snapshot(f.Company.Id)).Receipts.Length);
        });
        yield return Case("receipt reversal checks version and refuses legacy non-posted classification",async f=>
        {
            var receipt=await f.Receipt("Egér",f.Product.Data.Sku,3);
            receipt=await f.Receipts.Classify(new(f.Company.Id,receipt.Id,receipt.Version,"bevetelezve"),"receiver");
            await Check.ThrowsAsync<ReceivingConflictException>(async()=>await f.Receipts.Reverse(new(f.Company.Id,receipt.Id,1,"Téves tétel"),"receiver"));
            Check.Equal(3m,f.Balance());
            f.Database.Execute(f.Company.Id,tx=>{var state=tx.Read<ReceiptState>("receiving","state")!;tx.Write("receiving","state",state with{Receipts=state.Receipts.Select(r=>r with{Data=r.Data with{StockPosted=false}}).ToArray()});return 0;});
            await Check.ThrowsAsync<ArgumentException>(async()=>await f.Receipts.Reverse(new(f.Company.Id,receipt.Id,receipt.Version,"Régi besorolás"),"receiver"));
            Check.Equal(3m,f.Balance());
        });
        yield return Case("service-only receipt can close and reverse without inventing stock",async f=>
        {
            var receipt=await f.Receipt("Munkadíj","",1,true);
            receipt=await f.Receipts.Classify(new(f.Company.Id,receipt.Id,receipt.Version,"bevetelezve"),"receiver");
            receipt=await f.Receipts.Reverse(new(f.Company.Id,receipt.Id,receipt.Version,"Téves szolgáltatás"),"receiver");
            Check.Equal(0m,f.Balance());Check.Equal("visszaforditva",receipt.Data.Status);
        });
        yield return Case("sales references expose warehouse stock and match manufacturer and barcode",async f=>
        {
            var product=await f.Catalog.SaveProduct(new(f.Company.Id,f.Product.Id,f.Product.Version,f.Product.Data with{ManufacturerPartNumber="MPN-123",Barcode="5991234567890"}),"operator");
            f.Opening(10);f.Database.Execute(f.Company.Id,tx=>StockLedger.Post(tx,"warehouse2","inventory",Guid.NewGuid(),[new(product.Id,product.Data.Name,"DB","Másik",4)],"Nyitó","operator",f.Clock.Now));
            var probe=new SalesProbe(f.Database);await new SalesModule().StartAsync(probe,default);
            var result=await ((IContextualCommandHandler)probe.Commands["references"]).ExecuteAsync(JsonSerializer.SerializeToElement(new{companyId=f.Company.Id,warehouse="Másik",search="MPN-123"}),new("operator"),default);
            Check.Equal(4m,result.GetProperty("products")[0].GetProperty("availableQuantity").GetDecimal());
            result=await ((IContextualCommandHandler)probe.Commands["references"]).ExecuteAsync(JsonSerializer.SerializeToElement(new{companyId=f.Company.Id,warehouse="Bolt",search="5991234567890"}),new("operator"),default);
            Check.Equal(10m,result.GetProperty("products")[0].GetProperty("availableQuantity").GetDecimal());
        });
        yield return Case("sales filtered listing and export share the same records",async f=>
        {
            f.Opening(5);f.Database.Execute(f.Company.Id,tx=>SalesModule.Post(tx,f.Sale(1),"seller",f.Clock.Now));
            f.Database.Execute(f.Company.Id,tx=>SalesModule.Post(tx,f.Sale(1) with{CustomerId=null},"seller",f.Clock.Now));
            var probe=new SalesProbe(f.Database);await new SalesModule().StartAsync(probe,default);
            var input=JsonSerializer.SerializeToElement(new{companyId=f.Company.Id,recordSearch="Alkalmi"});
            var list=await ((IContextualCommandHandler)probe.Commands["list"]).ExecuteAsync(input,new("operator"),default);
            var export=await ((IContextualCommandHandler)probe.Commands["export"]).ExecuteAsync(input,new("operator"),default);
            Check.Equal(1,list.GetProperty("total").GetInt32());var csv=export.GetProperty("csv").GetString()!;
            Check.True(csv.Contains("Alkalmi vevő")&&!csv.Contains("Partner"));
        });
        yield return Case("future database schema is rejected before writing",f=>
        {
            var file=f.Database.DatabasePath(f.Company.Id);using(var c=new SqliteConnection("Data Source="+file+";Pooling=False")){c.Open();using var cmd=c.CreateCommand();cmd.CommandText="PRAGMA user_version=999";cmd.ExecuteNonQuery();}
            Check.Throws<IOException>(()=>f.Database.Execute(f.Company.Id,tx=>true));return Task.CompletedTask;
        });
    }
    private sealed class SalesProbe(ICompanyDatabase database):IModuleContext
    {
        public string ModuleId=>"sales";public TimeProvider TimeProvider=>TimeProvider.System;public ICompanyDatabase Database=>database;
        public Dictionary<string,ICommandHandler> Commands{get;}=[];
        public void RegisterCommand(ICommandHandler handler)=>Commands.Add(handler.Descriptor.Name,handler);
        public void RegisterPage(EntityPageDescriptor page){}
        public string GetCompanyDataDirectory(Guid company)=>throw new NotSupportedException();
    }
    private static int FailTransaction()=>throw new InvalidOperationException();
    private static (string,Func<Task>) Case(string name,Func<Fixture,Task> test)=>(name,async()=>{using var f=new Fixture();await f.Initialize();await test(f);});
    private sealed class Fixture:IDisposable
    {
        public TestDirectory Directory {get;}=new();public TestTime Clock {get;}=new();public CompanyStore Companies {get;}public CompanyDatabase Database {get;}public CompanyInfo Company {get;}
        public CatalogRepository Catalog {get;}public PartnerRepository Partners {get;}public ReceivingRepository Receipts {get;}public ProductRecord Product {get;private set;}=null!;public PartnerRecord Supplier {get;private set;}=null!;
        public Fixture(){Companies=new(Directory.Path);Company=Companies.Create("Tesztcég",false);Database=new(Companies.DataDirectory);Catalog=new(id=>Companies.ModuleDataDirectory(id,"catalog"),Clock,Database);Partners=new(id=>Companies.ModuleDataDirectory(id,"partners"),Clock,Database);Receipts=new(id=>Companies.ModuleDataDirectory(id,"receiving"),Clock,Database);}
        public async Task Initialize(){Supplier=await Partners.Save(new(Company.Id,null,0,new(){Name="Partner",IsSupplier=true,IsCustomer=true,IsActive=true}),"operator");await Catalog.SaveUnit(new(Company.Id,null,0,new(){Name="Darab",Code="DB",IsActive=true}),"operator");Product=await Catalog.SaveProduct(new(Company.Id,null,0,new(){Name="Egér",Sku="MOUSE-1",UnitCode="DB",IsActive=true,IsSalable=true,IsPurchaseable=true}),"operator");}
        public Task<ReceiptRecord> Receipt(string name,string sku="",decimal quantity=1,bool service=false)=>Receipts.Save(new(Company.Id,null,0,new(Guid.NewGuid().ToString("N"),Supplier.Id,"2026-10-20",null,null,"Bolt",[new(null,null,name,sku,"","DB",quantity,100,27,service)])),"operator");
        public void Opening(decimal quantity){var id=Guid.NewGuid();Database.Execute(Company.Id,tx=>StockLedger.Post(tx,"opening:"+id,"inventory",id,[new(Product.Id,Product.Data.Name,"DB","Bolt",quantity)],"Nyitó","operator",Clock.Now));}
        public decimal Balance()=>Database.Execute(Company.Id,tx=>StockLedger.Balances(tx).FirstOrDefault()?.Quantity??0);
        public SaleRequest Sale(decimal quantity)=>new(Company.Id,Guid.NewGuid(),Supplier.Id,"Bolt",[new(Product.Id,quantity,100,27)]);
        public void Dispose()=>Directory.Dispose();
    }
}
