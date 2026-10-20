using System.Globalization;
using System.Text.Json;

namespace PcImpressz.Modules.Partners;

public sealed record PartnerDossierQuery(Guid CompanyId, Guid Id);
public sealed record PartnerDossierCard(string Id, string Label, string Value, string Status);
public sealed record PartnerDossierLine(string Title, string Detail, string Amount);
public sealed record PartnerDossierSection(string Id, string Title, string Status, string Source, string Detail, PartnerDossierLine[] Lines);
public sealed record PartnerDossierView(Guid PartnerId, string Title, string Subtitle, bool FiguresAvailable,
    string Notice, PartnerDossierCard[] Cards, PartnerDossierSection[] Sections);
public sealed record PartnerDossierItem(string Name, decimal Quantity, decimal Net);
public sealed record PartnerDossierInvoice(string Number, string Date, decimal Net, decimal Gross, bool Paid, string Status, PartnerDossierItem[] Items);
public sealed record PartnerDossierSnapshot(Guid PartnerId, decimal PurchaseNet, decimal PurchaseGross, string? First, string? Last,
    PartnerDossierItem[] Frequent, PartnerDossierInvoice[] Invoices);
public sealed record PartnerDossierDocument(int SchemaVersion, PartnerDossierSnapshot[] Items);

public static class PartnerDossier
{
    public const string Waiting = "waiting";
    public const string Ready = "ready";
    private static readonly CultureInfo Hungarian = CultureInfo.GetCultureInfo("hu-HU");

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

    public static PartnerDossierView Apply(PartnerDossierView view, PartnerDossierSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.PartnerId != view.PartnerId || snapshot.Invoices is not { Length: > 0 }) return view;
        var cards = view.Cards.Select(card => card.Id switch
        {
            "purchase" => card with { Value = Money(snapshot.PurchaseGross), Status = Ready },
            "incoming" => card with { Value = snapshot.Invoices.Length.ToString(Hungarian), Status = Ready },
            _ => card
        }).ToArray();
        var lines = snapshot.Frequent.Take(6).Select(item => new PartnerDossierLine(item.Name, "gyakori tétel · " + Quantity(item.Quantity) + " db", Money(item.Net)))
            .Concat(snapshot.Invoices.Select(InvoiceLine)).ToArray();
        var sections = view.Sections.Select(section => section.Id == "incoming"
            ? section with
            {
                Status = Ready,
                Detail = "Bejövő számlák a jelenlegi raktárból, " + (snapshot.First ?? "—") + " — " + (snapshot.Last ?? "—") + ". Nettó " + Money(snapshot.PurchaseNet) + ".",
                Lines = lines
            }
            : section).ToArray();
        return view with
        {
            FiguresAvailable = true, Cards = cards, Sections = sections,
            Notice = "A beszerzés és a bejövő számlák a jelenlegi raktárból lettek átvéve. Kimenő forgalom, árajánlat, díjbekérő és megrendelés ennél a partnernél nincs."
        };
    }

    public static PartnerDossierSnapshot? Read(string directory, Guid partnerId)
    {
        var file = Path.Combine(directory, "dossiers.json");
        if (!File.Exists(file)) return null;
        if (new FileInfo(file).Length > 32 * 1024 * 1024) throw new IOException("A partnerdosszié túl nagy.");
        PartnerDossierDocument? document;
        try { document = JsonSerializer.Deserialize<PartnerDossierDocument>(File.ReadAllText(file), PartnerJson.Options); }
        catch (JsonException) { throw new IOException("A partnerdosszié fájl sérült."); }
        if (document is null || document.SchemaVersion != 1 || document.Items is null)
            throw new IOException("A partnerdosszié fájl sérült.");
        return document.Items.FirstOrDefault(item => item.PartnerId == partnerId);
    }

    private static PartnerDossierLine InvoiceLine(PartnerDossierInvoice invoice)
    {
        var names = string.Join(", ", invoice.Items.Take(3).Select(item => item.Name).Where(name => name.Length > 0));
        var paid = invoice.Paid ? "fizetve" : "nincs fizetve";
        var detail = string.Join(" · ", new[] { invoice.Date, paid, invoice.Status, names }.Where(part => part.Length > 0));
        return new(invoice.Number.Length > 0 ? invoice.Number : "számla", detail, Money(invoice.Gross));
    }

    private static string Money(decimal value) => value.ToString("N2", Hungarian) + " Ft";
    private static string Quantity(decimal value) => value.ToString(value == decimal.Truncate(value) ? "0" : "0.##", Hungarian);

    private static PartnerDossierSection Section(string id, string title, string source, string detail) =>
        new(id, title, Waiting, source, detail, []);

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
