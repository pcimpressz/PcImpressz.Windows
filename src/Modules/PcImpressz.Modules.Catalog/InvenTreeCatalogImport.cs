using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PcImpressz.Modules.Catalog;

public sealed class InvenTreeCatalogImport(CatalogRepository repository, TimeProvider time,
    Func<HttpMessageHandler>? handlerFactory = null)
{
    private sealed record Prepared(Guid Company, string Actor, long Revision, DateTimeOffset Expires, CatalogAdditions Additions);
    private readonly ConcurrentDictionary<Guid, Prepared> _prepared = new();

    public async Task<CatalogImportPreview> Preview(CatalogImportOptions options, string actor, CancellationToken ct)
    {
        var api = ValidateServer(options.ServerUrl, options.AllowLocalHttp);
        if (string.IsNullOrWhiteSpace(options.ApiToken) || options.ApiToken.Length > 1000
            || options.ApiToken.Any(char.IsControl)) throw new ArgumentException("Adj meg érvényes InvenTree API-kulcsot.");
        using var handler = handlerFactory?.Invoke() ?? new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", options.ApiToken.Trim());
        var remoteCategories = await Fetch(client, new Uri(api, "part/category/?limit=100&offset=0"), api, 2000, ct);
        var remoteParts = await Fetch(client, new Uri(api, "part/?limit=100&offset=0"), api, 5000, ct);
        var state = await repository.Snapshot(options.CompanyId, ct);
        var warnings = new List<string>();
        var source = api.AbsoluteUri.TrimEnd('/');
        var categories = state.Categories.ToList();
        var units = state.Units.ToList();
        var products = state.Products.ToList();
        var newCategories = new List<CategoryRecord>();
        var newUnits = new List<UnitRecord>();
        var newProducts = new List<ProductRecord>();
        var skipped = 0;
        var invalid = 0;
        var now = time.GetUtcNow();
        var categoryCodeByExternal = categories.Where(c => c.Source == source && c.ExternalId is > 0)
            .ToDictionary(c => c.ExternalId!.Value, c => c.Data.Code);
        foreach (var item in remoteCategories)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var externalId = item.GetProperty("pk").GetInt32();
                if (externalId <= 0) throw new ArgumentException("Érvénytelen külső azonosító.");
                var name = Text(item, "name");
                if (categories.Any(c => c.Source == source && c.ExternalId == externalId
                    || c.Data.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                { skipped++; continue; }
                var data = CatalogValidation.NormalizeCategory(new()
                {
                    Name = name, Code = "IT-" + externalId, Description = Text(item, "description"), IsActive = true
                });
                var record = new CategoryRecord(Guid.NewGuid(), 1, data, now, actor, now, actor, source, externalId);
                newCategories.Add(record); categories.Add(record); categoryCodeByExternal[externalId] = data.Code;
            }
            catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or InvalidOperationException or FormatException)
            { invalid++; }
        }
        for (var i = 0; i < newCategories.Count; i++)
        {
            var record = newCategories[i];
            var remote = remoteCategories.First(c => c.GetProperty("pk").GetInt32() == record.ExternalId);
            var parentId = Reference(remote, "parent");
            if (parentId <= 0) continue;
            if (!categoryCodeByExternal.TryGetValue(parentId, out var parentCode) || ParentCycles(categories, record.Data.Code, parentCode))
            {
                warnings.Add(parentId > 0 && categoryCodeByExternal.ContainsKey(parentId)
                    ? "Körkörös kategóriahivatkozás miatt egy szülő üresen marad."
                    : "Egy kategória szülője nem volt a letöltött jegyzékben, ezért gyökérként kerül be.");
                continue;
            }
            var updated = record with { Data = record.Data with { ParentCode = parentCode } };
            categories[categories.FindIndex(c => c.Id == record.Id)] = updated;
            newCategories[i] = updated;
        }
        foreach (var item in remoteParts)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var externalId = item.GetProperty("pk").GetInt32();
                if (externalId <= 0) throw new ArgumentException("Érvénytelen külső azonosító.");
                var sku = Text(item, "IPN");
                if (sku.Length == 0) sku = "IT-" + externalId;
                var name = Text(item, "name");
                if (products.Any(p => p.Source == source && p.ExternalId == externalId
                    || CatalogValidation.Key(p.Data.Sku) == CatalogValidation.Key(sku)
                    || p.Data.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                { skipped++; continue; }
                var unitText = Text(item, "units");
                if (unitText.Length == 0) unitText = "db";
                var unitCode = CatalogValidation.NormalizeUnit(new() { Name = unitText, Code = unitText, IsActive = true }).Code;
                if (!units.Any(u => CatalogValidation.Key(u.Data.Code) == unitCode))
                {
                    var unit = new UnitRecord(Guid.NewGuid(), 1, new() { Name = unitText, Code = unitCode, Symbol = unitText, IsActive = true },
                        now, actor, now, actor, source, null);
                    newUnits.Add(unit); units.Add(unit);
                }
                var categoryId = Reference(item, "category");
                var categoryCode = categoryId > 0 && categoryCodeByExternal.TryGetValue(categoryId, out var code) ? code : "";
                if (categoryId > 0 && categoryCode.Length == 0)
                    warnings.Add("Egy termék kategóriája hiányzik, ezért kategória nélkül kerül be.");
                var revision = Text(item, "revision");
                var data = CatalogValidation.NormalizeProduct(new()
                {
                    Name = name, Sku = sku, CategoryCode = categoryCode, UnitCode = unitCode, Revision = revision,
                    Description = Text(item, "description"),
                    IsPurchaseable = Flag(item, "purchaseable", false), IsSalable = Flag(item, "salable", false),
                    IsActive = Flag(item, "active", true)
                });
                var record = new ProductRecord(Guid.NewGuid(), 1, data, now, actor, now, actor, source, externalId);
                newProducts.Add(record); products.Add(record);
            }
            catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or InvalidOperationException or FormatException)
            { invalid++; }
        }
        foreach (var stale in _prepared.Where(p => p.Value.Expires <= time.GetUtcNow()).Select(p => p.Key))
            _prepared.TryRemove(stale, out _);
        if (_prepared.Count >= 10) throw new ArgumentException("Túl sok nyitott importelőnézet. Próbáld később.");
        var id = Guid.NewGuid();
        var additions = new CatalogAdditions(newCategories.ToArray(), newUnits.ToArray(), newProducts.ToArray());
        _prepared[id] = new(options.CompanyId, actor, state.Revision, time.GetUtcNow().AddMinutes(10), additions);
        return new(id, newCategories.Count + newUnits.Count + newProducts.Count, skipped, invalid,
            warnings.Distinct().ToArray(), newProducts.Take(10).Select(p => p.Data).ToArray());
    }

    public async Task<int> Commit(CatalogImportCommit input, string actor, CancellationToken ct)
    {
        if (!_prepared.TryGetValue(input.PreviewId, out var prepared) || prepared.Company != input.CompanyId
            || prepared.Actor != actor || prepared.Expires <= time.GetUtcNow())
            throw new ArgumentException("Az importelőnézet lejárt vagy másik céghez tartozik.");
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
    private static bool ParentCycles(List<CategoryRecord> categories, string code, string parentCode)
    {
        var cursor = parentCode;
        var guard = 0;
        while (cursor.Length > 0)
        {
            if (cursor == code || ++guard > 50) return true;
            cursor = categories.FirstOrDefault(c => c.Data.Code == cursor)?.Data.ParentCode ?? "";
        }
        return false;
    }
    private static int Reference(JsonElement value, string key)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(key, out var p)) return -1;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var number)) return number;
        return p.ValueKind == JsonValueKind.Object && p.TryGetProperty("pk", out var pk)
            && pk.ValueKind == JsonValueKind.Number && pk.TryGetInt32(out var id) ? id : -1;
    }
}
