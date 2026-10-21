using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace PcImpressz.Modules.Receiving;

public static class InvoicePdf
{
    public static string ReadText(string? contentBase64)
    {
        if (contentBase64 is null || contentBase64.Length > 2_000_004)
            throw new ArgumentException("Legfeljebb 1,5 MB-os PDF tölthető be.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(contentBase64); }
        catch (FormatException) { throw new ArgumentException("A PDF tartalma nem olvasható."); }
        if (bytes.Length is < 5 or > 1_500_000 || Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-")
            throw new ArgumentException("Érvényes, legfeljebb 1,5 MB-os PDF kell.");
        try
        {
            using var pdf = PdfDocument.Open(bytes);
            if (pdf.NumberOfPages is < 1 or > 30) throw new ArgumentException("Legfeljebb 30 oldalas PDF támogatott.");
            var text = new StringBuilder();
            foreach (var page in pdf.GetPages())
            {
                var value = ContentOrderTextExtractor.GetText(page);
                if (text.Length + value.Length > 100_000) throw new ArgumentException("A PDF túl sok szöveget tartalmaz.");
                text.AppendLine(value);
            }
            if (string.IsNullOrWhiteSpace(text.ToString()))
                throw new ArgumentException("A PDF nem tartalmaz kiolvasható szöveget. Szkennelt számlához NAV-lekérés vagy kézi bevitel szükséges; OCR még nincs.");
            return text.ToString().Trim();
        }
        catch (Exception ex) when (ex is not ArgumentException and not OutOfMemoryException and not OperationCanceledException)
        { throw new ArgumentException("A PDF sérült, titkosított vagy nem támogatott. Használj NAV-lekérést vagy kézi bevitelt."); }
    }
}
