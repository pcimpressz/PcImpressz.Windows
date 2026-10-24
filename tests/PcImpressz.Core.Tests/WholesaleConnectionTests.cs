using System.Net;
using System.Text;
using System.Text.Json;
using PcImpressz.Modules.Quotes;
namespace PcImpressz.Core.Tests;
internal static class WholesaleConnectionTests
{
    public static IEnumerable<(string,Func<Task>)> Cases()
    {
        yield return ("wholesale credentials encrypted, company scoped and preserved",()=>
        {
            using var dir=new TestDirectory();var company=Guid.NewGuid();
            WholesaleConnections.Save(dir.Path,new(company,"pca","Saját megjegyzés",true,new(){["pca_token"]="private-token-value"}));
            WholesaleConnections.Save(dir.Path,new(company,"pca","Második megjegyzés",false,new(){["pca_token"]=""}));
            var rows=JsonSerializer.SerializeToElement(WholesaleConnections.List(dir.Path,company),PcImpressz.Contracts.CompanyDocuments.Json);
            var pca=rows.EnumerateArray().Single(r=>r.GetProperty("id").GetString()=="pca");
            Check.True(pca.GetProperty("fields")[0].GetProperty("hasValue").GetBoolean());
            Check.True(pca.GetProperty("note").GetString()=="Második megjegyzés");
            Check.True(!rows.ToString().Contains("private-token-value"));
            Check.True(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(dir.Path,"wholesale-connections.bin"))).Contains("private-token-value"));
            var failed=false;try{WholesaleConnections.List(dir.Path,Guid.NewGuid());}catch(System.Security.Cryptography.CryptographicException){failed=true;}
            Check.True(failed);Check.True(rows.GetArrayLength()==10);return Task.CompletedTask;
        });
        yield return ("legacy invoice CompMarket connection is reused without exposing credentials",()=>
        {
            using var dir=new TestDirectory();var company=Guid.NewGuid();
            PcImpressz.Modules.Receiving.SupplierConnectionStore.Save(dir.Path,new(company,"old-pid","old-auth","http://user:pass@217.113.49.52:8888",true));
            var migrated=CompMarketQuoteConnectionStore.Read(dir.Path,company)!;
            Check.True(migrated.CustomerId=="old-pid"&&migrated.AuthCode=="old-auth"&&migrated.ProxyUrl.Contains("user:pass"));
            var rows=JsonSerializer.Serialize(WholesaleConnections.List(dir.Path,company));Check.True(!rows.Contains("old-auth")&&!rows.Contains("user:pass"));
            CompMarketQuoteConnectionStore.Save(dir.Path,new(company,"old-pid","","http://217.113.49.52:8888",true));
            Check.True(CompMarketQuoteConnectionStore.Read(dir.Path,company)!.AuthCode=="old-auth");return Task.CompletedTask;
        });
        yield return ("compmarket XML test tracks success then rejects authentication XML",async()=>
        {
            using var dir=new TestDirectory();var connection=new CompMarketQuoteConnection("pid","secret","http://217.113.49.52:8888",true);
            var success=await CompMarketQuoteFeed.TestAsync(dir.Path,connection,CancellationToken.None,new Response("<termekek><termek><cikkszam>123</cikkszam><nev>Teszt patron</nev><ar>1000</ar></termek></termekek>"));
            Check.True(success.Success&&success.Products==1&&success.SucceededAt is not null);
            try{await CompMarketQuoteFeed.TestAsync(dir.Path,connection,CancellationToken.None,new Response("<error>Hibas hitelesitesi adatok. A szolgaltatas jelenleg nem erheto el ezzel a partnerazonositoval.</error>"));throw new Exception("Accepted error XML");}catch(ArgumentException){}
            var failure=WholesaleConnections.ReadHealth(dir.Path)!;
            Check.True(!failure.Success&&failure.SucceededAt==success.SucceededAt&&failure.Products is null);
            Check.True(!failure.Message.Contains("secret"));
        });
        yield return ("compmarket test rejects HTML and disabled connections",async()=>
        {
            using var dir=new TestDirectory();
            try{await CompMarketQuoteFeed.TestAsync(dir.Path,new("pid","secret","http://217.113.49.52:8888",true),CancellationToken.None,new Response("<!DOCTYPE html><html><body>Access denied. This page is not a wholesale XML product list.</body></html>"));throw new Exception("Accepted HTML");}catch(ArgumentException){}
            var handler=new Response("unused");
            try{await CompMarketQuoteFeed.TestAsync(dir.Path,new("pid","secret","http://217.113.49.52:8888",false),CancellationToken.None,handler);throw new Exception("Accepted disabled connection");}catch(ArgumentException){}
            Check.True(handler.Calls==0);
        });
        yield return ("cached CompMarket search names a missing snapshot and reads a downloaded file", async () =>
        {
            using var dir = new TestDirectory();
            try { CompMarketQuoteFeed.SearchCached(dir.Path, "patron", 0, 27); throw new Exception("Üres mappa kereshető volt."); }
            catch (ArgumentException ex) { Check.True(ex.Message.Contains("Még nincs letöltött", StringComparison.Ordinal)); }
            var connection = new CompMarketQuoteConnection("pid", "secret", "http://217.113.49.52:8888", true);
            await CompMarketQuoteFeed.TestAsync(dir.Path, connection, CancellationToken.None, new Response("<termekek><termek><cikkszam>SSD1</cikkszam><nev>SSD 1 TB</nev><ar>22500</ar></termek></termekek>"));
            var found = CompMarketQuoteFeed.SearchCached(dir.Path, "ssd", 0, 27);
            Check.Equal("SSD 1 TB", found[0].Name);
            Check.Equal(22500m, found[0].PurchaseNet);
            Check.True(WholesaleDownloadJobs.Explain(new OperationCanceledException("The operation was canceled.")).Contains("megszakadt", StringComparison.Ordinal));
        });
    }
    private sealed class Response(string body):HttpMessageHandler
    {
        public int Calls {get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {Calls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/xml")});}
    }
}
