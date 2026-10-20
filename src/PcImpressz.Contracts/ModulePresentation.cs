using System.Text.Json;

namespace PcImpressz.Contracts;

public sealed record EntityField(string Id, string Label, string Kind = "text", bool Required = false, string? DefaultText = null, bool DefaultChecked = false);
public sealed record EntityColumn(string Id, string Label, double Width = 180, bool Visible = true);
public sealed record EntityPageCommands(string List, string Save, string Archive, string Export,
    string? ImportPreview = null, string? ImportCommit = null);
public sealed record EntityFilterOption(string Value, string Label);
// Optional copy for a module page. Null keeps the original partner wording in the Shell.
public sealed record EntityPageLabels
{
    public string NewLabel { get; init; } = "Új partner";
    public string NewTitle { get; init; } = "Új partner";
    public string EditTitle { get; init; } = "Partner szerkesztése";
    public string CountNoun { get; init; } = "partner";
    public string EmptyMessage { get; init; } = "Ebben a nézetben még nincs partner. Az Új partner gombbal vehetsz fel egyet.";
    public string SavedMessage { get; init; } = "A partner mentése sikerült.";
    public string NeedSelection { get; init; } = "Először válassz meglévő partnert.";
    public string ArchiveQuestion { get; init; } = "A partner inaktív állapotba kerül. Az adatai megmaradnak.";
    public string ArchiveTitle { get; init; } = "Partner archiválása";
    public string ArchivedMessage { get; init; } = "A partner archiválva.";
    public string ExportFileName { get; init; } = "partnerek.csv";
    public string ExportedMessage { get; init; } = "A szűrt partnerlista exportálva.";
    public string SearchHint { get; init; } = "Keresés név, adószám, e-mail, telefon vagy település alapján";
    public string ImportWindowTitle { get; init; } = "InvenTree-partnerek importálása";
    public string ImportLead { get; init; } = "Hozd át a meglévő partnereidet.";
    public string ImportHelp { get; init; } = "Csak olvasunk az InvenTree-ből. Az import a kiválasztott helyi cégbe kerül, a már meglévő partnereket nem írja felül.";
    public string ImportIdle { get; init; } = "A kulcsot nem mentjük el. A címek és a partner-alapadatok kerülnek átvételre.";
    public string ImportCommitLabel { get; init; } = "Új partnerek importálása";
    public string ResultNoun { get; init; } = "partner";
}
public sealed record EntityPageDescriptor(string Id, string ModuleId, string Title, string Description,
    string Icon, EntityField[] Fields, EntityColumn[] Columns, EntityPageCommands Commands,
    EntityPageLabels? Labels = null, EntityFilterOption[]? Filters = null);
public sealed record ModuleCommandRequest(Guid CompanyId, string ModuleId, string Command, JsonElement Input);
public sealed record LocalRequest(string Operation, JsonElement Payload);
public sealed record LocalResponse(bool Success, JsonElement? Data = null, string? Error = null, string? Code = null);
public sealed record CompanyInfo(Guid Id, string Name, bool IsDemo, DateTimeOffset CreatedAt);
public sealed record CreateCompanyRequest(string Name, bool IsDemo);
public sealed record SetModuleEnabledRequest(string ModuleId, bool Enabled);
public sealed record DemoDataClearance(int DemoCompanies, int ClearedDirectories);
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
