using PcImpressz.Contracts;
using PcImpressz.Core.Security;
using PcImpressz.Core.Storage;

namespace PcImpressz.Core.Tests;
internal static class AdministrationTests
{
    private const string Password = "Test-only-password-2026";
    private const string Sid = "test-windows-owner";
    private static readonly EntityPageDescriptor Page = new("products","catalog","Termékek","","",
        [new("name","Termék neve",Required:true),new("sku","Saját cikkszám"),new("notes","Megjegyzés","multiline")],[],new("list","save","archive","export"));
    public static IEnumerable<(string,Func<Task>)> Cases()
    {
        yield return Test("development access is opt-in SID-bound revocable and does not create accounts",f=>
        {
            Check.Throws<UnauthorizedAccessException>(()=>f.Service.DevelopmentSession(f.Company,Sid));
            var enabled=true;var dev=new UiAdministrationService(f.Data,f.Credentials,f.Clock,sid=>enabled&&sid==Sid);
            var session=dev.DevelopmentSession(f.Company,Sid);Check.Equal("admin",dev.Authenticate(f.Company,session.Token,Sid).Role);Check.True(!dev.Status(f.Company).Configured);
            Check.Throws<UnauthorizedAccessException>(()=>dev.Authenticate(Guid.NewGuid(),session.Token,Sid));Check.Throws<UnauthorizedAccessException>(()=>dev.Authenticate(f.Company,session.Token,"other-owner"));
            enabled=false;Check.Throws<UnauthorizedAccessException>(()=>dev.Authenticate(f.Company,session.Token,Sid));
            var admin=f.Admin();Check.Equal("admin",f.Service.Authenticate(f.Company,admin.Token,Sid).Role);
        });
        yield return Test("role policy separates warehouse sales and viewer permissions",f=>
        {
            foreach(var permission in new[]{"partners.read","partners.write","partners.export","catalog.read","catalog.write","catalog.export","inventory.read","inventory.write","inventory.export","receiving.read","receiving.write","receiving.export","sales.read","sales.write","sales.export"})Check.True(CompanyRoles.Allows("admin",permission));
            Check.True(CompanyRoles.Allows("warehouse","receiving.write"));Check.True(CompanyRoles.Allows("warehouse","catalog.write"));
            Check.True(!CompanyRoles.Allows("warehouse","sales.write"));Check.True(!CompanyRoles.Allows("warehouse","inventory.write"));
            Check.True(CompanyRoles.Allows("sales","sales.write"));Check.True(!CompanyRoles.Allows("sales","receiving.read"));Check.True(!CompanyRoles.Allows("sales","catalog.write"));
            Check.True(CompanyRoles.Allows("user","inventory.read"));Check.True(!CompanyRoles.Allows("user","sales.read"));Check.True(!CompanyRoles.Allows("unknown","catalog.read"));
        });
        yield return Test("account updates revoke sessions and protect the last active administrator",f=>
        {
            var admin=f.Admin();
            Check.Throws<ArgumentException>(()=>f.Service.UpdateUser(new(f.Company,admin.Token,"admin","sales",false),Sid));
            Check.Throws<ArgumentException>(()=>f.Service.UpdateUser(new(f.Company,admin.Token,"admin","admin",true),Sid));
            f.Service.CreateUser(new(f.Company,admin.Token,"worker",Password,"warehouse"),Sid);
            var worker=f.Service.Login(new(f.Company,"worker",Password),Sid);
            Check.Equal("warehouse",f.Service.Authenticate(f.Company,worker.Token,Sid).Role);
            Check.Throws<UnauthorizedAccessException>(()=>f.Service.ListUsers(f.Company,worker.Token,Sid));
            Check.Throws<UnauthorizedAccessException>(()=>f.Service.UpdateUser(new(f.Company,worker.Token,"worker","admin",false),Sid));
            f.Service.UpdateUser(new(f.Company,admin.Token,"worker","sales",true),Sid);
            Check.Throws<UnauthorizedAccessException>(()=>f.Service.Authenticate(f.Company,worker.Token,Sid));
            Check.Throws<UnauthorizedAccessException>(()=>f.Service.Login(new(f.Company,"worker",Password),Sid));
            f.Service.UpdateUser(new(f.Company,admin.Token,"worker","sales",false,"Replacement-password-2026"),Sid);
            Check.Throws<UnauthorizedAccessException>(()=>f.Service.Login(new(f.Company,"worker",Password),Sid));
            Check.Equal("sales",f.Service.Login(new(f.Company,"worker","Replacement-password-2026"),Sid).Role);
            Check.Equal(2,f.Service.ListUsers(f.Company,admin.Token,Sid).Length);
        });
        yield return Test("password change requires current password and invalidates every user session",f=>
        {
            var admin=f.Admin();var second=f.Service.Login(new(f.Company,"admin",Password),Sid);
            Check.Throws<UnauthorizedAccessException>(()=>f.Service.ChangePassword(new(f.Company,admin.Token,"wrong","Replacement-password-2026"),Sid));
            f.Service.ChangePassword(new(f.Company,admin.Token,Password,"Replacement-password-2026"),Sid);
            Check.Throws<UnauthorizedAccessException>(()=>f.Service.Authenticate(f.Company,admin.Token,Sid));
            Check.Throws<UnauthorizedAccessException>(()=>f.Service.Authenticate(f.Company,second.Token,Sid));
            Check.Throws<UnauthorizedAccessException>(()=>f.Service.Login(new(f.Company,"admin",Password),Sid));
            Check.Equal("admin",f.Service.Login(new(f.Company,"admin","Replacement-password-2026"),Sid).Role);
        });

        yield return Test("offline recovery rejects foreign Windows owners and invalidates old sessions",f=>
        {
            var admin=f.Admin();f.Service.CreateUser(new(f.Company,admin.Token,"reader",Password,"user"),Sid);
            Check.Throws<UnauthorizedAccessException>(()=>f.Service.RecoverLocalAdmin(new(f.Company,"reader","Replacement-password-2026"),"foreign-owner"));
            f.Service.RequireRecoveryOwner(f.Company,Sid);
            f.Service.RecoverLocalAdmin(new(f.Company,"reader","Replacement-password-2026"),Sid);
            Check.Throws<UnauthorizedAccessException>(()=>f.Save(admin.Token));
            Check.Throws<UnauthorizedAccessException>(()=>f.Service.Login(new(f.Company,"reader",Password),Sid));
            var recovered=f.Service.Login(new(f.Company,"reader","Replacement-password-2026"),Sid);
            Check.Equal("admin",recovered.Role);f.Save(recovered.Token);
            Check.True(f.Credentials.Execute(f.Company,tx=>string.Join("",tx.List("accounts","audit/"))).Contains("offline-admin-recovery"));
        });
        yield return Test("legacy admin recovery requires authenticated owner binding",f=>
        {
            f.Admin();f.Credentials.Execute(f.Company,tx=>{tx.Delete("accounts","owner");return 0;});
            Check.Throws<UnauthorizedAccessException>(()=>f.Service.RequireRecoveryOwner(f.Company,Sid));
            Check.Throws<UnauthorizedAccessException>(()=>f.Service.RecoverLocalAdmin(new(f.Company,"admin","Replacement-password-2026"),Sid));
            Check.Throws<ArgumentException>(()=>f.Service.Login(new(f.Company,"admin",Password),""));
            f.Service.Login(new(f.Company,"admin",Password),Sid);f.Service.RequireRecoveryOwner(f.Company,Sid);
            Check.Throws<UnauthorizedAccessException>(()=>f.Service.RequireRecoveryOwner(f.Company,"foreign-owner"));
        });
        yield return Test("first company admin is explicit and passwords are never stored as plaintext", f=>
        {
            Check.True(!f.Service.Status(f.Company).Configured);
            Check.Throws<ArgumentException>(()=>f.Service.Bootstrap(new(f.Company,"admin","short"),Sid));
            var admin=f.Admin();Check.Equal("admin",admin.Role);Check.Equal(64,admin.Token.Length);
            Check.Throws<UnauthorizedAccessException>(()=>f.Admin());
            var stored=f.Credentials.Execute(f.Company,tx=>string.Join("",tx.List("accounts","user/")));
            Check.True(!stored.Contains(Password,StringComparison.Ordinal));Check.True(stored.Contains("salt"));
        });
        yield return Test("normal users cannot save restore or create administrators",f=>
        {
            var admin=f.Admin();f.Service.CreateUser(new(f.Company,admin.Token,"reader",Password,"user"),Sid);
            var user=f.Service.Login(new(f.Company,"reader",Password),Sid);
            Check.Throws<UnauthorizedAccessException>(()=>f.Save(user.Token));
            Check.Throws<UnauthorizedAccessException>(()=>f.Service.Restore(new(f.Company,"catalog.products",user.Token,0,-1),Page,Sid));
            Check.Throws<UnauthorizedAccessException>(()=>f.Service.CreateUser(new(f.Company,user.Token,"attacker",Password,"admin"),Sid));
            Check.Throws<UnauthorizedAccessException>(()=>f.Save("forged-token"));
        });
        yield return Test("admin sessions cannot cross companies Windows callers expiry logout or restart",f=>
        {
            var admin=f.Admin();
            Check.Throws<UnauthorizedAccessException>(()=>f.Service.Save(new(Guid.NewGuid(),"catalog.products",admin.Token,0,UiAdministrationService.Defaults(Page)),Page,Sid));
            Check.Throws<UnauthorizedAccessException>(()=>f.Service.Save(new(f.Company,"catalog.products",admin.Token,0,UiAdministrationService.Defaults(Page)),Page,"other-owner"));
            var restarted=new UiAdministrationService(f.Data,f.Credentials,f.Clock);
            Check.Throws<UnauthorizedAccessException>(()=>restarted.Save(new(f.Company,"catalog.products",admin.Token,0,UiAdministrationService.Defaults(Page)),Page,Sid));
            f.Service.Logout(f.Company,admin.Token,Sid);Check.Throws<UnauthorizedAccessException>(()=>f.Save(admin.Token));
            admin=f.Service.Login(new(f.Company,"admin",Password),Sid);f.Clock.Now=f.Clock.Now.AddMinutes(31);Check.Throws<UnauthorizedAccessException>(()=>f.Save(admin.Token));
        });
        yield return Test("layout save is atomic versioned audited and preserves previous versions",f=>
        {
            var admin=f.Admin();var fields=UiAdministrationService.Defaults(Page).Reverse().ToArray();fields[0]=fields[0] with{Label="Belső megjegyzés",Span=2};
            var saved=f.Service.Save(new(f.Company,"catalog.products",admin.Token,0,fields),Page,Sid);
            Check.Equal(1L,saved.Revision);Check.Equal("notes",saved.Fields[0].Id);
            Check.Throws<InvalidOperationException>(()=>f.Save(admin.Token));
            Check.Equal(1L,f.Service.Read(f.Company,Page).Revision);
            var restored=f.Service.Restore(new(f.Company,"catalog.products",admin.Token,1,0),Page,Sid);
            Check.Equal(2L,restored.Revision);Check.Equal("name",restored.Fields[0].Id);
            Check.Equal(2,f.Data.Execute(f.Company,tx=>tx.List("ui-layout","audit/").Count));
            Check.Equal("admin",restored.History.Last().Actor);
        });
        yield return Test("layout rejects hidden required fields injection duplicates and invalid widths",f=>
        {
            var admin=f.Admin();var fields=UiAdministrationService.Defaults(Page);
            var bad=fields.ToArray();bad[0]=bad[0] with{Visible=false};Reject(bad);
            bad=fields.ToArray();bad[0]=bad[0] with{Id="unknown"};Reject(bad);
            bad=fields.ToArray();bad[1]=bad[0];Reject(bad);
            bad=fields.ToArray();bad[0]=bad[0] with{Span=3};Reject(bad);
            bad=fields.ToArray();bad[0]=bad[0] with{Label="bad\nlabel"};Reject(bad);
            Check.Equal(0L,f.Service.Read(f.Company,Page).Revision);
            void Reject(UiFieldLayout[] value)=>Check.Throws<ArgumentException>(()=>f.Service.Save(new(f.Company,"catalog.products",admin.Token,0,value),Page,Sid));
        });
        yield return Test("login rate limit expires and successful login clears failures",f=>
        {
            f.Admin();for(var i=0;i<5;i++)Check.Throws<UnauthorizedAccessException>(()=>f.Service.Login(new(f.Company,"admin","wrong"),Sid));
            Check.Throws<UnauthorizedAccessException>(()=>f.Service.Login(new(f.Company,"admin",Password),Sid));
            f.Clock.Now=f.Clock.Now.AddMinutes(2);Check.Equal("admin",f.Service.Login(new(f.Company,"admin",Password),Sid).Role);
        });
        yield return Test("future layout schemas show defaults and cannot be overwritten",f=>
        {
            var admin=f.Admin();var future=new UiLayoutState(5,UiAdministrationService.Defaults(Page),[]){SchemaVersion=2};
            f.Data.Execute(f.Company,tx=>{tx.Write("ui-layout","catalog.products",future);return 0;});
            Check.Equal(2,f.Service.Read(f.Company,Page).SchemaVersion);
            Check.Throws<InvalidOperationException>(()=>f.Service.Save(new(f.Company,"catalog.products",admin.Token,5,future.Fields),Page,Sid));
            Check.Equal(5L,f.Service.Read(f.Company,Page).Revision);
        });
        yield return ("company backups carry layouts but do not carry local admin credentials",()=>
        {
            using var dir=new TestDirectory();var companies=new PcImpressz.Core.Companies.CompanyStore(dir.Path);var company=companies.Create("Próba",true);
            var data=new CompanyDatabase(companies.DataDirectory);var credentials=new CompanyDatabase(id=>Path.Combine(dir.Path,"security",id.ToString("N")));
            var service=new UiAdministrationService(data,credentials,new TestTime());var admin=service.Bootstrap(new(company.Id,"admin",Password),Sid);
            var fields=UiAdministrationService.Defaults(Page);fields[0]=fields[0] with{Label="Saját megnevezés"};
            service.Save(new(company.Id,"catalog.products",admin.Token,0,fields),Page,Sid);
            var backups=new CompanyBackupService(companies,data);var file=Path.Combine(dir.Path,"backup.pcibak");backups.Create(company.Id,file);
            var restored=backups.Restore(file,"Visszaállított",true);
            Check.Equal("Saját megnevezés",service.Read(restored.Id,Page).Fields[0].Label);
            Check.True(!service.Status(restored.Id).Configured);
            Check.Equal(0,data.Execute(restored.Id,tx=>tx.List("accounts","user/").Count));
            return Task.CompletedTask;
        });
        yield return Test("layout state is company-isolated and incompatible fields fall back to defaults",f=>
        {
            var admin=f.Admin();f.Save(admin.Token);
            Check.Equal(0L,f.Service.Read(Guid.NewGuid(),Page).Revision);
            var newer=Page with{Fields=[..Page.Fields,new("extra","Új mező")]};
            Check.Equal(4,f.Service.Read(f.Company,newer).Fields.Length);
        });
    }
    private static (string,Func<Task>) Test(string name,Action<Fixture> action)=>(name,()=>{using var fixture=new Fixture();action(fixture);return Task.CompletedTask;});
    private sealed class Fixture:IDisposable
    {
        private readonly TestDirectory _directory=new();
        public readonly TestTime Clock=new();public readonly Guid Company=Guid.NewGuid();
        public CompanyDatabase Data{get;}public CompanyDatabase Credentials{get;}public UiAdministrationService Service{get;}
        public Fixture(){Data=new(id=>Path.Combine(_directory.Path,"data",id.ToString("N")));Credentials=new(id=>Path.Combine(_directory.Path,"credentials",id.ToString("N")));Service=new(Data,Credentials,Clock);}
        public UiSession Admin()=>Service.Bootstrap(new(Company,"admin",Password),Sid);
        public UiLayoutState Save(string token)=>Service.Save(new(Company,"catalog.products",token,0,UiAdministrationService.Defaults(Page)),Page,Sid);
        public void Dispose()=>_directory.Dispose();
    }
}
