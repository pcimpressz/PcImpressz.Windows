using PcImpressz.Business;
using PcImpressz.Contracts;
using PcImpressz.Core.Companies;
using PcImpressz.Core.Storage;
using PcImpressz.Modules.Catalog;
using PcImpressz.Modules.Partners;
using PcImpressz.Modules.Receiving;
namespace PcImpressz.Core.Tests;
internal static class DemoDataTests
{
    public static IEnumerable<(string,Func<Task>)> Cases()
    {
        yield return ("demo package contains twenty searchable products five customers five suppliers and useful stock scenarios",()=>
        {
            using var f=new Fixture();var report=f.Seed();Check.Equal(20,report.Products);Check.Equal(10,report.Partners);Check.Equal(8,report.Receipts);Check.Equal(10,report.Sales);Check.True(report.Movements>0);
            var masters=f.Database.Execute(f.Company,tx=>(Products:CompanyReferenceQueries.Products(tx),Partners:CompanyReferenceQueries.Partners(tx),Receipts:CompanyReferenceQueries.Receipts(tx),Sales:CompanyReferenceQueries.Sales(tx),Stock:CompanyReferenceQueries.StockQuantities(tx),Movements:StockLedger.Movements(tx)));
            Check.Equal(20,masters.Products.Length);Check.Equal(5,masters.Partners.Count(p=>p.IsCustomer));Check.Equal(5,masters.Partners.Count(p=>p.IsSupplier));
            Check.True(masters.Products.All(p=>p.IsSalable&&p.IsPurchaseable&&p.IsActive&&p.Barcode.StartsWith("DEMO-BC-",StringComparison.Ordinal)&&p.ManufacturerPartNumber.StartsWith("DEMO-MPN-",StringComparison.Ordinal)));
            Check.Equal(20,masters.Products.Select(p=>p.Barcode).Distinct().Count());
            Check.Equal(0m,masters.Stock.GetValueOrDefault(masters.Products.Single(p=>p.Sku=="DEMO-020").Id));
            Check.Equal(1m,masters.Stock[masters.Products.Single(p=>p.Sku=="DEMO-019").Id]);
            Check.True(masters.Stock.Values.All(q=>q>=0));Check.True(masters.Movements.Any(m=>m.Quantity%1m!=0));
            Check.True(masters.Receipts.Select(r=>r.Status).Distinct().Count()>=4);Check.Equal(2,masters.Sales.Count(s=>s.Status=="visszaforditva"));
            return Task.CompletedTask;
        });
        yield return ("demo retry preserves edits and does not duplicate any records",async()=>
        {
            using var f=new Fixture();f.Seed();var products=new CatalogRepository(id=>f.Companies.ModuleDataDirectory(id,"catalog"),TimeProvider.System,f.Database);
            var first=(await products.Snapshot(f.Company)).Products[0];await products.SaveProduct(new(f.Company,first.Id,first.Version,first.Data with{Notes="Felhasználói demópróba"}),"test");
            var before=f.Database.Execute(f.Company,tx=>string.Join("",tx.List("catalog",""))+string.Join("",tx.List("inventory",""))+string.Join("",tx.List("receiving",""))+string.Join("",tx.List("sales","")));
            Check.True(f.Seed().AlreadySeeded);
            var after=f.Database.Execute(f.Company,tx=>string.Join("",tx.List("catalog",""))+string.Join("",tx.List("inventory",""))+string.Join("",tx.List("receiving",""))+string.Join("",tx.List("sales","")));
            Check.Equal(before,after);
        });
        yield return ("demo failure during sales rolls back partners products receipts stock and completion marker",()=>
        {
            using var f=new Fixture();
            Check.Throws<IOException>(()=>DemoDataSeeder.Seed(id=>f.Companies.ModuleDataDirectory(id,"receiving"),new FailingDatabase(f.Database),f.Company,TimeProvider.System,"test"));
            f.Database.Execute(f.Company,tx=>{Check.Equal(0,CompanyReferenceQueries.Partners(tx).Length);Check.Equal(0,CompanyReferenceQueries.Products(tx).Length);Check.Equal(0,CompanyReferenceQueries.Receipts(tx).Length);Check.Equal(0,StockLedger.Movements(tx).Count());Check.True(tx.Get("demo",DemoDataset.Marker) is null);return 0;});
            Check.Equal(20,f.Seed().Products);return Task.CompletedTask;
        });
        yield return ("legacy demo marker prevents adding a second conflicting dataset",()=>
        {
            using var f=new Fixture();f.Database.Execute(f.Company,tx=>{tx.Write("demo",DemoDataset.LegacyMarker,new DemoSeedReport(20,20,8,10,70));return 0;});
            var report=f.Seed();Check.True(report.AlreadySeeded);Check.Equal(20,report.Partners);
            Check.Equal(0,f.Database.Execute(f.Company,tx=>CompanyReferenceQueries.Products(tx).Length));return Task.CompletedTask;
        });
    }
    private sealed class Fixture:IDisposable
    {
        private readonly TestDirectory _directory=new();public CompanyStore Companies{get;}public CompanyDatabase Database{get;}public Guid Company{get;}
        public Fixture(){Companies=new(_directory.Path);Company=Companies.Create("Demó próba",true).Id;Database=new(Companies.DataDirectory);}
        public DemoSeedReport Seed()=>DemoDataSeeder.Seed(id=>Companies.ModuleDataDirectory(id,"receiving"),Database,Company,TimeProvider.System,"test/demo");
        public void Dispose()=>_directory.Dispose();
    }
    private sealed class FailingDatabase(ICompanyDatabase inner):ICompanyDatabase
    {
        public T Execute<T>(Guid company,Func<ICompanyTransaction,T> action)=>inner.Execute(company,tx=>action(new FailingTransaction(tx)));
    }
    private sealed class FailingTransaction(ICompanyTransaction inner):ICompanyTransaction
    {
        public string? Get(string module,string key)=>inner.Get(module,key);
        public void Put(string module,string key,string json){if(module=="sales")throw new IOException("Injected sales write failure");inner.Put(module,key,json);}
        public void Delete(string module,string key)=>inner.Delete(module,key);
        public IReadOnlyList<string> List(string module,string prefix)=>inner.List(module,prefix);
    }
}
