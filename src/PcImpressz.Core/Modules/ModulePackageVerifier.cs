using System.Security.Cryptography;
using PcImpressz.Contracts;
using PcImpressz.Core.Security;

namespace PcImpressz.Core.Modules;

internal sealed record VerifiedModule(ModuleManifest Manifest, Dictionary<string, byte[]> Assemblies);

internal sealed class ModulePackageVerifier(SignedDocumentVerifier verifier)
{
    public VerifiedModule Verify(string directory)
    {
        RejectLink(directory);
        var manifestPath = Path.Combine(directory, "manifest.jws");
        RejectLink(manifestPath);
        if (new FileInfo(manifestPath).Length > 262_144)
            throw new DocumentValidationException("Manifest is too large.");
        var manifest = verifier.Verify<ModuleManifest>(File.ReadAllText(manifestPath).Trim(), ModuleManifest.DocumentType);
        ValidateManifest(manifest);
        if (Directory.EnumerateDirectories(directory).Any())
            throw new DocumentValidationException("Module packages must have a flat file layout.");
        var expected = manifest.Files.Keys.Append("manifest.jws").ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!expected.SetEquals(Directory.EnumerateFiles(directory).Select(Path.GetFileName)!))
            throw new DocumentValidationException("Unexpected or missing package file.");
        var assemblies = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        foreach (var (file, expectedHash) in manifest.Files)
        {
            var path = Path.Combine(directory, file);
            RejectLink(path);
            var size = new FileInfo(path).Length;
            totalBytes += size;
            if (size > 64 * 1024 * 1024 || totalBytes > 256 * 1024 * 1024)
                throw new DocumentValidationException("Module package exceeds size limit.");
            // Keep the verified bytes: loading these avoids a verify/open race on disk.
            var bytes = File.ReadAllBytes(path);
            if (bytes.LongLength != size || !CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(bytes), Convert.FromHexString(expectedHash)))
                throw new DocumentValidationException("Module file hash mismatch.");
            assemblies.Add(file, bytes);
        }
        return new(manifest, assemblies);
    }

    private static void ValidateManifest(ModuleManifest manifest)
    {
        Identifiers.Require(manifest.Id, "module ID");
        if (manifest.SchemaVersion != 1 || manifest.ApiVersion != PlatformContract.ApiVersion
            || !TryVersion(manifest.Version, out _) || string.IsNullOrWhiteSpace(manifest.EntryType)
            || manifest.Files is null || manifest.Files.Count is < 1 or > 32
            || manifest.Dependencies is null || manifest.Dependencies.Length > 64)
            throw new DocumentValidationException("Invalid or incompatible module manifest.");
        var filenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (file, hash) in manifest.Files)
        {
            if (file.Length > 160 || file != Path.GetFileName(file) || file.Contains(':')
                || file.Contains('\\') || file.Contains('/') || file.Contains("..", StringComparison.Ordinal)
                || file.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-' and not '_')
                || !file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !filenames.Add(file)
                || hash is null || hash.Length != 64 || !hash.All(char.IsAsciiHexDigit))
                throw new DocumentValidationException("Invalid module filename or hash.");
        }
        if (manifest.EntryAssembly is null || !manifest.Files.ContainsKey(manifest.EntryAssembly)
            || filenames.Contains("PcImpressz.Contracts.dll"))
            throw new DocumentValidationException("Entry assembly is missing or Contracts is bundled.");
        var dependencies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dependency in manifest.Dependencies)
        {
            if (dependency is null || !Identifiers.IsValid(dependency.ModuleId)
                || dependency.ModuleId == manifest.Id || !dependencies.Add(dependency.ModuleId)
                || !TryVersion(dependency.MinimumVersion, out var minimum)
                || !TryVersion(dependency.MaximumVersionExclusive, out var maximum) || minimum >= maximum)
                throw new DocumentValidationException("Invalid module dependency.");
        }
    }

    internal static bool TryVersion(string? text, out Version version)
    {
        version = new(0, 0, 0);
        return text is not null && text.Split('.').Length == 3
            && text.All(c => char.IsAsciiDigit(c) || c == '.') && Version.TryParse(text, out version!);
    }

    internal static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new DocumentValidationException("Linked module paths are not supported.");
    }
}
