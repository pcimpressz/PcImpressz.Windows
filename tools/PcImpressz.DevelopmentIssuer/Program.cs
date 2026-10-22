using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PcImpressz.Contracts;
using PcImpressz.Core.Licensing;
using PcImpressz.Core.Modules;

var root = Path.GetFullPath(args.Length == 1 ? args[0] : throw new ArgumentException("A fejlesztőprojekt mappáját add meg."));
var data = Path.Combine(root, ".local");
Directory.CreateDirectory(data);
var settingsPath = Path.Combine(data, "settings.json");
var settings = File.Exists(settingsPath) ? JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject() : new JsonObject();
var product = settings["Product"] as JsonObject ?? new();
var organization = product["OrganizationId"]?.GetValue<string>() ?? "";
if (organization.Length > 0 && organization != "development")
    throw new InvalidOperationException("A meglévő szervezeti licencet nem írjuk felül. Külön fejlesztői adatmappa szükséges.");

var issuerDirectory = new DirectoryInfo(Path.Combine(root, ".issuer"));
issuerDirectory.Create();
using var owner = WindowsIdentity.GetCurrent();
var sid = owner.User ?? throw new InvalidOperationException("A kiadói felhasználó nem azonosítható.");
var security = new DirectorySecurity();
security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
issuerDirectory.SetAccessControl(security);
using var moduleKey = ReadKey("module-signing.pem");
using var licenseKey = ReadKey("license-signing.pem");
InstallModule(moduleKey, "partners", "PcImpressz.Modules.Partners", "PcImpressz.Modules.Partners.PartnersModule");
InstallModule(moduleKey, "catalog", "PcImpressz.Modules.Catalog", "PcImpressz.Modules.Catalog.CatalogModule");
InstallModule(moduleKey, "inventory", "PcImpressz.Modules.Inventory", "PcImpressz.Modules.Inventory.InventoryModule",
[new() { ModuleId = "catalog", MinimumVersion = "0.3.0", MaximumVersionExclusive = "1.0.0" }]);
InstallModule(moduleKey, "sales", "PcImpressz.Modules.Sales", "PcImpressz.Modules.Sales.SalesModule",
[new() { ModuleId = "inventory", MinimumVersion = "0.3.0", MaximumVersionExclusive = "1.0.0" },
 new() { ModuleId = "partners", MinimumVersion = "0.3.0", MaximumVersionExclusive = "1.0.0" }]);
