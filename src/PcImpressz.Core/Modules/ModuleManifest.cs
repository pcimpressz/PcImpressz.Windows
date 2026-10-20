namespace PcImpressz.Core.Modules;

public sealed record ModuleDependency
{
    public required string ModuleId { get; init; }
    public required string MinimumVersion { get; init; }
    public required string MaximumVersionExclusive { get; init; }
}

public sealed record ModuleManifest
{
    public const string DocumentType = "pcimpressz-module+jws";
    public required int SchemaVersion { get; init; }
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string ApiVersion { get; init; }
    public required string EntryAssembly { get; init; }
    public required string EntryType { get; init; }
    public required Dictionary<string, string> Files { get; init; }
    public required ModuleDependency[] Dependencies { get; init; }
}

public enum ModuleStatus { Verified, Active, Rejected, Blocked, Failed, Stopped }

public sealed record ModuleReport(string Id, string? Version, ModuleStatus Status, string Reason);
