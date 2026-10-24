using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PcImpressz.Shell.Customization;

public sealed record OperationChoice(string Value, string Label);
public sealed record OperationField(string Key, string Label, string Kind, string Help, IReadOnlyList<OperationChoice>? Choices = null);
public sealed record OperationSection(string Title, string Description, IReadOnlyList<OperationField> Fields);
public sealed record OperationGroup(string Id, string Title, string Icon, string Description, IReadOnlyList<OperationSection> Sections);
public sealed record OperationSnapshot(IReadOnlyDictionary<string, string> Values, IReadOnlyDictionary<string, string> SecretHints);
public sealed record LocalBackupResult(string Directory, int FileCount, IReadOnlyList<string> Skipped);

public static class OperationCatalog
{
    public const string Billing = "billing";
    public const string Printer = "printer";
    public const string Backup = "backup";
    public const string Wholesale = "wholesale";
    public const string Mail = "mail";

    public static IReadOnlyList<OperationGroup> Groups { get; } = Build();
    public static IReadOnlySet<string> SecretKeys { get; } = Groups.SelectMany(group => group.Sections)
        .SelectMany(section => section.Fields).Where(field => field.Kind == "secret")
        .Select(field => field.Key).ToHashSet(StringComparer.Ordinal);
    public static IReadOnlyDictionary<string, string> Defaults { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["bizonylat_url"] = "https://api.bizonylat.hu",
        ["szamla_email_kuldes"] = "true",
        ["nyomtato_port"] = "9100",
        ["nyomtato_kodlap"] = "18",
        ["nyomtato_mod"] = "",
        ["smtp_port"] = "587",
        ["smtp_ssl"] = "false",
        ["innpro_shop"] = "5",
        ["innpro_language"] = "hun",
        ["jtc_vision_url"] = "https://jtc.hu/services/vision.asmx",
        ["jtc_soap_namespace"] = "http://www.jtc.hu/services/",
        ["orink_vision_url"] = "https://orink.hu/services/vision.asmx",
        ["orink_soap_namespace"] = "https://orink.hu/services/",
        ["cedrus_vision_url"] = "https://www.cedrus.hu/services/vision.asmx",
        ["cedrus_soap_namespace"] = "http://www.cedrus.hu/services/",
        ["hrp_gateway_url"] = "https://webApi.hrp.hu/Gateway"
    };

    public static OperationGroup? Find(string? id) => Groups.FirstOrDefault(group => group.Id == id);
    public static bool IsPage(string? id) => Find(id) is not null;

