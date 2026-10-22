using PcImpressz.Contracts;
using PcImpressz.Core.Security;
using PcImpressz.Core.Storage;

namespace PcImpressz.Core.Tests;
internal static class WorkspacePanelTests
{
    public static IEnumerable<(string,Func<Task>)> Cases()
    {
        yield return ("workspace layout rejects nonadmin writes and isolates company pages and surfaces",()=>
        {
            using var dir=new TestDirectory();var clock=new TestTime();var company=Guid.NewGuid();const string sid="test-owner";const string password="Test-only-password-2026";
            var data=new CompanyDatabase(id=>Path.Combine(dir.Path,"data",id.ToString("N")));var credentials=new CompanyDatabase(id=>Path.Combine(dir.Path,"credentials",id.ToString("N")));var admin=new UiAdministrationService(data,credentials,clock);
            var session=admin.Bootstrap(new(company,"admin",password),sid);admin.CreateUser(new(company,session.Token,"worker",password,"warehouse"),sid);var worker=admin.Login(new(company,"worker",password),sid);
            var request=new UiWorkspaceSave(company,"receiving.receiving","incoming",session.Token,0,[new("editor",620,420),new("list",0,0)]);
            Check.Throws<UnauthorizedAccessException>(()=>admin.SaveWorkspace(request with{Token=worker.Token},sid));
            var saved=admin.SaveWorkspace(request,sid);Check.Equal(1L,saved.Revision);Check.Equal("editor",saved.Panels[0].Id);Check.Equal(620d,saved.Panels[0].Width);
            Check.Equal(0L,admin.ReadWorkspace(new(company,"receiving.receiving","outgoing")).Revision);Check.Equal(0L,admin.ReadWorkspace(new(Guid.NewGuid(),"receiving.receiving","incoming")).Revision);
            Check.Throws<UnauthorizedAccessException>(()=>admin.SaveWorkspace(request with{CompanyId=Guid.NewGuid()},sid));
            Check.Throws<InvalidOperationException>(()=>admin.SaveWorkspace(request,sid));
            var reset=admin.SaveWorkspace(request with{Revision=1,Panels=[]},sid);Check.Equal(2L,reset.Revision);Check.Equal(0,reset.Panels.Length);Check.Equal(2,reset.History.Length);
            return Task.CompletedTask;
        });
        yield return ("workspace validates finite bounded sizes ids and bounded history",()=>
        {
            foreach(var panel in new[]{new UiPanelPlacement("../bad"),new("a",double.NaN,0),new("a",0,double.PositiveInfinity),new("a",20000,0),new("a",0,-1),new("a",20,0)})Check.Throws<ArgumentException>(()=>UiAdministrationService.ValidatePanels([panel]));
            Check.Throws<ArgumentException>(()=>UiAdministrationService.ValidatePanels([new("a"),new("a")]));
            UiAdministrationService.ValidatePanels([new("a",0,0),new("b",280,160)]);
            using var dir=new TestDirectory();var clock=new TestTime();var company=Guid.NewGuid();var data=new CompanyDatabase(id=>Path.Combine(dir.Path,"data",id.ToString("N")));var cred=new CompanyDatabase(id=>Path.Combine(dir.Path,"cred",id.ToString("N")));var service=new UiAdministrationService(data,cred,clock);var session=service.Bootstrap(new(company,"admin","Test-only-password-2026"),"sid");
            for(var i=0;i<15;i++)service.SaveWorkspace(new(company,"catalog.products","main",session.Token,i,[new("editor",500+i,300)]),"sid");
            Check.Equal(10,service.ReadWorkspace(new(company,"catalog.products","main")).History.Length);return Task.CompletedTask;
        });
    }
}
