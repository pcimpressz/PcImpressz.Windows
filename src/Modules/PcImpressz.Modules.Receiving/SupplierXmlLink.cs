using System.Net;
using System.Text.Json;

namespace PcImpressz.Modules.Receiving;

public sealed record SupplierXmlLink(string? Endpoint)
{
    public bool HasEndpoint => Endpoint is { Length: > 0 };
}

public static class SupplierXmlLinks
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(20) };

    public static SupplierXmlLink? Find(string? partnersDocument, string taxNumber)
    {
        if (string.IsNullOrWhiteSpace(partnersDocument)) return null;
        var key = ReceivingText.TaxKey(taxNumber);
        if (key.Length != 8) return null;
        JsonDocument document;
        try { document = JsonDocument.Parse(partnersDocument); }
        catch (JsonException) { return null; }
        using (document)
        {
            if (!document.RootElement.TryGetProperty("partners", out var partners) || partners.ValueKind != JsonValueKind.Array) return null;
            foreach (var row in partners.EnumerateArray())
            {
                if (!row.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) continue;
                if (ReceivingText.TaxKey(Text(data, "taxId")) != key) continue;
                if (data.TryGetProperty("isActive", out var active) && active.ValueKind == JsonValueKind.False) continue;
                return Parse(Text(data, "customFields"));
            }
        }
        return null;
    }

    public static SupplierXmlLink? Parse(string? customFields)
    {
        foreach (var raw in (customFields ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var split = raw.IndexOf('=');
            if (split <= 0) continue;
            if (!raw[..split].Trim().Equals("xml-szamla", StringComparison.OrdinalIgnoreCase)) continue;
            var value = raw[(split + 1)..].Trim();
            if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return new(Normalize(value));
            if (value.Equals("igen", StringComparison.OrdinalIgnoreCase) || value.Equals("bekapcsolva", StringComparison.OrdinalIgnoreCase) || value == "1")
                return new(null);
        }
        return null;
    }

    public static ImportedDocument MergeMissing(ImportedDocument primary, ImportedDocument extra)
    {
        var lines = primary.Lines.Select(line =>
        {
            var match = extra.Lines.FirstOrDefault(candidate => ReceivingText.Compact(candidate.Name) == ReceivingText.Compact(line.Name));
            if (match is null) return line;
            var sku = line.Sku.Length > 0 ? line.Sku : match.Sku;
            var barcode = line.Barcode.Length > 0 ? line.Barcode : match.Barcode;
            return line with { Sku = sku, Barcode = barcode };
        }).ToArray();
        return primary with { Lines = lines };
    }

    public static async Task<(ImportedDocument Document, string Note)> EnrichAsync(ImportedDocument invoice, SupplierXmlLink? link, CancellationToken ct, HttpClient? client = null)
    {
        if (link is null) return (invoice, "");
        if (!link.HasEndpoint)
            return (invoice, "A szállító XML-kapcsolata felismerve, az automatikus lekérés címe még nincs megadva.");
        if (!Allowed(link.Endpoint!))
            return (invoice, "A szállító XML-kapcsolatának címe nem hívható meg. Csak nyilvános https cím használható.");
        try
        {
            var separator = link.Endpoint!.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            var address = link.Endpoint + separator + "invoiceNumber=" + Uri.EscapeDataString(invoice.Number ?? "");
            using var response = await (client ?? Client).GetAsync(address, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.MovedPermanently or HttpStatusCode.Found)
                return (invoice, "A szállítói XML-cím átirányított, ezért nem követtük.");
            if (!response.IsSuccessStatusCode)
                return (invoice, "A szállítói XML-lekérés nem sikerült (HTTP " + (int)response.StatusCode + "). A NAV-számla megmaradt.");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var body = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, ct)) > 0)
            {
                if (body.Length + count > 1_500_000) return (invoice, "A szállítói XML nagyobb 1,5 MB-nál. A NAV-számla megmaradt.");
                body.Write(buffer, 0, count);
            }
            var text = System.Text.Encoding.UTF8.GetString(body.ToArray());
            var extra = ReceivingImport.FromNav(NavInvoiceClient.InvoiceXml(text));
            return (MergeMissing(invoice, extra), "A szállítói XML-kapcsolatból a hiányzó azonosítók pótlása lefutott.");
        }
        catch (Exception ex) when (ex is ArgumentException or HttpRequestException or TaskCanceledException or IOException)
        {
            return (invoice, "A szállítói XML-lekérés nem sikerült: " + ex.Message + " A NAV-számla megmaradt.");
        }
    }

    public static string? ReadPartners(string receivingDirectory, PcImpressz.Contracts.ICompanyTransaction? transaction)
    {
        var body = transaction?.Get("partners", "state");
        if (body is not null) return body.Length > 8_000_000 ? null : body;
        var file = Path.GetFullPath(Path.Combine(receivingDirectory, "..", "partners", "partners.json"));
        var root = Path.GetFullPath(Path.Combine(receivingDirectory, ".."));
        if (!file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(file)) return null;
        if (new FileInfo(file).Length > 8_000_000) return null;
        return File.ReadAllText(file);
    }

    private static bool Allowed(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;
        var host = uri.Host;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return false;
        if (IPAddress.TryParse(host, out var address))
        {
            if (IPAddress.IsLoopback(address)) return false;
            var bytes = address.GetAddressBytes();
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                && (bytes[0] == 10 || bytes[0] == 127 || bytes[0] == 0 || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 169 && bytes[1] == 254)))
                return false;
        }
        return true;
    }

    private static string Normalize(string value)
    {
        if (value.Length > 200) throw new ArgumentException("A szállítói XML-cím legfeljebb 200 karakter.");
        return value;
    }

    private static string Text(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