    private static IReadOnlyList<OperationGroup> Build()
    {
        OperationField Text(string key, string label, string help) => new(key, label, "text", help);
        OperationField Number(string key, string label, string help) => new(key, label, "number", help);
        OperationField Secret(string key, string label, string help) => new(key, label, "secret", help);
        OperationField Check(string key, string label, string help) => new(key, label, "check", help);
        OperationField Folder(string key, string label, string help) => new(key, label, "folder", help);
        OperationSection Section(string title, string description, params OperationField[] fields) => new(title, description, fields);
        return
        [
            new(Billing, "Számlázás", "\uE8A5",
                "Több számlázó bekötésének a helye. Most a Bizonylat.hu menthető. A NAV-kapcsolat a Beállítások főoldalán marad.",
                [Section("Bizonylat.hu", "A kapcsolat adatai. Számla kiállítása még nincs.",
                    Secret("bizonylat_kulcs", "Bizonylat.hu API kulcs", "A Bizonylat.hu felületén: API menü. Üresen hagyva a mentett kulcs marad."),
                    Text("bizonylat_url", "Bizonylat.hu címe", "Üresen: https://api.bizonylat.hu"),
                    Text("szamla_elotag", "Számla előtag", "Saját számlatömb jelölése, például BOLT. Üresen hagyható."),
                    Check("szamla_email_kuldes", "E-mail küldése a vevőnek", "Bekapcsolva a Bizonylat.hu a vevő címére küldi a számlát, ha a kiállítás később elkészül."))]),
            new(Printer, "Nyomtató", "\uE749", "A blokknyomtató címe és módja. Tesztnyomtatás nincs bekötve.",
                [Section("Blokknyomtató", "Közvetlen módban ez a gép éri el a nyomtatót. Ügynök módban a cím a másik gépen futó programé.",
                    new OperationField("nyomtato_mod", "Mód", "choice", "Üresen a program nem küld a nyomtatóra.",
                    [
                        new("", "Nincs"),
                        new("kozvetlen", "Közvetlen — ez a gép látja a nyomtatót"),
                        new("ugynok", "Ügynök — a nyomtató másik gépen van")
                    ]),
                    Text("nyomtato_ip", "Nyomtató IP-címe", "A Bixolon címe, például 192.168.120.245."),
                    Number("nyomtato_port", "Port", "Üresen vagy nullán: 9100."),
                    Secret("nyomtato_kulcs", "Ügynök kulcsa", "Ugyanez a jelszó kell a másik gépen. Üresen hagyva a mentett kulcs marad."),
                    Number("nyomtato_kodlap", "Kódlap", "18 = CP852. Ha a szöveg hibás, 19-et próbálj."))]),
            new(Backup, "Biztonsági mentés", "\uE74E", "Helyi mentés készítése és a mentés mappája. Külső szolgáltatást nem hív.",
                [Section("Helyi mentés", "A mentés az adatmappa másolata a megadott helyre. A titkosított kapcsolatfájlok is bemásolódnak.",
                    Folder("mentes_mappa", "Mentés helye", "Teljes elérési út, például D:\\PcImpressz\\mentesek. A mappa maga nem lehet az adatmappa."))]),
            new(Wholesale, "Nagykereskedések", "\uE7BF", "Nagyker API- és XML-kapcsolatok. Az élő CompMarket-lekérdezés a Beállítások CompMarket kártyáján mentett VPS-proxyt használja (217.113.49.52). Üres proxy esetén a bolt IP-jéről nem indul kérés.",
                [
                    Section("CompMarket", "Az élő pid, kód és VPS-proxy a Beállítások főoldalának CompMarket kártyáján van. Az itteni mezők nem indítanak kérést.",
                        Secret("compmarket_pid", "Ügyfélazonosító (pid)", "Csak helyben tároljuk."),
                        Secret("compmarket_authcode", "Autentikációs kód", "Üresen hagyva a mentett kód marad.")),
                    Section("Caseking", "Websync ár- és készletadatok.",
                        Secret("caseking_felhasznalo", "Websync felhasználónév", "Üresen hagyva a mentett név marad."),
                        Secret("caseking_jelszo", "Websync jelszó", "Üresen hagyva a mentett jelszó marad.")),
                    Section("Innpro", "IOF termékkatalógus, ár és készlet.",
                        Secret("innpro_client", "Ügyfélazonosító", "Üresen hagyva a mentett azonosító marad."),
                        Secret("innpro_token", "Token", "Üresen hagyva a mentett token marad."),
                        Text("innpro_shop", "Boltazonosító", "Üresen: 5."),
                        Text("innpro_language", "Nyelv", "Üresen: hun.")),
                    Section("Orink", "Vision SOAP kapcsolat.",
                        Text("orink_vision_url", "SOAP cím", "Üresen a dokumentáció szerinti cím."),
                        Secret("orink_pid", "Partnerazonosító (PID)", "Üresen hagyva a mentett érték marad."),
                        Secret("orink_partnerkod", "Partnerkód", "Üresen hagyva a mentett kód marad."),
                        Secret("orink_authcode", "Authcode", "Üresen hagyva a mentett kód marad."),
                        Text("orink_soap_namespace", "SOAP névtér", "Csak újabb WSDL alapján módosítsd.")),
                    Section("JTC", "Vision SOAP kapcsolat.",
                        Text("jtc_vision_url", "SOAP cím", "Üresen a JTC éles címe."),
                        Secret("jtc_pid", "Partnerazonosító (PID)", "Üresen hagyva a mentett érték marad."),
                        Secret("jtc_partnerkod", "Partnerkód", "Üresen hagyva a mentett kód marad."),
                        Secret("jtc_authcode", "Authcode", "Üresen hagyva a mentett kód marad."),
                        Secret("jtc_cegcsopid", "Cégcsoport-azonosító", "Csak ha a JTC megadta, az EAN-lekérdezéshez."),
                        Text("jtc_soap_namespace", "SOAP névtér", "Csak a JTC WSDL-je alapján módosítsd.")),
                    Section("Cédrus", "Vision SOAP katalógus, ár és készlet.",
                        Text("cedrus_vision_url", "SOAP cím", "Üresen a publikus cím."),
                        Secret("cedrus_pid", "Partnerazonosító (PID)", "Üresen hagyva a mentett érték marad."),
                        Secret("cedrus_partnerkod", "Partnerkód", "Üresen hagyva a mentett kód marad."),
                        Secret("cedrus_authcode", "Authcode", "Üresen hagyva a mentett kód marad."),
                        Text("cedrus_soap_namespace", "SOAP névtér", "Csak a Cédrus WSDL-je alapján módosítsd.")),
                    Section("HRP", "JSON Gateway katalógus, ár és készlet. Rendelés nincs bekötve.",
                        Text("hrp_gateway_url", "Gateway cím", "Üresen: https://webApi.hrp.hu/Gateway"),
                        Secret("hrp_client", "Kliensazonosító", "Üresen hagyva a mentett azonosító marad."),
                        Secret("hrp_jelszo", "Jelszó", "Üresen hagyva a mentett jelszó marad.")),
                    Section("PCA Arena", "Használt nagyker feed.",
                        Secret("pca_token", "Partner token", "Üresen hagyva a mentett token marad."))
                ]),
            new(Mail, "E-mail", "\uE715", "A kimenő levelek SMTP kapcsolata. Tesztlevelet nem küld.",
                [Section("SMTP küldés", "A jelszó titkosítva, külön fájlba kerül. Üresen hagyva a mentett jelszó marad.",
                    Text("smtp_host", "SMTP szerver", "A levélküldő szerver címe."),
                    Number("smtp_port", "SMTP port", "Üresen vagy nullán: 587."),
                    Check("smtp_ssl", "SMTP SSL", "587-es portnál kapcsold ki, ott STARTTLS megy. 465-ösnél kapcsold be."),
                    Text("smtp_user", "SMTP felhasználó", "A postafiók felhasználóneve."),
                    Secret("smtp_jelszo", "SMTP jelszó", "Üresen hagyva a mentett jelszó marad."),
                    Text("email_alairas", "Aláíró neve", "A kimenő levél aláírásának első sora."),
                    Text("ceg_web", "Weboldal", "Az aláírásban, például www.pcimpressz.hu."))])
        ];
    }
}

