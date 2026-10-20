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
        using var dir=new TestDirectory();using var crypto=new CryptoFixture();
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        var install=Guid.NewGuid().ToString("D");
        var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        crypto.Lease=crypto.Lease with{InstallationId=install,Iat=now-30,Nbf=now-30,Exp=now+3600};
        crypto.License=crypto.License with{Iat=now-60,Nbf=now-60,Exp=now+30*86400};
        crypto.License=crypto.License with{Modules=[
            new(){ModuleId="partners",Capabilities=["partners.write","partners.import"],ExpiresAt=crypto.License.Exp},
            new(){ModuleId="catalog",Capabilities=["catalog.write","catalog.import"],ExpiresAt=crypto.License.Exp}]};crypto.Refresh();
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
        var start=new ProcessStartInfo(Path.Combine(root,".tools","dotnet","dotnet.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(Path.Combine(root,"src","PcImpressz.ApplicationHost","bin","Release","net10.0","PcImpressz.ApplicationHost.dll"));
        start.ArgumentList.Add("--data-dir");start.ArgumentList.Add(dir.Path);
        using var host=Process.Start(start)??throw new IOException("Test Host did not start.");
        host.OutputDataReceived+=(_,_)=>{};host.ErrorDataReceived+=(_,_)=>{};host.BeginOutputReadLine();host.BeginErrorReadLine();
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));
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
                    Check.True(status.Modules.All(m=>m.State=="Active"),string.Join("; ",status.Modules.Select(m=>m.Reason)));
                    break;
                }
                await Task.Delay(100,deadline.Token);
            }
            Check.True(first.Success);Check.Equal(1,first.Data!.Value.GetArrayLength());
            var a=(await Send("companies.create",new CreateCompanyRequest("Teszt cég A",false))).Data!.Value.GetProperty("Id").GetGuid();
            var b=(await Send("companies.create",new CreateCompanyRequest("Teszt cég B",true))).Data!.Value.GetProperty("Id").GetGuid();
            Check.Equal(0,(await Module(a,"list",new{})).Data!.Value.GetProperty("total").GetInt32());
            var saved=await Module(a,"save",new Dictionary<string,object?>{["CompanyId"]=b,["companyId"]=b,["id"]=null,["version"]=0,["data"]=new{name="IPC partner",isCustomer=true,isActive=true},["actor"]="fake-user"});
            Check.True(saved.Success,saved.Error??"Save failed");
            Check.Equal(1,(await Module(a,"list",new{})).Data!.Value.GetProperty("total").GetInt32());
            Check.Equal(0,(await Module(b,"list",new{})).Data!.Value.GetProperty("total").GetInt32());
            using(var state=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir.Path,"companies",a.ToString("N"),"modules","partners","partners.json"))))
                Check.True(state.RootElement.GetProperty("partners")[0].GetProperty("createdBy").GetString()!.StartsWith("S-1-",StringComparison.Ordinal));
            var unit=await Catalog(a,"units-save",new{id=(Guid?)null,version=0,data=new{name="Darab",code="DB",symbol="db",isActive=true}});
            Check.True(unit.Success,unit.Error??"Unit save failed");
            var product=await Catalog(a,"products-save",new{id=(Guid?)null,version=0,data=new{name="Csavar",sku="CSV-1",unitCode="DB",isActive=true}});
            Check.True(product.Success,product.Error??"Product save failed");
            Check.Equal(1,(await Catalog(a,"products-list",new{})).Data!.Value.GetProperty("total").GetInt32());
            Check.Equal(0,(await Catalog(b,"products-list",new{})).Data!.Value.GetProperty("total").GetInt32());
            File.Delete(Path.Combine(dir.Path,"license.jws"));
            var denied=await Module(a,"save",new{id=(Guid?)null,version=0,data=new{name="Nem menthető",isActive=true}});
            Check.True(!denied.Success);Check.Equal("denied",denied.Code);Check.True((await Module(a,"export",new{})).Success);
            var deniedProduct=await Catalog(a,"products-save",new{id=(Guid?)null,version=0,data=new{name="Nem menthető",sku="NEM",unitCode="DB",isActive=true}});
            Check.True(!deniedProduct.Success);Check.Equal("denied",deniedProduct.Code);Check.True((await Catalog(a,"products-export",new{})).Success);
        }
        finally{if(!host.HasExited){host.Kill(entireProcessTree:true);await host.WaitForExitAsync();}}

        async Task<LocalResponse> Send(string operation,object payload)
        {
            using var connect=CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);connect.CancelAfter(800);
            await using var pipe=new NamedPipeClientStream(".",DiagnosticChannel.ForDirectory(dir.Path)+"-commands",PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(connect.Token);
            await LocalProtocol.WriteAsync(pipe,new LocalRequest(operation,JsonSerializer.SerializeToElement(payload)),deadline.Token);
            return await LocalProtocol.ReadAsync<LocalResponse>(pipe,deadline.Token);
        }
        Task<LocalResponse> Module(Guid company,string command,object payload)=>Send("module.execute",new ModuleCommandRequest(company,"partners",command,JsonSerializer.SerializeToElement(payload)));
        Task<LocalResponse> Catalog(Guid company,string command,object payload)=>Send("module.execute",new ModuleCommandRequest(company,"catalog",command,JsonSerializer.SerializeToElement(payload)));
    }
}
