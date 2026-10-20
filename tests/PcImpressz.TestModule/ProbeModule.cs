using System.Text.Json;
using PcImpressz.Contracts;

namespace PcImpressz.TestModule;

// Integration fixture only. It is never deployed with the application.
public sealed class ProbeModule : IModule
{
    public ValueTask StartAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        context.RegisterCommand(new ProbeCommand("write", "probe.write", CommandAccess.Licensed));
        context.RegisterCommand(new ProbeCommand("export", "probe.export", CommandAccess.RecoveryReadOnly));
        return ValueTask.CompletedTask;
    }
    public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

public sealed class FailingModule : IModule
{
    public ValueTask StartAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        context.RegisterCommand(new ProbeCommand("write", "probe.write", CommandAccess.Licensed));
        throw new InvalidOperationException("Fixture initialization failed.");
    }
    public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

internal sealed class ProbeCommand(string name, string permission, CommandAccess access) : ICommandHandler
{
    public CommandDescriptor Descriptor { get; } = new(name, permission, access);
    public ValueTask<JsonElement> ExecuteAsync(JsonElement input, CancellationToken cancellationToken) =>
        ValueTask.FromResult(JsonSerializer.SerializeToElement(new { success = true }));
}
