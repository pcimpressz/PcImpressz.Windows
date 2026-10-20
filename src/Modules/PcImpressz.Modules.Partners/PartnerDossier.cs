namespace PcImpressz.Modules.Partners;

public sealed record PartnerDossierQuery(Guid CompanyId, Guid Id);
public sealed record PartnerDossierCard(string Id, string Label, string Value, string Status);
public sealed record PartnerDossierSection(string Id, string Title, string Status, string Source, string Detail);
public sealed record PartnerDossierView(Guid PartnerId, string Title, string Subtitle, bool FiguresAvailable,
    string Notice, PartnerDossierCard[] Cards, PartnerDossierSection[] Sections);

public static class PartnerDossier
{
    public const string Waiting = "waiting";

    public static PartnerDossierView Open(PartnerState state, Guid id)
    {
        var partner = state.Partners.FirstOrDefault(item => item.Id == id)
            ?? throw new ArgumentException("Nincs ilyen partner.");
        return Build(partner);
    }

    public static PartnerDossierView Build(PartnerRecord partner)
    {
        var data = partner.Data;
        return new(partner.Id, data.Name + " dossziéja", Subtitle(data), false,
            "A partner adatlapja megvan. Számla, forgalom és beszerzés akkor jelenik meg, ha a hozzá tartozó modul elkészül. Most egyik sem tárol ilyen adatot, ezért nincs összesített összeg.",
            [
                Card("turnover", "Forgalom"), Card("outgoing", "Kimenő számla"),
                Card("purchase", "Beszerzés"), Card("incoming", "Bejövő számla"),
                Card("quotes", "Árajánlat"), Card("proforma", "Díjbekérő"),
                Card("orders", "Megrendelés"), Card("jobs", "Munkalap")
            ],
            [
                Section("sales", "Kimenő számlák és forgalom", "Eladás és kimenő számla",
                    "A régi dosszié a pultos eladásból és a lekért kimenő számlákból számolta a forgalmat, a számlaszámot és a gyakran vett tételeket. Eladás és kimenő számla modul még nincs."),
                Section("incoming", "Bejövő számlák és beszerzés", "Bevételezés",
                    "Beszállítónál a régi dosszié a bevételezés számláiból mutatta a bejövő számlaszámot, a bruttó és nettó beszerzést, a fizetettséget és a gyakori tételeket. Bevételezés modul még nincs."),
                Section("quotes", "Árajánlatok", "Árajánlat",
                    "A régi dosszié a partnerhez kötött árajánlatokat listázta. Árajánlat modul még nincs."),
                Section("proforma", "Díjbekérők", "Bizonylat",
                    "A régi dosszié a díjbekérő bizonylatokat mutatta. Bizonylat modul még nincs."),
                Section("orders", "Megrendelések", "Rendeléskönyv",
                    "A vevői és a beszállítói megrendelés a rendeléskönyv partner-kapcsolódó listájából jött. Rendeléskönyv kapcsolat még nincs."),
                Section("service", "Munkalap, garancia, teendő", "Rendeléskönyv",
                    "A munkalap, a garancia és a teendő ugyanabból a rendeléskönyv-listából jött. Amíg nincs kapcsolat, ezek a listák üresek.")
            ]);
    }

    private static PartnerDossierCard Card(string id, string label) => new(id, label, "—", Waiting);

    private static PartnerDossierSection Section(string id, string title, string source, string detail) =>
        new(id, title, Waiting, source, detail);

    private static string Subtitle(PartnerData data)
    {
        var roles = new[] { data.IsCustomer ? "vevő" : "", data.IsSupplier ? "szállító" : "", data.IsManufacturer ? "gyártó" : "" }
            .Where(role => role.Length > 0);
        var role = string.Join(", ", roles);
        var place = string.Join(" ", new[] { data.PostalCode, data.City, data.Address }.Where(part => part.Length > 0));
        var parts = new[] { data.IsActive ? "" : "inaktív", role.Length == 0 ? "nincs szerep" : role, data.TaxId, place, data.Phone, data.Email }
            .Where(part => part.Length > 0);
        return string.Join(" · ", parts);
    }
}
