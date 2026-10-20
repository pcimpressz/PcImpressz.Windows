using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PcImpressz.Modules.Receiving;

public static class ReceivingImport
{
    private const int MaximumBytes = 1_500_000;
    public static ImportedDocument Read(string? fileName, string? contentBase64)
    {
        var name = (fileName ?? "").Trim();
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (extension == ".pdf" || string.Equals(name, "pdf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A PDF-ből a tételsorok kiolvasása a következő kör. Most Excel (xlsx), CSV vagy NAV-számla XML olvasható.");
        if (extension == ".xls")
            throw new ArgumentException("A régi .xls helyett mentsd a fájlt xlsx vagy csv formátumba.");
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
    public static ImportedDocument FromTable(string[][] rows)
    {
        if (rows.Length < 2) throw new ArgumentException("A táblában fejléc és legalább egy tétel kell.");
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
            lines.Add(new(title, own.Length > 0 ? own : maker, ReceivingText.Digits(Cell(row, barcode)), Number(Cell(row, quantity), "Mennyiség"), Number(Cell(row, price), "Nettó egységár")));
        }
        if (lines.Count == 0) throw new ArgumentException("A fájlban nincs tételsor.");
        return new(null, null, null, null, null, lines.ToArray());
    }
    public static ImportedDocument FromNav(string xml)
    {
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (Exception ex) when (ex is XmlException or IOException) { throw new ArgumentException("A NAV-számla XML nem olvasható."); }
        var lines = document.Descendants().Where(element => element.Name.LocalName.Equals("line", StringComparison.OrdinalIgnoreCase))
            .Select(ParseLine).Where(line => line is not null).Cast<ImportedLine>().ToArray();
        if (lines.Length == 0)
            throw new ArgumentException("Az XML-ben nincs NAV-tételsor (<line>). Katalógusfájl vagy más nagyker formátuma nem számla.");
        return new(Header(document, "invoiceNumber"), Date(Header(document, "invoiceIssueDate")), Date(Header(document, "paymentDate")),
            Header(document, "supplierName"), Header(document, "supplierTaxNumber"), lines);
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
        return new(name, sku, ReceivingText.Digits(barcode), Number(Text(line, "quantity"), "Mennyiség"), Number(Text(line, "unitPrice"), "Nettó egységár"));
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
        if (value.Contains(',') && value.Contains('.')) value = value.Replace(".", "").Replace(',', '.');
        else if (value.Contains(',')) value = value.Replace(',', '.');
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            throw new ArgumentException(label + ": számot adj meg.");
        return parsed;
    }
    private static string[][] ParseCsv(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) throw new ArgumentException("A CSV üres.");
        var separator = lines[0].Contains(';') ? ';' : lines[0].Contains('\t') ? '\t' : ',';
        return lines.Select(line => Split(line, separator)).ToArray();
    }
    private static string[] Split(string line, char separator)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (quoted)
            {
                if (character == '"')
                {
                    if (index + 1 < line.Length && line[index + 1] == '"') { current.Append('"'); index++; }
                    else quoted = false;
                }
                else current.Append(character);
            }
            else if (character == '"') quoted = true;
            else if (character == separator) { cells.Add(current.ToString()); current.Clear(); }
            else current.Append(character);
        }
        cells.Add(current.ToString());
        return cells.ToArray();
    }
    private static string[][] ParseXlsx(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var shared = ReadShared(zip.GetEntry("xl/sharedStrings.xml"));
        var sheet = zip.Entries.FirstOrDefault(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
            && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Az Excel-fájlban nincs munkalap.");
        using var reader = XmlReader.Create(sheet.Open(), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var document = XDocument.Load(reader);
        var rows = new List<string[]>();
        foreach (var row in document.Descendants().Where(element => element.Name.LocalName == "row"))
        {
            var cells = new List<string>();
            foreach (var cell in row.Elements().Where(element => element.Name.LocalName == "c"))
            {
                var column = ColumnIndex(cell.Attribute("r")?.Value);
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
        using var reader = XmlReader.Create(entry.Open(), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
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
            index = index * 26 + char.ToUpperInvariant(character) - 'A' + 1;
        }
        return Math.Max(0, index - 1);
    }
}
