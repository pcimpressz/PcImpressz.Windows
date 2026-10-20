namespace PcImpressz.Core.Licensing;

public static class LicenseDocumentTypes
{
    public const string Entitlement = "pcimpressz-license+jwt";
    public const string Lease = "pcimpressz-lease+jwt";
}

public sealed record ModuleGrant
{
    public required string ModuleId { get; init; }
    public required string[] Capabilities { get; init; }
    public required long ExpiresAt { get; init; }
}

public sealed record EntitlementDocument
{
    public required int SchemaVersion { get; init; }
    public required string Iss { get; init; }
    public required string Aud { get; init; }
    public required string LicenseId { get; init; }
    public required string OrganizationId { get; init; }
    public required long Revision { get; init; }
    public required long Iat { get; init; }
    public required long Nbf { get; init; }
    public required long Exp { get; init; }
    public required ModuleGrant[] Modules { get; init; }
}

public sealed record LeaseDocument
{
    public required int SchemaVersion { get; init; }
    public required string Iss { get; init; }
    public required string Aud { get; init; }
    public required string LicenseId { get; init; }
    public required string OrganizationId { get; init; }
    public required string InstallationId { get; init; }
    public required long Revision { get; init; }
    public required long Iat { get; init; }
    public required long Nbf { get; init; }
    public required long Exp { get; init; }
}

public sealed record LicensePolicy(string Issuer, string ProductId, string OrganizationId,
    string InstallationId)
{
    public TimeSpan MaximumLeaseDuration { get; init; } = TimeSpan.FromDays(7);
    public TimeSpan FutureIssueTolerance { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan ClockRollbackTolerance { get; init; } = TimeSpan.FromMinutes(2);
}

public enum LicenseStatus
{
    Active, Missing, Invalid, NotYetValid, Expired, WrongInstallation, MissingCapability,
    RollbackDetected, StateUnavailable
}

public sealed record LicenseDecision(LicenseStatus Status, string Reason,
    DateTimeOffset? ValidUntil = null)
{
    public bool Allowed => Status == LicenseStatus.Active;
}

public interface ILicenseEvaluator
{
    LicenseDecision Inspect();
    LicenseDecision EvaluateModule(string moduleId);
    LicenseDecision Evaluate(string moduleId, string capability);
}

public sealed record LicenseCheckpoint(long LastObservedUnixTime,
    Dictionary<string, long> HighestRevisions);

public interface ILicenseCheckpointStore
{
    LicenseCheckpoint Read();
    void Write(LicenseCheckpoint checkpoint);
}
