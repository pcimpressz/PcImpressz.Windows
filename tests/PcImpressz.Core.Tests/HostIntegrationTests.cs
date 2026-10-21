using PcImpressz.Core.Storage;
using PcImpressz.Core.Companies;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using PcImpressz.Contracts;
using PcImpressz.Core.Modules;

namespace PcImpressz.Core.Tests;

internal static class HostIntegrationTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    { yield return ("real signed partner module: IPC, company isolation, caller identity and license enforcement", Run); }
    private static async Task Run()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("SKIP host integration: a névcső és a .tools dotnet.exe Windowsos.");
            return;
        }
        var sessionTokens=new Dictionary<Guid,string>();Guid? activeCompany=null;
        using var dir=new TestDirectory();using var crypto=new CryptoFixture();
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        var install=Guid.NewGuid().ToString("D");
        var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        crypto.Lease=crypto.Lease with{InstallationId=install,Iat=now-30,Nbf=now-30,Exp=now+3600};
        crypto.License=crypto.License with{Iat=now-60,Nbf=now-60,Exp=now+30*86400};
        crypto.License=crypto.License with{Modules=[
            new(){ModuleId="partners",Capabilities=["partners.write","partners.import"],ExpiresAt=crypto.License.Exp},
            new(){ModuleId="catalog",Capabilities=["catalog.write","catalog.import"],ExpiresAt=crypto.License.Exp},
            new(){ModuleId="inventory",Capabilities=["inventory.write"],ExpiresAt=crypto.License.Exp},
            new(){ModuleId="sales",Capabilities=["sales.write"],ExpiresAt=crypto.License.Exp},
            new(){ModuleId="receiving",Capabilities=["receiving.write"],ExpiresAt=crypto.License.Exp}]};crypto.Refresh();
        File.WriteAllText(Path.Combine(dir.Path,"installation-id.txt"),install);
        File.WriteAllText(Path.Combine(dir.Path,"settings.json"),JsonSerializer.Serialize(new{Product=new{Issuer="test-issuer",OrganizationId="company"}}));
        var keys=JsonSerializer.Serialize(new Dictionary<string,string>{["test-key"]=crypto.Key.ExportSubjectPublicKeyInfoPem()});
        File.WriteAllText(Path.Combine(dir.Path,"license-public-keys.json"),keys);File.WriteAllText(Path.Combine(dir.Path,"module-public-keys.json"),keys);
        File.WriteAllText(Path.Combine(dir.Path,"license.jws"),crypto.Tokens!.Entitlement);File.WriteAllText(Path.Combine(dir.Path,"lease.jws"),crypto.Tokens.Lease);
        var folder=Path.Combine(dir.Path,"modules","partners");Directory.CreateDirectory(folder);
        var bytes=File.ReadAllBytes(Path.Combine(root,"src","Modules","PcImpressz.Modules.Partners","bin","Release","net10.0","PcImpressz.Modules.Partners.dll"));
        File.WriteAllBytes(Path.Combine(folder,"PcImpressz.Modules.Partners.dll"),bytes);
        var manifest=new ModuleManifest{SchemaVersion=1,Id="partners",Version="0.3.0",ApiVersion=PlatformContract.ApiVersion,
            EntryAssembly="PcImpressz.Modules.Partners.dll",EntryType="PcImpressz.Modules.Partners.PartnersModule",
            Files=new(){["PcImpressz.Modules.Partners.dll"]=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))},Dependencies=[]};
        File.WriteAllText(Path.Combine(folder,"manifest.jws"),crypto.Sign(manifest,ModuleManifest.DocumentType));
        var catalogFolder=Path.Combine(dir.Path,"modules","catalog");Directory.CreateDirectory(catalogFolder);
        var catalogBytes=File.ReadAllBytes(Path.Combine(root,"src","Modules","PcImpressz.Modules.Catalog","bin","Release","net10.0","PcImpressz.Modules.Catalog.dll"));
        File.WriteAllBytes(Path.Combine(catalogFolder,"PcImpressz.Modules.Catalog.dll"),catalogBytes);
        var catalogManifest=new ModuleManifest{SchemaVersion=1,Id="catalog",Version="0.3.0",ApiVersion=PlatformContract.ApiVersion,
            EntryAssembly="PcImpressz.Modules.Catalog.dll",EntryType="PcImpressz.Modules.Catalog.CatalogModule",
            Files=new(){["PcImpressz.Modules.Catalog.dll"]=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(catalogBytes))},Dependencies=[]};
        File.WriteAllText(Path.Combine(catalogFolder,"manifest.jws"),crypto.Sign(catalogManifest,ModuleManifest.DocumentType));
        InstallBusinessModule("inventory","PcImpressz.Modules.Inventory.InventoryModule",["catalog"]);
        InstallBusinessModule("sales","PcImpressz.Modules.Sales.SalesModule",["inventory","partners"]);
        InstallBusinessModule("receiving","PcImpressz.Modules.Receiving.ReceivingModule",["inventory","partners","catalog"]);
        var hostDll=Environment.GetEnvironmentVariable("PCIMPRESSZ_TEST_HOST");
        if(string.IsNullOrWhiteSpace(hostDll))
            hostDll=Path.Combine(root,"src","PcImpressz.ApplicationHost","bin","Release","net10.0","PcImpressz.ApplicationHost.dll");
        var start=new ProcessStartInfo((Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? Environment.ProcessPath ?? throw new IOException("A dotnet teszt-host nem található."))){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(hostDll);
        start.ArgumentList.Add("--data-dir");start.ArgumentList.Add(dir.Path);
        using var host=Process.Start(start)??throw new IOException("Test Host did not start.");
        host.OutputDataReceived+=(_,_)=>{};host.ErrorDataReceived+=(_,_)=>{};host.BeginOutputReadLine();host.BeginErrorReadLine();
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            LocalResponse? first=null;
            while(first is null)
            {
                try{first=await Send("companies.list",new{});}
                catch(Exception ex) when(ex is TimeoutException or OperationCanceledException or IOException)
                {deadline.Token.ThrowIfCancellationRequested();if(host.HasExited)throw new IOException("Test Host exited.");await Task.Delay(100,deadline.Token);}
            }
            while(true)
            {
                await using var statusPipe=new NamedPipeClientStream(".",DiagnosticChannel.ForDirectory(dir.Path),
                    PipeDirection.In,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await statusPipe.ConnectAsync(deadline.Token);
                var status=await JsonSerializer.DeserializeAsync<HostDiagnostic>(statusPipe,cancellationToken:deadline.Token);
                if(status?.State=="Ready")
                {
                    Check.Equal(1,status.CompanyAuthenticationVersion);
                    Check.True(status.Modules.All(m=>m.State=="Active"),string.Join("; ",status.Modules.Select(m=>m.Reason)));
                    break;
                }
                await Task.Delay(100,deadline.Token);
            }
            Check.True(first.Success);Check.Equal(1,first.Data!.Value.GetArrayLength());
            var a=(await Send("companies.create",new CreateCompanyRequest("Teszt cég A",false))).Data!.Value.GetProperty("Id").GetGuid();
            var b=(await Send("companies.create",new CreateCompanyRequest("Teszt cég B",true))).Data!.Value.GetProperty("Id").GetGuid();
            var adminStatus=await Send("ui.auth.status",new{CompanyId=a});
            Check.True(!adminStatus.Data!.Value.Deserialize<UiAuthStatus>()!.Configured);
            var adminSession=(await Send("ui.auth.bootstrap",new UiLogin(a,"admin","Test-only-password-2026"))).Data!.Value.Deserialize<UiSession>()!;
            await Send("ui.auth.bootstrap",new UiLogin(b,"admin","Test-only-password-2026"));activeCompany=a;
            Check.True(!(await Send("ui.auth.bootstrap",new UiLogin(a,"other","Test-only-password-2026"))).Success);
            Check.True((await Send("ui.users.create",new UiCreateUser(a,adminSession.Token,"reader","Test-only-password-2026","user"))).Success);
            var readerSession=(await Send("ui.auth.login",new UiLogin(a,"reader","Test-only-password-2026"))).Data!.Value.Deserialize<UiSession>()!;
            var layout=(await Send("ui.layout.read",new UiPageRequest(a,"partners.partners"))).Data!.Value.Deserialize<UiLayoutState>()!;
            var deniedLayout=await Send("ui.layout.save",new{CompanyId=a,PageKey="partners.partners",Token=readerSession.Token,Revision=0,layout.Fields,Role="admin",Actor="admin"});
            Check.True(!deniedLayout.Success);Check.Equal("denied",deniedLayout.Code);
            Check.True(!(await Send("ui.layout.save",new UiLayoutSave(b,"partners.partners",adminSession.Token,0,layout.Fields))).Success);
            var adminSaved=await Send("ui.layout.save",new UiLayoutSave(a,"partners.partners",adminSession.Token,0,layout.Fields));
            Check.True(adminSaved.Success,adminSaved.Error??"Admin layout save failed");
            Check.Equal(1L,adminSaved.Data!.Value.Deserialize<UiLayoutState>()!.Revision);
            Check.True(!(await Send("ui.layout.save",new UiLayoutSave(a,"partners.partners",adminSession.Token,0,layout.Fields))).Success);
            Check.True((await Send("ui.layout.restore",new UiLayoutRestore(a,"partners.partners",adminSession.Token,1,-1))).Success);

            Check.True(!(await Send("module.execute",new ModuleCommandRequest(a,"partners","list",JsonSerializer.SerializeToElement(new{})),anonymous:true)).Success);
            Check.True((await Send("ui.users.create",new UiCreateUser(a,adminSession.Token,"seller","Test-only-password-2026","sales"))).Success);
            Check.True((await Send("ui.users.create",new UiCreateUser(a,adminSession.Token,"warehouse","Test-only-password-2026","warehouse"))).Success);
            var seller=(await Send("ui.auth.login",new UiLogin(a,"seller","Test-only-password-2026"))).Data!.Value.Deserialize<UiSession>()!;
            var warehouse=(await Send("ui.auth.login",new UiLogin(a,"warehouse","Test-only-password-2026"))).Data!.Value.Deserialize<UiSession>()!;
            foreach(var deniedRequest in new[]{new ModuleCommandRequest(a,"receiving","list",JsonSerializer.SerializeToElement(new{})),new ModuleCommandRequest(a,"inventory","save",JsonSerializer.SerializeToElement(new{role="admin",permissions=new[]{"inventory.write"}})),new ModuleCommandRequest(a,"partners","dossier",JsonSerializer.SerializeToElement(new{})),new ModuleCommandRequest(b,"sales","list",JsonSerializer.SerializeToElement(new{}))})
            {var response=await Send("module.execute",deniedRequest,seller.Token);Check.True(!response.Success);Check.Equal("denied",response.Code);}
            Check.True(!(await Send("module.execute",new ModuleCommandRequest(a,"sales","list",JsonSerializer.SerializeToElement(new{})),warehouse.Token)).Success);
            Check.True(!(await Send("module.execute",new ModuleCommandRequest(a,"receiving","nav-save",JsonSerializer.SerializeToElement(new{})),warehouse.Token)).Success);
            Check.True(!(await Send("companies.backup",new CompanyBackupRequest(a,Path.Combine(dir.Path,"denied.pcibak")),seller.Token)).Success);
            Check.True(!(await Send("ui.users.list",new{CompanyId=a},seller.Token)).Success);
            foreach(var (operation,input) in new (string,object)[]{("companies.create",new CreateCompanyRequest("Tiltott cég",false)),("companies.restore",new CompanyRestoreRequest("nincs.pcibak","Tiltott",false)),("companies.clear-demo",new{}),("modules.set-enabled",new SetModuleEnabledRequest("sales",false))})
            {var response=await Send(operation,input,seller.Token);Check.True(!response.Success);Check.Equal("denied",response.Code);}
            Check.True((await Send("module.execute",new ModuleCommandRequest(a,"sales","list",JsonSerializer.SerializeToElement(new{})),seller.Token)).Success);
            Check.True((await Send("module.execute",new ModuleCommandRequest(a,"receiving","list",JsonSerializer.SerializeToElement(new{})),warehouse.Token)).Success);
            Check.True((await Send("ui.users.update",new UiUpdateUser(a,adminSession.Token,"warehouse","warehouse",true))).Success);
            Check.True(!(await Send("module.execute",new ModuleCommandRequest(a,"receiving","list",JsonSerializer.SerializeToElement(new{})),warehouse.Token)).Success);
            Check.True((await Send("ui.users.update",new UiUpdateUser(a,adminSession.Token,"warehouse","warehouse",false))).Success);
            Check.True(!(await Send("module.execute",new ModuleCommandRequest(a,"receiving","list",JsonSerializer.SerializeToElement(new{})),warehouse.Token)).Success);
            Check.Equal(0,(await Module(a,"list",new{})).Data!.Value.GetProperty("total").GetInt32());
            var saved=await Module(a,"save",new Dictionary<string,object?>{["CompanyId"]=b,["companyId"]=b,["id"]=null,["version"]=0,["data"]=new{name="IPC partner",isCustomer=true,isSupplier=true,isActive=true},["actor"]="fake-user"});
            Check.True(saved.Success,saved.Error??"Save failed");
            Check.Equal(1,(await Module(a,"list",new{})).Data!.Value.GetProperty("total").GetInt32());
            Check.Equal(0,(await Module(b,"list",new{})).Data!.Value.GetProperty("total").GetInt32());
            using(var state=JsonDocument.Parse(new CompanyDatabase(new CompanyStore(dir.Path).DataDirectory).Execute(a,tx=>tx.Get("partners","state")!)))
                Check.True(state.RootElement.GetProperty("partners")[0].GetProperty("createdBy").GetString()!.StartsWith("S-1-",StringComparison.Ordinal));
            var unit=await Catalog(a,"units-save",new{id=(Guid?)null,version=0,data=new{name="Darab",code="DB",symbol="db",isActive=true}});
            Check.True(unit.Success,unit.Error??"Unit save failed");
            var product=await Catalog(a,"products-save",new{id=(Guid?)null,version=0,data=new{name="Csavar",sku="CSV-1",unitCode="DB",isActive=true,isSalable=true,isPurchaseable=true}});
            Check.True(product.Success,product.Error??"Product save failed");
            Check.Equal(1,(await Catalog(a,"products-list",new{})).Data!.Value.GetProperty("total").GetInt32());
            Check.Equal(0,(await Catalog(b,"products-list",new{})).Data!.Value.GetProperty("total").GetInt32());
            var productId=product.Data!.Value.GetProperty("id").GetGuid();
            var partnerId=saved.Data!.Value.GetProperty("id").GetGuid();
            var opening=await Business(a,"inventory","save",new{operationId=Guid.NewGuid(),productId,warehouse="Bolt",quantity=10m,reason="Nyitókészlet"});
            Check.True(opening.Success,opening.Error??"Opening failed");
            var saleId=Guid.NewGuid();
            var saleRequest=new{operationId=saleId,customerId=partnerId,warehouse="Bolt",lines=new[]{new{productId,quantity=2m,netUnitPrice=100m,vatRate=5m}}};
            var sale=await Business(a,"sales","save",saleRequest);Check.True(sale.Success,sale.Error??"Sale failed");
            Check.Equal(210m,sale.Data!.Value.GetProperty("gross").GetDecimal());
            Check.True((await Business(a,"sales","save",saleRequest)).Success);
            var saleList=(await Business(a,"sales","list",new{})).Data!.Value.GetProperty("rows");
            Check.True(!saleList[0].TryGetProperty("lines",out _));
            var saleDetails=await Business(a,"sales","get",new{id=saleId});Check.True(saleDetails.Success);Check.Equal(1,saleDetails.Data!.Value.GetProperty("lines").GetArrayLength());
            Check.Equal(0,(await Business(a,"inventory","list",new{search="nincsilyen"})).Data!.Value.GetProperty("rows").GetArrayLength());
            Check.Equal(8m,(await Business(a,"inventory","list",new{})).Data!.Value.GetProperty("rows")[0].GetProperty("quantity").GetDecimal());
            Check.True((await Business(a,"sales","reverse",new{id=saleId,version=1})).Success);
            var receipt=await Business(a,"receiving","save",new{id=(Guid?)null,version=0,draft=new{number="IPC-RECEIPT",supplierId=partnerId,issuedOn="2026-10-20",warehouse="Bolt",lines=new[]{new{productId,name="Csavar",unitCode="DB",quantity=3m,netUnitPrice=100m,vatRate=27m}}}});
            Check.True(receipt.Success,receipt.Error??"Receipt failed");
            Check.True((await Business(a,"receiving","classify",new{id=receipt.Data!.Value.GetProperty("id").GetGuid(),version=1,status="bevetelezve"})).Success);
            Check.Equal(13m,(await Business(a,"inventory","list",new{})).Data!.Value.GetProperty("rows")[0].GetProperty("quantity").GetDecimal());
            Check.Equal(13m,(await Catalog(a,"products-list",new{})).Data!.Value.GetProperty("rows")[0].GetProperty("quantity").GetDecimal());
            var receiptId=receipt.Data!.Value.GetProperty("id").GetGuid();
            var receiptReversed=await Business(a,"receiving","reverse",new{id=receiptId,version=2,reason="IPC visszafordítás"});Check.True(receiptReversed.Success,receiptReversed.Error??"Reverse failed");
            Check.True((await Business(a,"receiving","reverse",new{id=receiptId,version=2,reason="IPC visszafordítás"})).Success);
            Check.Equal(10m,(await Business(a,"inventory","list",new{})).Data!.Value.GetProperty("rows")[0].GetProperty("quantity").GetDecimal());
            Check.True((await Send("modules.set-enabled",new SetModuleEnabledRequest("inventory",false))).Success);
            var blockedSale=await Business(a,"sales","save",new{operationId=Guid.NewGuid(),customerId=partnerId,warehouse="Bolt",lines=saleRequest.lines});
            Check.True(!blockedSale.Success);Check.True(blockedSale.Error!.Contains("ki van kapcsolva",StringComparison.Ordinal));
            Check.True((await Business(a,"sales","list",new{})).Success);
            Check.True((await Send("modules.set-enabled",new SetModuleEnabledRequest("inventory",true))).Success);
            var turnedOff=await Send("modules.set-enabled",new SetModuleEnabledRequest("partners",false));
            Check.True(turnedOff.Success,turnedOff.Error??"Disable failed");
            var blocked=await Module(a,"save",new{id=(Guid?)null,version=0,data=new{name="Kikapcsolt",isActive=true}});
            Check.True(!blocked.Success);Check.Equal("validation",blocked.Code);
            Check.True(blocked.Error!.Contains("ki van kapcsolva",StringComparison.Ordinal),blocked.Error);
            Check.True((await Catalog(a,"products-list",new{})).Success);
            var disabledStatus=await Status();
            Check.True(!disabledStatus.Modules.Single(m=>m.Id=="partners").Enabled);
            Check.True(disabledStatus.Modules.Single(m=>m.Id=="catalog").Enabled);
            var frame=await Send("modules.set-enabled",new SetModuleEnabledRequest("settings",false));
            Check.True(!frame.Success);Check.True(frame.Error!.Contains("mindig elérhető",StringComparison.Ordinal),frame.Error??"");
            Check.True((await Send("modules.set-enabled",new SetModuleEnabledRequest("partners",true))).Success);
            Check.True((await Module(a,"list",new{})).Success);
            var demo=first.Data!.Value[0].GetProperty("Id").GetGuid();
            Check.True(first.Data.Value[0].GetProperty("IsDemo").GetBoolean());
            await Send("ui.auth.bootstrap",new UiLogin(demo,"admin","Test-only-password-2026"));
            var demoPartner=await Module(demo,"save",new{id=(Guid?)null,version=0,data=new{name="Demó partner",isCustomer=true,isActive=true}});
            Check.True(demoPartner.Success,demoPartner.Error??"Demo save failed");
            var otherDemo=await Module(b,"save",new{id=(Guid?)null,version=0,data=new{name="Másik demó",isCustomer=true,isActive=true}});
            Check.True(otherDemo.Success,otherDemo.Error??"Other demo save failed");
            var demoFolder=Path.Combine(dir.Path,"companies",demo.ToString("N"));
            File.WriteAllText(Path.Combine(demoFolder,"jegyzet.txt"),"egyeb");
            File.WriteAllText(Path.Combine(dir.Path,"appearance.json"),"megjelenes");
            var registry=File.ReadAllText(Path.Combine(dir.Path,"companies.json"));
            var enablement=File.ReadAllText(Path.Combine(dir.Path,"module-enablement.json"));
            var cleared=await Send("companies.clear-demo",new{CompanyId=demo});
            Check.True(cleared.Success,cleared.Error??"Clear failed");
            Check.Equal(1,cleared.Data!.Value.GetProperty("DemoCompanies").GetInt32());
            Check.True(!Directory.Exists(demoFolder));
            Check.True(Directory.Exists(Path.Combine(dir.Path,"companies",b.ToString("N"))));
            Check.Equal(1,(await Module(a,"list",new{})).Data!.Value.GetProperty("total").GetInt32());
            Check.Equal(1,(await Catalog(a,"products-list",new{})).Data!.Value.GetProperty("total").GetInt32());
            Check.Equal(0,(await Module(demo,"list",new{})).Data!.Value.GetProperty("total").GetInt32());
            Check.Equal(1,(await Module(b,"list",new{})).Data!.Value.GetProperty("total").GetInt32());
            Check.Equal(3,(await Send("companies.list",new{})).Data!.Value.GetArrayLength());
            Check.Equal(registry,File.ReadAllText(Path.Combine(dir.Path,"companies.json")));
            Check.Equal("megjelenes",File.ReadAllText(Path.Combine(dir.Path,"appearance.json")));
            Check.Equal(enablement,File.ReadAllText(Path.Combine(dir.Path,"module-enablement.json")));
            Check.True(File.Exists(Path.Combine(dir.Path,"license.jws")));
            var refilled=await Module(demo,"save",new{id=(Guid?)null,version=0,data=new{name="Új demó partner",isCustomer=true,isActive=true}});
            Check.True(refilled.Success,refilled.Error??"Refill failed");
            Check.True(!(await Send("companies.seed-demo",new CompanyDemoSeedRequest(a))).Success);
            Check.True(!(await Send("companies.seed-demo",new CompanyDemoSeedRequest(b),anonymous:true)).Success);
            Check.True(!(await Send("module.execute",new ModuleCommandRequest(b,"receiving","seed-demo",JsonSerializer.SerializeToElement(new{companyId=b,isDemo=true})))).Success);
            var bAdmin=sessionTokens[b];
            Check.True((await Send("ui.users.create",new UiCreateUser(b,bAdmin,"seedworker","Test-only-password-2026","warehouse"))).Success);
            var seedWorker=(await Send("ui.auth.login",new UiLogin(b,"seedworker","Test-only-password-2026"))).Data!.Value.Deserialize<UiSession>()!;
            Check.True(!(await Send("companies.seed-demo",new CompanyDemoSeedRequest(b),seedWorker.Token)).Success);
            Check.True((await Send("modules.set-enabled",new SetModuleEnabledRequest("sales",false))).Success);
            Check.True(!(await Send("companies.seed-demo",new CompanyDemoSeedRequest(b))).Success);
            Check.True((await Send("modules.set-enabled",new SetModuleEnabledRequest("sales",true))).Success);
            var seeded=await Send("companies.seed-demo",new CompanyDemoSeedRequest(b));Check.True(seeded.Success,seeded.Error??"Demo seed failed");
            var seedReport=seeded.Data!.Value.GetProperty("report");Check.Equal(20,seedReport.GetProperty("products").GetInt32());Check.Equal(10,seedReport.GetProperty("partners").GetInt32());
            Check.True(!seedReport.GetProperty("alreadySeeded").GetBoolean());var seedBackup=seeded.Data.Value.GetProperty("backupFile").GetString()!;Check.True(File.Exists(seedBackup));
            Check.Equal(20,(await Catalog(b,"products-list",new{pageSize=50})).Data!.Value.GetProperty("total").GetInt32());
            Check.Equal(11,(await Module(b,"list",new{pageSize=50})).Data!.Value.GetProperty("total").GetInt32());
            Check.Equal(8,(await Business(b,"receiving","list",new{filter="mind"})).Data!.Value.GetProperty("total").GetInt32());
            Check.Equal(10,(await Business(b,"sales","list",new{})).Data!.Value.GetProperty("rows").GetArrayLength());
            var seedAgain=await Send("companies.seed-demo",new CompanyDemoSeedRequest(b));Check.True(seedAgain.Success);Check.True(seedAgain.Data!.Value.GetProperty("report").GetProperty("alreadySeeded").GetBoolean());
            Check.Equal(JsonValueKind.Null,seedAgain.Data.Value.GetProperty("backupFile").ValueKind);
            Check.Equal(20,(await Catalog(b,"products-list",new{})).Data!.Value.GetProperty("total").GetInt32());
            var seedRestore=await Send("companies.restore",new CompanyRestoreRequest(seedBackup,"Feltöltés előtti ellenőrzés",true));Check.True(seedRestore.Success);
            var seedRestoreId=seedRestore.Data!.Value.GetProperty("Id").GetGuid();await Send("ui.auth.bootstrap",new UiLogin(seedRestoreId,"admin","Test-only-password-2026"));
            Check.Equal(0,(await Catalog(seedRestoreId,"products-list",new{})).Data!.Value.GetProperty("total").GetInt32());
            Check.Equal(1,(await Module(seedRestoreId,"list",new{})).Data!.Value.GetProperty("total").GetInt32());

            var failedSeedCompany=(await Send("companies.create",new CreateCompanyRequest("Mentéshiba demó",true))).Data!.Value.GetProperty("Id").GetGuid();
            await Send("ui.auth.bootstrap",new UiLogin(failedSeedCompany,"admin","Test-only-password-2026"));
            var blockedBackupFolder=Path.Combine(dir.Path,"backups","demo-seed",failedSeedCompany.ToString("N"));Directory.CreateDirectory(Path.GetDirectoryName(blockedBackupFolder)!);File.WriteAllText(blockedBackupFolder,"Injected backup failure");
            Check.True(!(await Send("companies.seed-demo",new CompanyDemoSeedRequest(failedSeedCompany))).Success);
            Check.Equal(0,(await Catalog(failedSeedCompany,"products-list",new{})).Data!.Value.GetProperty("total").GetInt32());
            Check.Equal(0,(await Module(failedSeedCompany,"list",new{})).Data!.Value.GetProperty("total").GetInt32());
            var backupFile=Path.Combine(dir.Path,"ipc-company.pcibak");
            var backupResponse=await Send("companies.backup",new CompanyBackupRequest(a,backupFile));Check.True(backupResponse.Success,backupResponse.Error??"Backup failed");
            var restoreResponse=await Send("companies.restore",new CompanyRestoreRequest(backupFile,"IPC visszaállított cég",false));Check.True(restoreResponse.Success,restoreResponse.Error??"Restore failed");
            var restoredCompany=restoreResponse.Data!.Value.GetProperty("Id").GetGuid();
            Check.True(!(await Business(restoredCompany,"inventory","list",new{})).Success);
            await Send("ui.auth.bootstrap",new UiLogin(restoredCompany,"admin","Test-only-password-2026"));
            Check.Equal(10m,(await Business(restoredCompany,"inventory","list",new{})).Data!.Value.GetProperty("rows")[0].GetProperty("quantity").GetDecimal());
            if(Environment.GetEnvironmentVariable("PCIMPRESSZ_REHEARSAL_BACKUP") is {Length:>0} rehearsalBackup)
            {
                var sourceHash=System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(rehearsalBackup));
                var copyResponse=await Send("companies.restore",new CompanyRestoreRequest(rehearsalBackup,"Másolati üzleti próba",true));Check.True(copyResponse.Success,copyResponse.Error??"Copy restore failed");
                var copy=copyResponse.Data!.Value.GetProperty("Id").GetGuid();
                var copyAdmin=(await Send("ui.auth.bootstrap",new UiLogin(copy,"admin","Test-only-password-2026"))).Data!.Value.Deserialize<UiSession>()!;
                await Send("ui.users.create",new UiCreateUser(copy,copyAdmin.Token,"warehouse","Test-only-password-2026","warehouse"));
                await Send("ui.users.create",new UiCreateUser(copy,copyAdmin.Token,"seller","Test-only-password-2026","sales"));
                var copyWarehouse=(await Send("ui.auth.login",new UiLogin(copy,"warehouse","Test-only-password-2026"))).Data!.Value.Deserialize<UiSession>()!;
                var copySeller=(await Send("ui.auth.login",new UiLogin(copy,"seller","Test-only-password-2026"))).Data!.Value.Deserialize<UiSession>()!;
                var copiedDatabase=new CompanyDatabase(new CompanyStore(dir.Path).DataDirectory);
                var masters=copiedDatabase.Execute(copy,tx=>(Products:CompanyReferenceQueries.Products(tx),Partners:CompanyReferenceQueries.Partners(tx)));
                Console.WriteLine($"Existing backup master data: {masters.Products.Length} products, {masters.Partners.Length} partners.");
                var existingProduct=masters.Products.FirstOrDefault(p=>p.IsActive&&p.IsSalable&&p.IsPurchaseable);
                var existingSupplier=masters.Partners.FirstOrDefault(p=>p.IsActive&&p.IsSupplier);
                if(existingProduct is null)
                {
                    var testUnit="PR"+Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
                    var unitResponse=await Send("module.execute",new ModuleCommandRequest(copy,"catalog","units-save",JsonSerializer.SerializeToElement(new{id=(Guid?)null,version=0,data=new{name="Másolati próbadarab",code=testUnit,symbol="db",isActive=true}})));Check.True(unitResponse.Success,unitResponse.Error??"Unit failed");
                    var productResponse=await Send("module.execute",new ModuleCommandRequest(copy,"catalog","products-save",JsonSerializer.SerializeToElement(new{id=(Guid?)null,version=0,data=new{name="Csak másolati próba",sku="PROBA-"+Guid.NewGuid().ToString("N"),unitCode=testUnit,isActive=true,isSalable=true,isPurchaseable=true}})));Check.True(productResponse.Success,productResponse.Error??"Product failed");
                    existingProduct=copiedDatabase.Execute(copy,tx=>CompanyReferenceQueries.Products(tx).Single(p=>p.Id==productResponse.Data!.Value.GetProperty("id").GetGuid()));
                    Console.WriteLine("No eligible source product; created a synthetic test product exclusively in the restored copy.");
                }
                if(existingSupplier is null)
                {
                    var partnerResponse=await Send("module.execute",new ModuleCommandRequest(copy,"partners","save",JsonSerializer.SerializeToElement(new{id=(Guid?)null,version=0,data=new{name="Csak másolati próbaszállító",isSupplier=true,isCustomer=true,isActive=true}})));Check.True(partnerResponse.Success,partnerResponse.Error??"Partner failed");
                    existingSupplier=copiedDatabase.Execute(copy,tx=>CompanyReferenceQueries.Partners(tx).Single(p=>p.Id==partnerResponse.Data!.Value.GetProperty("id").GetGuid()));
                    Console.WriteLine("No active source supplier; created a synthetic supplier exclusively in the restored copy.");
                }
                const string rehearsalWarehouse="Jogosultsági próba";
                Task<LocalResponse> CopyCommand(string module,string command,object input,string token)=>Send("module.execute",new ModuleCommandRequest(copy,module,command,JsonSerializer.SerializeToElement(input)),token);
                var copyReceipt=await CopyCommand("receiving","save",new{id=(Guid?)null,version=0,draft=new{number="MASOLATI-PROBA-"+Guid.NewGuid().ToString("N"),supplierId=existingSupplier.Id,issuedOn="2026-10-21",warehouse=rehearsalWarehouse,lines=new[]{new{productId=existingProduct.Id,name=existingProduct.Name,sku=existingProduct.Sku,unitCode=existingProduct.UnitCode,quantity=3m,netUnitPrice=100m,vatRate=27m}}}},copyWarehouse.Token);
                Check.True(copyReceipt.Success,copyReceipt.Error??"Copied receipt failed");var copyReceiptId=copyReceipt.Data!.Value.GetProperty("id").GetGuid();
                Check.True((await CopyCommand("receiving","classify",new{id=copyReceiptId,version=1,status="bevetelezve"},copyWarehouse.Token)).Success);
                var copySaleId=Guid.NewGuid();
                var copySale=await CopyCommand("sales","save",new{operationId=copySaleId,warehouse=rehearsalWarehouse,lines=new[]{new{productId=existingProduct.Id,quantity=2m,netUnitPrice=150m,vatRate=27m}}},copySeller.Token);
                Check.True(copySale.Success,copySale.Error??"Copied sale failed");
                Check.True(!(await CopyCommand("receiving","reverse",new{id=copyReceiptId,version=2,reason="Másolati készletvédelem"},copyWarehouse.Token)).Success);
                Check.True((await CopyCommand("sales","reverse",new{id=copySaleId,version=1},copySeller.Token)).Success);
                Check.True((await CopyCommand("receiving","reverse",new{id=copyReceiptId,version=2,reason="Másolati próba lezárása"},copyWarehouse.Token)).Success);
                var copyBalance=await CopyCommand("inventory","list",new{search=rehearsalWarehouse},copySeller.Token);Check.True(copyBalance.Success);
                Check.True(copyBalance.Data!.Value.GetProperty("rows").EnumerateArray().All(row=>row.GetProperty("quantity").GetDecimal()==0));
                var copiedDocuments=copiedDatabase.Execute(copy,tx=>string.Join("",tx.List("sales","sale/")));
                Check.True(copiedDocuments.Contains("/seller",StringComparison.Ordinal));
                Check.True(sourceHash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(rehearsalBackup))));
                Console.WriteLine($"PASS existing company backup rehearsal: {masters.Products.Length} products, {masters.Partners.Length} partners; warehouse receipt, seller sale, stock protection and both reversals; source unchanged.");
            }

            File.Delete(Path.Combine(dir.Path,"license.jws"));
            Check.True(!(await Send("companies.seed-demo",new CompanyDemoSeedRequest(b))).Success);
            var denied=await Module(a,"save",new{id=(Guid?)null,version=0,data=new{name="Nem menthető",isActive=true}});
            Check.True(!denied.Success);Check.Equal("denied",denied.Code);Check.True((await Module(a,"export",new{})).Success);
            var deniedStock=await Business(a,"inventory","save",new{operationId=Guid.NewGuid(),productId,warehouse="Bolt",quantity=1m,reason="Tiltott próba"});
            Check.True(!deniedStock.Success);Check.Equal("denied",deniedStock.Code);Check.True((await Business(a,"inventory","export",new{})).Success);
            var deniedSale=await Business(a,"sales","save",saleRequest);Check.True(!deniedSale.Success);Check.Equal("denied",deniedSale.Code);
            var deniedProduct=await Catalog(a,"products-save",new{id=(Guid?)null,version=0,data=new{name="Nem menthető",sku="NEM",unitCode="DB",isActive=true}});
            Check.True(!deniedProduct.Success);Check.Equal("denied",deniedProduct.Code);Check.True((await Catalog(a,"products-export",new{})).Success);
        }
        finally{if(!host.HasExited){host.Kill(entireProcessTree:true);await host.WaitForExitAsync();}}

        void InstallBusinessModule(string id,string entry,string[] dependencies)
        {
            var assembly="PcImpressz.Modules."+char.ToUpperInvariant(id[0])+id[1..];
            var package=Path.Combine(dir.Path,"modules",id);Directory.CreateDirectory(package);
            var files=new Dictionary<string,string>();
            foreach(var name in (id=="receiving"?new[]{assembly+".dll","PcImpressz.Business.dll","PcImpressz.Modules.Partners.dll","PcImpressz.Modules.Catalog.dll","PcImpressz.Modules.Sales.dll"}:new[]{assembly+".dll","PcImpressz.Business.dll"}))
            {
                var bytes=File.ReadAllBytes(Path.Combine(root,"src","Modules",assembly,"bin","Release","net10.0",name));
                File.WriteAllBytes(Path.Combine(package,name),bytes);files[name]=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            }
            var manifest=new ModuleManifest{SchemaVersion=1,Id=id,Version="0.4.0",ApiVersion=PlatformContract.ApiVersion,EntryAssembly=assembly+".dll",EntryType=entry,Files=files,
                Dependencies=dependencies.Select(d=>new ModuleDependency{ModuleId=d,MinimumVersion="0.3.0",MaximumVersionExclusive="1.0.0"}).ToArray()};
            File.WriteAllText(Path.Combine(package,"manifest.jws"),crypto.Sign(manifest,ModuleManifest.DocumentType));
        }
        Task<LocalResponse> Business(Guid company,string module,string command,object payload)=>Send("module.execute",new ModuleCommandRequest(company,module,command,JsonSerializer.SerializeToElement(payload)));
        async Task<LocalResponse> Send(string operation,object payload,string? overrideToken=null,bool anonymous=false)
        {
            using var connect=CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);connect.CancelAfter(800);
            await using var pipe=new NamedPipeClientStream(".",DiagnosticChannel.ForDirectory(dir.Path)+"-commands",PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(connect.Token);
            var json=JsonSerializer.SerializeToElement(payload);
            Guid? company=json.TryGetProperty("CompanyId",out var id)?id.GetGuid():activeCompany;
            var token=anonymous?null:overrideToken ?? (company is {} c&&sessionTokens.TryGetValue(c,out var t)?t:null);
            await LocalProtocol.WriteAsync(pipe,new LocalRequest(operation,json,company,token),deadline.Token);
            var response=await LocalProtocol.ReadAsync<LocalResponse>(pipe,deadline.Token);
            if(response.Success&&operation=="ui.auth.bootstrap") {var session=response.Data!.Value.Deserialize<UiSession>()!;sessionTokens[company!.Value]=session.Token;activeCompany ??= company;}
            return response;
        }
        async Task<HostDiagnostic> Status()
        {
            await using var statusPipe=new NamedPipeClientStream(".",DiagnosticChannel.ForDirectory(dir.Path),PipeDirection.In,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            await statusPipe.ConnectAsync(deadline.Token);
            return await JsonSerializer.DeserializeAsync<HostDiagnostic>(statusPipe,cancellationToken:deadline.Token)??throw new IOException("Nincs állapot.");
        }
        Task<LocalResponse> Module(Guid company,string command,object payload)=>Send("module.execute",new ModuleCommandRequest(company,"partners",command,JsonSerializer.SerializeToElement(payload)));
        Task<LocalResponse> Catalog(Guid company,string command,object payload)=>Send("module.execute",new ModuleCommandRequest(company,"catalog",command,JsonSerializer.SerializeToElement(payload)));
    }
}
