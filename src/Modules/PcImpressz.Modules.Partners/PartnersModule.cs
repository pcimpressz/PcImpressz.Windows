using System.Text.Json;
using PcImpressz.Contracts;

namespace PcImpressz.Modules.Partners;

public sealed class PartnersModule : IModule
{
    public ValueTask StartAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var repository = new PartnerRepository(context.GetCompanyDataDirectory, context.TimeProvider, context.Database);
        context.RegisterCommand(new Handler("list", "partners.read", CommandAccess.RecoveryReadOnly, async (input, caller, ct) =>
        {
            var query = PartnerJson.Read<PartnerQuery>(input);
            PartnerListing.Validate(query);
            var snapshot = await repository.Snapshot(query.CompanyId, ct);
            var matching = PartnerListing.Match(snapshot.Partners, query).Where(p => PartnerCrm.VisiblePartner(snapshot, p, caller.Role, caller.ActorId)).ToArray();
            var rows = matching.Skip(query.Page * (long)query.PageSize > int.MaxValue ? int.MaxValue : query.Page * query.PageSize)
                .Take(query.PageSize).Select(partner => Row(partner)).ToArray();
            return PartnerJson.Element(new { rows, total = matching.Length });
        }));
        context.RegisterCommand(new Handler("save", "partners.write", CommandAccess.Licensed, async (input, caller, ct) =>
        {
            var request = PartnerJson.Read<PartnerSave>(input);
            var before = await repository.Snapshot(request.CompanyId, ct);
            var saved = await repository.Save(request, caller.ActorId, ct, caller.Role);
            var warning = request.Data is { Forget: false } ? PartnerCrm.Warning(before, saved.Id, saved.Data) : null;
            return Row(saved, warning);
        }));
        context.RegisterCommand(new Handler("archive", "partners.write", CommandAccess.Licensed, async (input, caller, ct) =>
            Row(await repository.Archive(PartnerJson.Read<PartnerArchive>(input), caller.ActorId, ct, caller.Role))));
        context.RegisterCommand(new Handler("export", "partners.export", CommandAccess.RecoveryReadOnly, async (input, caller, ct) =>
        {
            var query = PartnerJson.Read<PartnerQuery>(input);
            PartnerListing.Validate(query);
            var snapshot = await repository.Snapshot(query.CompanyId, ct);
            var rows = PartnerListing.Match(snapshot.Partners, query).Where(p => PartnerCrm.VisiblePartner(snapshot, p, caller.Role, caller.ActorId));
            return PartnerJson.Element(new { csv = PartnerRepository.ExportCsv(rows) });
        }));
        context.RegisterCommand(new Handler("dossier", "partners.read", CommandAccess.RecoveryReadOnly, async (input, caller, ct) =>
        {
            var query = PartnerJson.Read<PartnerDossierQuery>(input);
            var (from, to) = PartnerDossier.Period(query.From, query.To);
            var opened = await repository.Snapshot(query.CompanyId, ct);
            if (opened.Partners.FirstOrDefault(p => p.Id == query.Id) is { } owned && !PartnerCrm.VisiblePartner(opened, owned, caller.Role, caller.ActorId))
                throw new ArgumentException("Ehhez a céghez nincs hozzáférésed.");
            var view = PartnerDossier.WithRelations(PartnerDossier.Open(opened, query.Id), opened, query.Id, context.TimeProvider.GetUtcNow());
            var directory = context.GetCompanyDataDirectory(query.CompanyId);
            var snapshot = context.Database is null
                ? PartnerDossier.FromReceipts(directory, query.Id) ?? PartnerDossier.Read(directory, query.Id)
                : context.Database.Execute(query.CompanyId, tx => PartnerDossier.FromReceipts(directory, query.Id, tx) ?? PartnerDossier.Read(directory, query.Id, tx));
            if (snapshot is not null) snapshot = PartnerDossier.Within(snapshot, from, to);
            var result = PartnerDossier.Apply(view, snapshot, from is not null || to is not null);
            if (context.Database is not null) result = context.Database.Execute(query.CompanyId,
                tx => PartnerDossier.ApplySales(result, CompanyReferenceQueries.Sales(tx), from, to));
            return PartnerJson.Element(result);
        }));
        context.RegisterCommand(new Handler("contacts-list", "partners.read", CommandAccess.RecoveryReadOnly, async (input, caller, ct) =>
        {
            var query = PartnerJson.Read<PartnerQuery>(input);
            var snapshot = await repository.Snapshot(query.CompanyId, ct);
            var matching = PartnerListing.Contacts(snapshot, query, caller.Role, caller.ActorId);
            return PartnerJson.Element(new { rows = matching.Skip(Skip(query)).Take(query.PageSize).Select(c => ContactRow(snapshot, c)), total = matching.Length });
        }));
        context.RegisterCommand(new Handler("contacts-save", "partners.write", CommandAccess.Licensed, async (input, caller, ct) =>
        {
            var request = PartnerJson.Read<ContactSave>(input);
            var saved = await repository.SaveContact(request, caller.ActorId, ct, caller.Role);
            return ContactRow(await repository.Snapshot(request.CompanyId, ct), saved);
        }));
        context.RegisterCommand(new Handler("contacts-archive", "partners.write", CommandAccess.Licensed, async (input, caller, ct) =>
        {
            var request = PartnerJson.Read<PartnerArchive>(input);
            var saved = await repository.ArchiveContact(request, caller.ActorId, ct, caller.Role);
            return ContactRow(await repository.Snapshot(request.CompanyId, ct), saved);
        }));
        context.RegisterCommand(new Handler("contacts-export", "partners.export", CommandAccess.RecoveryReadOnly, async (input, caller, ct) =>
        {
            var query = PartnerJson.Read<PartnerQuery>(input);
            var snapshot = await repository.Snapshot(query.CompanyId, ct);
            var rows = PartnerListing.Contacts(snapshot, query, caller.Role, caller.ActorId);
            return PartnerJson.Element(new { csv = PartnerRepository.ExportCsv("Név;Pozíció;E-mail;Telefon;Mobil;Cégek;Aktív", rows, c =>
                [c.Data.Name, c.Data.Title, c.Data.Email, c.Data.Phone, c.Data.Mobile, PartnerCrm.CompanyNames(snapshot, c.Id).Replace('\n', ','), c.Data.IsActive ? "Igen" : "Nem"]) });
        }));
        context.RegisterCommand(new Handler("activities-list", "partners.read", CommandAccess.RecoveryReadOnly, async (input, caller, ct) =>
        {
            var query = PartnerJson.Read<PartnerQuery>(input);
            var snapshot = await repository.Snapshot(query.CompanyId, ct);
            var matching = PartnerListing.Activities(snapshot, query, caller.Role, caller.ActorId);
            return PartnerJson.Element(new { rows = matching.Skip(Skip(query)).Take(query.PageSize).Select(ActivityRow), total = matching.Length });
        }));
        context.RegisterCommand(new Handler("activities-save", "partners.write", CommandAccess.Licensed, async (input, caller, ct) =>
            ActivityRow(await repository.SaveActivity(PartnerJson.Read<ActivitySave>(input), caller.ActorId, ct, caller.Role))));
        context.RegisterCommand(new Handler("activities-archive", "partners.write", CommandAccess.Licensed, async (input, caller, ct) =>
            ActivityRow(await repository.ArchiveActivity(PartnerJson.Read<PartnerArchive>(input), caller.ActorId, ct, caller.Role))));
        context.RegisterCommand(new Handler("activities-export", "partners.export", CommandAccess.RecoveryReadOnly, async (input, caller, ct) =>
        {
            var query = PartnerJson.Read<PartnerQuery>(input);
            var snapshot = await repository.Snapshot(query.CompanyId, ct);
            var rows = PartnerListing.Activities(snapshot, query, caller.Role, caller.ActorId);
            return PartnerJson.Element(new { csv = PartnerRepository.ExportCsv("Dátum;Típus;Cím;Cég;Kapcsolattartó;Leírás;Aktív", rows, a =>
                [a.Data.At, a.Data.Kind, a.Data.Title, a.Data.PartnerName, a.Data.ContactName, a.Data.Detail, a.Data.IsActive ? "Igen" : "Nem"]) });
        }));
        context.RegisterPage(new("partners", context.ModuleId, "Partnertörzs",
            "Cégek: hivatalos adatok, címek, besorolás és a dosszié. A kapcsolattartó és az idővonal külön lap. A cégjegyzék-kitöltés és a levelezés szinkronja még nincs bekötve.", "\uE716",
            [new("name", "Cégnév", Required: true), new("taxId", "Adószám"), new("euTaxId", "EU adószám"), new("registrationNumber", "Cégjegyzékszám"),
             new("bankAccounts", "Bankszámlaszámok", "multiline"), new("country", "Ország", DefaultText: "HU"),
             new("postalCode", "Irányítószám"), new("city", "Település"), new("address", "Székhely"),
             new("billingAddress", "Számlázási cím", "multiline"), new("shippingAddress", "Szállítási cím", "multiline"), new("sites", "Telephelyek", "multiline"),
             new("industry", "Iparág"), new("parentName", "Anyacég neve"),
             new("isCustomer", "Ügyfél", "boolean"), new("isSupplier", "Beszállító", "boolean"), new("isManufacturer", "Gyártó", "boolean"),
             new("isProspect", "Érdeklődő", "boolean"), new("isPartner", "Partner", "boolean"), new("isCompetitor", "Konkurens", "boolean"),
             new("employeeCount", "Alkalmazottak száma"), new("revenueCategory", "Éves árbevétel"), new("abcClass", "ABC besorolás (A, B, C)"),
             new("website", "Weboldal"), new("linkedIn", "LinkedIn"), new("email", "Központi e-mail"), new("phone", "Központi telefon"),
             new("lifecycle", "Életciklus: Érdeklődő, Minősített, Lehetőség, Ügyfél vagy Inaktív"), new("owner", "Felelős"), new("leadSource", "Adatforrás"),
             new("creditLimit", "Hitelkeret (Ft)", "decimal"), new("tags", "Címkék"), new("customFields", "Egyedi mezők (név=érték)", "multiline"),
             new("marketingConsent", "Marketing hozzájárulás", "boolean"), new("newsletterConsent", "Hírlevél", "boolean"), new("consentOn", "Hozzájárulás dátuma"),
             new("notes", "Megjegyzés", "multiline"), new("isActive", "Aktív", "boolean", DefaultChecked: true),
             new("forget", "Elfeledtetés mentéskor", "boolean")],
            [new("name", "Cég", 220), new("taxId", "Adószám", 140), new("city", "Település", 130), new("lifecycle", "Életciklus", 120),
             new("abcClass", "ABC", 70), new("owner", "Felelős", 140), new("email", "E-mail", 200), new("phone", "Telefon", 130, false),
             new("isCustomer", "Ügyfél", 90, false), new("isSupplier", "Beszállító", 110, false)],
            new("list", "save", "archive", "export"),
            new EntityPageLabels { SearchHint = "Keresés név, adószám, e-mail, felelős, címke vagy iparág alapján" },
            [new("all", "Aktív cégek"), new("customer", "Ügyfelek"), new("supplier", "Beszállítók"), new("manufacturer", "Gyártók"),
             new("prospect", "Érdeklődők"), new("partner", "Partnerek"), new("competitor", "Konkurensek"),
             new("abc-a", "A – kiemelt"), new("abc-b", "B – közepes"), new("abc-c", "C – kis ügyfél"), new("archived", "Archiváltak")])
            { DossierCommand = "dossier" });
        context.RegisterPage(new("contacts", context.ModuleId, "Kapcsolattartók",
            "Személyek, akik egy vagy több céghez tartoznak. A cégek nevét soronként add meg.", "\uE77B",
            [new("name", "Teljes név", Required: true), new("salutation", "Megszólítás"), new("title", "Pozíció"), new("decisionLevel", "Döntéshozó, Befolyásoló vagy Felhasználó"),
             new("email", "E-mail"), new("secondaryEmail", "Másodlagos e-mail"), new("phone", "Közvetlen telefon"), new("mobile", "Mobil"),
             new("linkedIn", "LinkedIn"), new("channel", "Csatorna: E-mail, Telefon, Személyes vagy LinkedIn"), new("language", "Nyelv"), new("birthday", "Születésnap"),
             new("companies", "Cégek, soronként egy", "multiline"), new("tags", "Címkék"), new("notes", "Jegyzet", "multiline"),
             new("marketingConsent", "Marketing hozzájárulás", "boolean"), new("newsletterConsent", "Hírlevél", "boolean"), new("consentOn", "Hozzájárulás dátuma"),
             new("isActive", "Aktív", "boolean", DefaultChecked: true), new("forget", "Elfeledtetés mentéskor", "boolean")],
            [new("name", "Név", 220), new("title", "Pozíció", 180), new("email", "E-mail", 220), new("phone", "Telefon", 140), new("companies", "Cégek", 240)],
            new("contacts-list", "contacts-save", "contacts-archive", "contacts-export"),
            Labels("kapcsolattartó", "Kapcsolattartó", "kapcsolattartót", "kapcsolattartok.csv", "Keresés név, e-mail, telefon vagy cég alapján"),
            [new("all", "Aktív kapcsolattartók"), new("archived", "Archiváltak")]));
        context.RegisterPage(new("activities", context.ModuleId, "Idővonal",
            "Kézi előzmények: hívás, e-mail, megbeszélés, dokumentum. Az automatikus levél- és naptárszinkron még nincs bekötve.", "\uE787",
            [new("at", "Dátum (éééé-hh-nn)"), new("kind", "Jegyzet, E-mail, Hívás, Megbeszélés, Rendszer vagy Dokumentum", DefaultText: "Jegyzet"), new("title", "Cím", Required: true),
             new("partnerName", "Cég neve"), new("contactName", "Kapcsolattartó neve"), new("detail", "Leírás", "multiline"),
             new("isActive", "Aktív", "boolean", DefaultChecked: true)],
            [new("at", "Dátum", 120), new("kind", "Típus", 120), new("title", "Cím", 260), new("partnerName", "Cég", 200), new("contactName", "Kapcsolattartó", 180)],
            new("activities-list", "activities-save", "activities-archive", "activities-export"),
            Labels("tevékenység", "Tevékenység", "tevékenységet", "idovonal.csv", "Keresés cím, cég vagy kapcsolattartó alapján"),
            [new("all", "Minden előzmény"), new("email", "E-mailek"), new("call", "Hívások"), new("meeting", "Megbeszélések"),
             new("note", "Jegyzetek"), new("document", "Dokumentumok"), new("archived", "Archiváltak")]));
        return ValueTask.CompletedTask;
    }
    public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    private static int Skip(PartnerQuery query) => query.Page * (long)query.PageSize > int.MaxValue ? int.MaxValue : query.Page * query.PageSize;
    private static JsonElement Row(PartnerRecord partner, string? warning = null)
    {
        var data = JsonSerializer.SerializeToNode(partner.Data, PartnerJson.Options)!;
        data["id"] = partner.Id; data["version"] = partner.Version;
        if (!string.IsNullOrWhiteSpace(warning)) data["warning"] = warning;
        return JsonSerializer.SerializeToElement(data);
    }
    private static JsonElement ContactRow(PartnerState state, ContactRecord contact)
    {
        var data = JsonSerializer.SerializeToNode(contact.Data with { Companies = PartnerCrm.CompanyNames(state, contact.Id), Forget = false }, PartnerJson.Options)!;
        data["id"] = contact.Id; data["version"] = contact.Version;
        return JsonSerializer.SerializeToElement(data);
    }
    private static JsonElement ActivityRow(ActivityRecord activity)
    {
        var data = JsonSerializer.SerializeToNode(activity.Data, PartnerJson.Options)!;
        data["id"] = activity.Id; data["version"] = activity.Version;
        return JsonSerializer.SerializeToElement(data);
    }
    private static EntityPageLabels Labels(string noun, string capital, string accusative, string file, string search) => new()
    {
        NewLabel = "Új " + noun, NewTitle = "Új " + noun, EditTitle = capital + " szerkesztése", CountNoun = noun,
        EmptyMessage = "Ebben a nézetben még nincs " + noun + ". Az Új " + noun + " gombbal vehetsz fel egyet.",
        SavedMessage = "A " + noun + " mentése sikerült.", NeedSelection = "Először válassz meglévő " + accusative + ".",
        ArchiveQuestion = "A " + noun + " inaktív állapotba kerül. Az adatai megmaradnak.",
        ArchiveTitle = capital + " archiválása", ArchivedMessage = "A " + noun + " archiválva.",
        ExportFileName = file, ExportedMessage = "A szűrt lista exportálva.", SearchHint = search
    };
    private sealed class Handler(string name, string permission, CommandAccess access,
        Func<JsonElement, CommandCallContext, CancellationToken, Task<JsonElement>> execute) : IContextualCommandHandler
    {
        public CommandDescriptor Descriptor { get; } = new(name, permission, access);
        public ValueTask<JsonElement> ExecuteAsync(JsonElement input, CommandCallContext caller, CancellationToken ct) =>
            new(execute(input, caller, ct));
    }
}