public sealed class OperationSettingsStore(string directory)
{
    public const string SettingsFileName = "operation-settings.json";
    public const string SecretsFileName = "operation-secrets.bin";
    private static readonly HashSet<string> NonZero = new(StringComparer.Ordinal) { "nyomtato_port", "smtp_port" };
    private static readonly HashSet<string> Ports = new(StringComparer.Ordinal) { "nyomtato_port", "smtp_port" };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _settings = Path.Combine(Path.GetFullPath(directory), SettingsFileName);
    private readonly string _secrets = Path.Combine(Path.GetFullPath(directory), SecretsFileName);
    private readonly string _directory = Path.GetFullPath(directory);

    public OperationSnapshot Load() => Project(ReadSettings(), ReadSecrets());

    public string ReadSecret(string key)
    {
        if (!OperationCatalog.SecretKeys.Contains(key)) throw new ArgumentException("Ismeretlen titkos mező.");
        return ReadSecrets().TryGetValue(key, out var value) ? value : "";
    }

    public bool SecretEquals(string key, string expected)
    {
        if (!OperationCatalog.SecretKeys.Contains(key)) return false;
        return ReadSecrets().TryGetValue(key, out var value) && string.Equals(value, expected, StringComparison.Ordinal);
    }

    public IReadOnlyList<string> Save(IReadOnlyDictionary<string, string> submitted)
    {
        var settings = ReadSettings();
        var secrets = ReadSecrets();
        var nextSettings = new Dictionary<string, string>(settings, StringComparer.Ordinal);
        var nextSecrets = new Dictionary<string, string>(secrets, StringComparer.Ordinal);
        foreach (var key in OperationCatalog.SecretKeys) nextSettings.Remove(key);
        var changed = new List<string>();
        var fields = Fields();
        foreach (var (key, raw) in submitted)
        {
            if (!fields.TryGetValue(key, out var field)) continue;
            if (field.Kind == "secret")
            {
                var text = raw.Trim();
                if (text.Length == 0) continue;
                ValidateSecret(text);
                var previous = secrets.TryGetValue(key, out var stored) ? stored : "";
                if (!string.Equals(previous, text, StringComparison.Ordinal))
                {
                    nextSecrets[key] = text;
                    changed.Add(key);
                }
                continue;
            }
            var normalized = Normalize(field, raw);
            var effective = settings.TryGetValue(key, out var current) ? current : Default(key);
            if (!string.Equals(effective, normalized, StringComparison.Ordinal)) changed.Add(key);
            if (!string.Equals(normalized, Default(key), StringComparison.Ordinal) || settings.ContainsKey(key))
                nextSettings[key] = normalized;
            else nextSettings.Remove(key);
        }
        if (!Same(settings, nextSettings)) WriteSettings(nextSettings);
        if (!Same(secrets, nextSecrets)) WriteSecrets(nextSecrets);
        return changed;
    }

