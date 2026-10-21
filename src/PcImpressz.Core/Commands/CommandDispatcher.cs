using System.Text.Json;
using Microsoft.Extensions.Logging;
using PcImpressz.Contracts;
using PcImpressz.Core.Licensing;

namespace PcImpressz.Core.Commands;

public sealed class CommandDeniedException(string message, LicenseStatus? licenseStatus = null)
    : Exception(message)
{
    public LicenseStatus? LicenseStatus { get; } = licenseStatus;
}

public sealed class CommandDispatcher(ILicenseEvaluator licenses, ILogger<CommandDispatcher> logger)
    : ICommandDispatcher
{
    private sealed record RegisteredCommand(ICommandHandler Handler, CommandDescriptor Descriptor,
        string[] RequiredModules);
    private readonly Dictionary<(string Module, string Command), RegisteredCommand> _handlers = new();
    private readonly object _gate = new();

    internal void RegisterModule(string moduleId, IReadOnlyList<ICommandHandler> handlers,
        IReadOnlyList<string> requiredModules)
    {
        lock (_gate)
        {
            if (_handlers.Keys.Any(k => k.Module == moduleId))
                throw new InvalidOperationException("Module commands already registered.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            var registrations = new List<RegisteredCommand>();
            foreach (var handler in handlers)
            {
                var descriptor = handler.Descriptor;
                Identifiers.Require(descriptor.Name, "command name");
                Identifiers.Require(descriptor.Permission, "permission");
                if (!Enum.IsDefined(descriptor.Access) || !names.Add(descriptor.Name))
                    throw new InvalidOperationException("Invalid or duplicate command descriptor.");
                registrations.Add(new(handler, descriptor, requiredModules.ToArray()));
            }
            foreach (var registration in registrations)
                _handlers.Add((moduleId, registration.Descriptor.Name), registration);
        }
    }

    public IReadOnlyList<string> RequiredEnabledModules(string moduleId, string commandName)
    {
        lock (_gate)
            return _handlers.TryGetValue((moduleId, commandName), out var command) && command.Descriptor.Access == CommandAccess.Licensed
                ? command.RequiredModules.ToArray() : [];
    }
    public IReadOnlySet<string> RegisteredPermissions
    {
        get { lock (_gate) return _handlers.Values.Select(c => c.Descriptor.Permission).ToHashSet(StringComparer.Ordinal); }
    }

    internal void UnregisterModule(string moduleId)
    {
        lock (_gate)
            foreach (var key in _handlers.Keys.Where(k => k.Module == moduleId).ToArray())
                _handlers.Remove(key);
    }

    public async ValueTask<JsonElement> ExecuteAsync(CommandRequest request, CallerIdentity caller,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RegisteredCommand registered;
        lock (_gate)
        {
            if (!_handlers.TryGetValue((request.ModuleId, request.CommandName), out registered!))
                throw new CommandDeniedException("A parancs nem érhető el.");
        }
        if (string.IsNullOrWhiteSpace(caller.UserId)
            || !caller.Permissions.Any(p => string.Equals(p, registered.Descriptor.Permission, StringComparison.Ordinal)))
        {
            logger.LogWarning("Command permission denied: {Module}/{Command}", request.ModuleId, request.CommandName);
            throw new CommandDeniedException("Nincs megfelelő felhasználói jogosultság.");
        }
        if (registered.Descriptor.Access == CommandAccess.Licensed)
        {
            foreach (var dependency in registered.RequiredModules)
            {
                var dependencyDecision = licenses.EvaluateModule(dependency);
                if (!dependencyDecision.Allowed)
                    throw new CommandDeniedException($"A szükséges modul jogosultsága hiányzik vagy lejárt: {dependency}",
                        dependencyDecision.Status);
            }
            var decision = licenses.Evaluate(request.ModuleId, registered.Descriptor.Permission);
            if (!decision.Allowed)
            {
                logger.LogWarning("Command license denied: {Module}/{Command}, {Status}",
                    request.ModuleId, request.CommandName, decision.Status);
                throw new CommandDeniedException(decision.Reason, decision.Status);
            }
        }
        // Once admitted, the handler owns its atomic transaction and business validation.
        // Long-running work must re-enter authorization at its next safe batch boundary.
        logger.LogInformation("Command admitted: {Module}/{Command}", request.ModuleId, request.CommandName);
        return registered.Handler is IContextualCommandHandler contextual
            ? await contextual.ExecuteAsync(request.Input, new(caller.UserId), cancellationToken)
            : await registered.Handler.ExecuteAsync(request.Input, cancellationToken);
    }
}
