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

namespace PcImpressz.ApplicationHost;

// Initial desktop mode: the Host's Windows user owns all local companies.
// There is no client-supplied identity, permission list or network listener.
internal sealed class LocalCommandService(HostOptions options, CompanyStore companies, CommandDispatcher commands,
    ModuleEnablementStore modulesEnabled, CompanyBackupService backups, ILogger<LocalCommandService> logger) : BackgroundService
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

    private async Task<LocalResponse> Execute(LocalRequest request, string actor, CancellationToken ct)
    {
        switch (request.Operation)
        {
            case "companies.list": return new(true, JsonSerializer.SerializeToElement(companies.List()));
            case "companies.create":
                var company = request.Payload.Deserialize<CreateCompanyRequest>() ?? throw new ArgumentException("Hiányzó cégnév.");
                return new(true, JsonSerializer.SerializeToElement(companies.Create(company.Name, company.IsDemo)));
            case "companies.backup":
                var backup = request.Payload.Deserialize<CompanyBackupRequest>() ?? throw new ArgumentException("Hiányzó mentési kérés.");
                return new(true, JsonSerializer.SerializeToElement(new { fileName = backups.Create(backup.CompanyId, backup.FileName) }));
            case "companies.restore":
                var restore = request.Payload.Deserialize<CompanyRestoreRequest>() ?? throw new ArgumentException("Hiányzó visszaállítási kérés.");
                return new(true, JsonSerializer.SerializeToElement(backups.Restore(restore.FileName, restore.Name, restore.IsDemo)));
            case "companies.clear-demo":
                return new(true, JsonSerializer.SerializeToElement(companies.ClearDemoData()));
            case "modules.set-enabled":
                var change = request.Payload.Deserialize<SetModuleEnabledRequest>() ?? throw new ArgumentException("Hiányzó modul.");
                modulesEnabled.SetEnabled(change.ModuleId, change.Enabled);
                return new(true, JsonSerializer.SerializeToElement(new { change.ModuleId, change.Enabled }));
            case "module.execute":
                var command = request.Payload.Deserialize<ModuleCommandRequest>() ?? throw new ArgumentException("Hiányzó parancs.");
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
                var caller = new CallerIdentity(actor, commands.RegisteredPermissions);
                var result = await commands.ExecuteAsync(new(command.ModuleId, command.Command,
                    JsonSerializer.SerializeToElement(payload)), caller, ct);
                return new(true, result);
            default: return new(false, Error: "Ismeretlen művelet.", Code: "unsupported");
        }
    }
}
