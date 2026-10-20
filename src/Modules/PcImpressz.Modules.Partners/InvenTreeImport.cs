using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PcImpressz.Modules.Partners;

public sealed class InvenTreeImport(PartnerRepository repository, TimeProvider time,
    Func<HttpMessageHandler>? handlerFactory = null)
{
    private sealed record Prepared(Guid Company, string Actor, long Revision, DateTimeOffset Expires,
        PartnerRecord[] Additions);
    private readonly ConcurrentDictionary<Guid, Prepared> _prepared = new();

    public async Task<ImportPreview> Preview(ImportOptions options, string actor, CancellationToken ct)
    {
        var api = ValidateServer(options.ServerUrl, options.AllowLocalHttp);
        if (string.IsNullOrWhiteSpace(options.ApiToken) || options.ApiToken.Length > 1000
            || options.ApiToken.Any(char.IsControl)) throw new ArgumentException("Adj meg érvényes InvenTree API-kulcsot.");
        using var handler = handlerFactory?.Invoke() ?? new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", options.ApiToken.Trim());
        var companies = await Fetch(client, new Uri(api, "company/?limit=100&offset=0"), api, 5000, ct);
        var warnings = new List<string>();
        JsonElement[] addresses;
        try { addresses = await Fetch(client, new Uri(api, "company/address/?limit=100&offset=0"), api, 10000, ct); }
        catch (HttpRequestException)
        { addresses = []; warnings.Add("A címjegyzék nem volt olvasható. Az alapadatok importálhatók, a címeket ellenőrizd."); }
        var state = await repository.Snapshot(options.CompanyId, ct);
        var records = state.Partners.ToList();
        var additions = new List<PartnerRecord>();
        var skipped = 0; var invalid = 0;
        var source = api.AbsoluteUri.TrimEnd('/');
        foreach (var item in companies)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var externalId = item.GetProperty("pk").GetInt32();
                if (externalId <= 0) throw new ArgumentException("Érvénytelen külső azonosító.");
                var address = addresses.Where(a => Number(a, "company") == externalId)
                    .OrderByDescending(a => Flag(a, "primary", false)).FirstOrDefault();
                var data = PartnerValidation.Normalize(new()
                {
                    Name = Text(item, "name"), TaxId = Text(item, "tax_id"), Email = Text(item, "email"),
                    Phone = Text(item, "phone"), Website = Text(item, "website"),
                    IsCustomer = Flag(item, "is_customer", false), IsSupplier = Flag(item, "is_supplier", false),
                    IsManufacturer = Flag(item, "is_manufacturer", false), IsActive = Flag(item, "active", true),
                    Notes = Text(item, "description"), Country = Text(address, "country"),
                    PostalCode = Text(address, "postal_code"), City = Text(address, "postal_city"),
                    Address = string.Join(", ", new[] { Text(address, "line1"), Text(address, "line2") }.Where(s => s.Length > 0))
                });
                var tax = PartnerValidation.TaxKey(data.TaxId);
                if (records.Any(p => p.Source == source && p.ExternalId == externalId
                    || tax.Length > 0 && PartnerValidation.TaxKey(p.Data.TaxId) == tax
                    || p.Data.Name.Equals(data.Name, StringComparison.OrdinalIgnoreCase)
                       && p.Data.Email.Equals(data.Email, StringComparison.OrdinalIgnoreCase)))
                { skipped++; continue; }
                var now = time.GetUtcNow();
                var record = new PartnerRecord(Guid.NewGuid(), 1, data, now, actor, now, actor, source, externalId);
                additions.Add(record); records.Add(record);
            }
            catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or InvalidOperationException or FormatException)
            { invalid++; }
        }
        foreach (var stale in _prepared.Where(p => p.Value.Expires <= time.GetUtcNow()).Select(p => p.Key))
            _prepared.TryRemove(stale, out _);
        if (_prepared.Count >= 10) throw new ArgumentException("Túl sok nyitott importelőnézet. Próbáld később.");
        var id = Guid.NewGuid();
        _prepared[id] = new(options.CompanyId, actor, state.Revision, time.GetUtcNow().AddMinutes(10), additions.ToArray());
        return new(id, additions.Count, skipped, invalid, warnings.ToArray(), additions.Take(10).Select(p => p.Data).ToArray());
    }

    public async Task<int> Commit(ImportCommit input, string actor, CancellationToken ct)
    {
        if (!_prepared.TryGetValue(input.PreviewId, out var prepared) || prepared.Company != input.CompanyId
            || prepared.Actor != actor || prepared.Expires <= time.GetUtcNow())
            throw new ArgumentException("Az importelőnézet lejárt vagy másik céghez tartozik.");
        // Remove before the write so two commit requests cannot apply the same preview twice.
        if (!_prepared.TryRemove(input.PreviewId, out _)) throw new ArgumentException("Az előnézet már fel lett használva.");
        return await repository.Import(input.CompanyId, prepared.Additions, prepared.Revision, actor, ct);
    }

    public static Uri ValidateServer(string text, bool allowLocalHttp)
    {
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Teljes InvenTree http(s) címet adj meg.");
        var privateIp = IPAddress.TryParse(uri.Host, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            && (ip.GetAddressBytes()[0] is 10 or 127
                || ip.GetAddressBytes()[0] == 192 && ip.GetAddressBytes()[1] == 168
                || ip.GetAddressBytes()[0] == 172 && ip.GetAddressBytes()[1] is >= 16 and <= 31);
        if (uri.Scheme != "https" && !(uri.Scheme == "http" && allowLocalHttp && (privateIp || uri.Host == "localhost")))
            throw new ArgumentException("HTTPS cím szükséges. Helyi HTTP kapcsolathoz külön engedélyezd a nem titkosított LAN módot.");
        var root = uri.AbsoluteUri.TrimEnd('/') + "/";
        return new(root.EndsWith("/api/", StringComparison.Ordinal) ? root : root + "api/");
    }

    private static async Task<JsonElement[]> Fetch(HttpClient client, Uri first, Uri api, int maximum, CancellationToken ct)
    {
        var result = new List<JsonElement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Uri? current = first;
        while (current is not null)
        {
            if (current.Scheme != api.Scheme || current.Host != api.Host || current.Port != api.Port
                || !current.AbsolutePath.StartsWith(api.AbsolutePath, StringComparison.Ordinal)
                || !string.IsNullOrEmpty(current.UserInfo) || !seen.Add(current.AbsoluteUri))
                throw new HttpRequestException("A lapozás idegen vagy ismétlődő címre mutat.");
            using var response = await client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new HttpRequestException("Az API-kulcs nem rendelkezik a szükséges olvasási jogosultsággal.");
            if (!response.IsSuccessStatusCode) throw new HttpRequestException("Az InvenTree lekérés nem sikerült (HTTP " + (int)response.StatusCode + ").");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var bytes = new MemoryStream(); var buffer = new byte[8192]; int count;
            while ((count = await stream.ReadAsync(buffer, ct)) > 0)
            {
                if (bytes.Length + count > 4 * 1024 * 1024) throw new HttpRequestException("Az API-válasz túl nagy.");
                bytes.Write(buffer, 0, count);
            }
            using var document = JsonDocument.Parse(bytes.ToArray());
            var root = document.RootElement;
            var items = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("results");
            foreach (var item in items.EnumerateArray()) result.Add(item.Clone());
            if (result.Count > maximum) throw new HttpRequestException("Az importrekordok korlátja elérve. Kisebb adag szükséges.");
            current = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("next", out var next)
                && next.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(next.GetString())
                ? new Uri(api, next.GetString()) : null;
        }
        return result.ToArray();
    }
    private static string Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
    private static bool Flag(JsonElement value, string key, bool fallback) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(key, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False ? p.GetBoolean() : fallback;
    private static int Number(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(key, out var p) && p.TryGetInt32(out var n) ? n : -1;
}
