using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PcImpressz.Contracts;
using PcImpressz.Core.Licensing;
using PcImpressz.Core.Modules;

namespace PcImpressz.ApplicationHost;

internal sealed class HostDiagnosticSource(HostOptions options, InstallationInfo installation, ILicenseEvaluator licenses,
    ModuleEnablementStore modulesEnabled)
{
    private ModuleDiagnostic[]? _modules;
    private EntityPageDescriptor[] _pages = [];
    public void MarkReady(IReadOnlyList<ModuleReport> reports, IReadOnlyList<EntityPageDescriptor> pages)
    {
        _pages = pages.ToArray();
        Volatile.Write(ref _modules, reports.Select(r => new ModuleDiagnostic(r.Id, r.Version, r.Status.ToString(), r.Reason)).ToArray());
    }

    public HostDiagnostic Snapshot()
    {
        var modules = Volatile.Read(ref _modules);
        var license = licenses.Inspect();
        var reported = modules?.Select(module => module with { Enabled = modulesEnabled.IsEnabled(module.Id) }).ToArray();
        return new(modules is null ? "Starting" : "Ready", installation.Id, options.DataDirectory,
            PlatformContract.ApiVersion, license.Status.ToString(), license.Reason, license.ValidUntil,
            DateTimeOffset.UtcNow, Environment.ProcessId, reported ?? [], modules is null ? [] : _pages) { DevelopmentLoginEnabled=DevelopmentAccess.Enabled(options.DataDirectory),CompanyAuthenticationVersion = 1, CheckoutVersion = 1 };
    }
}

// A same-Windows-user, one-way diagnostics channel. No network port or business command endpoint.
internal sealed class DiagnosticService(HostOptions options, HostDiagnosticSource source,
    ILogger<DiagnosticService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var name = DiagnosticChannel.ForDirectory(options.DataDirectory);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(name, PipeDirection.Out, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stoppingToken);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(3));
                await JsonSerializer.SerializeAsync(pipe, source.Snapshot(), cancellationToken: deadline.Token);
                await pipe.FlushAsync(deadline.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            {
                logger.LogDebug("Diagnostic connection ended: {Reason}", ex.GetType().Name);
                await Task.Delay(150, stoppingToken);
            }
        }
    }
}

internal sealed class ParentMonitorService(HostOptions options, IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.OwnerProcessId is not { } id) return;
        try
        {
            using var owner = Process.GetProcessById(id);
            await owner.WaitForExitAsync(stoppingToken);
            lifetime.StopApplication();
        }
        catch (ArgumentException) { lifetime.StopApplication(); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
