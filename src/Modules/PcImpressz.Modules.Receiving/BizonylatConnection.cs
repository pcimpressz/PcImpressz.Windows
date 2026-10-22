using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PcImpressz.Modules.Receiving;

public sealed record BizonylatConnectionStatus(bool Configured,bool TestMode,string InvoicePrefix);
public sealed record BizonylatConnectionSave(Guid CompanyId,string? ApiKey,bool TestMode,string? InvoicePrefix);
public sealed record BizonylatConnectionSecrets(string ApiKey,bool TestMode,string InvoicePrefix);

public static class BizonylatConnectionStore
{
    public const string FileName="bizonylat-connection.bin";
    private static readonly byte[] Magic="PCIBIZ1\n"u8.ToArray();

    public static BizonylatConnectionStatus Status(string directory,Guid companyId)
    {
        var s=Read(directory,companyId);return s is null?new(false,true,""):new(true,s.TestMode,s.InvoicePrefix);
    }
    public static BizonylatConnectionSecrets? Read(string directory,Guid companyId)
    {
        var file=Path.Combine(directory,FileName);if(!File.Exists(file))return null;
        if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("A Bizonylat kapcsolat hivatkozás, ezért nem olvasható.");
        if(new FileInfo(file).Length>16*1024)throw new IOException("A Bizonylat kapcsolat túl nagy.");
        var stored=File.ReadAllBytes(file);if(stored.Length is <16 or >16*1024||!stored.AsSpan(0,Magic.Length).SequenceEqual(Magic))throw new IOException("A Bizonylat kapcsolat sérült.");
        byte[] plain;try{plain=WindowsSecret.Unprotect(stored.AsSpan(Magic.Length).ToArray(),Entropy(companyId));}catch(CryptographicException){throw new IOException("A Bizonylat kapcsolat ezen a Windows-felhasználón nem nyitható meg.");}
        try{return JsonSerializer.Deserialize<BizonylatConnectionSecrets>(plain,ReceivingJson.Options)??throw new IOException("A Bizonylat kapcsolat sérült.");}
        catch(JsonException){throw new IOException("A Bizonylat kapcsolat sérült.");}
        finally{CryptographicOperations.ZeroMemory(plain);}
    }
    public static BizonylatConnectionStatus Save(string directory,Guid companyId,BizonylatConnectionSave request)
    {
        if(request.CompanyId!=companyId)throw new ArgumentException("Eltérő cégazonosító.");
        var current=Read(directory,companyId);var key=(request.ApiKey??"").Trim();if(key.Length==0)key=current?.ApiKey??"";
        if(key.Length is <12 or >300||key.Any(char.IsWhiteSpace)||key.Any(char.IsControl))throw new ArgumentException("Érvénytelen Bizonylat API-kulcs. Üresen hagyva a mentett kulcs marad.");
        var prefix=(request.InvoicePrefix??current?.InvoicePrefix??"").Trim().ToUpperInvariant();if(prefix.Length>20||prefix.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'&&c!='_'))throw new ArgumentException("A számlatömb előtag legfeljebb 20 karakter lehet.");
        var body=new BizonylatConnectionSecrets(key,request.TestMode,prefix);var plain=JsonSerializer.SerializeToUtf8Bytes(body,ReceivingJson.Options);byte[] cipher;
        try{cipher=WindowsSecret.Protect(plain,Entropy(companyId));}finally{CryptographicOperations.ZeroMemory(plain);}
        Directory.CreateDirectory(directory);var file=Path.Combine(directory,FileName);var tmp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllBytes(tmp,Magic.Concat(cipher).ToArray());if(File.Exists(file))File.Replace(tmp,file,null);else File.Move(tmp,file);}finally{if(File.Exists(tmp))File.Delete(tmp);}
        return new(true,request.TestMode,prefix);
    }
    private static byte[] Entropy(Guid companyId)=>Encoding.UTF8.GetBytes("pcimpressz-bizonylat-v1:"+companyId.ToString("D"));
}
