using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PcImpressz.Core.Commands;
using PcImpressz.Core.Licensing;
using PcImpressz.Core.Modules;
using PcImpressz.Core.Security;

namespace PcImpressz.Core.Tests;

internal static class Check
{
    public static void True(bool condition, string message = "Assertion failed")
    { if (!condition) throw new InvalidOperationException(message); }
    public static void Equal<T>(T expected, T actual) => True(EqualityComparer<T>.Default.Equals(expected, actual),
        $"Expected {expected}, actual {actual}");
    public static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }
    public static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }
}

internal sealed class TestTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-10-20T10:00:00Z");
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class MemoryCheckpointStore : ILicenseCheckpointStore
{
    private LicenseCheckpoint _value = new(0, new());
    public bool FailWrites { get; set; }
    public LicenseCheckpoint Read() => new(_value.LastObservedUnixTime, new(_value.HighestRevisions));
    public void Write(LicenseCheckpoint checkpoint)
    {
        if (FailWrites) throw new IOException("Simulated disk failure.");
        _value = new(checkpoint.LastObservedUnixTime, new(checkpoint.HighestRevisions));
    }
}

internal sealed class CryptoFixture : IDisposable
{
    public readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public readonly TestTime Clock = new();
    public readonly MemoryCheckpointStore Store = new();
    public readonly LicensePolicy Policy = new("test-issuer", "pc-impressz.windows", "company", "installation");
    public SignedDocumentVerifier Verifier { get; }
    public EntitlementDocument License { get; set; }
    public LeaseDocument Lease { get; set; }
    public LicenseTokens? Tokens { get; set; }

    public CryptoFixture()
    {
        Verifier = new(new Dictionary<string, string> { ["test-key"] = Key.ExportSubjectPublicKeyInfoPem() });
        var now = Clock.Now.ToUnixTimeSeconds();
        License = new()
        {
            SchemaVersion = 1, Iss = Policy.Issuer, Aud = Policy.ProductId, LicenseId = "license-1",
            OrganizationId = Policy.OrganizationId, Revision = 1, Iat = now - 60, Nbf = now - 60,
            Exp = now + 30 * 86400,
            Modules = [new() { ModuleId = "probe", Capabilities = ["probe.write"], ExpiresAt = now + 86400 }]
        };
        Lease = new()
        {
            SchemaVersion = 1, Iss = Policy.Issuer, Aud = Policy.ProductId, LicenseId = "license-1",
            OrganizationId = Policy.OrganizationId, InstallationId = Policy.InstallationId,
            Revision = 1, Iat = now - 60, Nbf = now - 60, Exp = now + 3600
        };
        Refresh();
    }
    public void Refresh() => Tokens = new(Sign(License, LicenseDocumentTypes.Entitlement),
        Sign(Lease, LicenseDocumentTypes.Lease));
    public LicenseEvaluator Evaluator() => new(Verifier, Policy, Clock, Store, () => Tokens);
    public string Sign<T>(T payload, string type) => SignRaw(
        JsonSerializer.Serialize(new { alg = "ES256", kid = "test-key", typ = type }),
        JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    public string SignRaw(string headerJson, string payloadJson)
    {
        var input = Encode(Encoding.UTF8.GetBytes(headerJson)) + "." + Encode(Encoding.UTF8.GetBytes(payloadJson));
        return input + "." + Encode(Key.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }
    public static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public void Dispose() => Key.Dispose();
}

internal sealed class TestDirectory : IDisposable
{
    private static readonly string Root = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "test-data"));
    public string Path { get; } = System.IO.Path.Combine(Root, Guid.NewGuid().ToString("N"));
    public TestDirectory() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        var resolved = System.IO.Path.GetFullPath(Path);
        if (!resolved.StartsWith(Root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unsafe test cleanup path.");
        Directory.Delete(resolved, recursive: true);
    }
}

internal sealed class RuntimeFixture : IAsyncDisposable
{
    public readonly CryptoFixture Crypto = new();
    public readonly TestDirectory Directory = new();
    public CommandDispatcher Dispatcher { get; }
    public ModuleRuntime Runtime { get; }
    public RuntimeFixture()
    {
        Dispatcher = new(Crypto.Evaluator(), NullLogger<CommandDispatcher>.Instance);
        Runtime = new(Crypto.Verifier, Dispatcher, Crypto.Clock, NullLogger<ModuleRuntime>.Instance);
    }
    public ModuleManifest Package(string id = "probe", ModuleDependency[]? dependencies = null,
        string entryType = "PcImpressz.TestModule.ProbeModule", string apiVersion = "1.0")
    {
        var destination = System.IO.Path.Combine(Directory.Path, id);
        System.IO.Directory.CreateDirectory(destination);
        var bytes = File.ReadAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "PcImpressz.TestModule.dll"));
        File.WriteAllBytes(System.IO.Path.Combine(destination, "PcImpressz.TestModule.dll"), bytes);
        var manifest = new ModuleManifest
        {
            SchemaVersion = 1, Id = id, Version = "1.0.0", ApiVersion = apiVersion,
            EntryAssembly = "PcImpressz.TestModule.dll", EntryType = entryType,
            Files = new() { ["PcImpressz.TestModule.dll"] = Convert.ToHexString(SHA256.HashData(bytes)) },
            Dependencies = dependencies ?? []
        };
        SaveManifest(id, manifest);
        return manifest;
    }
    public void SaveManifest(string directory, ModuleManifest manifest) => File.WriteAllText(
        System.IO.Path.Combine(Directory.Path, directory, "manifest.jws"), Crypto.Sign(manifest, ModuleManifest.DocumentType));
    public async ValueTask DisposeAsync()
    {
        await Runtime.DisposeAsync();
        Directory.Dispose();
        Crypto.Dispose();
    }
}
