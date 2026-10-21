using System.Text.Json;
using PcImpressz.Contracts;

namespace PcImpressz.Modules.Partners;

public sealed class PartnersModule : IModule
{
    public ValueTask StartAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var repository = new PartnerRepository(context.GetCompanyDataDirectory, context.TimeProvider, context.Database);
        context.RegisterCommand(new Handler("list", "partners.read", CommandAccess.RecoveryReadOnly, async (input, _, ct) =>
        {
            var query = PartnerJson.Read<PartnerQuery>(input);
            if (query.Page < 0 || query.PageSize is < 1 or > 200 || query.Search.Length > 200 || !PartnerListing.IsSort(query.Sort))
                throw new ArgumentException("Érvénytelen listázási kérés.");
            var snapshot = await repository.Snapshot(query.CompanyId, ct);
            var matching = PartnerListing.Match(snapshot.Partners, query);
            var rows = matching.Skip(query.Page * (long)query.PageSize > int.MaxValue ? int.MaxValue : query.Page * query.PageSize)
                .Take(query.PageSize).Select(Row).ToArray();
            return PartnerJson.Element(new { rows, total = matching.Length });
        }));
        context.RegisterCommand(new Handler("save", "partners.write", CommandAccess.Licensed, async (input, actor, ct) =>
            Row(await repository.Save(PartnerJson.Read<PartnerSave>(input), actor, ct))));
        context.RegisterCommand(new Handler("archive", "partners.write", CommandAccess.Licensed, async (input, actor, ct) =>
            Row(await repository.Archive(PartnerJson.Read<PartnerArchive>(input), actor, ct))));
        context.RegisterCommand(new Handler("export", "partners.export", CommandAccess.RecoveryReadOnly, async (input, _, ct) =>
        {
            var query = PartnerJson.Read<PartnerQuery>(input);
            return PartnerJson.Element(new { csv = PartnerRepository.ExportCsv(PartnerListing.Match((await repository.Snapshot(query.CompanyId, ct)).Partners, query)) });
        }));
        context.RegisterCommand(new Handler("dossier", "partners.read", CommandAccess.RecoveryReadOnly, async (input, _, ct) =>
        {
            var query = PartnerJson.Read<PartnerDossierQuery>(input);
            var (from, to) = PartnerDossier.Period(query.From, query.To);
            var view = PartnerDossier.Open(await repository.Snapshot(query.CompanyId, ct), query.Id);
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
        context.RegisterPage(new("partners", context.ModuleId, "Partnertörzs",
            "Vevők, szállítók és gyártók. A dosszié a bevételezés számláit mutatja ennél a partnernél.", "\uE716",
            [new("name", "Partner neve", Required: true), new("taxId", "Adószám"), new("email", "E-mail"),
             new("phone", "Telefon"), new("website", "Weboldal"), new("country", "Ország", DefaultText: "HU"),
             new("postalCode", "Irányítószám"), new("city", "Település"), new("address", "Cím"),
             new("isCustomer", "Vevő", "boolean"), new("isSupplier", "Szállító", "boolean"),
             new("isManufacturer", "Gyártó", "boolean"), new("isActive", "Aktív", "boolean", DefaultChecked: true),
             new("notes", "Megjegyzés", "multiline")],
            [new("name", "Partner", 240), new("taxId", "Adószám", 150), new("city", "Település", 150), new("email", "E-mail", 230),
             new("phone", "Telefon", 140, false), new("country", "Ország", 90, false),
             new("isCustomer", "Vevő", 90, false), new("isSupplier", "Szállító", 100, false), new("isManufacturer", "Gyártó", 90, false)],
            new("list", "save", "archive", "export")) { DossierCommand = "dossier" });
        return ValueTask.CompletedTask;
    }
    public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    private static JsonElement Row(PartnerRecord partner)
    {
        var data = JsonSerializer.SerializeToNode(partner.Data, PartnerJson.Options)!;
        data["id"] = partner.Id; data["version"] = partner.Version;
        return JsonSerializer.SerializeToElement(data);
    }
    private sealed class Handler(string name, string permission, CommandAccess access,
        Func<JsonElement, string, CancellationToken, Task<JsonElement>> execute) : IContextualCommandHandler
    {
        public CommandDescriptor Descriptor { get; } = new(name, permission, access);
        public ValueTask<JsonElement> ExecuteAsync(JsonElement input, CommandCallContext caller, CancellationToken ct) =>
            new(execute(input, caller.ActorId, ct));
    }
}

public static class PartnerListing
{
    private static readonly StringComparer NameOrder = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("hu-HU"), true);
    public static bool IsSort(string sort) => sort.Length <= 40 && sort.All(char.IsAsciiLetterOrDigit);
    public static PartnerRecord[] Match(IEnumerable<PartnerRecord> source, PartnerQuery query)
    {
        if (!IsSort(query.Sort)) throw new ArgumentException("Érvénytelen listázási kérés.");
        var rows = source.Where(p => query.Filter == "archived" ? !p.Data.IsActive : p.Data.IsActive)
            .Where(p => query.Filter switch
            { "customer" => p.Data.IsCustomer, "supplier" => p.Data.IsSupplier, "manufacturer" => p.Data.IsManufacturer, _ => true })
            .Where(p => string.IsNullOrWhiteSpace(query.Search) || string.Join(" ", p.Data.Name, p.Data.TaxId, p.Data.Email, p.Data.Phone, p.Data.City)
                .Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (query.Sort.Length > 0 && Keys.TryGetValue(query.Sort, out var key))
            return TextOrder.By(rows, query.Descending, key);
        return rows.OrderBy(p => p.Data.Name, NameOrder).ToArray();
    }
    private static string Yes(bool value) => value ? "Igen" : "Nem";
    private static readonly Dictionary<string, Func<PartnerRecord, string>> Keys = new(StringComparer.Ordinal)
    {
        ["name"] = p => p.Data.Name, ["taxId"] = p => p.Data.TaxId, ["city"] = p => p.Data.City, ["email"] = p => p.Data.Email,
        ["phone"] = p => p.Data.Phone, ["country"] = p => p.Data.Country,
        ["isCustomer"] = p => Yes(p.Data.IsCustomer), ["isSupplier"] = p => Yes(p.Data.IsSupplier), ["isManufacturer"] = p => Yes(p.Data.IsManufacturer)
    };
}
