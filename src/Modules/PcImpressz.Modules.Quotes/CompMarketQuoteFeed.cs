using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
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
        var entropy = Entropy(company);
        if (!File.Exists(path))
        {
            path = Path.Combine(directory, "compmarket-connection.bin");
            if (!File.Exists(path)) return null;
            entropy = Encoding.UTF8.GetBytes("pcimpressz-compmarket-v1:" + company);
        }
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length > 16384)
            throw new IOException("Hibás CompMarket-kapcsolat.");
        byte[] plain;
        try { plain = SecretBox.Unprotect(File.ReadAllBytes(path), entropy); }
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
        if (old is null || old.CustomerId != customer || old.AuthCode != auth || old.ProxyUrl != proxy || old.Enabled != request.Enabled)
            WholesaleConnections.InvalidateHealth(directory);
        var changed = old is null || old.CustomerId != customer || old.AuthCode != auth || old.ProxyUrl != proxy;
        var cache = Path.Combine(directory, "compmarket-keszlet.xml");
        if (changed && File.Exists(cache) && (File.GetAttributes(cache) & FileAttributes.ReparsePoint) == 0) File.Delete(cache);
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
        // Empty/redacted values preserve an existing secret; the service never
        // attempts a direct connection if neither an existing nor configured proxy exists.
        if (string.IsNullOrWhiteSpace(submitted))
        {
            if (!string.IsNullOrWhiteSpace(previous)) return previous;
            var configured = Environment.GetEnvironmentVariable("COMPMARKET_PROXY");
            if (string.IsNullOrWhiteSpace(configured)) configured = Environment.GetEnvironmentVariable("NAGYKER_PROXY");
            RequireProxy(configured);
            return configured!.Trim();
        }
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
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(12);
    private const long MaxXmlBytes = 512L * 1024 * 1024;

    public static async Task<object> ProxyTestAsync(CompMarketQuoteConnection connection, CancellationToken ct)
    {
        CompMarketQuoteConnectionStore.RequireProxy(connection.ProxyUrl);
        using var handler = Proxy.Handler(connection.ProxyUrl);
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        // This intentionally omits PID/authcode and never downloads the XML body.
        using var response = await client.GetAsync("https://compmarket.hu/xml/termeklista.php", HttpCompletionOption.ResponseHeadersRead, ct);
        return new { reachable = true, httpStatus = (int)response.StatusCode,
            message = "A CompMarket HTTPS-végpontja a VPS-proxyn keresztül elérhető. Az XML-t ez a teszt nem tölti le." };
    }

    public static IReadOnlyList<CompMarketQuoteItem> SearchCached(string directory, string query, decimal marginPercent, decimal vatRate)
    {
        var text = (query ?? "").Trim();
        if (text.Length < 2) throw new ArgumentException("Legalább két karakter szükséges.");
        var path = Path.Combine(directory, "compmarket-keszlet.xml");
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Még nincs letöltött CompMarket XML. A Beállítások, Külső szolgáltatók lapján indítsd a letöltést.");
        var info = new FileInfo(path);
        if (info.Length is <= 0 or > MaxXmlBytes) throw new IOException("A CompMarket XML pillanatkép érvénytelen.");
        return CompMarketQuoteImport.SearchFile(path, text, marginPercent, vatRate);
    }

    public static async Task<IReadOnlyList<CompMarketQuoteItem>> SearchAsync(string directory, CompMarketQuoteConnection connection, string query, decimal marginPercent, decimal vatRate, CancellationToken ct, HttpMessageHandler? handler = null)
    {
        if (!connection.Enabled) throw new ArgumentException("A CompMarket-kapcsolat ki van kapcsolva.");
        var text = (query ?? "").Trim();
        if (text.Length < 2) throw new ArgumentException("Legalább két karakter szükséges.");
        var path = await CatalogAsync(directory, connection, ct, handler);
        return CompMarketQuoteImport.SearchFile(path, text, marginPercent, vatRate);
    }

    private static async Task<string> CatalogAsync(string directory, CompMarketQuoteConnection connection, CancellationToken ct, HttpMessageHandler? handler, bool force = false, Action<string, long, long?>? progress = null)
    {
        CompMarketQuoteConnectionStore.RequireProxy(connection.ProxyUrl);
        var cache = Path.Combine(directory, "compmarket-keszlet.xml");
        if (!force && handler is null && File.Exists(cache) && (File.GetAttributes(cache) & FileAttributes.ReparsePoint) == 0)
        {
            var info = new FileInfo(cache);
            if (info.Length is > 0 and <= MaxXmlBytes && DateTime.UtcNow - info.LastWriteTimeUtc < CacheLifetime)
                return cache;
        }
        Directory.CreateDirectory(directory);
        var temporary = cache + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            // Retry transient proxy resets and slow upstream responses without corrupting the last good cache.
            const int maxAttempts = 3;
            for (var attempt = 1; ; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                if (File.Exists(temporary)) File.Delete(temporary);
                try
                {
                    progress?.Invoke("connecting", 0, null);
                    await DownloadToFileAsync(connection, temporary, ct, handler, progress);
                    break;
                }
                catch (Exception ex) when (attempt < maxAttempts && !ct.IsCancellationRequested &&
                    (ex is IOException || ex is HttpRequestException || ex is OperationCanceledException))
                {
                    progress?.Invoke("retrying", 0, null);
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 3), ct);
                }
            }
            progress?.Invoke("parsing", new FileInfo(temporary).Length, new FileInfo(temporary).Length);
            var count = CountProducts(temporary, progress);
            if (handler is null)
            {
                if (File.Exists(cache)) File.Replace(temporary, cache, null);
                else File.Move(temporary, cache);
                WholesaleConnections.RecordHealth(directory, true, count, "Sikeres XML-letöltés és ellenőrzés.");
                return cache;
            }
            // Tests with an injected handler still use the temporary file until caller completes.
            WholesaleConnections.RecordHealth(directory, true, count, "Sikeres XML-letöltés és ellenőrzés.");
            if (File.Exists(cache)) File.Replace(temporary, cache, null);
            else File.Move(temporary, cache);
            return cache;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            WholesaleConnections.RecordHealth(directory, false, null, ex.Message);
            throw;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static async Task<WholesaleHealth> TestAsync(string directory, CompMarketQuoteConnection connection, CancellationToken ct, HttpMessageHandler? handler = null, Action<string, long, long?>? progress = null)
    {
        if (!connection.Enabled) throw new ArgumentException("Előbb engedélyezd a CompMarket-kapcsolatot.");
        await CatalogAsync(directory, connection, ct, handler, true, progress);
        return WholesaleConnections.ReadHealth(directory)!;
    }

    private static int CountProducts(string path, Action<string, long, long?>? progress = null)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        var length = file.Length;
        using var reader = XmlReader.Create(file, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = MaxXmlBytes, MaxCharactersFromEntities = 0
        });
        var count = 0;
        try
        {
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName.Equals("cikkszam", StringComparison.OrdinalIgnoreCase)) count++;
                if (count % 500 == 0 && reader.NodeType == XmlNodeType.Element && reader.LocalName.Equals("cikkszam", StringComparison.OrdinalIgnoreCase))
                    progress?.Invoke("parsing", file.Position, length);
            }
        }
        catch (XmlException)
        {
            throw new ArgumentException("A CompMarket válasza nem terméklista XML (HTML vagy hibás dokumentum). Ellenőrizd a PID-et, az authcode-ot és a proxykapcsolatot.");
        }
        progress?.Invoke("parsing", length, length);
        if (count == 0) throw new ArgumentException("A CompMarket XML nem tartalmaz cikkszámokat. Ellenőrizd a PID-et és az authcode-ot.");
        return count;
    }

    private static async Task DownloadToFileAsync(CompMarketQuoteConnection connection, string path, CancellationToken ct, HttpMessageHandler? handler, Action<string, long, long?>? progress)
    {
        using var owned = handler is null ? Proxy.Handler(connection.ProxyUrl) : null;
        using var client = new HttpClient(owned ?? handler!, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(30) };
        var url = "https://compmarket.hu/xml/keszletlista.php?pid=" + Uri.EscapeDataString(connection.CustomerId)
            + "&authcode=" + Uri.EscapeDataString(connection.AuthCode)
            + "&type=order&fields=cikkszam,gy_cikkszam,nev,ar,keszletinfo,keszlet,vonalkod";
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                throw new IOException("A CompMarket XML HTTP hibakódja: " + (int)response.StatusCode);
            if (response.Content.Headers.ContentLength > MaxXmlBytes)
                throw new IOException("A CompMarket XML túllépi az 512 MB biztonsági korlátot.");
            var expected = response.Content.Headers.ContentLength;
            progress?.Invoke("downloading", 0, expected);
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[65536]; long total = 0; int read;
            while ((read = await input.ReadAsync(buffer, ct)) != 0)
            {
                total += read;
                if (total > MaxXmlBytes) throw new IOException("A CompMarket XML túllépi az 512 MB biztonsági korlátot.");
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                progress?.Invoke("downloading", total, expected);
            }
            if (total < 80) throw new IOException("A CompMarket túl rövid választ adott (ellenőrizd az API-azonosítókat).");
        }
        catch (HttpRequestException ex)
        {
            throw new IOException("CompMarket proxy/HTTPS kapcsolat hiba: " + ex.Message + " (kozvetlen kapcsolat tiltva)", ex);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new IOException("A CompMarket XML letoltese 30 perc alatt nem fejezodott be (proxy vagy szerver idokorlat).", ex);
        }
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

internal static class SecretBox
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
