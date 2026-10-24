using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PcImpressz.Contracts;

namespace PcImpressz.Modules.Quotes;

public sealed record WholesaleField(string Key, string Label, bool Secret = true, string Default = "");
public sealed record WholesaleProvider(string Id, string Name, string Description, bool Used, WholesaleField[] Fields);
public sealed record WholesaleSave(Guid CompanyId, string ProviderId, string Description, bool Enabled, Dictionary<string,string> Values);
public sealed record WholesaleEntry(string Description, bool Enabled, Dictionary<string,string> Values, Dictionary<string,bool>? Visibility = null);
public sealed record WholesaleVisibilitySave(Guid CompanyId, string ProviderId, string Context, bool Visible);
public sealed record WholesaleHealth(DateTimeOffset AttemptedAt, DateTimeOffset? SucceededAt, bool Success, int? Products, string Message);

public static class WholesaleConnections
{
    public static IReadOnlyList<WholesaleProvider> Providers { get; } = Build();
    private static WholesaleProvider[] Build()
    {
        WholesaleField F(string key,string label,bool secret=true,string value="")=>new(key,label,secret,value);
        WholesaleProvider Vision(string id,string name,string url,string ns)=>new(id,name,"Vision SOAP katalógus, ár és készlet. A Windows-adapter bekötésre előkészítve.",false,
            [F(id+"_vision_url","SOAP cím",false,url),F(id+"_pid","Partnerazonosító (PID)"),F(id+"_partnerkod","Partnerkód"),F(id+"_authcode","Hitelesítő kód"),F(id+"_soap_namespace","SOAP névtér",false,ns),..id=="jtc"?new[]{F("jtc_cegcsopid","Cégcsoport-azonosító")}:Array.Empty<WholesaleField>()]);
        return [
            new("compmarket","CompMarket","XML készletlista; élő árajánlatkeresés és számlaegyeztetés. A kéréshez VPS-proxy szükséges (217.113.49.52).",false,[F("compmarket_pid","Ügyfélazonosító (pid)",false),F("compmarket_authcode","Hitelesítő kód"),F("nagyker_proxy","VPS-proxy (jelszóval együtt; üresen a mentett marad)")]),
            new("caseking","Caseking","Websync ár- és készletadatok. A Windows-adapter bekötésre előkészítve.",false,[F("caseking_felhasznalo","Websync felhasználónév"),F("caseking_jelszo","Websync jelszó")]),
            new("innpro","Innpro","IOF katalógus, ár és készlet. A Windows-adapter bekötésre előkészítve.",false,[F("innpro_client","Ügyfélazonosító"),F("innpro_token","Token"),F("innpro_shop","Boltazonosító",false,"5"),F("innpro_language","Nyelv",false,"hun")]),
            Vision("jtc","JTC","https://jtc.hu/services/vision.asmx","http://www.jtc.hu/services/"),
            Vision("orink","Orink","https://orink.hu/services/vision.asmx","https://orink.hu/services/"),
            Vision("cedrus","Cédrus","https://www.cedrus.hu/services/vision.asmx","http://www.cedrus.hu/services/"),
            new("hrp","HRP","JSON Gateway katalógus, ár és készlet. A Windows-adapter bekötésre előkészítve.",false,[F("hrp_gateway_url","Gateway cím",false,"https://webApi.hrp.hu/Gateway"),F("hrp_client","Kliensazonosító"),F("hrp_jelszo","Jelszó")]),
            new("pcmax","PC-Max","Használt nagyker · publikus UNAS XML. A Windows-adapter bekötésre előkészítve.",true,[F("pcmax_url","XML cím",false,"https://export.pc-max.hu/unas_1231459_nagyker.xml")]),
            new("pca","PCA Arena","Használt nagyker · partner JSON feed. A Windows-adapter bekötésre előkészítve.",true,[F("pca_token","Partner token")]),
            new("szamtar","Számtárház","Használt nagyker · nettó nagykeráras XML. A Windows-adapter bekötésre előkészítve.",true,[F("szamtar_url","XML cím",false,"https://szamtarhaz.hu/aggregator/viszontelado_nagyker.xml")])
        ];
    }
    private static byte[] Entropy(Guid company)=>Encoding.UTF8.GetBytes("pcimpressz-wholesale-v1:"+company);
    private static string FileName(string directory)=>Path.Combine(directory,"wholesale-connections.bin");
    private static Dictionary<string,WholesaleEntry> Read(string directory,Guid company)
    {
        var path=FileName(directory);if(!File.Exists(path))return new();
        if(new FileInfo(path).Length>256_000||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Hibás nagyker-beállításfájl.");
        var plain=SecretBox.Unprotect(File.ReadAllBytes(path),Entropy(company));
        try{return JsonSerializer.Deserialize<Dictionary<string,WholesaleEntry>>(plain,CompanyDocuments.Json)??throw new IOException("Hibás nagyker-beállításfájl.");}
        finally{CryptographicOperations.ZeroMemory(plain);}
    }
    private static void Write(string directory,Guid company,Dictionary<string,WholesaleEntry> entries)
    {
        var plain=JsonSerializer.SerializeToUtf8Bytes(entries,CompanyDocuments.Json);byte[] cipher;
        try{cipher=SecretBox.Protect(plain,Entropy(company));}finally{CryptographicOperations.ZeroMemory(plain);}
        Directory.CreateDirectory(directory);var path=FileName(directory);var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllBytes(temp,cipher);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static bool IsVisible(string directory, Guid company, string providerId, string context)
    {
        if (context is not ("catalog" or "quotes" or "receiving")) throw new ArgumentException("Ismeretlen modulnézet.");
        var enabled = EnabledProviders(directory, company);
        if (!enabled.Contains(providerId)) return false;
        var entries = Read(directory, company);
        return !entries.TryGetValue(providerId, out var entry) || entry.Visibility is null ||
            !entry.Visibility.TryGetValue(context, out var visible) || visible;
    }
    public static object SaveVisibility(string directory, WholesaleVisibilitySave request)
    {
        if (request.Context is not ("catalog" or "quotes" or "receiving")) throw new ArgumentException("Ismeretlen modulnézet.");
        if (!Providers.Any(p => p.Id == request.ProviderId)) throw new ArgumentException("Ismeretlen nagyker.");
        var entries = Read(directory, request.CompanyId);
        entries.TryGetValue(request.ProviderId, out var previous);
        var visibility = previous?.Visibility is null ? new Dictionary<string,bool>() : new Dictionary<string,bool>(previous.Visibility);
        visibility[request.Context] = request.Visible;
        entries[request.ProviderId] = new(previous?.Description ?? "", previous?.Enabled ?? false,
            previous?.Values ?? new Dictionary<string,string>(), visibility);
        Write(directory, request.CompanyId, entries);
        return List(directory, request.CompanyId);
    }
    public static HashSet<string> EnabledProviders(string directory, Guid company)
    {
        var entries = Read(directory, company);
        var enabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, entry) in entries)
            if (entry.Enabled && id != "compmarket") enabled.Add(id);
        if (CompMarketQuoteConnectionStore.Read(directory, company)?.Enabled == true)
            enabled.Add("compmarket");
        return enabled;
    }
    public static object List(string directory,Guid company)
    {
        var entries=Read(directory,company);var cm=CompMarketQuoteConnectionStore.Read(directory,company);
        return Providers.Select(provider=>
        {
            entries.TryGetValue(provider.Id,out var entry);
            string Value(WholesaleField field)=>entry?.Values.GetValueOrDefault(field.Key)??field.Default;
            return new {provider.Id,provider.Name,provider.Description,provider.Used,enabled=provider.Id=="compmarket"?cm?.Enabled??false:entry?.Enabled??false,
                note=entry?.Description??"",visibility=new {catalog=entry?.Visibility?.GetValueOrDefault("catalog",true)??true,quotes=entry?.Visibility?.GetValueOrDefault("quotes",true)??true,receiving=entry?.Visibility?.GetValueOrDefault("receiving",false)??false},prepared=provider.Id!="compmarket",health=provider.Id=="compmarket"?ReadHealth(directory):null,
                fields=provider.Fields.Select(field=>new{field.Key,field.Label,field.Secret,value=field.Secret?"":field.Key=="compmarket_pid"?cm?.CustomerId??Value(field):Value(field),
                    hasValue=field.Key=="compmarket_authcode"?cm is not null:field.Key=="nagyker_proxy"?!string.IsNullOrEmpty(cm?.ProxyUrl):!string.IsNullOrEmpty(Value(field))}).ToArray()};
        }).ToArray();
    }
    public static object Save(string directory,WholesaleSave request)
    {
        var provider=Providers.SingleOrDefault(p=>p.Id==request.ProviderId)??throw new ArgumentException("Ismeretlen nagyker.");
        if(request.Description is null||request.Description.Length>2000)throw new ArgumentException("A leírás legfeljebb 2000 karakter lehet.");
        var entries=Read(directory,request.CompanyId);entries.TryGetValue(provider.Id,out var old);
        var values=new Dictionary<string,string>();
        foreach(var field in provider.Fields)
        {
            var text=request.Values.GetValueOrDefault(field.Key,"").Trim();
            if(text.Length>2000||text.Any(char.IsControl))throw new ArgumentException("Érvénytelen kapcsolati mező: "+field.Label);
            values[field.Key]=text.Length==0 && (field.Secret || field.Key=="nagyker_proxy")
                ? old?.Values.GetValueOrDefault(field.Key)??field.Default : text;
        }
        if(provider.Id=="compmarket")
        {
            var cm=CompMarketQuoteConnectionStore.Read(directory,request.CompanyId);
            CompMarketQuoteConnectionStore.Save(directory,new(request.CompanyId,values["compmarket_pid"],values["compmarket_authcode"],string.IsNullOrEmpty(values["nagyker_proxy"])?cm?.ProxyUrl??"":values["nagyker_proxy"],request.Enabled));
            values["compmarket_authcode"]="";values["nagyker_proxy"]="";
        }
        entries[provider.Id]=new(request.Description,request.Enabled,values,old?.Visibility);Write(directory,request.CompanyId,entries);
        return List(directory,request.CompanyId);
    }
    public static WholesaleHealth? ReadHealth(string directory)
    {
        var path=Path.Combine(directory,"compmarket-health.json");
        if(!File.Exists(path))return null;
        if(new FileInfo(path).Length>8192||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Hibás XML-állapotfájl.");
        return JsonSerializer.Deserialize<WholesaleHealth>(File.ReadAllText(path),CompanyDocuments.Json);
    }
    public static void InvalidateHealth(string directory)
    {
        var old=ReadHealth(directory);if(old is null)return;
        var path=Path.Combine(directory,"compmarket-health.json");var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(old with {Success=false,Message="A kapcsolati adatok megváltoztak; új teszt szükséges."},CompanyDocuments.Json));File.Replace(temp,path,null);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static void RecordHealth(string directory,bool success,int? count,string message)
    {
        var previous=ReadHealth(directory);var now=DateTimeOffset.UtcNow;Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"compmarket-health.json");var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(new WholesaleHealth(now,success?now:previous?.SucceededAt,success,count,message),CompanyDocuments.Json));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
