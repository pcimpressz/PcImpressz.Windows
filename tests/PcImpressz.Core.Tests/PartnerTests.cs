using System.Net;
using System.Text;
using System.Text.Json;
using PcImpressz.Contracts;
using PcImpressz.Core.Companies;
using PcImpressz.Modules.Partners;

namespace PcImpressz.Core.Tests;

internal static class PartnerTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return Case("dossier names the missing sources and invents no turnover", async f =>
        {
            var saved = await f.Repository.Save(new(f.First.Id, null, 0, Data("Beszállító Kft.") with
            { TaxId = "12345678-2-42", City = "Budapest", IsCustomer = false, IsSupplier = true }), "operator");
            var view = PartnerDossier.Open(await f.Repository.Snapshot(f.First.Id), saved.Id);
            Check.Equal(saved.Id, view.PartnerId);
            Check.Equal("Beszállító Kft. dossziéja", view.Title);
            Check.True(view.Subtitle.Contains("szállító", StringComparison.Ordinal));
            Check.True(view.Subtitle.Contains("12345678-2-42", StringComparison.Ordinal));
            Check.True(!view.FiguresAvailable);
            Check.Equal(8, view.Cards.Length);
            Check.True(view.Cards.All(card => card.Value == "—" && card.Status == PartnerDossier.Waiting));
            Check.Equal("incoming", view.Sections.Single(section => section.Source == "Bevételezés").Id);
            Check.True(view.Sections.All(section => section.Status == PartnerDossier.Waiting && section.Detail.Length > 20));
            var state = await f.Repository.Snapshot(f.First.Id);
            Check.Throws<ArgumentException>(() => PartnerDossier.Open(state, Guid.NewGuid()));
        });
        yield return Case("new company starts completely empty", async f =>
        { Check.Equal(0, (await f.Repository.Snapshot(f.First.Id)).Partners.Length); });
        yield return Case("partner data is isolated by company", async f =>
        {
            await f.Repository.Save(new(f.First.Id,null,0,Data("Első partner")),"operator");
            Check.Equal(1,(await f.Repository.Snapshot(f.First.Id)).Partners.Length);
            Check.Equal(0,(await f.Repository.Snapshot(f.Second.Id)).Partners.Length);
        });
        yield return Case("new company does not create sample records", async f =>
        {
            var third=f.Companies.Create("Új saját cég",false);
            Check.Equal(0,(await f.Repository.Snapshot(third.Id)).Partners.Length);
            Check.True(!Directory.Exists(f.Companies.ModuleDataDirectory(third.Id,"partners")));
        });
        yield return Case("unknown company cannot create an arbitrary data directory", async f =>
        { await Check.ThrowsAsync<ArgumentException>(async()=>await f.Repository.Save(new(Guid.NewGuid(),null,0,Data("Idegen")),"operator")); });
        yield return Case("partner audit is tied to trusted caller", async f =>
        {
            var item=await f.Repository.Save(new(f.First.Id,null,0,Data("Naplózott partner")),"windows-sid");
            Check.Equal("windows-sid",item.CreatedBy);
            Check.Equal("windows-sid",(await f.Repository.Snapshot(f.First.Id)).Audit.Single().Actor);
        });
        yield return Case("stale partner edit does not overwrite a newer record", async f =>
        {
            var first=await f.Repository.Save(new(f.First.Id,null,0,Data("Eredeti")),"operator");
            await f.Repository.Save(new(f.First.Id,first.Id,first.Version,Data("Újabb név")),"operator");
            await Check.ThrowsAsync<PartnerConflictException>(async()=>await f.Repository.Save(new(f.First.Id,first.Id,first.Version,Data("Elavult név")),"other"));
            Check.Equal("Újabb név",(await f.Repository.Snapshot(f.First.Id)).Partners.Single().Data.Name);
        });
        yield return Case("normalized active tax ID collision is rejected", async f =>
        {
            await f.Repository.Save(new(f.First.Id,null,0,Data("Első") with {TaxId="12345678-1-42"}),"operator");
            await Check.ThrowsAsync<ArgumentException>(async()=>await f.Repository.Save(new(f.First.Id,null,0,Data("Második") with {TaxId="12345678 1 42"}),"operator"));
            Check.Equal(1,(await f.Repository.Snapshot(f.First.Id)).Partners.Length);
        });
        yield return Case("invalid contact data is not persisted", async f =>
        {
            await Check.ThrowsAsync<ArgumentException>(async()=>await f.Repository.Save(new(f.First.Id,null,0,Data("Hibás") with {Email="not-an-email"}),"operator"));
            await Check.ThrowsAsync<ArgumentException>(async()=>await f.Repository.Save(new(f.First.Id,null,0,Data("Hibás") with {Website="javascript:bad"}),"operator"));
            Check.Equal(0,(await f.Repository.Snapshot(f.First.Id)).Partners.Length);
        });
        yield return Case("archive retains record and history", async f =>
        {
            var saved=await f.Repository.Save(new(f.First.Id,null,0,Data("Archiválandó")),"operator");
            var archived=await f.Repository.Archive(new(f.First.Id,saved.Id,saved.Version),"operator");
            Check.True(!archived.Data.IsActive); Check.Equal(2L,archived.Version);
            var state=await f.Repository.Snapshot(f.First.Id);
            Check.Equal(1,state.Partners.Length); Check.Equal(2,state.Audit.Length);
            Check.True(File.Exists(Path.Combine(f.Companies.ModuleDataDirectory(f.First.Id,"partners"),"partners.json.bak")));
        });
        yield return Case("corrupt partner file is not silently replaced", async f =>
        {
            var directory=f.Companies.ModuleDataDirectory(f.First.Id,"partners");Directory.CreateDirectory(directory);
            var file=Path.Combine(directory,"partners.json"); File.WriteAllText(file,"corrupt");
            await Check.ThrowsAsync<IOException>(async()=>await f.Repository.Save(new(f.First.Id,null,0,Data("Partner")),"operator"));
            Check.Equal("corrupt",File.ReadAllText(file));
        });
        yield return Case("CSV export neutralizes spreadsheet formulas", async f =>
        {
            var saved=await f.Repository.Save(new(f.First.Id,null,0,Data("=HYPERLINK(bad)")),"operator");
            var csv=PartnerRepository.ExportCsv([saved]); Check.True(csv.Contains("\"'=HYPERLINK(bad)\"",StringComparison.Ordinal));
        });
        yield return Case("import preview does not write and commit imports once", async f =>
        {
            var importer=f.Importer();var preview=await importer.Preview(f.Options(),"operator",default);
            Check.Equal(2,preview.NewCount); Check.Equal(0,(await f.Repository.Snapshot(f.First.Id)).Partners.Length);
            Check.Equal(2,await importer.Commit(new(f.First.Id,preview.PreviewId),"operator",default));
            var records=(await f.Repository.Snapshot(f.First.Id)).Partners;Check.Equal("Budapest",records.Single(p=>p.ExternalId==1).Data.City);
            await Check.ThrowsAsync<ArgumentException>(async()=>await importer.Commit(new(f.First.Id,preview.PreviewId),"operator",default));
            Check.Equal(2,(await f.Repository.Snapshot(f.First.Id)).Partners.Length);
        });
        yield return Case("repeated import skips existing partners", async f =>
        {
            var importer=f.Importer();var preview=await importer.Preview(f.Options(),"operator",default);
            await importer.Commit(new(f.First.Id,preview.PreviewId),"operator",default);
            var repeated=await importer.Preview(f.Options(),"operator",default);
            Check.Equal(0,repeated.NewCount);Check.Equal(2,repeated.SkippedCount);
        });
        yield return Case("import preview is bound to company and actor", async f =>
        {
            var importer=f.Importer();var preview=await importer.Preview(f.Options(),"operator",default);
            await Check.ThrowsAsync<ArgumentException>(async()=>await importer.Commit(new(f.Second.Id,preview.PreviewId),"operator",default));
            await Check.ThrowsAsync<ArgumentException>(async()=>await importer.Commit(new(f.First.Id,preview.PreviewId),"other",default));
            Check.Equal(0,(await f.Repository.Snapshot(f.First.Id)).Partners.Length);
        });
        yield return Case("expired import preview cannot commit", async f =>
        {
            var importer=f.Importer();var preview=await importer.Preview(f.Options(),"operator",default); f.Clock.Now=f.Clock.Now.AddMinutes(11);
            await Check.ThrowsAsync<ArgumentException>(async()=>await importer.Commit(new(f.First.Id,preview.PreviewId),"operator",default));
        });
        yield return Case("import does not overwrite edits after preview", async f =>
        {
            var importer=f.Importer();var preview=await importer.Preview(f.Options(),"operator",default);
            await f.Repository.Save(new(f.First.Id,null,0,Data("Közben felvett partner")),"operator");
            await Check.ThrowsAsync<PartnerConflictException>(async()=>await importer.Commit(new(f.First.Id,preview.PreviewId),"operator",default));
            Check.Equal(1,(await f.Repository.Snapshot(f.First.Id)).Partners.Length);
        });
        yield return Case("import token is never persisted", async f =>
        {
            var importer=f.Importer();var preview=await importer.Preview(f.Options(),"operator",default);
            await importer.Commit(new(f.First.Id,preview.PreviewId),"operator",default);
            foreach(var file in Directory.EnumerateFiles(f.Directory.Path,"*",SearchOption.AllDirectories))
                Check.True(!File.ReadAllText(file).Contains("TEST-API-TOKEN",StringComparison.Ordinal));
        });
        yield return Case("cross-origin pagination never receives the API token", async f =>
        {
            var calls=0;var importer=new InvenTreeImport(f.Repository,f.Clock,()=>new FakeHandler(_=>
            { calls++;return "{\"results\":[],\"next\":\"https://other.invalid/api/company/\"}"; }));
            await Check.ThrowsAsync<HttpRequestException>(async()=>await importer.Preview(f.Options(),"operator",default));
            Check.Equal(1,calls);
        });
        yield return ("HTTP import requires explicit LAN permission", ()=>
        {
            Check.Throws<ArgumentException>(()=>InvenTreeImport.ValidateServer("http://192.168.110.71",false));
            Check.Equal("http://192.168.110.71/api/",InvenTreeImport.ValidateServer("http://192.168.110.71",true).AbsoluteUri);
            Check.Throws<ArgumentException>(()=>InvenTreeImport.ValidateServer("http://public.invalid",true));
            Check.Throws<ArgumentException>(()=>InvenTreeImport.ValidateServer("https://user:secret@host.invalid",false));
            return Task.CompletedTask;
        });
        yield return ("bounded protocol rejects an oversized request before allocation",async()=>
        {
            using var stream=new MemoryStream();var header=new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header,int.MaxValue);stream.Write(header);stream.Position=0;
            await Check.ThrowsAsync<IOException>(async()=>await LocalProtocol.ReadAsync<LocalRequest>(stream,default));
        });
    }

    private static PartnerData Data(string name)=>new(){Name=name,IsCustomer=true};
    private static (string,Func<Task>) Case(string name,Func<Fixture,Task> test)=>(name,async()=>{using var f=new Fixture();await test(f);});
    private sealed class Fixture:IDisposable
    {
        public readonly TestDirectory Directory=new();public readonly TestTime Clock=new();
        public CompanyStore Companies {get;} public CompanyInfo First {get;} public CompanyInfo Second {get;}
        public PartnerRepository Repository {get;}
        public Fixture()
        {
            Companies=new(Directory.Path);First=Companies.Create("Saját cég",false);Second=Companies.Create("Üres demó",true);
            Repository=new(id=>Companies.ModuleDataDirectory(id,"partners"),Clock);
        }
        public ImportOptions Options()=>new(First.Id,"https://inventree.test","TEST-API-TOKEN");
        public InvenTreeImport Importer()=>new(Repository,Clock,()=>new FakeHandler(request=>
            request.RequestUri!.AbsolutePath.Contains("/address/",StringComparison.Ordinal)
            ? "[{\"pk\":3,\"company\":1,\"primary\":true,\"country\":\"HU\",\"postal_city\":\"Budapest\",\"postal_code\":\"1111\",\"line1\":\"Teszt utca 1.\"}]"
            : "{\"results\":[{\"pk\":1,\"name\":\"Első partner\",\"tax_id\":\"1234\",\"is_customer\":true},{\"pk\":2,\"name\":\"Második partner\",\"tax_id\":\"5678\",\"is_supplier\":true}],\"next\":null}"));
        public void Dispose()=>Directory.Dispose();
    }
    private sealed class FakeHandler(Func<HttpRequestMessage,string> response):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            Check.Equal(HttpMethod.Get,request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(response(request),Encoding.UTF8,"application/json")});
        }
    }
}
