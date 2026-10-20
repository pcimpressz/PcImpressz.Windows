using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PcImpressz.Modules.Receiving;

public sealed record NavConnectionStatus(bool Configured, string TaxNumber, string UserName, bool TestMode);
public sealed record NavConnectionSave(Guid CompanyId, string? TaxNumber, string? UserName, string? Password,
    string? SignKey, string? ExchangeKey, bool TestMode);
public sealed record NavConnectionSecrets(string TaxNumber, string UserName, string Password, string SignKey, string ExchangeKey, bool TestMode);

public static class NavConnectionStore
{
    public const string FileName = "nav-connection.bin";
    private static readonly byte[] Magic = "PCINAV1\n"u8.ToArray();

    public static NavConnectionStatus Status(string directory, Guid companyId)
    {
        var secrets = Read(directory, companyId);
        return secrets is null
            ? new(false, "", "", false)
            : new(true, secrets.TaxNumber, secrets.UserName, secrets.TestMode);
    }

    public static NavConnectionSecrets? Read(string directory, Guid companyId)
    {
        var file = Path.Combine(directory, FileName);
        if (!File.Exists(file)) return null;
        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("A NAV-kapcsolat hivatkozás, ezért nem olvasható.");
        var stored = File.ReadAllBytes(file);
        if (stored.Length is < 16 or > 16 * 1024 || !stored.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new IOException("A NAV-kapcsolat sérült.");
        byte[] plain;
        try { plain = WindowsSecret.Unprotect(stored.AsSpan(Magic.Length).ToArray(), Entropy(companyId)); }
        catch (CryptographicException) { throw new IOException("A NAV-kapcsolat ezen a Windows-felhasználón nem nyitható meg."); }
        try
        {
            var body = JsonSerializer.Deserialize<SecretBody>(plain, ReceivingJson.Options)
                ?? throw new IOException("A NAV-kapcsolat sérült.");
            if (body.SchemaVersion != 1) throw new IOException("A NAV-kapcsolat verziója ismeretlen.");
            return new(body.TaxNumber, body.UserName, body.Password, body.SignKey, body.ExchangeKey, body.TestMode);
        }
        catch (JsonException) { throw new IOException("A NAV-kapcsolat sérült."); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public static NavConnectionStatus Save(string directory, Guid companyId, NavConnectionSave request)
    {
        var current = Read(directory, companyId);
        var tax = Tax(request.TaxNumber);
        var user = User(request.UserName);
        var password = Secret(request.Password, current?.Password, "jelszó", 1);
        var sign = Secret(request.SignKey, current?.SignKey, "aláírókulcs", 8);
        var exchange = Secret(request.ExchangeKey, current?.ExchangeKey, "cserekulcs", 8);
        var body = new SecretBody(1, tax, user, password, sign, exchange, request.TestMode);
        var plain = JsonSerializer.SerializeToUtf8Bytes(body, ReceivingJson.Options);
        byte[] protectedBytes;
        try { protectedBytes = WindowsSecret.Protect(plain, Entropy(companyId)); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        var payload = new byte[Magic.Length + protectedBytes.Length];
        Magic.CopyTo(payload, 0);
        protectedBytes.CopyTo(payload, Magic.Length);
        Write(directory, payload);
        return new(true, tax, user, request.TestMode);
    }

    private static void Write(string directory, byte[] payload)
    {
        if ((Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0))
            throw new IOException("A cégmappa hivatkozás, ezért a NAV-kapcsolat nem menthető.");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, FileName);
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(payload); stream.Flush(true);
            }
            if (File.Exists(file)) File.Replace(temporary, file, destinationBackupFileName: null);
            else File.Move(temporary, file);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static byte[] Entropy(Guid companyId) =>
        Encoding.UTF8.GetBytes("pcimpressz-nav-v1:" + companyId.ToString("D"));

    private static string Tax(string? value)
    {
        var digits = ReceivingText.Digits(value);
        if (digits.Length is 11) digits = digits[..8];
        if (digits.Length != 8) throw new ArgumentException("Az adószám első nyolc számjegye kell. Példa: 12345678-2-42.");
        return digits;
    }

    private static string User(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length is < 1 or > 30 || text.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)))
            throw new ArgumentException("A technikai felhasználó 1–30 karakter, szóköz nélkül.");
        return text;
    }

    private static string Secret(string? incoming, string? current, string label, int minimum)
    {
        var text = incoming ?? "";
        if (text.Length == 0) text = current ?? "";
        if (text.Length < minimum || text.Length > 200 || text.Any(char.IsControl) || text.Any(char.IsWhiteSpace))
            throw new ArgumentException("A " + label + " " + minimum + "–200 karakter, szóköz nélkül. Üresen hagyva a mentett érték marad.");
        return text;
    }

    private sealed record SecretBody(int SchemaVersion, string TaxNumber, string UserName, string Password, string SignKey, string ExchangeKey, bool TestMode);
}

internal static class WindowsSecret
{
    private const uint UiForbidden = 0x1;

    public static byte[] Protect(byte[] plain, byte[] entropy)
    {
        var input = default(DataBlob);
        var extra = default(DataBlob);
        var output = default(DataBlob);
        try
        {
            input = Allocate(plain);
            extra = Allocate(entropy);
            if (!CryptProtectData(ref input, "PcImpressz NAV", ref extra, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output))
                throw new CryptographicException("A NAV-kapcsolat titkosítása nem sikerült.");
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            Free(ref input);
            Free(ref extra);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }

    public static byte[] Unprotect(byte[] cipher, byte[] entropy)
    {
        var input = default(DataBlob);
        var extra = default(DataBlob);
        var output = default(DataBlob);
        try
        {
            input = Allocate(cipher);
            extra = Allocate(entropy);
            if (!CryptUnprotectData(ref input, IntPtr.Zero, ref extra, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output))
                throw new CryptographicException("A NAV-kapcsolat visszafejtése nem sikerült.");
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            Free(ref input);
            Free(ref extra);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }

    private static DataBlob Allocate(byte[] bytes)
    {
        var data = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, data, bytes.Length);
        return new() { Size = bytes.Length, Data = data };
    }

    private static void Free(ref DataBlob blob)
    {
        if (blob.Data == IntPtr.Zero) return;
        if (blob.Size > 0)
        {
            var zeros = new byte[blob.Size];
            Marshal.Copy(zeros, 0, blob.Data, zeros.Length);
        }
        Marshal.FreeHGlobal(blob.Data);
        blob = default;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, uint flags, ref DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, uint flags, ref DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);
}
