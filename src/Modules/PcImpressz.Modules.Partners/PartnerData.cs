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
    public bool IsProspect { get; init; }
    public bool IsPartner { get; init; }
    public bool IsCompetitor { get; init; }
    public bool IsActive { get; init; } = true;
    public string EuTaxId { get; init; } = "";
    public string RegistrationNumber { get; init; } = "";
    public string BankAccounts { get; init; } = "";
    public string BillingAddress { get; init; } = "";
    public string ShippingAddress { get; init; } = "";
    public string Sites { get; init; } = "";
    public string Industry { get; init; } = "";
    public string EmployeeCount { get; init; } = "";
    public string RevenueCategory { get; init; } = "";
    public string AbcClass { get; init; } = "";
    public string LinkedIn { get; init; } = "";
    public string Lifecycle { get; init; } = "";
    public string Owner { get; init; } = "";
    public string LeadSource { get; init; } = "";
    public decimal? CreditLimit { get; init; }
    public Guid? ParentId { get; init; }
    public string ParentName { get; init; } = "";
    public string Tags { get; init; } = "";
    public string CustomFields { get; init; } = "";
    public bool MarketingConsent { get; init; }
    public bool NewsletterConsent { get; init; }
    public string ConsentOn { get; init; } = "";
    public bool Forget { get; init; }
}
public sealed record ContactData
{
    public string Name { get; init; } = "";
    public string Salutation { get; init; } = "";
    public string Title { get; init; } = "";
    public string DecisionLevel { get; init; } = "";
    public string Email { get; init; } = "";
    public string SecondaryEmail { get; init; } = "";
    public string Phone { get; init; } = "";
    public string Mobile { get; init; } = "";
    public string LinkedIn { get; init; } = "";
    public string Channel { get; init; } = "";
    public string Language { get; init; } = "";
    public string Birthday { get; init; } = "";
    public string Companies { get; init; } = "";
    public string Tags { get; init; } = "";
    public string Notes { get; init; } = "";
    public bool MarketingConsent { get; init; }
    public bool NewsletterConsent { get; init; }
    public string ConsentOn { get; init; } = "";
    public bool IsActive { get; init; } = true;
    public bool Forget { get; init; }
}
public sealed record ActivityData
{
    public string Kind { get; init; } = "Jegyzet";
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string At { get; init; } = "";
    public Guid? PartnerId { get; init; }
    public string PartnerName { get; init; } = "";
    public Guid? ContactId { get; init; }
    public string ContactName { get; init; } = "";
    public bool IsActive { get; init; } = true;
}
public sealed record PartnerRecord(Guid Id, long Version, PartnerData Data, DateTimeOffset CreatedAt,
    string CreatedBy, DateTimeOffset UpdatedAt, string UpdatedBy, string? Source = null, int? ExternalId = null);
public sealed record ContactRecord(Guid Id, long Version, ContactData Data, DateTimeOffset CreatedAt,
    string CreatedBy, DateTimeOffset UpdatedAt, string UpdatedBy);
public sealed record ActivityRecord(Guid Id, long Version, ActivityData Data, DateTimeOffset CreatedAt,
    string CreatedBy, DateTimeOffset UpdatedAt, string UpdatedBy);
public sealed record PartnerLink(Guid ContactId, Guid PartnerId);
public sealed record PartnerAudit(Guid PartnerId, string Action, long Version, DateTimeOffset At, string Actor);
public sealed record PartnerState(int SchemaVersion, long Revision, PartnerRecord[] Partners, PartnerAudit[] Audit,
    ContactRecord[]? Contacts = null, PartnerLink[]? Links = null, ActivityRecord[]? Activities = null);
