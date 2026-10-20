using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcImpressz.Core.Security;

public sealed class DocumentValidationException(string message) : Exception(message);

public sealed class SignedDocumentVerifier
{
    private readonly Dictionary<string, byte[]> _publicKeys = new(StringComparer.Ordinal);
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 32
    };

    public SignedDocumentVerifier(IReadOnlyDictionary<string, string> publicKeys)
    {
        foreach (var (id, pem) in publicKeys)
        {
            Identifiers.Require(id, "key ID");
            if (!pem.TrimStart().StartsWith("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal))
                throw new ArgumentException("Only public-key PEM files are accepted.");
            using var key = ECDsa.Create();
            key.ImportFromPem(pem);
            if (key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7")
                throw new ArgumentException("ES256 requires a P-256 key.");
            _publicKeys.Add(id, key.ExportSubjectPublicKeyInfo());
        }
    }

    public T Verify<T>(string compactJws, string expectedType)
    {
        try
        {
            if (compactJws.Length > 262_144)
                throw new DocumentValidationException("Signed document is too large.");
            var parts = compactJws.Split('.');
            if (parts.Length != 3)
                throw new DocumentValidationException("Expected a compact JWS document.");
            var headerBytes = Decode(parts[0]);
            var payloadBytes = Decode(parts[1]);
            var signature = Decode(parts[2]);
            using var header = ParseStrict(headerBytes);
            var fields = header.RootElement;
            if (fields.ValueKind != JsonValueKind.Object || fields.EnumerateObject().Count() != 3
                || fields.GetProperty("alg").GetString() != "ES256"
                || fields.GetProperty("typ").GetString() != expectedType)
                throw new DocumentValidationException("Unsupported algorithm, type or header.");
            var keyId = fields.GetProperty("kid").GetString();
            if (keyId is null || !_publicKeys.TryGetValue(keyId, out var publicKey))
                throw new DocumentValidationException("Unknown signing key.");
            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(publicKey, out _);
            if (signature.Length != 64 || !verifier.VerifyData(
                    Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), signature,
                    HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw new DocumentValidationException("Invalid signature.");
            using var payload = ParseStrict(payloadBytes);
            return payload.RootElement.Deserialize<T>(JsonOptions)
                ?? throw new DocumentValidationException("Empty document.");
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or FormatException
                                   or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            throw new DocumentValidationException("Malformed or invalid signed document.");
        }
    }

    private static byte[] Decode(string input)
    {
        if (input.Length == 0 || input.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new DocumentValidationException("Invalid base64url encoding.");
        var bytes = Convert.FromBase64String(input.Replace('-', '+').Replace('_', '/')
            + new string('=', (4 - input.Length % 4) % 4));
        var canonical = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        if (canonical != input)
            throw new DocumentValidationException("Non-canonical base64url encoding.");
        return bytes;
    }

    private static JsonDocument ParseStrict(byte[] bytes)
    {
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        try { CheckDuplicates(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }

    private static void CheckDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new DocumentValidationException("Duplicate JSON property.");
                CheckDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CheckDuplicates(item);
    }
}
