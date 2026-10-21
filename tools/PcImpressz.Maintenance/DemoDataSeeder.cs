using PcImpressz.Business;
using PcImpressz.Contracts;
using PcImpressz.Core.Companies;
using PcImpressz.Modules.Catalog;
using PcImpressz.Modules.Partners;
using PcImpressz.Modules.Receiving;
using PcImpressz.Modules.Sales;

namespace PcImpressz.Maintenance;

public sealed record DemoSeedReport(int Partners,int Products,int Receipts,int Sales,int Movements,bool AlreadySeeded=false);

// Explicit offline maintenance action, never automatic startup data.
// One outer company transaction covers every repository write and the completion marker.
public static class DemoDataSeeder
{
    public const string Marker="dataset-v1";
    public static void RequireDemo(CompanyStore companies,Guid company)
    {
        if(companies.List().SingleOrDefault(c=>c.Id==company) is not {IsDemo:true})
            throw new ArgumentException("Mintaadat csak kifejezetten demóként jelölt cégbe tölthető.");
    }
    public static DemoSeedReport Seed(CompanyStore companies,ICompanyDatabase database,Guid company,TimeProvider time,string actor)
    {
        RequireDemo(companies,company);
        if(string.IsNullOrWhiteSpace(actor))throw new ArgumentException("A feltöltő felhasználó azonosítása kötelező.");
        return database.Execute(company,tx=>
        {
            if(tx.Read<DemoSeedReport>("demo",Marker) is {} previous)return previous with{AlreadySeeded=true};
            var single=new SingleTransaction(company,tx);
            var partners=new PartnerRepository(id=>companies.ModuleDataDirectory(id,"partners"),time,single);
            var catalog=new CatalogRepository(id=>companies.ModuleDataDirectory(id,"catalog"),time,single);
            var receiving=new ReceivingRepository(id=>companies.ModuleDataDirectory(id,"receiving"),time,single);
            var before=catalog.Snapshot(company).GetAwaiter().GetResult();
            if(before.Products.Any(p=>p.Data.Sku.StartsWith("DEMO-",StringComparison.OrdinalIgnoreCase))
                ||before.Categories.Any(c=>c.Data.Code.StartsWith("DEMO-",StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("A DEMO- cikkszám/kategória névtér már használatban van; a meglévő adatokat nem írjuk felül.");
            foreach(var (code,name,symbol) in new[]{("DB","Darab","db"),("M","Méter","m")})
            {
                var existing=before.Units.SingleOrDefault(u=>u.Data.Code.Equals(code,StringComparison.OrdinalIgnoreCase));
                if(existing is not null && !existing.Data.IsActive)throw new ArgumentException("A demóhoz szükséges mértékegység inaktív: "+code);
                if(existing is null)catalog.SaveUnit(new(company,null,0,new(){Code=code,Name=name,Symbol=symbol,IsActive=true}),actor).GetAwaiter().GetResult();
            }
            var categories=new[]{("DEMO-PER","[DEMO] Perifériák"),("DEMO-ALK","[DEMO] Alkatrészek"),("DEMO-KAB","[DEMO] Kábelek és hálózat"),("DEMO-IRO","[DEMO] Irodai kiegészítők")};
            foreach(var (code,name) in categories)catalog.SaveCategory(new(company,null,0,new(){Code=code,Name=name,Description="Mesterséges tesztkategória",IsActive=true}),actor).GetAwaiter().GetResult();
            var names=new[]{"Napfény Informatika","Kék Hálózat","Fenyő Alkatrész","Duna Periféria","Tiszavirág Technika","Minta Nagyker","Hársfa Elektronika","Rézkapu Disztribúció","Kovács Teszt Anna","Szabó Teszt Péter","Fekete Teszt Éva","Nagy Teszt Bálint","Minta Könyvelőiroda","Próba Műhely","Teszt Oktatóközpont","Minta Stúdió","Próba Szerviz","Demó Iroda","Minta Kereskedő","Archivált Mintapartner"};
            var cities=new[]{"Budapest","Szeged","Győr","Pécs","Debrecen"};
            var contacts=new PartnerRecord[20];
            for(var i=0;i<20;i++)contacts[i]=partners.Save(new(company,null,0,new()
            {
                Name="[DEMO] "+names[i],Email=$"demo{i+1:00}@example.test",Website=$"https://demo{i+1:00}.example.test",
                Country="HU",PostalCode=new[]{"1000","6700","9000","7600","4000"}[i%5],City=cities[i%5],Address=$"Minta utca {i+1}.",
                IsSupplier=i<8||i==18,IsCustomer=i>=8,IsManufacturer=i is 2 or 6,IsActive=i!=19,
                Notes="DEMO MINTAADAT – kitalált partner és cím. Az adószám szándékosan üres. Tesztelésre, nem valódi kapcsolattartásra."
            }),actor).GetAwaiter().GetResult();
            var productNames=new[]{"USB optikai egér","Magyar billentyűzet","24 colos monitor","HDMI kábel 2 m","USB pendrive 64 GB","SSD 500 GB","Memória 16 GB","USB-C elosztó","Notebook 14 col","Tápegység 500 W","Számítógépház","Webkamera","USB fejhallgató","UTP kábel méterre","Hálózati kapcsoló","Egérpad","Informatikai kézikönyv","Laptopállvány","Micro USB kábel – alacsony készlet","Készlet nélküli mintacikk"};
            var prices=new decimal[]{1990,4990,45990,2490,3990,14990,12990,8990,149990,16990,11990,9990,7990,190,12990,990,2990,6990,790,4990};
            var products=new ProductRecord[20];
            for(var i=0;i<20;i++)products[i]=catalog.SaveProduct(new(company,null,0,new()
            {
                Name="[DEMO] "+productNames[i],Sku=$"DEMO-{i+1:000}",UnitCode=i==13?"M":"DB",CategoryCode=categories[i%4].Item1,
                IsActive=true,IsPurchaseable=true,IsSalable=true,Description="Teszttermék a PC-Impressz demóhoz.",Parameters=$"minta=igen\nnévleges tesztár={prices[i]}",
                Notes="DEMO MINTAADAT – nem valós termékajánlat. A tesztár a mintabizonylatokban és a paraméterekben található."
            }),actor).GetAwaiter().GetResult();
            var now=time.GetUtcNow();
            for(var i=0;i<19;i++)
            {
                var product=products[i];var op=Guid.NewGuid();
                var changes=new List<StockChange>{new(product.Id,product.Data.Name,product.Data.UnitCode,"Bolt",i==18?1:10+(i%6)*5)};
                if(i!=18)changes.Add(new(product.Id,product.Data.Name,product.Data.UnitCode,"Központi raktár",20+(i%4)*10));
                StockLedger.Post(tx,"demo-opening:"+op.ToString("N"),"inventory",op,changes.ToArray(),"[DEMO] Nyitókészlet",actor,now.AddDays(-25));
            }
            for(var i=0;i<8;i++)
            {
                var item=products[i];
                ReceiptLineDraft[] lines=i==6
                    ?[new(null,null,"[DEMO] Futárszolgáltatás","","","DB",1,4900,27,true)]
                    :i is 4 or 5
                    ?[new(null,null,"[DEMO] Párosításra váró szállítói tétel","","","DB",1,1500,27)]
                    :[new(null,item.Id,item.Data.Name,item.Data.Sku,"",item.Data.UnitCode,3,prices[i]*0.7m,27),
                      new(null,products[i+1].Id,products[i+1].Data.Name,products[i+1].Data.Sku,"",products[i+1].Data.UnitCode,2,prices[i+1]*0.7m,27)];
                var issued=now.AddDays(-20+i).ToString("yyyy-MM-dd");
                var receipt=receiving.Save(new(company,null,0,new($"DEMO-BEV-{i+1:000}",contacts[i].Id,issued,now.AddDays(i-6).ToString("yyyy-MM-dd"),
                    "DEMO MINTAADAT – nem valós számla, fizetési kötelezettséget nem jelent.",i%2==0?"Bolt":"Központi raktár",lines)),actor).GetAwaiter().GetResult();
                var status=i<4?"bevetelezve":i<6?"fuggo":i==6?"koltseg":"uj";
                if(status!="uj")receiving.Classify(new(company,receipt.Id,receipt.Version,status),actor).GetAwaiter().GetResult();
            }
            for(var i=0;i<10;i++)
            {
                var first=i;var second=i+5;var operation=Guid.NewGuid();
                var request=new SaleRequest(company,operation,contacts[8+i].Id,i%2==0?"Bolt":"Központi raktár",
                    [new(products[first].Id,1,prices[first],first==16?5:27),new(products[second].Id,second==13?1.5m:2,prices[second],second==16?5:27)]);
                var sale=SalesModule.Post(tx,request,actor,now.AddDays(-10+i));
                if(i is 3 or 8)SalesModule.Reverse(tx,new(company,sale.Id,sale.Version),actor,now);
            }
            var report=new DemoSeedReport(20,20,8,10,StockLedger.Movements(tx).Count(m=>m.Reason.StartsWith("[DEMO]",StringComparison.Ordinal)||m.SourceModule is "sales" or "receiving"));
            tx.Write("demo",Marker,report);
            return report;
        });
    }
    private sealed class SingleTransaction(Guid company,ICompanyTransaction transaction):ICompanyDatabase
    {
        public T Execute<T>(Guid companyId,Func<ICompanyTransaction,T> action)
        {
            if(companyId!=company)throw new InvalidOperationException("A demófeltöltés nem írhat másik cégbe.");
            return action(transaction);
        }
    }
}
