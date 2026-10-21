using System.Text.Json;

namespace PcImpressz.Contracts;

public static class PlatformContract
{
    public const string ApiVersion = "1.1";
    public const string ProductId = "pc-impressz.windows";
}

public interface IModule
{
    ValueTask StartAsync(IModuleContext context, CancellationToken cancellationToken);
    ValueTask StopAsync(CancellationToken cancellationToken);
}

public interface IModuleContext
{
    ICompanyDatabase? Database => null;
    string ModuleId { get; }
    TimeProvider TimeProvider { get; }
    void RegisterCommand(ICommandHandler handler);
    void RegisterPage(EntityPageDescriptor page);
    string GetCompanyDataDirectory(Guid companyId);
}

public enum CommandAccess
{
    Licensed,
    // Only data viewing and basic export belong here. User permission still applies.
    RecoveryReadOnly
}

public sealed record CommandDescriptor(string Name, string Permission, CommandAccess Access);

public interface ICommandHandler
{
    CommandDescriptor Descriptor { get; }
    ValueTask<JsonElement> ExecuteAsync(JsonElement input, CancellationToken cancellationToken);
}

// This identity must be created by a trusted authentication boundary, never from request JSON.
public sealed record CallerIdentity(string UserId, IReadOnlySet<string> Permissions);

public sealed record CommandRequest(string ModuleId, string CommandName, JsonElement Input);

public interface ICommandDispatcher
{
    ValueTask<JsonElement> ExecuteAsync(CommandRequest request, CallerIdentity caller,
        CancellationToken cancellationToken = default);
}
