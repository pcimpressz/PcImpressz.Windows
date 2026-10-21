using System.Security.Cryptography;
using System.Text;

namespace PcImpressz.Contracts;

// Diagnostic data only. These records never authorize business commands.
public sealed record ModuleDiagnostic(string Id, string? Version, string State, string Reason, bool Enabled = true);
public sealed record HostDiagnostic(string State, string InstallationId, string DataDirectory,
    string ContractVersion, string LicenseState, string LicenseReason, DateTimeOffset? LicenseValidUntil,
    DateTimeOffset ObservedAt, int ProcessId, ModuleDiagnostic[] Modules, EntityPageDescriptor[]? Pages = null)
{ public bool DevelopmentLoginEnabled {get;init;} public int CompanyAuthenticationVersion { get; init; } public int CheckoutVersion { get; init; } }

public static class DiagnosticChannel
{
    public static string ForDirectory(string directory)
    {
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)).ToUpperInvariant();
        return "pcimpressz-status-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..24];
    }
}
