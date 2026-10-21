using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PcImpressz.Modules.Receiving;

public sealed record NavDigestInvoice(string Number, string SupplierTaxNumber, string SupplierName, string IssuedOn, string? DueOn, string Operation);
public sealed record NavDigestPage(int CurrentPage, int AvailablePage, NavDigestInvoice[] Invoices);

public static class NavInvoiceClient
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly XNamespace Api = "http://schemas.nav.gov.hu/OSA/3.0/api";
    private static readonly XNamespace Common = "http://schemas.nav.gov.hu/NTCA/1.0/common";
    private const int ResponseLimit = 8 * 1024 * 1024;

    public static XDocument DigestRequest(NavConnectionSecrets secrets, DateOnly from, DateOnly to, int page, DateTimeOffset at, string id) =>
        Envelope(secrets, at, id, "QueryInvoiceDigestRequest",
            new XElement(Api + "page", page),
            new XElement(Api + "invoiceDirection", "INBOUND"),
            new XElement(Api + "invoiceQueryParams",
                new XElement(Api + "mandatoryQueryParams",
                    new XElement(Api + "invoiceIssueDate",
                        new XElement(Api + "dateFrom", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                        new XElement(Api + "dateTo", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))))));

    public static XDocument DataRequest(NavConnectionSecrets secrets, string invoiceNumber, string supplierTaxNumber, DateTimeOffset at, string id) =>
        Envelope(secrets, at, id, "QueryInvoiceDataRequest",
            new XElement(Api + "invoiceNumberQuery",
                new XElement(Api + "invoiceNumber", invoiceNumber),
                new XElement(Api + "invoiceDirection", "INBOUND"),
                new XElement(Api + "supplierTaxNumber", NavTaxpayerClient.TaxNumber(supplierTaxNumber))));

    public static async Task<NavDigestPage> QueryDigestAsync(NavConnectionSecrets secrets, DateOnly from, DateOnly to, int page, CancellationToken ct, HttpClient? client = null)
    {
        var id = RequestId();
        var xml = DigestRequest(secrets, from, to, page, DateTimeOffset.UtcNow, id);
        var text = await PostAsync(client ?? Client, Root(secrets) + "queryInvoiceDigest", xml, ct);
        return ParseDigest(text, id);
    }

    public static async Task<string> QueryInvoiceAsync(NavConnectionSecrets secrets, string invoiceNumber, string supplierTaxNumber, CancellationToken ct, HttpClient? client = null)
    {
        var id = RequestId();
        var xml = DataRequest(secrets, invoiceNumber, supplierTaxNumber, DateTimeOffset.UtcNow, id);
        var text = await PostAsync(client ?? Client, Root(secrets) + "queryInvoiceData", xml, ct);
        return InvoiceXml(text, id);
    }

    public static NavDigestPage ParseDigest(string text, string? requestId = null)
    {
        var document = Load(text);
        EnsureOk(document, requestId, "QueryInvoiceDigestResponse");
        var invoices = document.Descendants().Where(element => element.Name.LocalName == "invoiceDigest").Select(digest => new NavDigestInvoice(
            Value(digest, "invoiceNumber"),
            Value(digest, "supplierTaxNumber"),
            Value(digest, "supplierName"),
            Value(digest, "invoiceIssueDate"),
            Blank(Value(digest, "paymentDate")),
            Value(digest, "invoiceOperation"))).Where(invoice => invoice.Number.Length > 0).ToArray();
        return new(Number(document, "currentPage", 1), Number(document, "availablePage", 1), invoices);
    }

    public static string InvoiceXml(string text, string? requestId = null)
    {
        var document = Load(text);
        if (document.Descendants().Any(element => element.Name.LocalName == "invoiceData"))
        {
            EnsureOk(document, requestId, "QueryInvoiceDataResponse");
            var payload = document.Descendants().First(element => element.Name.LocalName == "invoiceData").Value.Trim();
            if (payload.Length == 0) throw new ArgumentException("A NAV nem adott számlaadatot.");
            byte[] raw;
            try { raw = Convert.FromBase64String(payload); }
            catch (FormatException) { throw new ArgumentException("A NAV számlaadata nem olvasható."); }
            var compressed = Value(document.Root!, "compressedContentIndicator").Equals("true", StringComparison.OrdinalIgnoreCase)
                || raw.Length > 2 && raw[0] == 0x1F && raw[1] == 0x8B;
            if (compressed)
            {
                using var input = new MemoryStream(raw);
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                var buffer = new byte[8192];
                int count;
                while ((count = gzip.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + count > ResponseLimit) throw new ArgumentException("A NAV-számla kibontva túl nagy.");
                    output.Write(buffer, 0, count);
                }
                raw = output.ToArray();
            }
            if (raw.Length is < 1 or > ResponseLimit) throw new ArgumentException("A NAV-számla üres vagy túl nagy.");
            return Encoding.UTF8.GetString(raw);
        }
        return text;
    }

    private static XDocument Envelope(NavConnectionSecrets secrets, DateTimeOffset at, string id, string root, params XElement[] body)
    {
        if (id.Length is < 1 or > 30 || id.Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw new ArgumentException("Érvénytelen NAV-kérésazonosító.");
        var utc = at.UtcDateTime;
        var signature = Convert.ToHexString(NavSha3.Hash(Encoding.UTF8.GetBytes(
            id + utc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + secrets.SignKey)));
        var content = new List<XElement>
        {
            new(Common + "header",
                new XElement(Common + "requestId", id),
                new XElement(Common + "timestamp", utc.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture) + "Z"),
                new XElement(Common + "requestVersion", "3.0"),
                new XElement(Common + "headerVersion", "1.0")),
            new(Common + "user",
                new XElement(Common + "login", secrets.UserName),
                new XElement(Common + "passwordHash", new XAttribute("cryptoType", "SHA-512"), Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(secrets.Password)))),
                new XElement(Common + "taxNumber", secrets.TaxNumber),
                new XElement(Common + "requestSignature", new XAttribute("cryptoType", "SHA3-512"), signature)),
            new(Api + "software",
                new XElement(Api + "softwareId", "HU00000000RAKTAR01"),
                new XElement(Api + "softwareName", "PC-Impressz Windows"),
                new XElement(Api + "softwareOperation", "LOCAL_SOFTWARE"),
                new XElement(Api + "softwareMainVersion", "0.4.7"),
                new XElement(Api + "softwareDevName", "PC-Impressz"),
                new XElement(Api + "softwareDevContact", "info@pcimpressz.hu"))
        };
        content.AddRange(body);
        return new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement(Api + root, new XAttribute(XNamespace.Xmlns + "common", Common), content));
    }

    private static async Task<string> PostAsync(HttpClient client, string url, XDocument xml, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(xml.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "application/xml")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var text = await ReadAsync(response, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new ArgumentException("A NAV elutasította a technikai felhasználót. Az admin ellenőrizze a NAV-kapcsolatot.");
        if (!response.IsSuccessStatusCode)
            throw new ArgumentException("A NAV-lekérdezés nem sikerült (HTTP " + (int)response.StatusCode + ").");
        return text;
    }

    private static async Task<string> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (body.Length + count > ResponseLimit) throw new ArgumentException("Túl nagy NAV-válasz.");
            body.Write(buffer, 0, count);
        }
        return Encoding.UTF8.GetString(body.ToArray());
    }

    private static XDocument Load(string text)
    {
        if (text.Length > ResponseLimit) throw new ArgumentException("Túl nagy NAV-válasz.");
        using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = ResponseLimit
        });
        try { return XDocument.Load(reader); }
        catch (XmlException) { throw new ArgumentException("A NAV-válasz nem olvasható XML."); }
    }

    private static void EnsureOk(XDocument document, string? requestId, string expectedRoot)
    {
        if (document.Root?.Name.LocalName != expectedRoot) throw new ArgumentException("A NAV-válasz típusa nem megfelelő.");
        if (requestId is not null)
        {
            var echoed = Value(document.Root, "requestId");
            if (echoed.Length > 0 && echoed != requestId) throw new ArgumentException("Eltérő NAV-válaszazonosító.");
        }
        var code = Value(document.Root, "funcCode");
        if (code.Length > 0 && code != "OK")
            throw new ArgumentException("A NAV nem teljesítette a lekérdezést. Hibakód: " + First(Value(document.Root, "errorCode"), Value(document.Root, "message"), code));
    }

    private static string Root(NavConnectionSecrets secrets) =>
        (secrets.TestMode ? "https://api-test.onlineszamla.nav.gov.hu" : "https://api.onlineszamla.nav.gov.hu") + "/invoiceService/v3/";

    private static string RequestId() => "N" + Guid.NewGuid().ToString("N")[..29].ToUpperInvariant();
    private static string Value(XElement node, string name) =>
        node.DescendantsAndSelf().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim() ?? "";
    private static int Number(XDocument document, string name, int fallback) =>
        int.TryParse(Value(document.Root!, name), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : fallback;
    private static string? Blank(string value) => value.Length == 0 ? null : value;
    private static string First(params string[] values) => values.FirstOrDefault(value => value.Length > 0) ?? "";
}
