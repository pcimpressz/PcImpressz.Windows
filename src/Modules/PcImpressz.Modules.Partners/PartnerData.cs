using System.Net.Mail;
using System.Text.Json;

namespace PcImpressz.Modules.Partners;

public sealed record PartnerData
{
    public string Name { get; init; } = "";
    public string TaxId { get; init; } = "";
    public string Email { get; init; } = "";
    public string Phone { get; init; } = "";
    public string Website { get; init; } = "";
    public string PostalCode { get; init; } = "";
    public string City { get; init; } = "";
    public string Address { get; init; } = "";
    public string Country { get; init; } = "HU";
    public string Notes { get; init; } = "";
    public bool IsCustomer { get; init; }
    public bool IsSupplier { get; init; }
    public bool IsManufacturer { get; init; }
    public bool IsActive { get; init; } = true;
}
public sealed record PartnerRecord(Guid Id, long Version, PartnerData Data, DateTimeOffset CreatedAt,
    string CreatedBy, DateTimeOffset UpdatedAt, string UpdatedBy, string? Source = null, int? ExternalId = null);
public sealed record PartnerAudit(Guid PartnerId, string Action, long Version, DateTimeOffset At, string Actor);
public sealed record PartnerState(int SchemaVersion, long Revision, PartnerRecord[] Partners, PartnerAudit[] Audit);
public sealed record PartnerSave(Guid CompanyId, Guid? Id, long Version, PartnerData Data);
public sealed record PartnerQuery(Guid CompanyId, string Search = "", string Filter = "all", int Page = 0, int PageSize = 50,
    string Sort = "", bool Descending = false);
public sealed record PartnerArchive(Guid CompanyId, Guid Id, long Version);
public sealed class PartnerConflictException(string message) : Exception(message);
public static class PartnerJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static T Read<T>(JsonElement input) => input.Deserialize<T>(Options)
        ?? throw new ArgumentException("Hiányzó kérésadat.");
    public static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, Options);
}
public static class PartnerValidation
{
    public static PartnerData Normalize(PartnerData data)
    {
        string Text(string? value, int maximum, string label)
        {
            value = (value ?? "").Trim();
            if (value.Length > maximum || value.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t'))
                throw new ArgumentException(label + ": érvénytelen vagy túl hosszú érték.");
            return value;
        }
        var normalized = data with
        {
            Name = Text(data.Name, 200, "Név"), TaxId = Text(data.TaxId, 50, "Adószám"),
            Email = Text(data.Email, 254, "E-mail"), Phone = Text(data.Phone, 80, "Telefon"),
            Website = Text(data.Website, 500, "Weboldal"), PostalCode = Text(data.PostalCode, 30, "Irányítószám"),
            City = Text(data.City, 150, "Település"), Address = Text(data.Address, 500, "Cím"),
            Country = Text(data.Country, 50, "Ország"), Notes = Text(data.Notes, 4000, "Megjegyzés")
        };
        if (normalized.Name.Length < 2) throw new ArgumentException("A partner neve legalább 2 karakter legyen.");
        if (normalized.Email.Length > 0 && (!MailAddress.TryCreate(normalized.Email, out var email)
            || email.Address != normalized.Email)) throw new ArgumentException("Érvénytelen e-mail-cím.");
        if (normalized.Website.Length > 0 && (!Uri.TryCreate(normalized.Website, UriKind.Absolute, out var url)
            || url.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(url.UserInfo)))
            throw new ArgumentException("A weboldal teljes http:// vagy https:// cím legyen.");
        return normalized;
    }
    public static string TaxKey(string value) => string.Concat(value.Where(char.IsAsciiLetterOrDigit)).ToUpperInvariant();
}
