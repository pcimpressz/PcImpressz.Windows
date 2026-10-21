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
            Check.Equal(0, view.Sections.Single(section => section.Id == "incoming").Lines.Length);
            var filled = PartnerDossier.Apply(view, new(saved.Id, 1000m, 1270m, "2026-08-04", "2026-10-14",
                [new("Szállítás", 2, 400m)], [new("2026/1", "2026-10-14", 1000m, 1270m, false, "bevételezve", [new("Egérpad", 1, 1000m)])]));
            Check.True(filled.FiguresAvailable);
            var purchase = filled.Cards.Single(card => card.Id == "purchase").Value;
            Check.True(purchase.EndsWith(" Ft", StringComparison.Ordinal) && purchase.Contains("270,00", StringComparison.Ordinal));
            Check.Equal("1", filled.Cards.Single(card => card.Id == "incoming").Value);
            Check.Equal("—", filled.Cards.Single(card => card.Id == "turnover").Value);
            var invoiceLine = filled.Sections.Single(section => section.Id == "incoming").Lines[1];
            Check.Equal("2026/1", invoiceLine.Title);
            Check.Equal("Egérpad", invoiceLine.Items[0].Name);
            var both = new PartnerDossierSnapshot(saved.Id, 2000m, 2540m, "2026-08-04", "2026-10-14", [],
                [new("2026/1", "2026-10-14", 1000m, 1270m, false, "bevételezve", [new("Egérpad", 1, 1000m)]),
                 new("2026/2", "2026-08-04", 1000m, 1270m, true, "bevételezve", [new("Egérpad", 2, 1000m)])]);
            Check.Equal(1, PartnerDossier.Within(both, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)).Invoices.Length);
            Check.Throws<ArgumentException>(() => PartnerDossier.Period("2026-12-01", "2026-01-01"));
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
        yield return ("bounded protocol rejects an oversized request before allocation",async()=>
        {
            using var stream=new MemoryStream();var header=new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header,int.MaxValue);stream.Write(header);stream.Position=0;
            await Check.ThrowsAsync<IOException>(async()=>await LocalProtocol.ReadAsync<LocalRequest>(stream,default));
        });
        yield return Case("partner search keeps a name fragment and sorts by city", async f =>
        {
            await f.Repository.Save(new(f.First.Id, null, 0, Data("Alma Kft.") with { City = "Zalaegerszeg" }), "operator");
            await f.Repository.Save(new(f.First.Id, null, 0, Data("Béla Bt.") with { City = "Ajka" }), "operator");
            await f.Repository.Save(new(f.First.Id, null, 0, Data("Cédrus") with { City = "Pécs" }), "operator");
            var partners = (await f.Repository.Snapshot(f.First.Id)).Partners;
            Check.Equal("Alma Kft.", PartnerListing.Match(partners, new(f.First.Id, "kft")).Single().Data.Name);
            var sorted = PartnerListing.Match(partners, new(f.First.Id, Sort: "city"));
            Check.Equal("Ajka", sorted[0].Data.City);
            Check.Equal("Zalaegerszeg", sorted[^1].Data.City);
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
        public void Dispose()=>Directory.Dispose();
    }
}
