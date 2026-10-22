using System.Security.Cryptography;
namespace PcImpressz.Shell.Customization;
public static class InternalBarcodes
{
    public static string Generate()=>Complete("20"+RandomNumberGenerator.GetInt32(0,1_000_000_000).ToString("D9")+RandomNumberGenerator.GetInt32(0,10));
    public static string Complete(string prefix)
    {
        if(prefix.Length!=12||prefix.Any(c=>!char.IsAsciiDigit(c)))throw new ArgumentException("12 számjegy szükséges.");
        var sum=prefix.Select((c,i)=>(c-'0')*(i%2==0?1:3)).Sum();return prefix+((10-sum%10)%10);
    }
}
