using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PcImpressz.Modules.Receiving;

public static class ReceivingImport
{
    private const int MaximumBytes = 1_500_000;
    private const int ExpandedLimit = 8 * 1024 * 1024;
    private static XmlReaderSettings XmlSettings() => new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = ExpandedLimit };
    public static ImportedDocument Read(string? fileName, string? contentBase64)
    {
        var name = (fileName ?? "").Trim();
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (extension == ".pdf" || string.Equals(name, "pdf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A PDF-ből a tételsorok kiolvasása a következő kör. Most Excel (xlsx), CSV vagy NAV-számla XML olvasható.");
        if (extension == ".xls")
            throw new ArgumentException("A régi .xls helyett mentsd a fájlt xlsx vagy csv formátumba.");
        if ((contentBase64?.Length ?? 0) > 2_000_004) throw new ArgumentException("A fájl nagyobb 1,5 MB-nál.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(contentBase64 ?? ""); }
        catch (FormatException) { throw new ArgumentException("A fájl tartalma nem olvasható."); }
        if (bytes.Length is < 1 or > MaximumBytes) throw new ArgumentException("A fájl üres vagy nagyobb 1,5 MB-nál.");
        return extension switch
        {
            ".csv" or ".txt" => FromTable(ParseCsv(Encoding.UTF8.GetString(bytes))),
            ".xlsx" => FromTable(ParseXlsx(bytes)),
            ".xml" => FromNav(Encoding.UTF8.GetString(bytes)),
            _ => throw new ArgumentException("Excel (xlsx), CSV vagy NAV-számla XML tölthető be.")
        };
    }
    public static ImportedDocument ReadSupplement(string? fileName, string? contentBase64)
    {
        var extension = Path.GetExtension((fileName ?? "").Trim()).ToLowerInvariant();
        var maximum = extension == ".pdf" ? 8 * 1024 * 1024 : MaximumBytes;
        if ((contentBase64?.Length ?? 0) > 12_000_000) throw new ArgumentException("A fájl nagyobb a megengedett méretnél.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(contentBase64 ?? ""); }
        catch (FormatException) { throw new ArgumentException("A fájl tartalma nem olvasható."); }
        if (bytes.Length < 1 || bytes.Length > maximum)
            throw new ArgumentException(extension == ".pdf" ? "A fájl üres vagy nagyobb 8 MB-nál." : "A fájl üres vagy nagyobb 1,5 MB-nál.");
        var xml = extension switch
        {
            ".xml" => NavInvoiceClient.InvoiceXml(Encoding.UTF8.GetString(bytes)),
            ".pdf" => EmbeddedInvoice(bytes),
            _ => throw new ArgumentException("A hiányzó adat XML-ből vagy PDF-ből tölthető be.")
        };
        return FromNav(xml);
    }
    public static ImportedDocument FromTable(string[][] rows)
    {
        if (rows.Length is < 2 or > 401 || rows.Any(row => row.Length > 64 || row.Any(cell => cell.Length > 10000))) throw new ArgumentException("A táblában fejléc és legalább egy tétel kell.");
        var header = rows[0].Select(cell => ReceivingText.Compact(cell)).ToArray();
        int Column(params string[] names)
        {
            for (var index = 0; index < header.Length; index++)
                if (names.Any(name => header[index] == ReceivingText.Compact(name))) return index;
            return -1;
        }
        var name = Column("termék neve", "termek neve", "megnevezés", "megnevezes", "név", "nev");
        var quantity = Column("mennyiség", "mennyiseg");
        var price = Column("nettó egységár", "netto egysegar", "nettó ár", "netto ar");
        if (name < 0 || quantity < 0 || price < 0)
            throw new ArgumentException("Hiányzó oszlop. Kell: termék neve, mennyiség, nettó egységár. Talált fejléc: " + string.Join(", ", rows[0]));
        var vat = Column("áfa", "áfa %", "afakulcs", "vat");
        if (vat < 0) throw new ArgumentException("Az importhoz explicit áfa oszlop kell (0, 5, 18, 27). Hiányzó értéket nem találunk ki.");
        var unit = Column("mértékegység", "egység", "unit");
        var service = Column("szolgáltatás", "service");
        var sku = Column("saját cikkszám (belső id)", "sajat cikkszam", "cikkszám", "cikkszam");
        var mpn = Column("gyártói cikkszám", "gyartoi cikkszam");
        var barcode = Column("vonalkód / ean", "vonalkod", "ean");
        var lines = new List<ImportedLine>();
        for (var rowIndex = 1; rowIndex < rows.Length; rowIndex++)
        {
            var row = rows[rowIndex];
            var title = Cell(row, name);
            if (title.Length == 0) continue;
            var own = Cell(row, sku);
            var maker = Cell(row, mpn);
            lines.Add(new(title, own.Length > 0 ? own : maker, ReceivingText.Digits(Cell(row, barcode)), Number(Cell(row, quantity), "Mennyiség"), Number(Cell(row, price), "Nettó egységár"), Number(Cell(row, vat), "Áfa"),
                unit < 0 ? "DB" : Cell(row, unit), service >= 0 && Service(Cell(row, service))));
        }
        if (lines.Count == 0) throw new ArgumentException("A fájlban nincs tételsor.");
        return new(null, null, null, null, null, lines.ToArray());
    }
    public static ImportedDocument FromNav(string xml)
    {
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), XmlSettings());
            document = XDocument.Load(reader);
        }
        catch (Exception ex) when (ex is XmlException or IOException) { throw new ArgumentException("A NAV-számla XML nem olvasható."); }
        if (Header(document, "currencyCode") != "HUF") throw new ArgumentException("Csak explicit HUF pénznemű NAV XML támogatott.");
        var lines = document.Descendants().Where(element => element.Name.LocalName.Equals("line", StringComparison.OrdinalIgnoreCase))
            .Select(ParseLine).Where(line => line is not null).Cast<ImportedLine>().ToArray();
        if (lines.Length == 0)
            throw new ArgumentException("Az XML-ben nincs NAV-tételsor (<line>). Katalógusfájl vagy más nagyker formátuma nem számla.");
        return new(Header(document, "invoiceNumber"), Date(Header(document, "invoiceIssueDate")), Date(Header(document, "paymentDate")),
            Header(document, "supplierName"), Header(document, "supplierTaxNumber"), lines, "HUF",
            Summary(document,"invoiceNetAmount"),Summary(document,"invoiceVatAmount"),Summary(document,"invoiceGrossAmount"));
    }
    private static decimal? Summary(XDocument document,string name)
    {
        var text=Header(document,name);
        return text.Length==0?null:Number(text,"Számlaösszesítő");
    }
    private static ImportedLine? ParseLine(XElement line)
    {
        var name = Text(line, "lineDescription");
        if (name.Length == 0) return null;
        var codes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in line.Descendants().Where(element => element.Name.LocalName.Equals("productCode", StringComparison.OrdinalIgnoreCase)))
        {
            var category = Text(code, "productCodeCategory");
            var value = Text(code, "productCodeValue");
            if (value.Length == 0) value = Text(code, "productCodeOwnValue");
            if (category.Length > 0 && value.Length > 0) codes.TryAdd(category, value);
        }
        var barcode = codes.TryGetValue("EAN", out var ean) ? ean : codes.TryGetValue("GTIN", out var gtin) ? gtin : "";
        var sku = codes.TryGetValue("OWN", out var own) ? own : codes.TryGetValue("OTHER", out var other) ? other : "";
        var vatText = Text(line, "vatPercentage");
        if (vatText.Length == 0 || line.Descendants().Any(e => e.Name.LocalName is "vatExemption" or "vatOutOfScope" or "vatDomesticReverseCharge"))
            throw new ArgumentException("A NAV-tétel áfakezelése hiányzik vagy még nem támogatott.");
        var unit = Text(line, "unitOfMeasure");
        unit = unit switch { "" or "PIECE" => "DB", "OWN" => Text(line, "unitOfMeasureOwn"), _ => unit };
        return new(name, sku, ReceivingText.Digits(barcode), Number(Text(line, "quantity"), "Mennyiség"), Number(Text(line, "unitPrice"), "Nettó egységár"),
            Number(vatText, "Áfa") * 100m, unit);
    }
    private static string Header(XDocument document, string name) =>
        document.Descendants().FirstOrDefault(element => element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)
            && !element.Ancestors().Any(ancestor => ancestor.Name.LocalName.Equals("line", StringComparison.OrdinalIgnoreCase)))?.Value.Trim() ?? "";
    private static string Text(XElement scope, string name) =>
        scope.Descendants().FirstOrDefault(element => element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "";
    private static string? Date(string value)
    {
        var text = value.Trim();
        if (text.Length == 0) return null;
        if (text.Length >= 10 && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
            return iso.ToString("yyyy-MM-dd");
        return ReceivingText.Date(text, "A számla kelte", false) is { Length: > 0 } parsed ? parsed : null;
    }
    private static string Cell(string[] row, int index) => index >= 0 && index < row.Length ? row[index].Trim() : "";
    private static decimal Number(string text, string label)
    {
        var value = text.Trim().Replace(" ", "").Replace("\u00a0", "");
        if (value.Contains(',') && value.Contains('.')) throw new ArgumentException(label + ": használj tizedespontot vagy tizedesvesszőt, ezreselválasztó nélkül.");
        else if (value.Contains(',')) value = value.Replace(',', '.');
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            throw new ArgumentException(label + ": számot adj meg.");
        return parsed;
    }
    private static bool Service(string value) => ReceivingText.Fold(value) switch
    {
        "igen" or "true" or "1" => true, "" or "nem" or "false" or "0" => false,
        _ => throw new ArgumentException("A szolgáltatás oszlop igen/nem értéket kér.")
    };
    private static string EmbeddedInvoice(byte[] bytes)
    {
        foreach (var encoding in new Encoding[] { Encoding.UTF8, Encoding.Latin1 })
        {
            var slice = InvoiceSlice(encoding.GetString(bytes));
            if (slice is not null) return slice;
        }
        throw new ArgumentException("A PDF-ben nincs olvasható számla-XML. A képi tételsorok olvasása még nincs kész; XML-ből töltsd be a hiányzó adatot.");
    }
    private static string? InvoiceSlice(string text)
    {
        foreach (var name in new[] { "InvoiceData", "Invoice" })
        {
            var start = OpenTag(text, name);
            if (start < 0) continue;
            var end = CloseTag(text, name, start);
            if (end < 0) continue;
            var length = end - start;
            if (length is < 1 or > 2 * 1024 * 1024) throw new ArgumentException("A PDF-be ágyazott számla-XML nagyobb 2 MB-nál.");
            var slice = text.Substring(start, length);
            if (slice.Contains("invoiceNumber", StringComparison.OrdinalIgnoreCase) || slice.Contains("<line", StringComparison.OrdinalIgnoreCase))
                return slice;
        }
        return null;
    }
    private static int OpenTag(string text, string localName)
    {
        var cursor = 0;
        while (cursor < text.Length)
        {
            var at = text.IndexOf(localName, cursor, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return -1;
            var start = text.LastIndexOf('<', at);
            if (start >= 0 && TagBoundary(text, start, at, localName)) return start;
            cursor = at + localName.Length;
        }
        return -1;
    }
    private static int CloseTag(string text, string localName, int from)
    {
        var cursor = from + 1;
        while (cursor < text.Length)
        {
            var at = text.IndexOf(localName, cursor, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return -1;
            var start = text.LastIndexOf('<', at);
            if (start > from && start + 1 < text.Length && text[start + 1] == '/' && TagBoundary(text, start, at, localName))
            {
                var close = text.IndexOf('>', at + localName.Length);
                return close < 0 ? -1 : close + 1;
            }
            cursor = at + localName.Length;
        }
        return -1;
    }
    private static bool TagBoundary(string text, int start, int nameAt, string localName)
    {
        if (nameAt + localName.Length > text.Length) return false;
        var after = nameAt + localName.Length;
        if (after < text.Length && char.IsLetterOrDigit(text[after])) return false;
        var prefix = text[(start + 1)..nameAt].TrimStart('/');
        return prefix.Length == 0 || prefix.EndsWith(':') && prefix[..^1].All(character => char.IsLetterOrDigit(character) || character is '_' or '-');
    }
    private static string[][] ParseCsv(string text)
    {
        var first = text.Split('\n')[0];
        var separator = first.Contains(';') ? ";" : first.Contains('\t') ? "\t" : ",";
        using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(new StringReader(text.TrimStart('\uFEFF')))
        { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
        parser.SetDelimiters(separator);
        var rows = new List<string[]>();
        try
        {
            while (!parser.EndOfData)
            {
                var fields = parser.ReadFields() ?? [];
                if (rows.Count >= 401 || fields.Length > 64 || fields.Any(v => v.Length > 10000))
                    throw new ArgumentException("A táblázat legfeljebb 400 tételt és 64 oszlopot tartalmazhat.");
                rows.Add(fields);
            }
        }
        catch (Microsoft.VisualBasic.FileIO.MalformedLineException) { throw new ArgumentException("Hibás CSV-idézőjelezés."); }
        return rows.ToArray();
    }
    private static string[][] ParseXlsx(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Count > 128 || zip.Entries.Any(e => e.Length > ExpandedLimit)
            || zip.Entries.Sum(e => e.Length) > ExpandedLimit)
            throw new ArgumentException("Az Excel kibontott mérete túl nagy.");
        var shared = ReadShared(zip.GetEntry("xl/sharedStrings.xml"));
        var sheet = zip.Entries.FirstOrDefault(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
            && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Az Excel-fájlban nincs munkalap.");
        using var reader = XmlReader.Create(sheet.Open(), XmlSettings());
        var document = XDocument.Load(reader);
        var rows = new List<string[]>();
        foreach (var row in document.Descendants().Where(element => element.Name.LocalName == "row"))
        {
            if (rows.Count >= 401) throw new ArgumentException("Az Excel legfeljebb 400 tételt tartalmazhat.");
            var cells = new List<string>();
            foreach (var cell in row.Elements().Where(element => element.Name.LocalName == "c"))
            {
                var column = ColumnIndex(cell.Attribute("r")?.Value);
                if (column >= 64 || cells.Count >= 64) throw new ArgumentException("Az Excel legfeljebb 64 oszlopot tartalmazhat.");
                while (cells.Count < column) cells.Add("");
                cells.Add(CellValue(cell, shared));
            }
            rows.Add(cells.ToArray());
        }
        return rows.ToArray();
    }
    private static string[] ReadShared(ZipArchiveEntry? entry)
    {
        if (entry is null) return [];
        using var reader = XmlReader.Create(entry.Open(), XmlSettings());
        var document = XDocument.Load(reader);
        return document.Descendants().Where(element => element.Name.LocalName == "si")
            .Select(item => string.Concat(item.Descendants().Where(element => element.Name.LocalName == "t").Select(element => element.Value))).ToArray();
    }
    private static string CellValue(XElement cell, string[] shared)
    {
        var type = cell.Attribute("t")?.Value;
        if (type == "inlineStr") return string.Concat(cell.Descendants().Where(element => element.Name.LocalName == "t").Select(element => element.Value));
        var raw = cell.Elements().FirstOrDefault(element => element.Name.LocalName == "v")?.Value ?? "";
        if (type == "s" && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < shared.Length)
            return shared[index];
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) && number == decimal.Truncate(number) && number < 100_000_000_000_000m)
            return decimal.Truncate(number).ToString(CultureInfo.InvariantCulture);
        return raw;
    }
    private static int ColumnIndex(string? reference)
    {
        var index = 0;
        foreach (var character in reference ?? "")
        {
            if (!char.IsLetter(character)) break;
            if (index > 64) throw new ArgumentException("Túl nagy oszlopazonosító.");
            index = index * 26 + char.ToUpperInvariant(character) - 'A' + 1;
        }
        return Math.Max(0, index - 1);
    }
}
