using System.Text.Json;
using PcImpressz.Contracts;
using PcImpressz.Core.Commands;
namespace PcImpressz.ApplicationHost;
internal sealed partial class LocalCommandService
{
    private async Task<LocalResponse> SeedDemoAsync(LocalRequest request,string sid,CancellationToken ct)
    {
        var input=request.Payload.Deserialize<CompanyDemoSeedRequest>()??throw new ArgumentException("Hiányzó demócég.");
        var session=administration.RequireAdmin(input.CompanyId,request.SessionToken,sid);
        if(companies.List().SingleOrDefault(c=>c.Id==input.CompanyId) is not {IsDemo:true})
            throw new ArgumentException("Mintaadat kizárólag a kiválasztott demócégbe tölthető.");
        foreach(var module in new[]{"partners","catalog","inventory","receiving","sales"})
        {
            if(!modulesEnabled.IsEnabled(module)||!runtime.Reports.Any(r=>r.Id==module&&r.Status==PcImpressz.Core.Modules.ModuleStatus.Active))
                throw new InvalidOperationException("A mintaadatokhoz mind az öt üzleti modul legyen bekapcsolva és aktív. Hiányzik: "+module);
            var decision=licenses.Evaluate(module,module+".write");
            if(!decision.Allowed)throw new CommandDeniedException(decision.Reason,decision.Status);
        }
        var already=database.Execute(input.CompanyId,tx=>tx.Get("demo",DemoDataset.Marker) is not null||tx.Get("demo",DemoDataset.LegacyMarker) is not null);
        string? snapshot=null;
        if(!already)
        {
            var folder=Path.Combine(options.DataDirectory,"backups","demo-seed",input.CompanyId.ToString("N"));
            Directory.CreateDirectory(folder);
            snapshot=backups.Create(input.CompanyId,Path.Combine(folder,"feltoltes-elott-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".pcibak"));
        }
        ct.ThrowIfCancellationRequested();
        var caller=new CallerIdentity(sid+"/"+session.UserName,commands.RegisteredPermissions);
        var report=await commands.ExecuteAsync(new("receiving","seed-demo",JsonSerializer.SerializeToElement(new{companyId=input.CompanyId})),caller,ct);
        return new(true,JsonSerializer.SerializeToElement(new{report,backupFile=snapshot}));
    }
}
