using System.Text.Json;

namespace PcImpressz.Contracts;

public sealed record EntityField(string Id, string Label, string Kind = "text", bool Required = false, string? DefaultText = null, bool DefaultChecked = false);
public sealed record EntityColumn(string Id, string Label, double Width = 180);
public sealed record EntityPageCommands(string List, string Save, string Archive, string Export,
    string? ImportPreview = null, string? ImportCommit = null);
public sealed record EntityPageDescriptor(string Id, string ModuleId, string Title, string Description,
    string Icon, EntityField[] Fields, EntityColumn[] Columns, EntityPageCommands Commands);
public sealed record ModuleCommandRequest(Guid CompanyId, string ModuleId, string Command, JsonElement Input);
public sealed record LocalRequest(string Operation, JsonElement Payload);
public sealed record LocalResponse(bool Success, JsonElement? Data = null, string? Error = null, string? Code = null);
public sealed record CompanyInfo(Guid Id, string Name, bool IsDemo, DateTimeOffset CreatedAt);
public sealed record CreateCompanyRequest(string Name, bool IsDemo);
public sealed record CommandCallContext(string ActorId);

public interface IContextualCommandHandler : ICommandHandler
{
    ValueTask<JsonElement> ExecuteAsync(JsonElement input, CommandCallContext context, CancellationToken cancellationToken);
    ValueTask<JsonElement> ICommandHandler.ExecuteAsync(JsonElement input, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("A trusted caller context is required.");
}

public static class LocalProtocol
{
    public const int MaximumMessageBytes = 8 * 1024 * 1024;
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value);
        if (payload.Length > MaximumMessageBytes) throw new IOException("Az üzenet túl nagy.");
        var length = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
        await stream.WriteAsync(length, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, cancellationToken);
        var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 1 || length > MaximumMessageBytes) throw new IOException("Érvénytelen üzenetméret.");
        var payload = new byte[length]; await stream.ReadExactlyAsync(payload, cancellationToken);
        return JsonSerializer.Deserialize<T>(payload) ?? throw new IOException("Üres üzenet.");
    }
}
