using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PcImpressz.Contracts;
using PcImpressz.Core.Commands;
using PcImpressz.Core.Companies;
using PcImpressz.Core.Licensing;
using PcImpressz.Core.Modules;
using PcImpressz.Core.Security;
using PcImpressz.Core.Storage;

namespace PcImpressz.ApplicationHost;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = ParseArguments(args);
            if (options.Help)
            {
                Console.WriteLine("PC-Impressz mag\n  --status             Állapotellenőrzés és kilépés\n"
                    + "  --data-dir <mappa>   Saját adat- és modulmappa\n  --help               Súgó");
                return 0;
            }
            Directory.CreateDirectory(options.DataDirectory);
            // One writer per installation: checkpoint updates and module lifetime are serialized.
            using var instanceLock = new FileStream(Path.Combine(options.DataDirectory, "host.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            var installationId = LoadInstallationId(options.DataDirectory);
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = [], ContentRootPath = AppContext.BaseDirectory
            });
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
                .AddJsonFile(Path.Combine(options.DataDirectory, "settings.json"), optional: true);
            builder.Logging.ClearProviders();
            builder.Logging.AddSimpleConsole(c => { c.SingleLine = true; c.TimestampFormat = "HH:mm:ss "; });
            builder.Logging.AddProvider(new JsonFileLoggerProvider(Path.Combine(options.DataDirectory, "logs")));
            var licenseVerifier = ReadKeyring(Path.Combine(options.DataDirectory, "license-public-keys.json"));
            var moduleVerifier = ReadKeyring(Path.Combine(options.DataDirectory, "module-public-keys.json"));
            var policy = new LicensePolicy(builder.Configuration["Product:Issuer"] ?? "pcimpressz-issuer",
                PlatformContract.ProductId, builder.Configuration["Product:OrganizationId"] ?? "", installationId);
            var companies = new CompanyStore(options.DataDirectory);
            if (companies.List().Length == 0) companies.Create("Demó cég", true);
            var moduleEnablement = new ModuleEnablementStore(options.DataDirectory);
            builder.Services.AddSingleton(companies);
            builder.Services.AddSingleton(moduleEnablement);
            builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
            builder.Services.AddSingleton<ILicenseCheckpointStore>(new FileLicenseCheckpointStore(
                Path.Combine(options.DataDirectory, "license-checkpoint.json")));
            builder.Services.AddSingleton<ILicenseEvaluator>(sp => new LicenseEvaluator(licenseVerifier, policy,
                sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILicenseCheckpointStore>(),
                () => ReadTokens(options.DataDirectory)));
            builder.Services.AddSingleton<CommandDispatcher>();
            builder.Services.AddSingleton<ICommandDispatcher>(sp => sp.GetRequiredService<CommandDispatcher>());
            builder.Services.AddSingleton(sp => new ModuleRuntime(moduleVerifier,
                sp.GetRequiredService<CommandDispatcher>(), sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<ModuleRuntime>>(), companies.ModuleDataDirectory));
            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton(new InstallationInfo(installationId));
            builder.Services.AddSingleton<HostDiagnosticSource>();
            var result = new HostRunResult();
            builder.Services.AddSingleton(result);
            builder.Services.AddHostedService<CoreWorker>();
            builder.Services.AddHostedService<DiagnosticService>();
            builder.Services.AddHostedService<LocalCommandService>();
            builder.Services.AddHostedService<ParentMonitorService>();
            var host = builder.Build();
            try
            {
                await host.StartAsync();
                await host.WaitForShutdownAsync();
            }
            finally
            {
                if (host is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync();
                else host.Dispose();
            }
            return result.Failed ? 1 : 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"A mag nem indult el ({ex.GetType().Name}): {ex.Message}");
            return 1;
        }
    }

    private static SignedDocumentVerifier ReadKeyring(string path)
    {
        if (!File.Exists(path)) return new(new Dictionary<string, string>());
        if (new FileInfo(path).Length > 262_144) throw new IOException("Public key file is too large.");
        var keys = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
            ?? throw new IOException("Invalid public key file.");
        return new(keys);
    }

    private static LicenseTokens? ReadTokens(string directory)
    {
        var entitlement = Path.Combine(directory, "license.jws");
        var lease = Path.Combine(directory, "lease.jws");
        if (!File.Exists(entitlement) || !File.Exists(lease)) return null;
        if (new FileInfo(entitlement).Length > 262_144 || new FileInfo(lease).Length > 262_144)
            throw new DocumentValidationException("License file is too large.");
        return new(File.ReadAllText(entitlement).Trim(), File.ReadAllText(lease).Trim());
    }

    private static string LoadInstallationId(string directory)
    {
        var path = Path.Combine(directory, "installation-id.txt");
        if (!File.Exists(path)) File.WriteAllText(path, Guid.NewGuid().ToString("D"));
        var id = File.ReadAllText(path).Trim();
        if (!Guid.TryParseExact(id, "D", out _)) throw new IOException("Invalid installation ID.");
        return id;
    }

    private static HostOptions ParseArguments(string[] args)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PcImpressz", "WindowsEdition");
        var status = false;
        var help = false;
        int? ownerProcessId = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--status": status = true; break;
                case "--help": help = true; break;
                case "--data-dir" when i + 1 < args.Length: directory = args[++i]; break;
                case "--owner-pid" when i + 1 < args.Length && int.TryParse(args[++i], out var pid) && pid > 0:
                    ownerProcessId = pid; break;
                default: throw new ArgumentException("Ismeretlen vagy hiányos indítási kapcsoló: " + args[i]);
            }
        }
        return new(Path.GetFullPath(directory), status, help, ownerProcessId);
    }
}

internal sealed record HostOptions(string DataDirectory, bool StatusOnly, bool Help, int? OwnerProcessId);
internal sealed record InstallationInfo(string Id);
internal sealed class HostRunResult { public bool Failed { get; set; } }

internal sealed class CoreWorker(ModuleRuntime modules, HostOptions options, InstallationInfo installation,
    ILicenseEvaluator licenses, IHostApplicationLifetime lifetime, HostRunResult result,
    HostDiagnosticSource diagnostics, ILogger<CoreWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await RunCoreAsync(stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            result.Failed = true;
            logger.LogError("Core startup/execution failed: {Reason}", ex.Message);
            lifetime.StopApplication();
        }
    }

    private async Task RunCoreAsync(CancellationToken stoppingToken)
    {
        await modules.StartAsync(Path.Combine(options.DataDirectory, "modules"), stoppingToken);
        diagnostics.MarkReady(modules.Reports, modules.Pages);
        Console.WriteLine("PC-Impressz Windows Edition – a mag elindult.");
        Console.WriteLine($"Telepítés: {installation.Id}");
        Console.WriteLine($"Adatmappa: {options.DataDirectory}");
        Console.WriteLine($"Modulok: {modules.Reports.Count(r => r.Status == ModuleStatus.Active)} aktív, "
            + $"{modules.Reports.Count(r => r.Status != ModuleStatus.Active)} nem indult el.");
        foreach (var report in modules.Reports)
            Console.WriteLine($"  {report.Id} {report.Version}: {report.Status} – {report.Reason}");
        var license = licenses.Inspect();
        // Core needs no commercial license. This diagnostic does not grant any module rights.
        Console.WriteLine($"Licencellenőrzés: {license.Status} – {license.Reason}");
        logger.LogInformation("Core ready; contract API {Version}", PlatformContract.ApiVersion);
        if (options.StatusOnly) { lifetime.StopApplication(); return; }
        Console.WriteLine("Leállítás: Ctrl+C.");
        try { await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await modules.DisposeAsync();
    }
}
