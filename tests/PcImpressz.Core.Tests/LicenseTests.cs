using System.Text.Json;
using PcImpressz.Core.Licensing;
using PcImpressz.Core.Security;
using PcImpressz.Core.Storage;

namespace PcImpressz.Core.Tests;

internal static class LicenseTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return Case("valid license permits capability", f => Check.True(f.Evaluator().Evaluate("probe", "probe.write").Allowed));
        yield return Case("general license inspection does not require a Core commercial grant", f =>
            Check.Equal(LicenseStatus.Active, f.Evaluator().Inspect().Status));
        yield return Case("future issued lease denies beyond tolerance", f =>
        {
            f.Lease = f.Lease with { Iat = f.Clock.Now.ToUnixTimeSeconds() + 121 }; f.Refresh();
            Check.Equal(LicenseStatus.NotYetValid, f.Evaluator().Evaluate("probe", "probe.write").Status);
        });
        yield return Case("missing license denies", f =>
        { f.Tokens = null; Check.Equal(LicenseStatus.Missing, f.Evaluator().Evaluate("probe", "probe.write").Status); });
        yield return Case("unknown capability denies", f =>
            Check.Equal(LicenseStatus.MissingCapability, f.Evaluator().Evaluate("probe", "probe.delete").Status));
        yield return Case("unlicensed module denies", f =>
            Check.Equal(LicenseStatus.MissingCapability, f.Evaluator().Evaluate("other", "probe.write").Status));
        yield return Case("lease expiry is exclusive", f =>
        {
            f.Clock.Now = DateTimeOffset.FromUnixTimeSeconds(f.Lease.Exp - 1);
            Check.True(f.Evaluator().Evaluate("probe", "probe.write").Allowed);
            f.Clock.Now = f.Clock.Now.AddSeconds(1);
            Check.Equal(LicenseStatus.Expired, f.Evaluator().Evaluate("probe", "probe.write").Status);
        });
        yield return Case("module expiry is independent of lease", f =>
        {
            var now = f.Clock.Now.ToUnixTimeSeconds();
            f.License = f.License with { Modules = [f.License.Modules[0] with { ExpiresAt = now }] };
            f.Refresh();
            Check.Equal(LicenseStatus.Expired, f.Evaluator().Evaluate("probe", "probe.write").Status);
        });
        yield return Case("not-before boundary is inclusive", f =>
        {
            f.Lease = f.Lease with { Nbf = f.Clock.Now.ToUnixTimeSeconds() + 1 }; f.Refresh();
            Check.Equal(LicenseStatus.NotYetValid, f.Evaluator().Evaluate("probe", "probe.write").Status);
            f.Clock.Now = f.Clock.Now.AddSeconds(1);
            Check.True(f.Evaluator().Evaluate("probe", "probe.write").Allowed);
        });
        yield return Case("lease cannot bind another installation", f =>
        {
            f.Lease = f.Lease with { InstallationId = "other-installation" }; f.Refresh();
            Check.Equal(LicenseStatus.WrongInstallation, f.Evaluator().Evaluate("probe", "probe.write").Status);
        });
        yield return Case("wrong organization denies", f =>
        {
            f.License = f.License with { OrganizationId = "other-company" }; f.Refresh();
            Check.Equal(LicenseStatus.Invalid, f.Evaluator().Evaluate("probe", "probe.write").Status);
        });
        yield return Case("wrong product denies", f =>
        {
            f.License = f.License with { Aud = "other-product" }; f.Refresh();
            Check.Equal(LicenseStatus.Invalid, f.Evaluator().Evaluate("probe", "probe.write").Status);
        });
        yield return Case("wrong issuer denies", f =>
        {
            f.Lease = f.Lease with { Iss = "other-issuer" }; f.Refresh();
            Check.Equal(LicenseStatus.Invalid, f.Evaluator().Evaluate("probe", "probe.write").Status);
        });
        yield return Case("lease cannot mix license revisions", f =>
        {
            f.Lease = f.Lease with { Revision = 2 }; f.Refresh();
            Check.Equal(LicenseStatus.Invalid, f.Evaluator().Evaluate("probe", "probe.write").Status);
        });
        yield return Case("lease cannot mix license IDs", f =>
        {
            f.Lease = f.Lease with { LicenseId = "license-2" }; f.Refresh();
            Check.Equal(LicenseStatus.Invalid, f.Evaluator().Evaluate("probe", "probe.write").Status);
        });
        yield return Case("lease longer than seven days denies", f =>
        {
            f.Lease = f.Lease with { Exp = f.Lease.Iat + 7 * 86400 + 1 }; f.Refresh();
            Check.Equal(LicenseStatus.Invalid, f.Evaluator().Evaluate("probe", "probe.write").Status);
        });
        yield return Case("old entitlement revision cannot replay", f =>
        {
            var old = f.Tokens;
            f.License = f.License with { Revision = 2 }; f.Lease = f.Lease with { Revision = 2 }; f.Refresh();
            Check.True(f.Evaluator().Evaluate("probe", "probe.write").Allowed);
            f.Tokens = old;
            Check.Equal(LicenseStatus.RollbackDetected, f.Evaluator().Evaluate("probe", "probe.write").Status);
        });
        yield return Case("observed clock rollback denies", f =>
        {
            Check.True(f.Evaluator().Evaluate("probe", "probe.write").Allowed);
            f.Clock.Now = f.Clock.Now.AddMinutes(-3);
            Check.Equal(LicenseStatus.RollbackDetected, f.Evaluator().Evaluate("probe", "probe.write").Status);
        });
        yield return Case("checkpoint write failure fails closed", f =>
        {
            f.Store.FailWrites = true;
            Check.Equal(LicenseStatus.StateUnavailable, f.Evaluator().Evaluate("probe", "probe.write").Status);
        });
        yield return Case("duplicate module grants deny", f =>
        {
            f.License = f.License with { Modules = [f.License.Modules[0], f.License.Modules[0]] }; f.Refresh();
            Check.Equal(LicenseStatus.Invalid, f.Evaluator().Evaluate("probe", "probe.write").Status);
        });
        yield return Case("tampered signed bytes deny", f =>
        {
            var token = f.Tokens!.Entitlement.Split('.');
            token[1] = (token[1][0] == 'a' ? "b" : "a") + token[1][1..];
            f.Tokens = f.Tokens with { Entitlement = string.Join('.', token) };
            Check.Equal(LicenseStatus.Invalid, f.Evaluator().Evaluate("probe", "probe.write").Status);
        });
        yield return Case("wrong document type denies", f =>
        {
            f.Tokens = f.Tokens! with { Entitlement = f.Sign(f.License, LicenseDocumentTypes.Lease) };
            Check.Equal(LicenseStatus.Invalid, f.Evaluator().Evaluate("probe", "probe.write").Status);
        });
        yield return Case("unknown signing key denies", f =>
        {
            var token = f.SignRaw(Header("ES256", "other-key"), "{}");
            Check.Throws<DocumentValidationException>(() => f.Verifier.Verify<EntitlementDocument>(token, LicenseDocumentTypes.Entitlement));
        });
        yield return Case("algorithm substitution denies", f =>
        {
            foreach (var algorithm in new[] { "none", "HS256", "RS256" })
                Check.Throws<DocumentValidationException>(() => f.Verifier.Verify<EntitlementDocument>(
                    f.SignRaw(Header(algorithm, "test-key"), "{}"), LicenseDocumentTypes.Entitlement));
        });
        yield return Case("duplicate JSON header keys deny", f =>
        {
            var header = "{\"alg\":\"ES256\",\"alg\":\"ES256\",\"kid\":\"test-key\",\"typ\":\"pcimpressz-license+jwt\"}";
            Check.Throws<DocumentValidationException>(() => f.Verifier.Verify<EntitlementDocument>(f.SignRaw(header, "{}"), LicenseDocumentTypes.Entitlement));
        });
        yield return Case("duplicate JSON payload keys deny", f =>
        {
            var payload = "{\"schemaVersion\":1,\"schemaVersion\":1}";
            Check.Throws<DocumentValidationException>(() => f.Verifier.Verify<EntitlementDocument>(f.SignRaw(Header("ES256", "test-key"), payload), LicenseDocumentTypes.Entitlement));
        });
        yield return Case("unexpected critical header denies", f =>
        {
            var header = "{\"alg\":\"ES256\",\"kid\":\"test-key\",\"typ\":\"pcimpressz-license+jwt\",\"crit\":[\"custom\"]}";
            Check.Throws<DocumentValidationException>(() => f.Verifier.Verify<EntitlementDocument>(f.SignRaw(header, "{}"), LicenseDocumentTypes.Entitlement));
        });
        yield return Case("missing required document fields deny", f =>
        {
            f.Tokens = f.Tokens! with { Entitlement = f.SignRaw(Header("ES256", "test-key"), "{}") };
            Check.Equal(LicenseStatus.Invalid, f.Evaluator().Evaluate("probe", "probe.write").Status);
        });
        yield return Case("private signing keys cannot be configured as trust anchors", f =>
            Check.Throws<ArgumentException>(() => new SignedDocumentVerifier(new Dictionary<string, string>
                { ["private"] = f.Key.ExportPkcs8PrivateKeyPem() })));
        yield return ("checkpoint persists and corrupted state denies", () =>
        {
            using var directory = new TestDirectory();
            using var crypto = new CryptoFixture();
            var path = Path.Combine(directory.Path, "checkpoint.json");
            var store = new FileLicenseCheckpointStore(path);
            var evaluator = new LicenseEvaluator(crypto.Verifier, crypto.Policy, crypto.Clock, store, () => crypto.Tokens);
            Check.True(evaluator.Evaluate("probe", "probe.write").Allowed);
            Check.Equal(1L, new FileLicenseCheckpointStore(path).Read().HighestRevisions["license-1"]);
            File.WriteAllText(path, "not-json");
            Check.Equal(LicenseStatus.StateUnavailable, evaluator.Evaluate("probe", "probe.write").Status);
            return Task.CompletedTask;
        });
    }

    private static string Header(string algorithm, string keyId) => JsonSerializer.Serialize(new
        { alg = algorithm, kid = keyId, typ = LicenseDocumentTypes.Entitlement });
    private static (string, Func<Task>) Case(string name, Action<CryptoFixture> test) => (name, () =>
    { using var fixture = new CryptoFixture(); test(fixture); return Task.CompletedTask; });
}
