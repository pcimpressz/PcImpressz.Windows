using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PcImpressz.Contracts;
using PcImpressz.Business;
using PcImpressz.Core.Storage;
using PcImpressz.Modules.Catalog;
namespace PcImpressz.Core.Tests;
internal static class PerformanceStorageTests
{
    public static IEnumerable<(string,Func<Task>)> Cases()
    {
        yield return ("SQLite native runtime includes the WAL reset fix",()=>
        {
            using var c=new SqliteConnection("Data Source=:memory:;Pooling=False");c.Open();var version=Version.Parse(c.ServerVersion);Console.WriteLine("SQLITE_NATIVE "+version);
            Check.True(version>=new Version(3,51,3)||version>=new Version(3,50,7)&&version<new Version(3,51)||version>=new Version(3,44,6)&&version<new Version(3,45),"SQLite WAL reset fix is required");return Task.CompletedTask;
        });
        yield return ("indexed product SQL preserves search, filtering, sort, stock and optimistic writes",async()=>
        {
            using var f=new Fixture();
            var a=await f.Repo.SaveProduct(new(f.Company,null,0,new(){Name="Árvíztűrő egér",Sku="ABC",ManufacturerPartNumber="AB-123",Barcode="599123",UnitCode="DB",CategoryCode="EG",SaleNetUnitPrice=100,IsSalable=true}),"test");
            var b=await f.Repo.SaveProduct(new(f.Company,null,0,new(){Name="Egér ABC tartozék",Sku="XYZ",UnitCode="DB",CategoryCode="EG",SaleNetUnitPrice=10,IsPurchaseable=true}),"test");
            f.Db.Execute(f.Company,tx=>StockLedger.Post(tx,"seed","inventory",Guid.NewGuid(),[new(a.Id,a.Data.Name,"DB","Bolt",2.0001m)],"seed","test",DateTimeOffset.UtcNow));
            var snapshot=(await f.Repo.Snapshot(f.Company)).Products;
            foreach(var query in new[]{new CatalogQuery(f.Company,"abc"),new(f.Company,"arviz egér"),new(f.Company,"gyartoi:=AB-123"),new(f.Company,"nev:\"egér abc\""),new(f.Company,"ean:599"),new(f.Company,"",MinStock:1),new(f.Company,"",MaxStock:0),new(f.Company,"",MinPrice:11),new(f.Company,"",MaxPrice:10),new(f.Company,"",Filter:"salable"),new(f.Company,"",Filter:"purchaseable"),new(f.Company,"",CategoryCodes:["EG"]),new(f.Company,"",NoCategoryMatch:true),new(f.Company,"",Sort:"saleGross",Descending:true),new(f.Company,"",Sort:"name",Descending:true),new(f.Company,"",Page:1,PageSize:1)})
            {
                var expected=CatalogModule.FilterStock(CatalogListing.Products(snapshot,query),f.Db.Execute(f.Company,CompanyReferenceQueries.StockQuantities),query);
                var page=f.Repo.IndexedProducts(query)!;
                Check.Equal(expected.Length,page.Total);Check.True(expected.Skip(query.Page*query.PageSize).Take(query.PageSize).Select(p=>p.Id).SequenceEqual(page.Records.Select(body=>JsonSerializer.Deserialize<ProductRecord>(body,CatalogJson.Options)!.Id)),query.ToString());
            }
            var changed=await f.Repo.SaveProduct(new(f.Company,a.Id,a.Version,a.Data with{Name="Új név"}),"test");
            Check.Equal(0,f.Repo.IndexedProducts(new(f.Company,"arviz"))!.Total);Check.Equal(1,f.Repo.IndexedProducts(new(f.Company,"uj nev"))!.Total);
            await Check.ThrowsAsync<CatalogConflictException>(()=>f.Repo.SaveProduct(new(f.Company,a.Id,a.Version,a.Data),"test"));
            await Check.ThrowsAsync<ArgumentException>(()=>f.Repo.SaveProduct(new(f.Company,null,0,b.Data with{Sku="ABC"}),"test"));
            Check.Equal(changed.Version,(await f.Repo.GetProduct(f.Company,a.Id))!.Version);
        });
        yield return ("stock projection is exact, immutable, idempotent and rolled back with its document",()=>
        {
            using var f=new Fixture();var product=Guid.NewGuid();var source=Guid.NewGuid();
            var changes=new[]{new StockChange(product,"Teszt","DB","Bolt",0.0001m)};
            f.Db.Execute(f.Company,tx=>StockLedger.Post(tx,"one","inventory",source,changes,"ok","test",DateTimeOffset.UtcNow));
            f.Db.Execute(f.Company,tx=>StockLedger.Post(tx,"one","inventory",source,changes,"ok","test",DateTimeOffset.UtcNow));
            Check.Equal(0.0001m,f.Db.Read(f.Company,tx=>CompanyReferenceQueries.StockQuantity(tx,product)));
            Check.Throws<InvalidOperationException>(()=>f.Db.Execute<int>(f.Company,tx=>{StockLedger.Post(tx,"rollback","inventory",Guid.NewGuid(),changes,"ok","test",DateTimeOffset.UtcNow);throw new InvalidOperationException();}));
            Check.Equal(0.0001m,f.Db.Read(f.Company,tx=>StockLedger.Balances(tx).Single().Quantity));
            Check.Throws<ArgumentException>(()=>f.Db.Read(f.Company,tx=>tx is IIndexedCompanyTransaction index?index.Products(new([],"all","",false,-1,10)):throw new Exception()));
            Check.Throws<SqliteException>(()=>f.Db.Read(f.Company,tx=>{tx.Put("test","denied","{}");return 0;}));
            return Task.CompletedTask;
        });
        yield return ("v1 catalog and stock migrate atomically and remain portable in backups",async()=>
        {
            using var f=new Fixture();var product=await f.Repo.SaveProduct(new(f.Company,null,0,new(){Name="Örökölt termék",Sku="OLD",UnitCode="DB"}),"test");
            var snapshot=await f.Repo.Snapshot(f.Company);
            var file=f.Db.DatabasePath(f.Company);
            using(var c=new SqliteConnection("Data Source="+file+";Pooling=False")){c.Open();c.CreateCollation("HU",StringComparer.OrdinalIgnoreCase.Compare);using var cmd=c.CreateCommand();cmd.CommandText="DROP TRIGGER IF EXISTS order_ai;DROP TRIGGER IF EXISTS order_ad;DROP TRIGGER IF EXISTS order_au;DROP TABLE IF EXISTS order_fts;DROP TABLE IF EXISTS customer_orders;DROP TABLE IF EXISTS stock_reservations;DROP TRIGGER catalog_ai;DROP TRIGGER catalog_ad;DROP TRIGGER catalog_au;DROP TABLE catalog_fts;DROP TABLE catalog_products;DROP TABLE catalog_audit;DROP TABLE stock_balances;DROP TABLE stock_history;UPDATE documents SET body=$body WHERE module='catalog' AND id='state';PRAGMA user_version=1";cmd.Parameters.AddWithValue("$body",JsonSerializer.Serialize(snapshot,CatalogJson.Options));cmd.ExecuteNonQuery();}
            var fresh=new CompanyDatabase(_=>f.Directory.Path);var repo=new CatalogRepository(_=>f.Directory.Path,TimeProvider.System,fresh);
            Check.Equal(product.Id,JsonSerializer.Deserialize<ProductRecord>(repo.IndexedProducts(new(f.Company,"orokolt"))!.Records.Single(),CatalogJson.Options)!.Id);
            var backup=Path.Combine(f.Directory.Path,"portable.db");fresh.Backup(f.Company,backup);CompanyDatabase.ValidateBackup(backup);
            Check.Equal(snapshot.Revision,(await repo.Snapshot(f.Company)).Revision);
        });
        yield return ("VAT uses exact decimals and preserves imported source totals including negative lines",()=>
        {
            foreach(var rate in new decimal[]{0,5,18,27})foreach(var q in new decimal[]{0.0001m,1m,3m,-3m})
            {var value=VatMath.Line(q,123.4567m,rate);Check.Equal(value.Net+value.Vat,value.Gross);Check.Equal(decimal.Round(q*123.4567m,2,MidpointRounding.AwayFromZero),value.Net);}
            var source=VatMath.Line(2,100.123456789m,27,190,51.3m,241.3m);Check.Equal(241.3m,source.Gross);Check.Equal(190m,source.Net);
            Check.Throws<ArgumentException>(()=>VatMath.Line(1,100,99));return Task.CompletedTask;
        });
        yield return ("legacy retail price tiers match Python rounding and preserve zero VAT",()=>
        {
            Check.Equal(0m,PcImpressz.Shell.Customization.RetailPricing.Tiered(-1));
            foreach(var pair in new[]{(995m,1000m),(985m,980m),(1025m,1000m),(1075m,1100m),(10050m,10000m),(10150m,10200m)})Check.Equal(pair.Item2,PcImpressz.Shell.Customization.RetailPricing.Tiered(pair.Item1));
            var before=PcImpressz.Shell.Customization.RetailPricing.UseTieredRounding;
            try{PcImpressz.Shell.Customization.RetailPricing.UseTieredRounding=true;Check.Equal(1300m,PcImpressz.Shell.Customization.RetailPricing.Suggest(1000,30,0));}finally{PcImpressz.Shell.Customization.RetailPricing.UseTieredRounding=before;}
            return Task.CompletedTask;
        });
        if(Environment.GetEnvironmentVariable("PCIMPRESSZ_BENCHMARK")=="1")yield return ("benchmark 50000 products and 50000 stock movements",Benchmark);
    }
    private static async Task Benchmark()
    {
        using var f=new Fixture();var products=Enumerable.Range(0,50000).Select(i=>new ProductRecord(Guid.NewGuid(),1,new(){Name=$"Árvíztűrő termék {i:D5}",Sku=$"SKU-{i:D5}",Barcode=$"599{i:D10}",ManufacturerPartNumber=$"MPN-{i:D5}",UnitCode="DB",CategoryCode="EG",SaleNetUnitPrice=i+0.1234m,IsSalable=true},DateTimeOffset.UtcNow,"bench",DateTimeOffset.UtcNow,"bench")).ToArray();
        var state=await f.Repo.Snapshot(f.Company);var json=JsonSerializer.Serialize(state with{Products=products},CatalogJson.Options);
        var seed=Stopwatch.StartNew();f.Db.Execute(f.Company,tx=>{tx.Put("catalog","state",json);foreach(var p in products)tx.Write("inventory","movement/"+p.Id.ToString("N"),new StockMovement(p.Id,"seed","inventory",p.Id,p.Id,p.Data.Name,"DB","Bolt",1,"seed",DateTimeOffset.UtcNow,"bench"));return 0;});seed.Stop();
        var query=new CatalogQuery(f.Company,"termek 49999");
        // Same data and search; legacy path includes the deserialization every request previously required.
        double Measure(Action action){var sw=Stopwatch.StartNew();action();return sw.Elapsed.TotalMilliseconds;}
        var baseline=new List<double>();var indexed=new List<double>();
        f.Repo.IndexedProducts(query);
        for(var i=0;i<10;i++)
        {
            baseline.Add(Measure(()=>{var old=JsonSerializer.Deserialize<CatalogState>(json,CatalogJson.Options)!;Check.Equal(1,CatalogListing.Products(old.Products,query).Length);}));
            indexed.Add(Measure(()=>Check.Equal(1,f.Repo.IndexedProducts(query)!.Total)));
        }
        var firstPage=new List<double>();var save=new List<double>();var stock=new List<double>();
        for(var i=0;i<10;i++)
        {
            firstPage.Add(Measure(()=>Check.Equal(50,f.Repo.IndexedProducts(new(f.Company))!.Records.Length)));
            var current=(await f.Repo.GetProduct(f.Company,products[i].Id))!;
            save.Add(Measure(()=>f.Repo.SaveProduct(new(f.Company,current.Id,current.Version,current.Data with{Notes="updated"}),"bench").GetAwaiter().GetResult()));
            stock.Add(Measure(()=>f.Db.Execute(f.Company,tx=>StockLedger.Post(tx,"bench-"+i,"inventory",Guid.NewGuid(),[new(products[i].Id,products[i].Data.Name,"DB","Bolt",1)],"bench","bench",DateTimeOffset.UtcNow))));
        }
        double P95(List<double> values)=>values.Order().ElementAt((int)Math.Ceiling(values.Count*.95)-1);
        var result=new{products=50000,movements=50000,runs=10,seedMs=seed.Elapsed.TotalMilliseconds,legacySearchP95Ms=P95(baseline),indexedSearchP95Ms=P95(indexed),firstPageP95Ms=P95(firstPage),singleSaveP95Ms=P95(save),stockPostP95Ms=P95(stock),speedup=P95(baseline)/P95(indexed),runtime=Environment.Version.ToString(),os=Environment.OSVersion.ToString(),cpu=Environment.ProcessorCount};
        var output=Path.Combine(AppContext.BaseDirectory,"performance-50000.json");File.WriteAllText(output,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("BENCHMARK "+JsonSerializer.Serialize(result));
        Check.True(P95(indexed)<500,"Indexed product search p95 exceeded 500 ms");Check.True(P95(firstPage)<2000,"First page p95 exceeded 2000 ms");
    }
    private sealed class Fixture:IDisposable
    {
        public TestDirectory Directory{get;}=new();public Guid Company{get;}=Guid.NewGuid();public CompanyDatabase Db{get;}public CatalogRepository Repo{get;}
        public Fixture(){Db=new(_=>Directory.Path);Repo=new(_=>Directory.Path,TimeProvider.System,Db);Repo.SaveUnit(new(Company,null,0,new(){Name="Darab",Code="DB"}),"test").GetAwaiter().GetResult();Repo.SaveCategory(new(Company,null,0,new(){Name="Egér",Code="EG"}),"test").GetAwaiter().GetResult();}
        public void Dispose()=>Directory.Dispose();
    }
}
