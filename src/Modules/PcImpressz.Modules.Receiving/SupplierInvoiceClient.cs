using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
namespace PcImpressz.Modules.Receiving;
public sealed record SupplierConnectionSave(Guid CompanyId,string CustomerId,string? AuthCode,string ProxyUrl,bool Enabled);
public sealed record SupplierConnection(string CustomerId,string AuthCode,string ProxyUrl,bool Enabled);
public sealed record SupplierConnectionStatus(bool Configured,string CustomerId,string ProxyUrl,bool Enabled);
public sealed record SupplierInvoiceLine(string Name,string Sku,string Barcode,string ManufacturerPartNumber,string Category);
public static class SupplierConnectionStore
{
    private static byte[] Entropy(Guid id)=>Encoding.UTF8.GetBytes("pcimpressz-compmarket-v1:"+id);
    public static SupplierConnection? Read(string directory,Guid company)
    {
        var path=Path.Combine(directory,"compmarket-connection.bin");if(!File.Exists(path))return null;
        if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0||new FileInfo(path).Length>16384)throw new IOException("Hibás CompMarket-kapcsolat.");
        byte[] plain;try{plain=WindowsSecret.Unprotect(File.ReadAllBytes(path),Entropy(company));}catch(CryptographicException){throw new IOException("A CompMarket-kapcsolat másik Windows-fiókhoz tartozik.");}
        try{return JsonSerializer.Deserialize<SupplierConnection>(plain,ReceivingJson.Options)??throw new IOException("Hibás CompMarket-kapcsolat.");}finally{CryptographicOperations.ZeroMemory(plain);}
    }
    public static SupplierConnectionStatus Status(string directory,Guid company){var c=Read(directory,company);return new(c is not null,c?.CustomerId??"",c is null?"":Redact(c.ProxyUrl),c?.Enabled??false);}
    public static void RequireProxy(string? url)
    {
        var text=(url??"").Trim();
        if(text.Length==0)throw new ArgumentException("Nincs VPS-proxy. A CompMarket a 217.113.49.52 fix IP-t várja. Közvetlen kapcsolattal nem próbálkozunk.");
        if(text.Length>500||text.Any(char.IsControl)||!Uri.TryCreate(text,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https")||string.IsNullOrEmpty(uri.Host))throw new ArgumentException("A nagyker-proxy csak http:// vagy https:// cím lehet, például http://felhasznalo:jelszo@217.113.49.52:8888");
    }
    public static string Redact(string url){if(!Uri.TryCreate(url,UriKind.Absolute,out var uri))return "";var bare=new UriBuilder(uri){UserName="",Password=""};return bare.Uri.GetComponents(UriComponents.SchemeAndServer,UriFormat.UriEscaped);}
    public static string MergeProxy(string submitted,string? previous)
    {
        RequireProxy(submitted);var next=new Uri(submitted.Trim());
        if(string.IsNullOrEmpty(next.UserInfo)&&previous is not null&&Uri.TryCreate(previous,UriKind.Absolute,out var old)&&!string.IsNullOrEmpty(old.UserInfo)&&old.Scheme.Equals(next.Scheme,StringComparison.OrdinalIgnoreCase)&&old.Host.Equals(next.Host,StringComparison.OrdinalIgnoreCase)&&old.Port==next.Port)return previous;
        return submitted.Trim();
    }
    public static SupplierConnectionStatus Save(string directory,SupplierConnectionSave request)
    {
        var old=Read(directory,request.CompanyId);var auth=string.IsNullOrWhiteSpace(request.AuthCode)?old?.AuthCode??"":request.AuthCode.Trim();
        if(request.CustomerId.Length is <1 or >100||auth.Length is <1 or >500||request.CustomerId.Any(char.IsControl)||auth.Any(char.IsControl))throw new ArgumentException("Ügyfélazonosító és hitelesítő kód szükséges.");
        var proxyUrl=MergeProxy(request.ProxyUrl,old?.ProxyUrl);
        var plain=JsonSerializer.SerializeToUtf8Bytes(new SupplierConnection(request.CustomerId.Trim(),auth,proxyUrl,request.Enabled),ReceivingJson.Options);
        byte[] cipher;try{cipher=WindowsSecret.Protect(plain,Entropy(request.CompanyId));}finally{CryptographicOperations.ZeroMemory(plain);}
        Directory.CreateDirectory(directory);var path=Path.Combine(directory,"compmarket-connection.bin");if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("A kapcsolatfájl hivatkozás.");
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllBytes(temporary,cipher);if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        return Status(directory,request.CompanyId);
    }
}
public static class SupplierInvoiceClient
{
    public static async Task<SupplierInvoiceLine[]> ReadAsync(SupplierConnection connection,string number,CancellationToken ct,HttpClient? testClient=null)
    {
        if(!connection.Enabled)return [];
        if(string.IsNullOrWhiteSpace(number)||number.Length>80||number.Any(char.IsControl))throw new ArgumentException("Teljes CompMarket-számlaszám szükséges.");
        SupplierConnectionStore.RequireProxy(connection.ProxyUrl);
        using var owned=testClient is null?new HttpClient(ProxyHandler(connection.ProxyUrl)){Timeout=TimeSpan.FromSeconds(30)}:null;
        try
        {
            var url="https://compmarket.hu/xml/szamla.php?pid="+Uri.EscapeDataString(connection.CustomerId)+"&authcode="+Uri.EscapeDataString(connection.AuthCode)+"&szamlaszam="+Uri.EscapeDataString(number);
            using var response=await (testClient??owned!).GetAsync(url,HttpCompletionOption.ResponseHeadersRead,ct);
            if(!response.IsSuccessStatusCode)throw new ArgumentException("A CompMarket nem adta vissza a számlát (HTTP "+(int)response.StatusCode+").");
            await using var input=await response.Content.ReadAsStreamAsync(ct);using var memory=new MemoryStream();var buffer=new byte[8192];int length;
            while((length=await input.ReadAsync(buffer,ct))>0){if(memory.Length+length>4_000_000)throw new ArgumentException("Túl nagy CompMarket-válasz.");memory.Write(buffer,0,length);}
            return Parse(memory.ToArray(),number);
        }
        catch(HttpRequestException){throw new IOException("A CompMarket a VPS-proxyn keresztül nem elérhető. Közvetlen kapcsolattal nem próbálkozunk.");}
    }
    private static HttpClientHandler ProxyHandler(string proxyUrl)
    {
        var uri=new Uri(proxyUrl.Trim());var bare=new UriBuilder(uri){UserName="",Password=""}.Uri;var proxy=new WebProxy(bare){BypassProxyOnLocal=false};
        if(!string.IsNullOrEmpty(uri.UserInfo)){var raw=Uri.UnescapeDataString(uri.UserInfo);var split=raw.Split(':',2);proxy.Credentials=new NetworkCredential(split[0],split.Length==2?split[1]:"");}
        return new HttpClientHandler{Proxy=proxy,UseProxy=true,AllowAutoRedirect=false,UseCookies=false};
    }
    public static SupplierInvoiceLine[] Parse(byte[] xml,string expectedNumber)
    {
        using var stream=new MemoryStream(xml);using var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=4_000_000});var doc=XDocument.Load(reader);
        string Value(XContainer root,params string[] names)=>root.Descendants().FirstOrDefault(e=>names.Contains(e.Name.LocalName,StringComparer.OrdinalIgnoreCase))?.Value.Trim()??"";
        var invoices=doc.Descendants().Where(e=>e.Name.LocalName=="szamla").ToArray();if(invoices.Length>1)throw new ArgumentException("A CompMarket több számlát küldött vissza.");
        var number=Value(doc,"szamlaszam","szamla_szama");if(number.Length>0&&number!=expectedNumber)throw new ArgumentException("Eltérő CompMarket-számlaszám.");
        var rows=doc.Descendants().Where(e=>e.Name.LocalName=="tetel").Take(401).Select(e=>new SupplierInvoiceLine(Value(e,"cikknev","nev","megnevezes"),Value(e,"cikkszam"),Value(e,"cikkazon","ean","vonalkod"),Value(e,"gy_cikkszam","gyartoi_cikkszam"),Value(e,"kategoria","category"))).Where(l=>l.Name.Length>0).ToArray();
        if(rows.Length>400)throw new ArgumentException("Túl sok CompMarket-tétel.");return rows;
    }
}
