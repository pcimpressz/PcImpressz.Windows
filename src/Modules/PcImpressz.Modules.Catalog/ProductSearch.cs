using System.Globalization;
using System.Text;

namespace PcImpressz.Modules.Catalog;

public sealed class ProductSearch
{
    private sealed record Term(string Field, string Value, bool Exact);
    private readonly Term[] _terms;
    public ProductSearch(string? search)
    {
        if (search is null || search.Length > 200 || search.Any(char.IsControl))
            throw new ArgumentException("A keresés legfeljebb 200 karakter lehet.");
        var words = new List<string>(); var current = new StringBuilder(); var quoted = false;
        foreach (var c in search)
        {
            if (c == '"') { quoted = !quoted; continue; }
            if (char.IsWhiteSpace(c) && !quoted)
            { if (current.Length > 0) { words.Add(current.ToString()); current.Clear(); } }
            else current.Append(c);
        }
        if (quoted) throw new ArgumentException("A keresésben zárd le az idézőjelet.");
        if (current.Length > 0) words.Add(current.ToString());
        if (words.Count > 12) throw new ArgumentException("Legfeljebb 12 keresési feltétel adható meg.");
        _terms = words.Select(word =>
        {
            var colon = word.IndexOf(':');
            var field = colon < 0 ? "" : Normalize(word[..colon]).ToLowerInvariant();
            var value = colon < 0 ? word : word[(colon + 1)..];
            if (field is not ("" or "nev" or "sku" or "cikkszam" or "mpn" or "gyartoi" or "vonalkod" or "ean" or "kategoria" or "egyseg" or "revizio"))
                throw new ArgumentException("Ismeretlen keresési mező. Használható: nev, sku, gyartoi, vonalkod, kategoria, egyseg, revizio.");
            var exact = value.StartsWith('=');
            if (exact) value = value[1..];
            if (value.Length == 0) throw new ArgumentException("A keresési mező után adj meg értéket.");
            return new Term(field, Normalize(value), exact);
        }).ToArray();
    }
    public bool Matches(ProductData p) => _terms.All(term => Values(p, term.Field).Any(value =>
        term.Exact ? Normalize(value) == term.Value : Normalize(value).Contains(term.Value, StringComparison.Ordinal)));
    public int Rank(ProductData p) => _terms.Length == 1 &&
        new[] { p.Sku, p.ManufacturerPartNumber, p.Barcode }.Any(value => Normalize(value) == _terms[0].Value) ? 0 : 1;
    private static IEnumerable<string> Values(ProductData p, string field) => field switch
    {
        "nev" => [p.Name], "sku" or "cikkszam" => [p.Sku], "mpn" or "gyartoi" => [p.ManufacturerPartNumber],
        "vonalkod" or "ean" => [p.Barcode], "kategoria" => [p.CategoryCode],
        "egyseg" => [p.UnitCode], "revizio" => [p.Revision],
        _ => [p.Name, p.Sku, p.ManufacturerPartNumber, p.Barcode, p.CategoryCode, p.UnitCode, p.Description, p.Parameters]
    };
    private static string Normalize(string? value)
    {
        var text = (value ?? "").Normalize(NormalizationForm.FormD);
        return new string(text.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
            .Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }
}
