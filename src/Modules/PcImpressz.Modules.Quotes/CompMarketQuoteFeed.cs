using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PcImpressz.Contracts;

namespace PcImpressz.Modules.Quotes;

public sealed record CompMarketConnectionSave(Guid CompanyId, string CustomerId, string? AuthCode, string ProxyUrl, bool Enabled);
public sealed record CompMarketQuoteConnection(string CustomerId, string AuthCode, string ProxyUrl, bool Enabled);

// The wholesale checks the caller IP. The shop address is rejected, so an empty
// proxy must not open a direct connection. The fixed address is the VPS Tinyproxy.
public static class CompMarketQuoteConnectionStore
{
    public const string FixedIp = "217.113.49.52";
    private static byte[] Entropy(Guid id) => Encoding.UTF8.GetBytes("pcimpressz-compmarket-quote-v1:" + id);

    public static CompMarketQuoteConnection? Read(string directory, Guid company)
    {
        var path = Path.Combine(directory, "compmarket-quote.bin");
        if (!File.Exists(path)) return null;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length > 16384)
            throw new IOException("Hibás CompMarket-kapcsolat.");
        byte[] plain;
        try { plain = SecretBox.Unprotect(File.ReadAllBytes(path), Entropy(company)); }
        catch (CryptographicException) { throw new IOException("A CompMarket-kapcsolat másik Windows-fiókhoz tartozik."); }
        try { return JsonSerializer.Deserialize<CompMarketQuoteConnection>(plain, CompanyDocuments.Json) ?? throw new IOException("Hibás CompMarket-kapcsolat."); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public static object Status(string directory, Guid company)
    {
        var current = Read(directory, company);
        return new
        {
            configured = current is not null,
            customerId = current?.CustomerId ?? "",
            proxyUrl = current is null ? "" : Redact(current.ProxyUrl),
            enabled = current?.Enabled ?? false,
            fixedIp = FixedIp
        };
    }

    public static object Save(string directory, CompMarketConnectionSave request)
    {
        var old = Read(directory, request.CompanyId);
        var auth = string.IsNullOrWhiteSpace(request.AuthCode) ? old?.AuthCode ?? "" : request.AuthCode.Trim();
        var customer = request.CustomerId.Trim();
        if (customer.Length is < 1 or > 100 || auth.Length is < 1 or > 500 || customer.Any(char.IsControl) || auth.Any(char.IsControl))
            throw new ArgumentException("Ügyfélazonosító és hitelesítő kód szükséges.");
        var proxy = MergeProxy(request.ProxyUrl, old?.ProxyUrl);
        var plain = JsonSerializer.SerializeToUtf8Bytes(new CompMarketQuoteConnection(customer, auth, proxy, request.Enabled), CompanyDocuments.Json);
        byte[] cipher;
        try { cipher = SecretBox.Protect(plain, Entropy(request.CompanyId)); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "compmarket-quote.bin");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("A kapcsolatfájl hivatkozás.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, cipher);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        var cache = Path.Combine(directory, "compmarket-keszlet.xml");
        if (File.Exists(cache) && (File.GetAttributes(cache) & FileAttributes.ReparsePoint) == 0) File.Delete(cache);
        return Status(directory, request.CompanyId);
    }

    public static void RequireProxy(string? url)
    {
        var text = (url ?? "").Trim();
        if (text.Length == 0)
            throw new ArgumentException("Nincs VPS-proxy. A CompMarket a " + FixedIp + " fix IP-t várja. Közvetlen kapcsolattal nem próbálkozunk.");
        if (text.Length > 500 || text.Any(char.IsControl) || !Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host))
            throw new ArgumentException("A nagyker-proxy csak http:// vagy https:// cím lehet, például http://felhasznalo:jelszo@" + FixedIp + ":8888");
    }

    public static string Redact(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "";
        var bare = new UriBuilder(uri) { UserName = "", Password = "" };
        return bare.Uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
    }

    public static string MergeProxy(string submitted, string? previous)
    {
        RequireProxy(submitted);
        var next = new Uri(submitted.Trim());
        if (string.IsNullOrEmpty(next.UserInfo) && previous is not null && Uri.TryCreate(previous, UriKind.Absolute, out var old) && !string.IsNullOrEmpty(old.UserInfo) && SameEndpoint(old, next))
            return previous;
        return submitted.Trim();
    }

    private static bool SameEndpoint(Uri left, Uri right) =>
        left.Scheme.Equals(right.Scheme, StringComparison.OrdinalIgnoreCase)
        && left.Host.Equals(right.Host, StringComparison.OrdinalIgnoreCase)
        && left.Port == right.Port;
}

public static class CompMarketQuoteFeed
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    public static async Task<IReadOnlyList<CompMarketQuoteItem>> SearchAsync(string directory, CompMarketQuoteConnection connection, string query, decimal marginPercent, decimal vatRate, CancellationToken ct, HttpMessageHandler? handler = null)
    {
        if (!connection.Enabled) throw new ArgumentException("A CompMarket-kapcsolat ki van kapcsolva a beállításokban.");
        CompMarketQuoteConnectionStore.RequireProxy(connection.ProxyUrl);
        var text = (query ?? "").Trim();
        if (text.Length < 2) throw new ArgumentException("A nagykeres kereséshez legalább 2 karakter kell.");
        var bytes = await CatalogAsync(directory, connection, ct, handler);
        return CompMarketQuoteImport.Search(bytes, text, marginPercent, vatRate);
    }

