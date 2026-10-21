using System.Globalization;

namespace PcImpressz.Modules.Partners;

public static class PartnerAccess
{
    public static string UserName(string actor)
    {
        var split = (actor ?? "").Split('/', 2);
        return split.Length == 2 && split[1].Length > 0 ? split[1] : actor ?? "";
    }

    public static bool CanSee(string? owner, string role, string actor)
    {
        if (!string.Equals(role, "sales", StringComparison.Ordinal)) return true;
        var name = (owner ?? "").Trim();
        return name.Length == 0 || string.Equals(name, UserName(actor), StringComparison.OrdinalIgnoreCase);
    }
}

public static class PartnerCrm
{
    private static readonly StringComparer Names = StringComparer.Create(CultureInfo.GetCultureInfo("hu-HU"), true);

    public static PartnerData Company(PartnerState state, Guid? self, PartnerData data, string role, string actor)
    {
        var normalized = PartnerValidation.Normalize(data);
        if (string.Equals(role, "sales", StringComparison.Ordinal))
        {
            var user = PartnerAccess.UserName(actor);
            if (normalized.Owner.Length == 0) normalized = normalized with { Owner = user };
            else if (!string.Equals(normalized.Owner, user, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Értékesítőként csak a saját cégedet kezelheted.");
        }
        return Parent(state, self, normalized);
    }

    public static string? Warning(PartnerState state, Guid? self, PartnerData data)
    {
        var notes = new List<string>();
        if (data.Email.Length > 0 && state.Partners.Any(p => p.Id != self && p.Data.IsActive && data.IsActive
            && string.Equals(p.Data.Email, data.Email, StringComparison.OrdinalIgnoreCase)))
            notes.Add("Ezzel az e-mail-címmel már van aktív cég.");
        var key = PartnerValidation.NameKey(data.Name);
        if (key.Length > 0 && state.Partners.Any(p => p.Id != self && p.Data.IsActive && data.IsActive && PartnerValidation.NameKey(p.Data.Name) == key))
            notes.Add("Hasonló nevű aktív cég már létezik.");
        if (data.RegistrationNumber.Length > 0 && state.Partners.Any(p => p.Id != self && p.Data.IsActive && data.IsActive
            && string.Equals(p.Data.RegistrationNumber, data.RegistrationNumber, StringComparison.OrdinalIgnoreCase)))
            notes.Add("Ezzel a cégjegyzékszámmal már van aktív cég.");
        return notes.Count == 0 ? null : string.Join(" ", notes) + " A mentés így is megtörtént.";
    }

    public static PartnerData AnonymizedCompany() => new()
    {
        Name = "Anonimizált partner", IsActive = false, Country = "HU"
    };

    public static ContactData AnonymizedContact() => new() { Name = "Anonimizált kapcsolattartó", IsActive = false };

    public static ContactData Contact(ContactData data)
    {
        var normalized = data with
        {
            Name = Required(data.Name, "A kapcsolattartó neve"),
            Salutation = Short(data.Salutation, 40, "Megszólítás"),
            Title = Short(data.Title, 120, "Pozíció"),
            DecisionLevel = One(data.DecisionLevel, "Döntéshozói szint", "", "Döntéshozó", "Befolyásoló", "Felhasználó"),
            Email = Mail(data.Email, "E-mail"),
            SecondaryEmail = Mail(data.SecondaryEmail, "Másodlagos e-mail"),
            Phone = Short(data.Phone, 80, "Telefon"),
            Mobile = Short(data.Mobile, 80, "Mobil"),
            LinkedIn = Link(data.LinkedIn),
            Channel = One(data.Channel, "Kommunikációs csatorna", "", "E-mail", "Telefon", "Személyes", "LinkedIn"),
            Language = Short(data.Language, 40, "Nyelv"),
            Birthday = Date(data.Birthday, "Születésnap"),
            Companies = Short(data.Companies, 2000, "Cégek"),
            Tags = Short(data.Tags, 500, "Címkék"),
            Notes = Short(data.Notes, 4000, "Jegyzet"),
            ConsentOn = Date(data.ConsentOn, "Hozzájárulás dátuma")
        };
        if (normalized.Name.Length < 2) throw new ArgumentException("A kapcsolattartó neve legalább 2 karakter legyen.");
        return normalized;
    }

    public static PartnerLink[] Links(PartnerState state, Guid contactId, string companies)
    {
        var links = new List<PartnerLink>();
        foreach (var line in companies.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var matches = state.Partners.Where(p => Names.Equals(p.Data.Name, line)).ToArray();
            var partner = matches.FirstOrDefault(p => p.Data.IsActive) ?? matches.FirstOrDefault()
                ?? throw new ArgumentException("Nincs ilyen cég a kapcsolattartónál: " + line);
            if (matches.Count(p => p.Data.IsActive) > 1)
                throw new ArgumentException("Több aktív cég is ezt a nevet viseli: " + line);
            if (links.All(link => link.PartnerId != partner.Id)) links.Add(new(contactId, partner.Id));
        }
        return links.ToArray();
    }

    public static string CompanyNames(PartnerState state, Guid contactId) =>
        string.Join('\n', (state.Links ?? []).Where(link => link.ContactId == contactId)
            .Select(link => state.Partners.FirstOrDefault(p => p.Id == link.PartnerId)?.Data.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name)));

    public static ActivityData Activity(PartnerState state, ActivityData data, DateTimeOffset today)
    {
        var normalized = data with
        {
            Kind = One(data.Kind, "Tevékenység típusa", "Jegyzet", "E-mail", "Hívás", "Megbeszélés", "Rendszer", "Dokumentum"),
            Title = Required(data.Title, "A tevékenység címe"),
            Detail = Short(data.Detail, 4000, "Leírás"),
            At = data.At is null || data.At.Trim().Length == 0 ? today.ToString("yyyy-MM-dd") : Date(data.At, "Dátum"),
            PartnerName = Short(data.PartnerName, 200, "Cég"),
            ContactName = Short(data.ContactName, 200, "Kapcsolattartó")
        };
        if (normalized.Title.Length < 2) throw new ArgumentException("A tevékenység címe legalább 2 karakter legyen.");
        Guid? partnerId = null;
        if (normalized.PartnerName.Length > 0)
            partnerId = OnePartner(state, normalized.PartnerName).Id;
        Guid? contactId = null;
        if (normalized.ContactName.Length > 0)
        {
            var contacts = (state.Contacts ?? []).Where(c => Names.Equals(c.Data.Name, normalized.ContactName)).ToArray();
            var contact = contacts.FirstOrDefault(c => c.Data.IsActive) ?? contacts.FirstOrDefault()
                ?? throw new ArgumentException("Nincs ilyen kapcsolattartó.");
            contactId = contact.Id;
            normalized = normalized with { ContactName = contact.Data.Name };
        }
        return partnerId is null && contactId is null
            ? normalized
            : normalized with { PartnerId = partnerId, PartnerName = partnerId is null ? "" : OnePartner(state, normalized.PartnerName).Data.Name, ContactId = contactId };
    }

    public static bool VisiblePartner(PartnerState state, PartnerRecord partner, string role, string actor) =>
        PartnerAccess.CanSee(partner.Data.Owner, role, actor);

    public static bool VisibleContact(PartnerState state, ContactRecord contact, string role, string actor)
    {
        var links = (state.Links ?? []).Where(link => link.ContactId == contact.Id).ToArray();
        if (links.Length == 0) return true;
        return links.Any(link => state.Partners.FirstOrDefault(p => p.Id == link.PartnerId) is { } partner && VisiblePartner(state, partner, role, actor));
    }

    private static PartnerData Parent(PartnerState state, Guid? self, PartnerData data)
    {
        if (data.ParentName.Length == 0) return data with { ParentId = null };
        var parent = OnePartner(state, data.ParentName);
        if (self is Guid id && parent.Id == id) throw new ArgumentException("A cég nem lehet a saját anyacéje.");
        var cursor = parent.Id;
        for (var guard = 0; guard < 30; guard++)
        {
            if (self is Guid own && cursor == own) throw new ArgumentException("Az anyacég-lánc körbe ér.");
            var next = state.Partners.FirstOrDefault(p => p.Id == cursor)?.Data.ParentId;
            if (next is not Guid child) return data with { ParentId = parent.Id, ParentName = parent.Data.Name };
            cursor = child;
        }
        throw new ArgumentException("Az anyacég-lánc túl mély.");
    }

    private static PartnerRecord OnePartner(PartnerState state, string name)
    {
        var matches = state.Partners.Where(p => Names.Equals(p.Data.Name, name)).ToArray();
        if (matches.Count(p => p.Data.IsActive) > 1) throw new ArgumentException("Több aktív cég is ezt a nevet viseli: " + name);
        return matches.FirstOrDefault(p => p.Data.IsActive) ?? matches.FirstOrDefault()
            ?? throw new ArgumentException("Nincs ilyen cég: " + name);
    }

    private static string Required(string? value, string label)
    {
        var text = Short(value, 200, label);
        if (text.Length == 0) throw new ArgumentException(label + " kötelező.");
        return text;
    }
    private static string Short(string? value, int maximum, string label)
    {
        value = (value ?? "").Trim();
        if (value.Length > maximum || value.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t'))
            throw new ArgumentException(label + ": érvénytelen vagy túl hosszú érték.");
        return value;
    }
    private static string Mail(string? value, string label)
    {
        var text = Short(value, 254, label);
        if (text.Length > 0 && (!System.Net.Mail.MailAddress.TryCreate(text, out var email) || email.Address != text))
            throw new ArgumentException("Érvénytelen e-mail-cím.");
        return text;
    }
    private static string Link(string? value)
    {
        var text = Short(value, 500, "LinkedIn");
        if (text.Length == 0) return "";
        if (!Uri.TryCreate(text, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            throw new ArgumentException("A LinkedIn profil teljes http:// vagy https:// cím legyen.");
        return text;
    }
    private static string Date(string? value, string label)
    {
        var text = Short(value, 10, label);
        if (text.Length == 0) return "";
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ArgumentException(label + ": éééé-hh-nn formátum kell.");
        return text;
    }
    private static string One(string? value, string label, params string[] allowed)
    {
        var text = Short(value, 40, label);
        if (text.Length == 0 && allowed.Contains("")) return "";
        if (!allowed.Contains(text)) throw new ArgumentException(label + ": " + string.Join(", ", allowed.Where(item => item.Length > 0)) + ".");
        return text;
    }
}
