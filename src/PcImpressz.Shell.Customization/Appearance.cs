using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcImpressz.Shell.Customization;

public sealed record SkinPalette(string Background, string Surface, string Elevated, string Sidebar,
    string Stroke, string Text, string Muted, string Accent);
public sealed record SkinDefinition(int SchemaVersion, string Id, string Name, SkinPalette Palette);

public static class BuiltInSkins
{
    public static IReadOnlyList<SkinDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        new SkinDefinition(1, "midnight", "Éjkék", new("#0D121A", "#141C27", "#1D2837", "#101721",
            "#2A3748", "#EDF3FA", "#A2B1C5", "#38D9BC")),
        new SkinDefinition(1, "paper", "Porcelán", new("#F2F5F9", "#FFFFFF", "#E8EEF5", "#FFFFFF",
            "#D7DFE9", "#162334", "#54657B", "#007F71")),
        new SkinDefinition(1, "graphite", "Grafit", new("#151518", "#1D1D22", "#292930", "#19191D",
            "#383842", "#F1EFF8", "#B0AEBE", "#B6A0FF"))
    });
}

public sealed record AppearancePreferences
{
    public int InvoicePageSize {get;init;}=20;
    public string LabelPrinter {get;init;}="Niimbot D110-M Bluetooth";
    public string NavReceivedColor {get;init;}="#D9F2DC";
    public string NavPendingColor {get;init;}="#FFFFFF";
    public bool FormatPartnerNames {get;init;}=true;
    public int MoneyDecimals { get; init; } = 0;
    public int SchemaVersion { get; init; } = 1;
    public string SkinId { get; init; } = "midnight";
    public string? AccentOverride { get; init; }
    public bool CollapsedNavigation { get; init; }
    public bool CompactDensity { get; init; }
    public double FontScale { get; init; } = 1;
    public double WindowWidth { get; init; } = 1440;
    public double WindowHeight { get; init; } = 920;
    public Guid? SelectedCompanyId { get; init; }
}

public static class SkinValidation
{
    public static bool IsColor(string? value) => value is { Length: 7 } && value[0] == '#'
        && value.AsSpan(1).ToArray().All(char.IsAsciiHexDigit);
    public static bool IsId(string? value) => value is { Length: > 0 and <= 60 }
        && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    public static void Validate(SkinDefinition skin)
    {
        if (skin.SchemaVersion != 1 || !IsId(skin.Id) || string.IsNullOrWhiteSpace(skin.Name)
            || skin.Name.Length > 40 || skin.Name.Any(char.IsControl) || skin.Palette is null)
            throw new InvalidDataException("Érvénytelen skinleírás.");
        var p = skin.Palette;
        if (new[] { p.Background, p.Surface, p.Elevated, p.Sidebar, p.Stroke, p.Text, p.Muted, p.Accent }.Any(c => !IsColor(c)))
            throw new InvalidDataException("A skin színei #RRGGBB formátumúak legyenek.");
        foreach (var surface in new[] { p.Background, p.Surface, p.Elevated, p.Sidebar })
        {
            if (Contrast(p.Text, surface) < 4.5 || Contrast(p.Muted, surface) < 3)
                throw new InvalidDataException("A szöveg és a háttér kontrasztja túl alacsony.");
        }
    }

    public static double Contrast(string a, string b)
    {
        static double Luminance(string value)
        {
            double Channel(int offset)
            {
                var n = Convert.ToInt32(value.Substring(offset, 2), 16) / 255d;
                return n <= 0.04045 ? n / 12.92 : Math.Pow((n + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
        }
        var first = Luminance(a); var second = Luminance(b);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    public static string AccentForeground(string accent) =>
        Contrast(accent, "#FFFFFF") >= Contrast(accent, "#10151D") ? "#FFFFFF" : "#10151D";
}

public sealed class AppearanceStore(string directory)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public string? LastWarning { get; private set; }

    public AppearancePreferences Load()
    {
        var path = Path.Combine(directory, "appearance.json");
        if (!File.Exists(path)) return new();
        try
        {
            if (new FileInfo(path).Length > 16_384) throw new InvalidDataException("Túl nagy beállításfájl.");
            var value = JsonSerializer.Deserialize<AppearancePreferences>(File.ReadAllText(path), Options)
                ?? throw new InvalidDataException("Üres beállításfájl.");
            ValidatePreferences(value);
            return value;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            LastWarning = "A mentett megjelenés nem tölthető be; alapbeállításokat használunk.";
            return new();
        }
    }

    public void Save(AppearancePreferences value)
    {
        ValidatePreferences(value);
        WriteAtomic(Path.Combine(directory, "appearance.json"), value);
    }

    public IReadOnlyList<SkinDefinition> ReadSkins()
    {
        var result = BuiltInSkins.All.ToList();
        var skinDirectory = Path.Combine(directory, "skins");
        if (!Directory.Exists(skinDirectory)) return result;
        foreach (var file in Directory.EnumerateFiles(skinDirectory, "*.json").Take(50))
        {
            try
            {
                var skin = ReadSkin(file);
                if (result.All(s => s.Id != skin.Id)) result.Add(skin);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
            { LastWarning = "Egy hibás skin kimaradt a betöltésből."; }
        }
        return result;
    }

    public SkinDefinition Import(string path)
    {
        var skin = ReadSkin(path);
        if (BuiltInSkins.All.Any(s => s.Id == skin.Id))
            throw new InvalidDataException("A beépített skin nem írható felül. Adj másik id értéket a saját skinnek.");
        WriteAtomic(Path.Combine(directory, "skins", skin.Id + ".json"), skin);
        return skin;
    }

    public void Export(string path, SkinDefinition skin) { SkinValidation.Validate(skin); WriteAtomic(path, skin); }

    private static SkinDefinition ReadSkin(string path)
    {
        if (new FileInfo(path).Length > 16_384) throw new InvalidDataException("A skinfájl legfeljebb 16 KB lehet.");
        var skin = JsonSerializer.Deserialize<SkinDefinition>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("Üres skinfájl.");
        SkinValidation.Validate(skin);
        return skin;
    }

    private static void ValidatePreferences(AppearancePreferences value)
    {
        if (!SkinValidation.IsColor(value.NavReceivedColor)||!SkinValidation.IsColor(value.NavPendingColor))throw new InvalidDataException("A számlaállapot színe #RRGGBB legyen.");
        if (!new[]{10,20,50,100}.Contains(value.InvoicePageSize)||value.LabelPrinter.Length>200||value.LabelPrinter.Any(char.IsControl))throw new InvalidDataException("Érvénytelen számlalista vagy nyomtatóbeállítás.");
        if (value.SchemaVersion != 1 || !SkinValidation.IsId(value.SkinId)
            || value.AccentOverride is not null && !SkinValidation.IsColor(value.AccentOverride)
            || value.MoneyDecimals is <0 or >4
            || value.FontScale is not (1 or 1.1 or 1.25)
            || !double.IsFinite(value.WindowWidth) || value.WindowWidth is < 1000 or > 4000
            || !double.IsFinite(value.WindowHeight) || value.WindowHeight is < 680 or > 2400)
            throw new InvalidDataException("Érvénytelen megjelenési beállítás.");
    }

    private static void WriteAtomic<T>(string path, T value)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
