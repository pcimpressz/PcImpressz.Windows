using System.Text.Json;
using PcImpressz.Contracts;

namespace PcImpressz.Modules.Partners;

public sealed class PartnersModule : IModule
{
    public ValueTask StartAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var repository = new PartnerRepository(context.GetCompanyDataDirectory, context.TimeProvider);
        var importer = new InvenTreeImport(repository, context.TimeProvider);
        context.RegisterCommand(new Handler("list", "partners.read", CommandAccess.RecoveryReadOnly, async (input, _, ct) =>
        {
            var query = PartnerJson.Read<PartnerQuery>(input);
            if (query.Page < 0 || query.PageSize is < 1 or > 200 || query.Search.Length > 200)
                throw new ArgumentException("Érvénytelen listázási kérés.");
            var snapshot = await repository.Snapshot(query.CompanyId, ct);
            var matching = Filter(snapshot.Partners, query);
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
            return PartnerJson.Element(new { csv = PartnerRepository.ExportCsv(Filter((await repository.Snapshot(query.CompanyId, ct)).Partners, query)) });
        }));
        context.RegisterCommand(new Handler("import-preview", "partners.import", CommandAccess.Licensed, async (input, actor, ct) =>
            PartnerJson.Element(await importer.Preview(PartnerJson.Read<ImportOptions>(input), actor, ct))));
        context.RegisterCommand(new Handler("import-commit", "partners.import", CommandAccess.Licensed, async (input, actor, ct) =>
            PartnerJson.Element(new { imported = await importer.Commit(PartnerJson.Read<ImportCommit>(input), actor, ct) })));
        context.RegisterCommand(new Handler("dossier", "partners.read", CommandAccess.RecoveryReadOnly, async (input, _, ct) =>
        {
            var query = PartnerJson.Read<PartnerDossierQuery>(input);
            var (from, to) = PartnerDossier.Period(query.From, query.To);
            var view = PartnerDossier.Open(await repository.Snapshot(query.CompanyId, ct), query.Id);
            var snapshot = PartnerDossier.Read(context.GetCompanyDataDirectory(query.CompanyId), query.Id);
            if (snapshot is not null) snapshot = PartnerDossier.Within(snapshot, from, to);
            return PartnerJson.Element(PartnerDossier.Apply(view, snapshot, from is not null || to is not null));
        }));
        context.RegisterPage(new("partners", context.ModuleId, "Partnertörzs",
            "Vevők, szállítók és gyártók. A mentett partner dossziéja mutatja, honnan jön majd a számla és a forgalom.", "\uE716",
            [new("name", "Partner neve", Required: true), new("taxId", "Adószám"), new("email", "E-mail"),
             new("phone", "Telefon"), new("website", "Weboldal"), new("country", "Ország", DefaultText: "HU"),
             new("postalCode", "Irányítószám"), new("city", "Település"), new("address", "Cím"),
             new("isCustomer", "Vevő", "boolean"), new("isSupplier", "Szállító", "boolean"),
             new("isManufacturer", "Gyártó", "boolean"), new("isActive", "Aktív", "boolean", DefaultChecked: true),
             new("notes", "Megjegyzés", "multiline")],
            [new("name", "Partner", 240), new("taxId", "Adószám", 150), new("city", "Település", 150), new("email", "E-mail", 230)],
            new("list", "save", "archive", "export", "import-preview", "import-commit")));
        return ValueTask.CompletedTask;
    }
    public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    private static PartnerRecord[] Filter(IEnumerable<PartnerRecord> source, PartnerQuery query) =>
        source.Where(p => query.Filter == "archived" ? !p.Data.IsActive : p.Data.IsActive)
        .Where(p => query.Filter switch
        { "customer" => p.Data.IsCustomer, "supplier" => p.Data.IsSupplier, "manufacturer" => p.Data.IsManufacturer, _ => true })
        .Where(p => string.IsNullOrWhiteSpace(query.Search) || string.Join(" ", p.Data.Name, p.Data.TaxId, p.Data.Email, p.Data.Phone, p.Data.City)
            .Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase))
        .OrderBy(p => p.Data.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("hu-HU"), true)).ToArray();
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
