using System.Globalization;

namespace PcImpressz.Modules.Receiving;

public sealed record NavUnknownSupplier(string TaxNumber, string Name, string InvoiceNumber);
public sealed record NavPullResult(string From, string To, int Found, string[] Imported, int Skipped,
    NavUnknownSupplier[] UnknownSuppliers, string[] Errors, string[] XmlFeeds, bool Truncated);

public static class NavIntake
{
    public static (DateOnly From, DateOnly To) Range(string? from, string? to, DateOnly today)
    {
        var end = Parse(to, today, "A záró nap");
        var start = Parse(from, end.AddDays(-30), "A kezdő nap");
        if (start > end) throw new ArgumentException("A kezdő nap későbbi, mint a záró nap.");
        if (end.DayNumber - start.DayNumber > 366) throw new ArgumentException("Legfeljebb egy évnyi tartomány kérdezhető le egyszerre.");
        return (start, end);
    }

    public static async Task<NavPullResult> PullAsync(ReceivingRepository repository, NavConnectionSecrets secrets, Guid company,
        string? from, string? to, DateOnly today, string actor, CancellationToken ct, HttpClient? client = null)
    {
        var (start, end) = Range(from, to, today);
        var pages = new List<NavDigestInvoice>();
        var truncated = false;
        var available = 1;
        for (var page = 1; page <= available && page <= 20; page++)
        {
            var digest = await NavInvoiceClient.QueryDigestAsync(secrets, start, end, page, ct, client);
            available = digest.AvailablePage;
            pages.AddRange(digest.Invoices);
            if (page == 20 && available > 20) truncated = true;
        }
        var state = await repository.Snapshot(company, ct);
        var known = state.Receipts.Select(receipt => Key(receipt.Data.SupplierTaxId, receipt.Data.Number)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? partners = null;
        var imported = new List<string>();
        var unknown = new List<NavUnknownSupplier>();
        var errors = new List<string>();
        var feeds = new List<string>();
        var skipped = 0;
        foreach (var invoice in pages)
        {
            ct.ThrowIfCancellationRequested();
            var operation = invoice.Operation.Length == 0 ? "CREATE" : invoice.Operation;
            if (operation is not ("CREATE" or "MODIFY")) { skipped++; continue; }
            if (known.Contains(Key(invoice.SupplierTaxNumber, invoice.Number))) { skipped++; continue; }
            if (!await repository.KnowsSupplier(company, invoice.SupplierTaxNumber, ct))
            {
                unknown.Add(new(ReceivingText.TaxKey(invoice.SupplierTaxNumber), invoice.SupplierName, invoice.Number));
                continue;
            }
            try
            {
                var xml = await NavInvoiceClient.QueryInvoiceAsync(secrets, invoice.Number, invoice.SupplierTaxNumber, ct, client);
                var document = ReceivingImport.FromNav(xml);
                partners ??= await repository.ReadPartnersAsync(company, ct);
                var link = SupplierXmlLinks.Find(partners, invoice.SupplierTaxNumber);
                var enriched = await SupplierXmlLinks.EnrichAsync(document, link, ct, client);
                if (enriched.Note.Length > 0) feeds.Add(invoice.SupplierName + " · " + invoice.Number + ": " + enriched.Note);
                var saved = await repository.Import(new(company, null, null, null, null, "nav", invoice.Number + ".xml", null, enriched.Note), enriched.Document, actor, ct);
                imported.Add(saved.Data.SupplierName + " · " + saved.Data.Number);
                known.Add(Key(saved.Data.SupplierTaxId, saved.Data.Number));
            }
            catch (Exception ex) when ((ex is ArgumentException or HttpRequestException or IOException or TaskCanceledException) && !ct.IsCancellationRequested)
            {
                errors.Add(invoice.Number + ": " + ex.Message);
            }
        }
        return new(start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            pages.Count, imported.ToArray(), skipped, unknown.ToArray(), errors.ToArray(), feeds.ToArray(), truncated);
    }

    private static DateOnly Parse(string? value, DateOnly fallback, string label)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) return fallback;
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            throw new ArgumentException(label + " éééé-hh-nn formátumú legyen.");
        return parsed;
    }

    private static string Key(string? tax, string? number) => ReceivingText.TaxKey(tax) + "|" + (number ?? "").Trim();
}
