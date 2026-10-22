using PcImpressz.Core.Storage;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PcImpressz.Contracts;
using PcImpressz.Core.Commands;
using PcImpressz.Core.Companies;
using PcImpressz.Core.Modules;
using PcImpressz.Core.Security;
using PcImpressz.Core.Licensing;

namespace PcImpressz.ApplicationHost;

// Company sessions and roles are checked before dispatch; the OS caller SID is trusted.
// There is no client-supplied identity, permission list or network listener.
internal sealed partial class LocalCommandService(HostOptions options, CompanyStore companies, CommandDispatcher commands,
    ModuleEnablementStore modulesEnabled, CompanyBackupService backups, UiAdministrationService administration, ICompanyDatabase database, ILicenseEvaluator licenses, ModuleRuntime runtime, ILogger<LocalCommandService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("A helyi parancscsatorna Windows rendszert igényel.");
        using var identity = WindowsIdentity.GetCurrent();
        var actor = identity.User?.Value ?? throw new IOException("A Windows-felhasználó nem azonosítható.");
        var name = DiagnosticChannel.ForDirectory(options.DataDirectory) + "-commands";
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stoppingToken);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(90));
                LocalResponse response;
                try
                {
                    var request = await LocalProtocol.ReadAsync<LocalRequest>(pipe, deadline.Token);
                    response = await Execute(request, actor, deadline.Token);
                }
                catch (RecordNotFoundException ex) { response = new(false, Error: ex.Message, Code: "not-found"); }
                catch (UnauthorizedAccessException ex) { response = new(false, Error: ex.Message, Code: "denied"); }
                catch (CommandDeniedException ex) { response = new(false, Error: ex.Message, Code: "denied"); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                { response = new(false, Error: ex.Message, Code: "validation"); }
                catch (Exception ex) when (ex is not OutOfMemoryException && !stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning("Local command failed: {ErrorType}", ex.GetType().Name);
                    response = new(false, Error: ex is IOException ? ex.Message
                        : ex.GetType().Name is "PartnerConflictException" or "CatalogConflictException" or "ReceivingConflictException" ? ex.Message
                        : ex is HttpRequestException ? ex.Message : "A művelet nem sikerült. Ellenőrizd az adatokat és a naplót.", Code: "failed");
                }
                await LocalProtocol.WriteAsync(pipe, response, deadline.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            { logger.LogDebug("Local command channel closed: {Reason}", ex.GetType().Name); }
        }
    }

    private LocalResponse AdminCommand(LocalRequest request, string sid)
    {
        var company = request.Payload.GetProperty("CompanyId").GetGuid();
        if (!companies.List().Any(c => c.Id == company)) throw new ArgumentException("Ismeretlen cég.");
        T Read<T>() => request.Payload.Deserialize<T>() ?? throw new ArgumentException("Hiányzó kérésadat.");
        EntityPageDescriptor Page()
        {
            var key = request.Payload.GetProperty("PageKey").GetString();
            return runtime.Pages.SingleOrDefault(p => p.ModuleId + "." + p.Id == key && p.ViewKind == "entity")
                ?? throw new ArgumentException("Ismeretlen vagy nem szerkeszthető adatlap.");
        }
        object value = request.Operation switch
        {
            "ui.auth.development" => administration.DevelopmentSession(company,sid),
            "ui.auth.status" => administration.Status(company),
            "ui.auth.bootstrap" => administration.Bootstrap(Read<UiLogin>(), sid),
            "ui.auth.login" => administration.Login(Read<UiLogin>(), sid),
            "ui.auth.session" => administration.Authenticate(company, request.SessionToken, sid),
            "ui.users.list" => administration.ListUsers(company, request.SessionToken ?? "", sid),
            "ui.workspace.read" => ReadWorkspace(),
            "ui.workspace.save" => SaveWorkspace(),
            "ui.layout.read" => ReadLayout(),
            "ui.layout.save" => administration.Save(Read<UiLayoutSave>(), Page(), sid),
            "ui.layout.restore" => administration.Restore(Read<UiLayoutRestore>(), Page(), sid),
            _ => ExecuteOther()
        };
        return new(true, JsonSerializer.SerializeToElement(value));
        void WorkspacePage(string key){if(!runtime.Pages.Any(p=>p.ModuleId+"."+p.Id==key))throw new ArgumentException("Ismeretlen munkafelület.");}
        object ReadWorkspace(){administration.Authenticate(company,request.SessionToken,sid);var input=Read<UiWorkspaceRequest>();WorkspacePage(input.PageKey);return administration.ReadWorkspace(input);}
        object SaveWorkspace(){var input=Read<UiWorkspaceSave>() with{Token=request.SessionToken??""};WorkspacePage(input.PageKey);return administration.SaveWorkspace(input,sid);}
        object ReadLayout() { administration.Authenticate(company, request.SessionToken, sid); return administration.Read(company, Page()); }
        object ExecuteOther()
        {
            if (request.Operation == "ui.users.update") { administration.UpdateUser(Read<UiUpdateUser>(), sid); return new { saved = true }; }
            if (request.Operation == "ui.auth.change-password") { administration.ChangePassword(Read<UiChangePassword>(), sid); return new { saved = true }; }
            if (request.Operation == "ui.users.create") { administration.CreateUser(Read<UiCreateUser>(), sid); return new { saved = true }; }
            if (request.Operation == "ui.auth.logout") { administration.Logout(company, request.Payload.GetProperty("Token").GetString() ?? "", sid); return new { loggedOut = true }; }
            throw new ArgumentException("Ismeretlen adminisztrációs művelet.");
        }
    }
    private async Task<LocalResponse> Execute(LocalRequest request, string actor, CancellationToken ct)
    {
        if (request.Operation.StartsWith("ui.", StringComparison.Ordinal)) return AdminCommand(request, actor);
        UiSession Session(Guid company) => administration.Authenticate(company,request.SessionToken,actor);
        void Admin(Guid? company=null) => administration.RequireAdmin(company ?? request.AuthCompanyId ?? throw new UnauthorizedAccessException("Adminbejelentkezés szükséges."),request.SessionToken,actor);
        switch (request.Operation)
        {
            case "partners.taxpayer": return await LookupPartnerTaxpayer(request,actor,ct);
            case "sales.customer-nav": return await LookupCheckoutCustomer(request,actor,ct);
            case "companies.list": return new(true, JsonSerializer.SerializeToElement(companies.List()));
            case "companies.create":
                if(companies.List().Any(c=>administration.Status(c.Id).Configured)) Admin();
                var company = request.Payload.Deserialize<CreateCompanyRequest>() ?? throw new ArgumentException("Hiányzó cégnév.");
                return new(true, JsonSerializer.SerializeToElement(companies.Create(company.Name, company.IsDemo)));
            case "companies.seed-demo": return await SeedDemoAsync(request,actor,ct);
            case "companies.backup":
                var backup = request.Payload.Deserialize<CompanyBackupRequest>() ?? throw new ArgumentException("Hiányzó mentési kérés.");
                Admin(backup.CompanyId);
                return new(true, JsonSerializer.SerializeToElement(new { fileName = backups.Create(backup.CompanyId, backup.FileName) }));
            case "companies.restore":
                Admin();
                var restore = request.Payload.Deserialize<CompanyRestoreRequest>() ?? throw new ArgumentException("Hiányzó visszaállítási kérés.");
                return new(true, JsonSerializer.SerializeToElement(backups.Restore(restore.FileName, restore.Name, restore.IsDemo)));
            case "companies.clear-demo":
                Admin();
                return new(true, JsonSerializer.SerializeToElement(companies.ClearDemoData(request.AuthCompanyId)));
            case "modules.set-enabled":
                Admin();
                var change = request.Payload.Deserialize<SetModuleEnabledRequest>() ?? throw new ArgumentException("Hiányzó modul.");
                modulesEnabled.SetEnabled(change.ModuleId, change.Enabled);
                return new(true, JsonSerializer.SerializeToElement(new { change.ModuleId, change.Enabled }));
            case "module.execute":
                var command = request.Payload.Deserialize<ModuleCommandRequest>() ?? throw new ArgumentException("Hiányzó parancs.");
                if(command.ModuleId=="receiving"&&command.Command is "nav-store" or "outgoing-reconcile-store")throw new CommandDeniedException("A NAV-import mentése csak ellenőrzött letöltés után hívható.");
                if(command.ModuleId=="receiving"&&command.Command=="seed-demo")throw new CommandDeniedException("A mintaadat-feltöltés csak az admin demócég-műveletén keresztül indítható.");
                if (!companies.List().Any(c => c.Id == command.CompanyId)) throw new ArgumentException("Ismeretlen cég.");
                if (!modulesEnabled.IsEnabled(command.ModuleId))
                    throw new InvalidOperationException("Ez a modul ki van kapcsolva.");
                foreach (var dependency in commands.RequiredEnabledModules(command.ModuleId, command.Command))
                    if (!modulesEnabled.IsEnabled(dependency)) throw new InvalidOperationException("A szükséges modul ki van kapcsolva: " + dependency);
                if (command.Input.ValueKind != JsonValueKind.Object) throw new ArgumentException("Érvénytelen parancsadat.");
                var payload = JsonNode.Parse(command.Input.GetRawText()) as JsonObject
                    ?? throw new ArgumentException("Érvénytelen parancsadat.");
                foreach (var key in payload.Select(p => p.Key).Where(k => k.Equals("companyId", StringComparison.OrdinalIgnoreCase)).ToArray())
                    payload.Remove(key);
                payload["companyId"] = command.CompanyId;
                var session=Session(command.CompanyId);
                // Dossiers expose cross-module history; NAV settings contain local secrets.
                if ((command.ModuleId=="receiving" && command.Command is "nav-save" or "bizonylat-save" or "invoicing-settings" or "invoicing-settings-save" or "bizonylat-status" or "outgoing-issue-sale") || (command.ModuleId=="partners" && command.Command=="dossier") || (command.ModuleId=="catalog"&&command.Command=="products-overview"))
                    Admin(command.CompanyId);
                var caller = new CallerIdentity(actor+"/"+session.UserName, commands.RegisteredPermissions.Where(p=>CompanyRoles.Allows(session.Role,p)).ToHashSet(StringComparer.Ordinal), session.Role);
                if(command.ModuleId=="sales"&&command.Command=="customer-save")CustomerWriteLicense();
                if(command.ModuleId=="sales"&&command.Command=="save")
                {
                    var checkout=await commands.ExecuteAsync(new("sales","checkout-info",JsonSerializer.SerializeToElement(payload)),caller,ct);
                    if(!checkout.TryGetProperty("version",out var version)||version.GetInt32()<(payload.TryGetPropertyValue("returnOf",out var returnValue)&&returnValue is not null?2:1))throw new CommandDeniedException("Az eladásmodult frissíteni kell a pénztári fizetés és számlaigény mentéséhez.");
                }
                var result = await commands.ExecuteAsync(new(command.ModuleId, command.Command,
                    JsonSerializer.SerializeToElement(payload)), caller, ct);
                if(command.ModuleId=="receiving"&&command.Command is "nav-import" or "nav-list" or "nav-outgoing-list" or "nav-outgoing-reconcile")
                {
                    var current=Session(command.CompanyId);
                    if(command.Command=="nav-outgoing-reconcile")
                    {
                        foreach(var required in commands.RequiredEnabledModules("receiving","outgoing-reconcile-store").Prepend("receiving"))if(!modulesEnabled.IsEnabled(required))throw new CommandDeniedException("A szükséges modul ki van kapcsolva: "+required);
                        var refreshed=new CallerIdentity(actor+"/"+current.UserName,commands.RegisteredPermissions.Where(p=>CompanyRoles.Allows(current.Role,p)).ToHashSet(StringComparer.Ordinal),current.Role);
                        result=await commands.ExecuteAsync(new("receiving","outgoing-reconcile-store",JsonSerializer.SerializeToElement(new{companyId=command.CompanyId,request=payload,rows=result})),refreshed,ct);
                    }
                    if(command.Command=="nav-import")
                    {
                        foreach(var required in commands.RequiredEnabledModules("receiving","nav-store").Prepend("receiving"))if(!modulesEnabled.IsEnabled(required))throw new CommandDeniedException("A szükséges modul ki van kapcsolva: "+required);
                        var refreshed=new CallerIdentity(actor+"/"+current.UserName,commands.RegisteredPermissions.Where(p=>CompanyRoles.Allows(current.Role,p)).ToHashSet(StringComparer.Ordinal),current.Role);
                        result=await commands.ExecuteAsync(new("receiving","nav-store",JsonSerializer.SerializeToElement(new{companyId=command.CompanyId,request=payload,document=result})),refreshed,ct);
                    }
                }
                return new(true, result);
            default: return new(false, Error: "Ismeretlen művelet.", Code: "unsupported");
        }
    }
}