InstallModule(moduleKey, "receiving", "PcImpressz.Modules.Receiving", "PcImpressz.Modules.Receiving.ReceivingModule",
[
    new() { ModuleId = "partners", MinimumVersion = "0.3.0", MaximumVersionExclusive = "1.0.0" },
    new() { ModuleId = "catalog", MinimumVersion = "0.3.0", MaximumVersionExclusive = "1.0.0" },
    new() { ModuleId = "inventory", MinimumVersion = "0.3.0", MaximumVersionExclusive = "1.0.0" }
]);
AddPublicKey("module-public-keys.json", "dev-module-key", moduleKey);
AddPublicKey("license-public-keys.json", "dev-license-key", licenseKey);
product["Issuer"] = "pcimpressz-development";
product["OrganizationId"] = "development"; settings["Product"] = product;
AtomicText(settingsPath, settings.ToJsonString(new() { WriteIndented = true }));
var installationPath = Path.Combine(data, "installation-id.txt");
if (!File.Exists(installationPath)) AtomicText(installationPath, Guid.NewGuid().ToString("D"));
var installation = File.ReadAllText(installationPath).Trim();
if (!Guid.TryParseExact(installation, "D", out _)) throw new IOException("Érvénytelen telepítésazonosító.");
var revisionPath = Path.Combine(issuerDirectory.FullName, "license-revision.txt");
var previous = File.Exists(revisionPath) ? long.Parse(File.ReadAllText(revisionPath)) : 0;
var revision = Math.Max(previous + 1, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
AtomicText(revisionPath, revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
var licenseId = "development-" + installation.Replace("-", "");
var masterExpiry = new DateTimeOffset(2099, 12, 31, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
var license = new EntitlementDocument
{
    SchemaVersion = 1, Iss = "pcimpressz-development", Aud = PlatformContract.ProductId, LicenseId = licenseId,
    OrganizationId = "development", Revision = revision, Iat = now, Nbf = now - 30, Exp = masterExpiry,
    Modules = [
        new() { ModuleId = "partners", Capabilities = ["partners.write", "partners.read", "partners.export"], ExpiresAt = masterExpiry },
        new() { ModuleId = "catalog", Capabilities = ["catalog.write", "catalog.read", "catalog.export"], ExpiresAt = masterExpiry },
        new() { ModuleId = "inventory", Capabilities = ["inventory.write", "inventory.read", "inventory.export"], ExpiresAt = masterExpiry },
        new() { ModuleId = "sales", Capabilities = ["sales.write", "sales.read", "sales.export"], ExpiresAt = masterExpiry },
        new() { ModuleId = "receiving", Capabilities = ["receiving.write", "receiving.read", "receiving.export"], ExpiresAt = masterExpiry }]
};
var lease = new LeaseDocument
{
    SchemaVersion = 1, Iss = license.Iss, Aud = license.Aud, LicenseId = licenseId, OrganizationId = license.OrganizationId,
    InstallationId = installation, Revision = revision, Iat = now, Nbf = now - 30, Exp = masterExpiry
};
AtomicText(Path.Combine(data, "license.jws"), Sign(licenseKey, "dev-license-key", LicenseDocumentTypes.Entitlement, license));
AtomicText(Path.Combine(data, "lease.jws"), Sign(licenseKey, "dev-license-key", LicenseDocumentTypes.Lease, lease));
Console.WriteLine("Aláírt partner-, termék-, bevételezés-, készlet- és eladásmodul telepítve. Géphez kötött, aláírt fejlesztői jogosultság és működési engedély: 2099-12-31-ig.");
Console.WriteLine("Kiadói kulcsok kizárólag a .issuer mappában; ügyfélkiadásba nem kerülnek.");

void InstallModule(ECDsa key, string id, string assemblyName, string entryType, ModuleDependency[]? dependencies = null)
{
    var dll = Path.Combine(root, "src", "Modules", assemblyName, "bin", "Release", "net10.0", assemblyName + ".dll");
    var moduleBytes = File.ReadAllBytes(dll);
    var manifest = new ModuleManifest
    {
        SchemaVersion = 1, Id = id, Version = "0.4.20", ApiVersion = PlatformContract.ApiVersion,
        EntryAssembly = assemblyName + ".dll", EntryType = entryType,
        Files = new() { [assemblyName + ".dll"] = Convert.ToHexString(SHA256.HashData(moduleBytes)) },
        Dependencies = dependencies ?? []
    };
    var domain = Path.Combine(Path.GetDirectoryName(dll)!, "PcImpressz.Business.dll");
    if (File.Exists(domain)) manifest.Files["PcImpressz.Business.dll"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(domain)));
    foreach(var name in id=="receiving"?new[]{"PcImpressz.Modules.Partners.dll","PcImpressz.Modules.Catalog.dll","PcImpressz.Modules.Sales.dll","UglyToad.PdfPig.dll","UglyToad.PdfPig.Core.dll","UglyToad.PdfPig.DocumentLayoutAnalysis.dll","UglyToad.PdfPig.Fonts.dll","UglyToad.PdfPig.Package.dll","UglyToad.PdfPig.Tokenization.dll","UglyToad.PdfPig.Tokens.dll"}:id=="sales"?new[]{"PcImpressz.Modules.Partners.dll"}:Array.Empty<string>())
        manifest.Files[name]=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(dll)!,name))));
    var package = Path.Combine(data, "modules", id);
    Directory.CreateDirectory(package);
    Atomic(Path.Combine(package, manifest.EntryAssembly), moduleBytes);
    if (File.Exists(domain)) Atomic(Path.Combine(package, "PcImpressz.Business.dll"), File.ReadAllBytes(domain));
    foreach(var name in id=="receiving"?new[]{"PcImpressz.Modules.Partners.dll","PcImpressz.Modules.Catalog.dll","PcImpressz.Modules.Sales.dll","UglyToad.PdfPig.dll","UglyToad.PdfPig.Core.dll","UglyToad.PdfPig.DocumentLayoutAnalysis.dll","UglyToad.PdfPig.Fonts.dll","UglyToad.PdfPig.Package.dll","UglyToad.PdfPig.Tokenization.dll","UglyToad.PdfPig.Tokens.dll"}:id=="sales"?new[]{"PcImpressz.Modules.Partners.dll"}:Array.Empty<string>())
        Atomic(Path.Combine(package,name),File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(dll)!,name)));
    AtomicText(Path.Combine(package, "manifest.jws"), Sign(key, "dev-module-key", ModuleManifest.DocumentType, manifest));
}
ECDsa ReadKey(string name)
{
    var file = Path.Combine(issuerDirectory.FullName, name);
    var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    if (File.Exists(file)) key.ImportFromPem(File.ReadAllText(file));
    else AtomicText(file, key.ExportPkcs8PrivateKeyPem());
    return key;
}
void AddPublicKey(string name, string id, ECDsa key)
{
    var file = Path.Combine(data, name);
    var keys = File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))! : new();
    keys[id] = key.ExportSubjectPublicKeyInfoPem();
    AtomicText(file, JsonSerializer.Serialize(keys));
}
static string Sign<T>(ECDsa key, string keyId, string type, T payload)
{
    static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
    var header = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", kid = keyId, typ = type }));
    var body = B64(JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    var input = header + "." + body;
    return input + "." + B64(key.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256,
        DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
}
static void AtomicText(string path, string text) => Atomic(path, Encoding.UTF8.GetBytes(text));
static void Atomic(string path, byte[] bytes)
{
    var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
    try { File.WriteAllBytes(temporary, bytes); File.Move(temporary,path,overwrite:true); }
    finally { if (File.Exists(temporary)) File.Delete(temporary); }
}
