using System.Globalization;
using System.Text.Json;

namespace PcImpressz.Modules.Partners;

public sealed record PartnerDossierQuery(Guid CompanyId, Guid Id, string? From = null, string? To = null);
public sealed record PartnerDossierCard(string Id, string Label, string Value, string Status);
public sealed record PartnerDossierItemView(string Name, string Quantity, string Amount);
public sealed record PartnerDossierLine(string Title, string Detail, string Amount, PartnerDossierItemView[] Items);
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
                    "A bevételezés a szállítóhoz kötött számlákat tartja. Ha ennél a partnernél még nincs számla, a beszerzés és a bejövő lista üres marad. A fizetettség külön pénzügy modul, ezért itt nem jelenik meg."),
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

    public static (DateOnly? From, DateOnly? To) Period(string? from, string? to)
    {
        var start = Day(from, "Kezdő dátum");
        var end = Day(to, "Záró dátum");
        if (start is DateOnly first && end is DateOnly last && first > last)
            throw new ArgumentException("A kezdő dátum nem lehet későbbi, mint a záró.");
        return (start, end);
    }

    public static PartnerDossierSnapshot Within(PartnerDossierSnapshot snapshot, DateOnly? from, DateOnly? to)
    {
        if (from is null && to is null) return snapshot;
        var invoices = snapshot.Invoices.Where(invoice => InPeriod(invoice.Date, from, to)).ToArray();
        var frequent = invoices.SelectMany(invoice => invoice.Items)
            .GroupBy(item => item.Name.Trim(), StringComparer.Create(Hungarian, true))
            .Select(group => new PartnerDossierItem(group.First().Name, group.Sum(item => item.Quantity), group.Sum(item => item.Net)))
            .OrderByDescending(item => item.Quantity).ThenBy(item => item.Name, StringComparer.Create(Hungarian, true)).Take(15).ToArray();
        var dates = invoices.Select(invoice => invoice.Date).Where(date => date.Length > 0).Order(StringComparer.Ordinal).ToArray();
        return snapshot with
        {
            PurchaseNet = invoices.Sum(invoice => invoice.Net), PurchaseGross = invoices.Sum(invoice => invoice.Gross),
            First = dates.FirstOrDefault(), Last = dates.LastOrDefault(), Frequent = frequent, Invoices = invoices
        };
    }

    public static PartnerDossierView Apply(PartnerDossierView view, PartnerDossierSnapshot? snapshot, bool period = false)
    {
        if (snapshot is null || snapshot.PartnerId != view.PartnerId) return view;
        if (snapshot.Invoices is not { Length: > 0 })
            return period ? view with
            {
                FiguresAvailable = true,
                Cards = view.Cards.Select(card => card.Id switch
                {
                    "purchase" => card with { Value = Money(0), Status = Ready },
                    "incoming" => card with { Value = "0", Status = Ready },
                    _ => card
                }).ToArray(),
                Sections = view.Sections.Select(section => section.Id == "incoming"
                    ? section with { Status = Ready, Detail = "Ebben az időszakban nincs bejövő számla.", Lines = [] } : section).ToArray(),
                Notice = "Ebben az időszakban nincs bejövő számla."
            } : view;
        var cards = view.Cards.Select(card => card.Id switch
        {
            "purchase" => card with { Value = Money(snapshot.PurchaseGross), Status = Ready },
            "incoming" => card with { Value = snapshot.Invoices.Length.ToString(Hungarian), Status = Ready },
            _ => card
        }).ToArray();
        var lines = snapshot.Frequent.Take(6).Select(item => new PartnerDossierLine(item.Name, "gyakori tétel · " + Quantity(item.Quantity) + " db", Money(item.Net), []))
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
            Notice = "A beszerzés és a bejövő számlák a partner számláiból jönnek. Kimenő forgalom, árajánlat, díjbekérő és megrendelés ennél a partnernél nincs."
        };
    }

    public static PartnerDossierSnapshot? FromReceipts(string partnerDirectory, Guid partnerId)
    {
        var modules = Directory.GetParent(partnerDirectory)?.FullName;
        if (modules is null) return null;
        var file = Path.Combine(modules, "receiving", "receipts.json");
        if (!File.Exists(file)) return null;
        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("A bevételezés hivatkozás, ezért a dosszié nem olvassa.");
        if (new FileInfo(file).Length > 64 * 1024 * 1024) throw new IOException("A bevételezés túl nagy.");
        JsonDocument document;
        try { document = JsonDocument.Parse(File.ReadAllText(file)); }
        catch (JsonException) { throw new IOException("A bevételezés sérült, a dosszié nem tudja olvasni."); }
        using (document)
        {
            if (!document.RootElement.TryGetProperty("receipts", out var rows) || rows.ValueKind != JsonValueKind.Array) return null;
            var invoices = new List<PartnerDossierInvoice>();
            foreach (var row in rows.EnumerateArray())
            {
                if (!row.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) continue;
                if (!Guid.TryParse(JsonText(data, "supplierId"), out var supplier) || supplier != partnerId) continue;
                var items = new List<PartnerDossierItem>();
                if (data.TryGetProperty("lines", out var lines) && lines.ValueKind == JsonValueKind.Array)
                {
                    foreach (var line in lines.EnumerateArray())
                    {
                        var quantity = JsonAmount(line, "quantity");
                        items.Add(new(JsonText(line, "name"), quantity, quantity * JsonAmount(line, "netUnitPrice")));
                    }
                }
                invoices.Add(new(JsonText(data, "number"), JsonText(data, "issuedOn"), JsonAmount(data, "net"), JsonAmount(data, "gross"),
                    false, ReceiptStatus(JsonText(data, "status")), items.ToArray()));
            }
            if (invoices.Count == 0) return null;
            var received = invoices.Where(invoice => invoice.Status == "Bevételezve").ToArray();
            var frequent = received.SelectMany(invoice => invoice.Items)
                .GroupBy(item => item.Name, StringComparer.Create(Hungarian, true))
                .Select(group => new PartnerDossierItem(group.First().Name, group.Sum(item => item.Quantity), group.Sum(item => item.Net)))
                .OrderByDescending(item => item.Quantity).Take(6).ToArray();
            var dates = invoices.Select(invoice => invoice.Date).Where(date => date.Length > 0).Order(StringComparer.Ordinal).ToArray();
            return new(partnerId, received.Sum(invoice => invoice.Net), received.Sum(invoice => invoice.Gross),
                dates.FirstOrDefault(), dates.LastOrDefault(), frequent,
                invoices.OrderByDescending(invoice => invoice.Date, StringComparer.Ordinal).ToArray());
        }
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
        var paid = invoice.Paid ? "fizetve" : "nincs fizetve";
        var detail = string.Join(" · ", new[] { invoice.Date, paid, invoice.Status, invoice.Items.Length + " tétel" }.Where(part => part.Length > 0));
        var items = invoice.Items.Select(item => new PartnerDossierItemView(item.Name, Quantity(item.Quantity) + " db", Money(item.Net))).ToArray();
        return new(invoice.Number.Length > 0 ? invoice.Number : "számla", detail, Money(invoice.Gross), items);
    }

    private static string JsonText(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";
    private static decimal JsonAmount(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var amount)
            ? amount : 0;
    private static string ReceiptStatus(string status) => status switch
    {
        "uj" => "Új, besorolásra vár",
        "bevetelezve" => "Bevételezve",
        "koltseg" => "Költség",
        "fuggo" => "Függő",
        _ => status
    };
    private static DateOnly? Day(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new ArgumentException(label + ": éééé-hh-nn formátum kell.");
        return date;
    }

    private static bool InPeriod(string value, DateOnly? from, DateOnly? to) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        && (from is null || date >= from) && (to is null || date <= to);

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