    private static async Task<byte[]> CatalogAsync(string directory, CompMarketQuoteConnection connection, CancellationToken ct, HttpMessageHandler? handler)
    {
        var cache = Path.Combine(directory, "compmarket-keszlet.xml");
        if (handler is null && File.Exists(cache) && (File.GetAttributes(cache) & FileAttributes.ReparsePoint) == 0)
        {
            var info = new FileInfo(cache);
            if (info.Length is > 0 and <= 8_000_000 && DateTime.UtcNow - info.LastWriteTimeUtc < CacheLifetime)
                return await File.ReadAllBytesAsync(cache, ct);
        }
        var fresh = await DownloadAsync(connection, ct, handler);
        if (handler is null)
        {
            Directory.CreateDirectory(directory);
            var temporary = cache + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(temporary, fresh, ct);
            if (File.Exists(cache)) File.Replace(temporary, cache, null);
            else File.Move(temporary, cache);
        }
        return fresh;
    }

    private static async Task<byte[]> DownloadAsync(CompMarketQuoteConnection connection, CancellationToken ct, HttpMessageHandler? handler)
    {
        using var owned = handler is null ? Proxy.Handler(connection.ProxyUrl) : null;
        using var client = new HttpClient(owned ?? handler!, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(25) };
        var url = "https://compmarket.hu/xml/keszletlista.php?pid=" + Uri.EscapeDataString(connection.CustomerId)
            + "&authcode=" + Uri.EscapeDataString(connection.AuthCode)
            + "&type=order&fields=cikkszam,gy_cikkszam,nev,ar,keszletinfo,keszlet,vonalkod";
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                throw new ArgumentException("A CompMarket nem adta vissza a készletlistát (HTTP " + (int)response.StatusCode + "). Közvetlen kapcsolattal nem próbálkozunk.");
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            using var memory = new MemoryStream();
            var buffer = new byte[8192];
            int length;
            while ((length = await input.ReadAsync(buffer, ct)) > 0)
            {
                if (memory.Length + length > 8_000_000) throw new ArgumentException("A CompMarket készletlista túl nagy.");
                memory.Write(buffer, 0, length);
            }
            var body = memory.ToArray();
            if (body.Length < 80) throw new ArgumentException("A CompMarket üres választ adott. Ellenőrizd a proxyt és a belépési adatokat.");
            var head = Encoding.ASCII.GetString(body, 0, Math.Min(20, body.Length)).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            if (head.StartsWith("<html", StringComparison.OrdinalIgnoreCase) || head.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A CompMarket nem terméklistát adott. A kérés a " + CompMarketQuoteConnectionStore.FixedIp + " VPS-proxyn ment ki.");
            return body;
        }
        catch (HttpRequestException) { throw new IOException("A CompMarket a VPS-proxyn keresztül nem elérhető. Közvetlen kapcsolattal nem próbálkozunk."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new IOException("A CompMarket a VPS-proxyn keresztül nem válaszolt időben. Közvetlen kapcsolattal nem próbálkozunk."); }
    }
}

internal static class Proxy
{
    public static HttpClientHandler Handler(string proxyUrl)
    {
        CompMarketQuoteConnectionStore.RequireProxy(proxyUrl);
        var uri = new Uri(proxyUrl.Trim());
        var bare = new UriBuilder(uri) { UserName = "", Password = "" }.Uri;
        var proxy = new WebProxy(bare) { BypassProxyOnLocal = false };
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var raw = Uri.UnescapeDataString(uri.UserInfo);
            var split = raw.Split(':', 2);
            proxy.Credentials = new NetworkCredential(split[0], split.Length == 2 ? split[1] : "");
        }
        return new HttpClientHandler { Proxy = proxy, UseProxy = true, AllowAutoRedirect = false, UseCookies = false };
    }
}

file static class SecretBox
{
    private const uint UiForbidden = 0x1;
    public static byte[] Protect(byte[] plain, byte[] entropy) => Transform(plain, entropy, true);
    public static byte[] Unprotect(byte[] cipher, byte[] entropy) => Transform(cipher, entropy, false);

    private static byte[] Transform(byte[] inputBytes, byte[] entropyBytes, bool protect)
    {
        var input = default(DataBlob);
        var extra = default(DataBlob);
        var output = default(DataBlob);
        try
        {
            input = Allocate(inputBytes);
            extra = Allocate(entropyBytes);
            var ok = protect
                ? CryptProtectData(ref input, "PcImpressz CompMarket", ref extra, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output)
                : CryptUnprotectData(ref input, IntPtr.Zero, ref extra, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output);
            if (!ok) throw new CryptographicException("A CompMarket-kapcsolat titkosítása nem sikerült.");
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            if (input.Data != IntPtr.Zero) Marshal.FreeHGlobal(input.Data);
            if (extra.Data != IntPtr.Zero) Marshal.FreeHGlobal(extra.Data);
            if (output.Data != IntPtr.Zero) Marshal.FreeHGlobal(output.Data);
        }
    }

    private static DataBlob Allocate(byte[] bytes)
    {
        var data = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, data, bytes.Length);
        return new DataBlob { Size = bytes.Length, Data = data };
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, uint flags, ref DataBlob dataOut);
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, uint flags, ref DataBlob dataOut);

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Size; public IntPtr Data; }
}