    public string CheckFolder(string path)
    {
        var full = RequireFolder(path);
        Directory.CreateDirectory(full);
        var probe = Path.Combine(full, ".irhato_proba");
        File.WriteAllText(probe, "x");
        File.Delete(probe);
        return "A mappa írható.";
    }

    public LocalBackupResult CreateBackup(string folder)
    {
        var text = (folder ?? "").Trim();
        if (text.Length == 0 && Load().Values.TryGetValue("mentes_mappa", out var saved)) text = saved;
        if (text.Length == 0) throw new InvalidDataException("Előbb add meg a mentés helyét.");
        Save(new Dictionary<string, string>(StringComparer.Ordinal) { ["mentes_mappa"] = text });
        return LocalDataBackup.Copy(_directory, Load().Values["mentes_mappa"]);
    }

    private OperationSnapshot Project(Dictionary<string, string> settings, Dictionary<string, string> secrets)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var hints = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in Fields().Values)
        {
            if (field.Kind == "secret")
            {
                var set = secrets.TryGetValue(field.Key, out var secret) && secret.Length > 0;
                var suffix = set && secret!.Length > 8 ? " (…" + secret[^4..] + ")" : "";
                hints[field.Key] = set ? "Be van állítva" + suffix + "." : "Nincs beállítva.";
            }
            else values[field.Key] = settings.TryGetValue(field.Key, out var value) ? value : Default(field.Key);
        }
        return new(values, hints);
    }

    private static Dictionary<string, OperationField> Fields() => OperationCatalog.Groups
        .SelectMany(group => group.Sections).SelectMany(section => section.Fields)
        .ToDictionary(field => field.Key, StringComparer.Ordinal);

    private static string Default(string key) =>
        OperationCatalog.Defaults.TryGetValue(key, out var value) ? value : "";

    private static string Normalize(OperationField field, string raw)
    {
        if (field.Kind == "check")
        {
            var flag = raw.Trim();
            if (flag.Equals("true", StringComparison.OrdinalIgnoreCase)) return "true";
            if (flag.Equals("false", StringComparison.OrdinalIgnoreCase)) return "false";
            throw new InvalidDataException("A(z) " + field.Label + " igen vagy nem lehet.");
        }
        if (field.Kind == "number")
        {
            var text = raw.Trim();
            if (text.Length == 0) return Default(field.Key);
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                throw new InvalidDataException("A(z) " + field.Label + " mezőbe számot írj.");
            if (number == 0 && NonZero.Contains(field.Key)) return Default(field.Key);
            if (Ports.Contains(field.Key) && number is < 1 or > 65535)
                throw new InvalidDataException("A(z) " + field.Label + " 1 és 65535 között legyen.");
            return number.ToString(CultureInfo.InvariantCulture);
        }
        if (field.Kind == "choice")
        {
            var selected = raw.Trim();
            if (field.Choices is null || field.Choices.All(choice => choice.Value != selected))
                throw new InvalidDataException("A(z) " + field.Label + " értéke ismeretlen.");
            return selected;
        }
        var plain = raw.Trim();
        if (plain.Length == 0) return field.Kind == "folder" ? "" : Default(field.Key);
        if (plain.Length > 500 || plain.Any(char.IsControl))
            throw new InvalidDataException("A(z) " + field.Label + " túl hosszú vagy érvénytelen.");
        if (field.Kind == "folder") return RequireFolder(plain);
        return plain;
    }

    private static void ValidateSecret(string text)
    {
        if (text.Length > 4000 || text.Any(char.IsControl))
            throw new InvalidDataException("A titkos mező túl hosszú vagy érvénytelen.");
    }

    public static string RequireFolder(string path)
    {
        var text = path.Trim();
        if (text.Length is 0 or > 400 || text.Any(char.IsControl) || text.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathRooted(text))
            throw new InvalidDataException("A mappa teljes elérési út legyen.");
        try { return Path.GetFullPath(text); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new InvalidDataException("A mappa elérési útja érvénytelen."); }
    }

    private Dictionary<string, string> ReadSettings() => ReadMap(_settings, plain: true);
    private Dictionary<string, string> ReadSecrets() => ReadMap(_secrets, plain: false);

    private static Dictionary<string, string> ReadMap(string path, bool plain)
    {
        if (!File.Exists(path)) return new(StringComparer.Ordinal);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("A beállításfájl hivatkozás, ezért nem olvasható.");
        var length = new FileInfo(path).Length;
        if (length is < 1 or > 262_144) throw new InvalidDataException("A beállításfájl mérete érvénytelen.");
        try
        {
            string body;
            if (plain) body = File.ReadAllText(path);
            else
            {
                var stored = File.ReadAllBytes(path);
                if (stored.Length < OperationProtector.Magic.Length || !stored.AsSpan(0, OperationProtector.Magic.Length).SequenceEqual(OperationProtector.Magic))
                    throw new InvalidDataException("A titkos beállítás sérült.");
                var unprotected = OperationProtector.Unprotect(stored.AsSpan(OperationProtector.Magic.Length).ToArray());
                try { body = Encoding.UTF8.GetString(unprotected); }
                finally { CryptographicOperations.ZeroMemory(unprotected); }
            }
            var file = JsonSerializer.Deserialize<SettingsBody>(body, Json) ?? throw new InvalidDataException("A beállításfájl üres.");
            if (file.SchemaVersion != 1 || file.Values is null) throw new InvalidDataException("A beállításfájl verziója ismeretlen.");
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in file.Values)
            {
                if (key.Length is < 1 or > 80 || key.Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '_'))
                    || value.Length > 4000 || value.Any(char.IsControl))
                    throw new InvalidDataException("A beállításfájl sérült.");
                map[key] = value;
            }
            return map;
        }
        catch (JsonException) { throw new InvalidDataException("A beállításfájl sérült."); }
    }

    private void WriteSettings(Dictionary<string, string> values)
    {
        var body = JsonSerializer.Serialize(new SettingsBody { SchemaVersion = 1, Values = Ordered(values) }, Json);
        WriteAtomic(_settings, Encoding.UTF8.GetBytes(body), backup: true);
    }

    private void WriteSecrets(Dictionary<string, string> values)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(new SettingsBody { SchemaVersion = 1, Values = Ordered(values) }, Json);
        byte[] protectedBytes;
        try { protectedBytes = OperationProtector.Protect(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        var payload = new byte[OperationProtector.Magic.Length + protectedBytes.Length];
        OperationProtector.Magic.CopyTo(payload);
        protectedBytes.AsSpan().CopyTo(payload.AsSpan(OperationProtector.Magic.Length));
        WriteAtomic(_secrets, payload, backup: true);
    }

    private static Dictionary<string, string> Ordered(Dictionary<string, string> values) =>
        values.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static bool Same(Dictionary<string, string> left, Dictionary<string, string> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static void WriteAtomic(string path, byte[] payload, bool backup)
    {
        var folder = Path.GetDirectoryName(path)!;
        if (Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Az adatmappa hivatkozás, ezért a beállítás nem menthető.");
        Directory.CreateDirectory(folder);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, payload);
            if (File.Exists(path))
            {
                if (backup) File.Copy(path, path + ".bak", overwrite: true);
                File.Replace(temporary, path, destinationBackupFileName: null);
            }
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed class SettingsBody
    {
        public int SchemaVersion { get; set; }
        public Dictionary<string, string>? Values { get; set; }
    }
}

public static class LocalDataBackup
{
    public static LocalBackupResult Copy(string dataDirectory, string destinationRoot)
    {
        dataDirectory = Path.GetFullPath(dataDirectory);
        destinationRoot = OperationSettingsStore.RequireFolder(destinationRoot);
        if (!Directory.Exists(dataDirectory)) throw new DirectoryNotFoundException("Az adatmappa nem található.");
        if (Same(dataDirectory, destinationRoot))
            throw new InvalidDataException("A mentés helye nem lehet maga az adatmappa.");
        if (Inside(dataDirectory, destinationRoot))
            throw new InvalidDataException("A mentés helye ne tartalmazza az adatmappát.");
        Directory.CreateDirectory(destinationRoot);
        var target = Path.Combine(destinationRoot, "pcimpressz-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(target);
        var skipped = new List<string>();
        var count = CopyTree(dataDirectory, dataDirectory, target, destinationRoot, skipped);
        return new(target, count, skipped);
    }

    private static int CopyTree(string current, string source, string target, string destination, List<string> skipped)
    {
        var count = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(current))
        {
            if (count + skipped.Count > 20_000) throw new IOException("A mentés túl sok fájlt érintene.");
            var attributes = File.GetAttributes(entry);
            var relative = Path.GetRelativePath(source, entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) { skipped.Add(relative); continue; }
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if (Inside(entry, destination) || Same(entry, destination) || Inside(entry, target))
                { skipped.Add(relative); continue; }
                count += CopyTree(entry, source, target, destination, skipped);
                continue;
            }
            var name = Path.GetFileName(entry);
            if (name.Equals("host.lock", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            { skipped.Add(relative); continue; }
            var dest = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            try { File.Copy(entry, dest); count++; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped.Add(relative); }
        }
        return count;
    }

    private static bool Same(string left, string right) =>
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar).Equals(
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static bool Inside(string path, string parent)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && !Same(path, parent);
    }
}

file static class OperationProtector
{
    public static ReadOnlySpan<byte> Magic => "PCIOPS1\n"u8;
    private static ReadOnlySpan<byte> Entropy => "pcimpressz-operation-v1"u8;
    private const uint UiForbidden = 0x1;

    public static byte[] Protect(byte[] plain) => Transform(plain, protect: true);
    public static byte[] Unprotect(byte[] cipher) => Transform(cipher, protect: false);

    private static byte[] Transform(byte[] inputBytes, bool protect)
    {
        var input = default(DataBlob);
        var extra = default(DataBlob);
        var output = default(DataBlob);
        try
        {
            input = Allocate(inputBytes);
            extra = Allocate(Entropy.ToArray());
            var ok = protect
                ? CryptProtectData(ref input, "PcImpressz működés", ref extra, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output)
                : CryptUnprotectData(ref input, IntPtr.Zero, ref extra, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output);
            if (!ok) throw new CryptographicException(protect ? "A titok titkosítása nem sikerült." : "A titok visszafejtése nem sikerült.");
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            Free(ref input);
            Free(ref extra);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }

    private static DataBlob Allocate(byte[] bytes)
    {
        var data = Marshal.AllocHGlobal(Math.Max(bytes.Length, 1));
        if (bytes.Length > 0) Marshal.Copy(bytes, 0, data, bytes.Length);
        return new() { Size = bytes.Length, Data = data };
    }

    private static void Free(ref DataBlob blob)
    {
        if (blob.Data == IntPtr.Zero) return;
        if (blob.Size > 0)
        {
            var zeros = new byte[blob.Size];
            Marshal.Copy(zeros, 0, blob.Data, zeros.Length);
        }
        Marshal.FreeHGlobal(blob.Data);
        blob = default;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, uint flags, ref DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, uint flags, ref DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);
}