public static class PartnerListing
{
    private static readonly StringComparer NameOrder = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("hu-HU"), true);
    public static bool IsSort(string? sort) => sort is not null && sort.Length <= 40 && sort.All(char.IsAsciiLetterOrDigit);
    public static void Validate(PartnerQuery query)
    {
        if (query.Page < 0 || query.PageSize is < 1 or > 200 || query.Search is null || query.Search.Length > 200
            || query.Filter is not ("all" or "customer" or "supplier" or "manufacturer" or "prospect" or "partner" or "competitor" or "abc-a" or "abc-b" or "abc-c" or "archived"
                or "email" or "call" or "meeting" or "note" or "document")
            || !IsSort(query.Sort))
            throw new ArgumentException("Érvénytelen listázási kérés.");
    }
    public static PartnerRecord[] Match(IEnumerable<PartnerRecord> source, PartnerQuery query)
    {
        Validate(query);
        var rows = source.OrderBy(p => p.Id).Where(p => query.Filter == "archived" ? !p.Data.IsActive : p.Data.IsActive)
            .Where(p => query.Filter switch
            {
                "customer" => p.Data.IsCustomer, "supplier" => p.Data.IsSupplier, "manufacturer" => p.Data.IsManufacturer,
                "prospect" => p.Data.IsProspect, "partner" => p.Data.IsPartner, "competitor" => p.Data.IsCompetitor,
                "abc-a" => p.Data.AbcClass == "A", "abc-b" => p.Data.AbcClass == "B", "abc-c" => p.Data.AbcClass == "C",
                _ => true
            })
            .Where(p => string.IsNullOrWhiteSpace(query.Search) || string.Join(" ", p.Data.Name, p.Data.TaxId, p.Data.EuTaxId, p.Data.Email, p.Data.Phone, p.Data.City, p.Data.Owner, p.Data.Tags, p.Data.Industry)
                .Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (query.Sort.Length > 0 && Keys.TryGetValue(query.Sort, out var key))
            return TextOrder.By(rows, query.Descending, key);
        return rows.OrderBy(p => p.Data.Name, NameOrder).ToArray();
    }
    private static string Yes(bool value) => value ? "Igen" : "Nem";
    private static readonly Dictionary<string, Func<PartnerRecord, string>> Keys = new(StringComparer.Ordinal)
    {
        ["name"] = p => p.Data.Name, ["taxId"] = p => p.Data.TaxId, ["city"] = p => p.Data.City, ["email"] = p => p.Data.Email,
        ["phone"] = p => p.Data.Phone, ["country"] = p => p.Data.Country, ["lifecycle"] = p => p.Data.Lifecycle,
        ["abcClass"] = p => p.Data.AbcClass, ["owner"] = p => p.Data.Owner,
        ["isCustomer"] = p => Yes(p.Data.IsCustomer), ["isSupplier"] = p => Yes(p.Data.IsSupplier), ["isManufacturer"] = p => Yes(p.Data.IsManufacturer)
    };
    public static ContactRecord[] Contacts(PartnerState state, PartnerQuery query, string role, string actor)
    {
        Validate(query);
        var rows = (state.Contacts ?? []).OrderBy(c => c.Id).Where(c => query.Filter == "archived" ? !c.Data.IsActive : c.Data.IsActive)
            .Where(c => PartnerCrm.VisibleContact(state, c, role, actor))
            .Where(c => string.IsNullOrWhiteSpace(query.Search) || string.Join(" ", c.Data.Name, c.Data.Email, c.Data.Phone, c.Data.Mobile, c.Data.Title, PartnerCrm.CompanyNames(state, c.Id))
                .Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        return rows.OrderBy(c => c.Data.Name, NameOrder).ToArray();
    }
    public static ActivityRecord[] Activities(PartnerState state, PartnerQuery query, string role, string actor)
    {
        Validate(query);
        var kind = query.Filter switch { "email" => "E-mail", "call" => "Hívás", "meeting" => "Megbeszélés", "note" => "Jegyzet", "document" => "Dokumentum", _ => "" };
        return (state.Activities ?? []).OrderBy(a => a.Id).Where(a => query.Filter == "archived" ? !a.Data.IsActive : a.Data.IsActive)
            .Where(a => kind.Length == 0 || a.Data.Kind == kind)
            .Where(a => a.Data.PartnerId is not Guid partner || state.Partners.FirstOrDefault(p => p.Id == partner) is not { } row || PartnerCrm.VisiblePartner(state, row, role, actor))
            .Where(a => string.IsNullOrWhiteSpace(query.Search) || string.Join(" ", a.Data.Title, a.Data.Detail, a.Data.PartnerName, a.Data.ContactName, a.Data.Kind)
                .Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.Data.At, StringComparer.Ordinal).ToArray();
    }
}
