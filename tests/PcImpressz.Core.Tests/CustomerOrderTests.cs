using System.Text.Json;
using PcImpressz.Contracts;
using PcImpressz.Business;
using PcImpressz.Core.Companies;
using PcImpressz.Core.Storage;
using PcImpressz.Modules.Catalog;
using PcImpressz.Modules.Partners;
using PcImpressz.Modules.Sales;
namespace PcImpressz.Core.Tests;
internal static class CustomerOrderTests
{
    public static IEnumerable<(string,Func<Task>)> Cases()
    {
        yield return Case("orders reserve free stock once and protect it from normal checkout and corrections",f=>
        {
            var initial=f.Physical();var order=f.Save(3);Check.Equal(initial,f.Physical());
            var reserve=new OrderAction(f.Company,Guid.NewGuid(),order.Id,order.Version);order=f.Reserve(reserve);
            Check.Equal(3m,order.Lines[0].Reserved);Check.Equal(initial-3,f.Free());Check.Equal(initial,f.Physical());
            var same=f.Reserve(reserve);Check.Equal(order.Version,same.Version);Check.Equal(3m,f.Reserved());
            Check.Throws<ArgumentException>(()=>f.Db.Execute(f.Company,tx=>SalesModule.Post(tx,new(f.Company,Guid.NewGuid(),null,"Bolt",[new(f.Product.Id,initial-2,100,27)]),"seller",f.Clock.Now)));
            Check.Throws<ArgumentException>(()=>f.Db.Execute(f.Company,tx=>StockLedger.Post(tx,"invalid-adjust","inventory",Guid.NewGuid(),[new(f.Product.Id,"Teszt","DB","Bolt",-(initial-2))],"test","test",f.Clock.Now)));
            Check.Equal(initial,f.Physical());return Task.CompletedTask;
        });
        yield return Case("orders partially reserve shared products across lines without overselling",f=>
        {
            var first=f.Save(8);first=f.Reserve(new(f.Company,Guid.NewGuid(),first.Id,first.Version));
            var second=f.Save(5,duplicateProduct:true);second=f.Reserve(new(f.Company,Guid.NewGuid(),second.Id,second.Version));
            Check.Equal(2m,second.Lines.Sum(l=>l.Reserved));Check.Equal(10m,f.Reserved());Check.Equal(0m,f.Free());
            return Task.CompletedTask;
        });
        yield return Case("partial order delivery keeps prices, records sale and is idempotent across restart",f=>
        {
            var order=f.Save(3);order=f.Reserve(new(f.Company,Guid.NewGuid(),order.Id,order.Version));var line=order.Lines[0];
            var request=new OrderDeliver(f.Company,Guid.NewGuid(),order.Id,order.Version,[new(line.Id,1)],"card");
            order=f.Deliver(request);Check.Equal("partial",order.Status);Check.Equal(1m,order.Lines[0].Delivered);Check.Equal(2m,order.Lines[0].Reserved);Check.Equal(9m,f.Physical());
            var sale=f.Db.Read(f.Company,tx=>SalesModule.Records(tx).Single());Check.Equal(100m,sale.Lines[0].NetUnitPrice);Check.Equal(127m,sale.Gross);Check.Equal(f.Customer.Id,sale.CustomerId!.Value);
            var restarted=new CompanyDatabase(f.Companies.DataDirectory);var repeated=restarted.Execute(f.Company,tx=>CustomerOrders.Deliver(tx,request,"seller",f.Clock.Now));Check.Equal(order.Version,repeated.Version);Check.Equal(9m,f.Physical());
            order=f.Deliver(new(f.Company,Guid.NewGuid(),order.Id,order.Version,[new(line.Id,2)],"cash"));Check.Equal("completed",order.Status);Check.Equal(7m,f.Physical());Check.Equal(0m,f.Reserved());Check.Equal(2,order.Deliveries!.Length);
            Check.Throws<ArgumentException>(()=>f.Deliver(new(f.Company,Guid.NewGuid(),order.Id,order.Version,[new(line.Id,1)])));return Task.CompletedTask;
        });
        yield return Case("failed delivery rolls back released reservation, stock, sale and order version",f=>
        {
            var order=f.Save(2);order=f.Reserve(new(f.Company,Guid.NewGuid(),order.Id,order.Version));
            var request=new OrderDeliver(f.Company,Guid.NewGuid(),order.Id,order.Version,[new(order.Lines[0].Id,1)],"invalid-payment");
            Check.Throws<ArgumentException>(()=>f.Deliver(request));Check.Equal(10m,f.Physical());Check.Equal(2m,f.Reserved());Check.Equal(order.Version,f.Get(order.Id).Version);Check.Equal(0,f.Db.Read(f.Company,tx=>SalesModule.Records(tx).Length));
            Check.Throws<ArgumentException>(()=>f.Deliver(request with{RequestId=Guid.NewGuid(),PaymentMethod="card",Lines=[new(order.Lines[0].Id,3)]}));
            return Task.CompletedTask;
        });
        yield return Case("expired reservation frees stock without deletion and can be renewed only from free stock",f=>
        {
            var order=f.Save(5);order=f.Reserve(new(f.Company,Guid.NewGuid(),order.Id,order.Version,1));f.Clock.Now=f.Clock.Now.AddDays(2);
            Check.Equal(0m,f.Reserved());Check.Equal(10m,f.Free());var expired=f.Get(order.Id);Check.Equal(0m,expired.Lines[0].Reserved);Check.Equal("open",expired.Status);
            var query=new OrderQuery(f.Company,Status:"reserved");Check.Equal(0,f.Db.Read(f.Company,tx=>CustomerOrders.List(tx,query,f.Clock.Now).Total));
            order=f.Reserve(new(f.Company,Guid.NewGuid(),order.Id,order.Version,7));Check.Equal(5m,f.Reserved());Check.True(order.ReservationUntil>f.Clock.Now);return Task.CompletedTask;
        });
        yield return Case("order release permits editing and stale saves cannot overwrite newer order",f=>
        {
            var order=f.Save(2);var original=order;order=f.Reserve(new(f.Company,Guid.NewGuid(),order.Id,order.Version));
            Check.Throws<ArgumentException>(()=>f.Db.Execute(f.Company,tx=>CustomerOrders.Save(tx,f.SaveRequest(order.Id,order.Version,5),"seller",f.Clock.Now)));
            order=f.Db.Execute(f.Company,tx=>CustomerOrders.Release(tx,new(f.Company,Guid.NewGuid(),order.Id,order.Version),"seller",f.Clock.Now));Check.Equal(0m,f.Reserved());
            order=f.Db.Execute(f.Company,tx=>CustomerOrders.Save(tx,f.SaveRequest(order.Id,order.Version,5),"seller",f.Clock.Now));Check.Equal(5m,order.Lines[0].Quantity);
            Check.Throws<ArgumentException>(()=>f.Db.Execute(f.Company,tx=>CustomerOrders.Save(tx,f.SaveRequest(original.Id,original.Version,9),"seller",f.Clock.Now)));return Task.CompletedTask;
        });
        yield return Case("order cancellation keeps prior deliveries and only releases the remaining reservation",f=>
        {
            var order=f.Save(3);order=f.Reserve(new(f.Company,Guid.NewGuid(),order.Id,order.Version));order=f.Deliver(new(f.Company,Guid.NewGuid(),order.Id,order.Version,[new(order.Lines[0].Id,1)]));
            order=f.Db.Execute(f.Company,tx=>CustomerOrders.Release(tx,new(f.Company,Guid.NewGuid(),order.Id,order.Version),"seller",f.Clock.Now,true));
            Check.Equal("cancelled",order.Status);Check.Equal(1m,order.Lines[0].Delivered);Check.Equal(9m,f.Physical());Check.Equal(0m,f.Reserved());Check.Equal(1,order.Deliveries!.Length);return Task.CompletedTask;
        });
        yield return Case("indexed order filters pages companies and operation fingerprints remain isolated",f=>
        {
            var request=f.SaveRequest(Guid.NewGuid(),0,1);var first=f.Db.Execute(f.Company,tx=>CustomerOrders.Save(tx,request,"seller",f.Clock.Now));
            Check.Equal(first.Id,f.Db.Execute(f.Company,tx=>CustomerOrders.Save(tx,request,"seller",f.Clock.Now)).Id);
            Check.Throws<ArgumentException>(()=>f.Db.Execute(f.Company,tx=>CustomerOrders.Save(tx,request with{Notes="eltérő kérés"},"seller",f.Clock.Now)));
            for(var i=0;i<24;i++){f.Clock.Now=f.Clock.Now.AddSeconds(1);f.Save(1);}
            var page=f.Db.Read(f.Company,tx=>CustomerOrders.List(tx,new(f.Company,"teszt",Page:1,PageSize:20),f.Clock.Now));Check.Equal(25,page.Total);Check.Equal(5,page.Rows.Length);
            var other=f.Companies.Create("Másik tesztcég",true).Id;Check.Equal(0,f.Db.Read(other,tx=>CustomerOrders.List(tx,new(other),f.Clock.Now).Total));
            Check.Throws<RecordNotFoundException>(()=>f.Db.Read(other,tx=>CustomerOrders.Get(tx,first.Id,f.Clock.Now)));return Task.CompletedTask;
        });
        yield return Case("concurrent orders cannot reserve the same last unit",async f=>
        {
            var a=f.Save(10);var b=f.Save(10);
            await Task.WhenAll(Task.Run(()=>f.Reserve(new(f.Company,Guid.NewGuid(),a.Id,a.Version))),Task.Run(()=>f.Reserve(new(f.Company,Guid.NewGuid(),b.Id,b.Version))));
            Check.Equal(10m,f.Reserved());Check.Equal(0m,f.Free());Check.Equal(10m,f.Physical());
        });
        yield return Case("order validation blocks duplicate line ids invalid VAT missing customers and invalid dates",f=>
        {
            var request=f.SaveRequest(Guid.NewGuid(),0,1);
            foreach(var bad in new[]{request with{CustomerId=Guid.NewGuid()},request with{DueDate="holnap"},request with{Lines=[request.Lines[0],request.Lines[0]]},request with{Lines=[request.Lines[0] with{Quantity=-1}]},request with{Lines=[request.Lines[0] with{VatRate=99}]}})
                Check.Throws<ArgumentException>(()=>f.Db.Execute(f.Company,tx=>CustomerOrders.Save(tx,bad,"seller",f.Clock.Now)));
            return Task.CompletedTask;
        });
        if(Environment.GetEnvironmentVariable("PCIMPRESSZ_BENCHMARK")=="1")yield return ("benchmark 20000 customer orders with indexed search and paging",BenchmarkOrders);
    }
    private static Task BenchmarkOrders()
    {
        using var f=new Fixture();var seed=f.Save(1);var watch=System.Diagnostics.Stopwatch.StartNew();
        f.Db.Execute(f.Company,tx=>{for(var i=0;i<20000;i++){var o=seed with{Id=Guid.NewGuid(),Number="VR-BENCH-"+i.ToString("D5"),CustomerName="Benchmark vevő "+i.ToString("D5")};tx.Write("sales","order/"+o.Id.ToString("N"),o);}return 0;});watch.Stop();
        var first=new List<double>();var search=new List<double>();
        for(var i=0;i<10;i++)
        {
            var sw=System.Diagnostics.Stopwatch.StartNew();var page=f.Db.Read(f.Company,tx=>CustomerOrders.List(tx,new(f.Company),f.Clock.Now));first.Add(sw.Elapsed.TotalMilliseconds);Check.Equal(20,page.Rows.Length);Check.Equal(20001,page.Total);
            sw.Restart();var found=f.Db.Read(f.Company,tx=>CustomerOrders.List(tx,new(f.Company,"19999"),f.Clock.Now));search.Add(sw.Elapsed.TotalMilliseconds);Check.Equal(1,found.Total);
        }
        double P95(List<double> x)=>x.Order().Last();var result=new{orders=20001,runs=10,seedMs=watch.Elapsed.TotalMilliseconds,firstPageP95Ms=P95(first),searchP95Ms=P95(search)};
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"performance-orders-20000.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("ORDER_BENCHMARK "+JsonSerializer.Serialize(result));
        Check.True(P95(search)<500,"Order search p95 exceeded 500 ms");Check.True(P95(first)<2000,"Order first page p95 exceeded 2000 ms");return Task.CompletedTask;
    }
    private static (string,Func<Task>) Case(string name,Func<Fixture,Task> run)=>(name,async()=>{using var f=new Fixture();await run(f);});
    private sealed class Fixture:IDisposable
    {
        private readonly TestDirectory _directory=new();public CompanyStore Companies{get;}public CompanyDatabase Db{get;}public Guid Company{get;}public ProductReference Product{get;}public PartnerRecord Customer{get;}public TestTime Clock{get;}=new();
        public Fixture()
        {
            Companies=new(_directory.Path);Company=Companies.Create("Rendelés teszt",true).Id;Db=new(Companies.DataDirectory);
            var partners=new PartnerRepository(id=>Companies.ModuleDataDirectory(id,"partners"),Clock,Db);Customer=partners.Save(new(Company,null,0,new(){Name="Teszt Vevő",Country="HU",PostalCode="1111",City="Budapest",Address="Teszt utca 1",IsCustomer=true,IsActive=true}),"test").GetAwaiter().GetResult();
            var catalog=new CatalogRepository(id=>Companies.ModuleDataDirectory(id,"catalog"),Clock,Db);catalog.SaveUnit(new(Company,null,0,new(){Name="Darab",Code="DB"}),"test").GetAwaiter().GetResult();
            var product=catalog.SaveProduct(new(Company,null,0,new(){Name="Teszt termék",Sku="TESZT",UnitCode="DB",SaleNetUnitPrice=100,IsSalable=true}),"test").GetAwaiter().GetResult();Product=Db.Read(Company,tx=>CompanyReferenceQueries.Products(tx,[product.Id]).Single());
            Db.Execute(Company,tx=>StockLedger.Post(tx,"opening","inventory",Guid.NewGuid(),[new(Product.Id,Product.Name,"DB","Bolt",10)],"seed","test",Clock.Now));
        }
        public OrderSave SaveRequest(Guid id,long version,decimal quantity)=>new(Company,Guid.NewGuid(),id,version,Customer.Id,"Bolt","2026-12-01","teszt",[new(Guid.NewGuid(),Product.Id,quantity,100,27)]);
        public CustomerOrder Save(decimal quantity,bool duplicateProduct=false){var request=SaveRequest(Guid.NewGuid(),0,quantity);if(duplicateProduct)request=request with{Lines=[request.Lines[0],request.Lines[0] with{Id=Guid.NewGuid()}]};return Db.Execute(Company,tx=>CustomerOrders.Save(tx,request,"seller",Clock.Now));}
        public CustomerOrder Reserve(OrderAction request)=>Db.Execute(Company,tx=>CustomerOrders.Reserve(tx,request,"seller",Clock.Now));
        public CustomerOrder Deliver(OrderDeliver request)=>Db.Execute(Company,tx=>CustomerOrders.Deliver(tx,request,"seller",Clock.Now));
        public CustomerOrder Get(Guid id)=>Db.Read(Company,tx=>CustomerOrders.Get(tx,id,Clock.Now));
        public decimal Physical()=>Db.Read(Company,tx=>StockReservations.Physical(tx,Product.Id,"Bolt"));public decimal Reserved()=>Db.Read(Company,tx=>StockReservations.Quantity(tx,Product.Id,"Bolt",Clock.Now));public decimal Free()=>Db.Read(Company,tx=>StockReservations.Free(tx,Product.Id,"Bolt",Clock.Now));
        public void Dispose()=>_directory.Dispose();
    }
}