public sealed record ContactSave(Guid CompanyId, Guid? Id, long Version, ContactData Data);
public sealed record ActivitySave(Guid CompanyId, Guid? Id, long Version, ActivityData Data);
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
        var normalized = data with
        {
            Name = Text(data.Name, 200, "Név"), TaxId = Text(data.TaxId, 50, "Adószám"),
            Email = Text(data.Email, 254, "E-mail"), Phone = Text(data.Phone, 80, "Telefon"),
            Website = Text(data.Website, 500, "Weboldal"), PostalCode = Text(data.PostalCode, 30, "Irányítószám"),
            City = Text(data.City, 150, "Település"), Address = Text(data.Address, 500, "Cím"),
            Country = Text(data.Country, 50, "Ország"), Notes = Text(data.Notes, 4000, "Megjegyzés"),
            EuTaxId = Text(data.EuTaxId, 50, "EU adószám"), RegistrationNumber = Text(data.RegistrationNumber, 50, "Cégjegyzékszám"),
            BankAccounts = Lines(data.BankAccounts, 10, 80, "Bankszámlaszám"),
            BillingAddress = Text(data.BillingAddress, 500, "Számlázási cím"), ShippingAddress = Text(data.ShippingAddress, 500, "Szállítási cím"),
            Sites = Lines(data.Sites, 20, 200, "Telephely"), Industry = Text(data.Industry, 80, "Iparág"),
            EmployeeCount = Count(data.EmployeeCount), RevenueCategory = Text(data.RevenueCategory, 80, "Árbevétel"),
            AbcClass = Choice((data.AbcClass ?? "").Trim().ToUpperInvariant(), "Besorolás", "", "A", "B", "C"), LinkedIn = Url(data.LinkedIn, "LinkedIn"),
            Lifecycle = Choice(data.Lifecycle, "Életciklus", "", "Érdeklődő", "Minősített", "Lehetőség", "Ügyfél", "Inaktív"),
            Owner = Text(data.Owner, 80, "Felelős"), LeadSource = Text(data.LeadSource, 80, "Adatforrás"),
            ParentName = Text(data.ParentName, 200, "Anyacég"), Tags = Tags(data.Tags), CustomFields = Pairs(data.CustomFields),
            ConsentOn = Day(data.ConsentOn, "Hozzájárulás dátuma")
        };
        if (normalized.CreditLimit is < 0 or > 1_000_000_000_000m) throw new ArgumentException("Érvénytelen hitelkeret.");
        if (normalized.EuTaxId.Length > 0 && (normalized.EuTaxId.Length < 4 || !char.IsAsciiLetter(normalized.EuTaxId[0]) || !char.IsAsciiLetter(normalized.EuTaxId[1])
            || normalized.EuTaxId[2..].Any(c => !char.IsAsciiLetterOrDigit(c))))
            throw new ArgumentException("Az EU adószám országkóddal kezdődjön, például HU12345678.");
        if (normalized.Name.Length < 2) throw new ArgumentException("A partner neve legalább 2 karakter legyen.");
        if (normalized.Email.Length > 0 && (!MailAddress.TryCreate(normalized.Email, out var email)
            || email.Address != normalized.Email)) throw new ArgumentException("Érvénytelen e-mail-cím.");
        if (normalized.Website.Length > 0 && (!Uri.TryCreate(normalized.Website, UriKind.Absolute, out var url)
            || url.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(url.UserInfo)))
            throw new ArgumentException("A weboldal teljes http:// vagy https:// cím legyen.");
        return normalized;
    }
    private static string Text(string? value, int maximum, string label)
    {
        value = (value ?? "").Trim();
        if (value.Length > maximum || value.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t'))
            throw new ArgumentException(label + ": érvénytelen vagy túl hosszú érték.");
        return value;
    }
    public static string TaxKey(string value) => string.Concat(value.Where(char.IsAsciiLetterOrDigit)).ToUpperInvariant();
    public static string NameKey(string value) => string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
    private static string Lines(string? value, int maximum, int lineMaximum, string label)
    {
        var text = Text(value, 4000, label);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length > maximum || lines.Any(line => line.Length > lineMaximum))
            throw new ArgumentException(label + ": legfeljebb " + maximum + " sor, soronként " + lineMaximum + " karakter.");
        return string.Join('\n', lines);
    }
    private static string Count(string? value)
    {
        var text = Text(value, 12, "Alkalmazottak száma");
        if (text.Length == 0) return "";
        if (!int.TryParse(text, out var count) || count < 0) throw new ArgumentException("Az alkalmazottak száma egész szám legyen.");
        return count.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    private static string Choice(string? value, string label, params string[] allowed)
    {
        var text = Text(value, 40, label);
        if (!allowed.Contains(text)) throw new ArgumentException(label + ": " + string.Join(", ", allowed.Where(item => item.Length > 0)) + ".");
        return text;
    }
    private static string Url(string? value, string label)
    {
        var text = Text(value, 500, label);
        if (text.Length == 0) return "";
        if (!Uri.TryCreate(text, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(url.UserInfo))
            throw new ArgumentException(label + ": teljes http:// vagy https:// cím legyen.");
        return text;
    }
    private static string Tags(string? value)
    {
        var text = Text(value, 500, "Címkék");
        var tags = text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (tags.Length > 20 || tags.Any(tag => tag.Length > 40)) throw new ArgumentException("Legfeljebb 20 címke, egyenként 40 karakter.");
        return string.Join(", ", tags);
    }
    private static string Pairs(string? value)
    {
        var text = Text(value, 4000, "Egyedi mezők");
        if (text.Length == 0) return "";
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length > 30) throw new ArgumentException("Legfeljebb 30 egyedi mező adható meg.");
        var normalized = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            var split = line.IndexOfAny(['=', ':']);
            if (split <= 0 || split == line.Length - 1) throw new ArgumentException("Az egyedi mező formátuma: név=érték.");
            var name = line[..split].Trim();
            var field = line[(split + 1)..].Trim();
            if (name.Length is < 1 or > 80 || field.Length is < 1 or > 200)
                throw new ArgumentException("Érvénytelen egyedi mező.");
            normalized.Add(name + "=" + field);
        }
        return string.Join('\n', normalized);
    }
    private static string Day(string? value, string label)
    {
        var text = Text(value, 10, label);
        if (text.Length == 0) return "";
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
            throw new ArgumentException(label + ": éééé-hh-nn formátum kell.");
        return text;
    }
}
