using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using PcImpressz.Contracts;
namespace PcImpressz.Modules.Receiving;
public static class NavTaxpayerClient
{
    private static readonly HttpClient Client=new(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=TimeSpan.FromSeconds(25)};
    private static readonly XNamespace Api="http://schemas.nav.gov.hu/OSA/3.0/api",Common="http://schemas.nav.gov.hu/NTCA/1.0/common";
    public static string TaxNumber(string input)
    {
        var value=input.Trim().Replace("-","").Replace(" ","");
        if(value.Length is not (8 or 11)||!value.All(char.IsAsciiDigit))throw new ArgumentException("Az adószám 8 számjegyű törzsszámát vagy teljes magyar adószámát add meg.");return value[..8];
    }
    public static XDocument Build(NavConnectionSecrets secrets,string tax,DateTimeOffset at,string id)
    {
        var utc=at.UtcDateTime;var signature=Convert.ToHexString(NavSha3.Hash(Encoding.UTF8.GetBytes(id+utc.ToString("yyyyMMddHHmmss",CultureInfo.InvariantCulture)+secrets.SignKey)));
        return new(new XDeclaration("1.0","utf-8",null),new XElement(Api+"QueryTaxpayerRequest",new XAttribute(XNamespace.Xmlns+"common",Common),
            new XElement(Common+"header",new XElement(Common+"requestId",id),new XElement(Common+"timestamp",utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ",CultureInfo.InvariantCulture)),new XElement(Common+"requestVersion","3.0"),new XElement(Common+"headerVersion","1.0")),
            new XElement(Common+"user",new XElement(Common+"login",secrets.UserName),new XElement(Common+"passwordHash",new XAttribute("cryptoType","SHA-512"),Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(secrets.Password)))),new XElement(Common+"taxNumber",secrets.TaxNumber),new XElement(Common+"requestSignature",new XAttribute("cryptoType","SHA3-512"),signature)),
            new XElement(Api+"software",new XElement(Api+"softwareId","HU00000000RAKTAR01"),new XElement(Api+"softwareName","PC-Impressz Windows"),new XElement(Api+"softwareOperation","LOCAL_SOFTWARE"),new XElement(Api+"softwareMainVersion","0.4.7"),new XElement(Api+"softwareDevName","PC-Impressz"),new XElement(Api+"softwareDevContact","info@pcimpressz.hu")),new XElement(Api+"taxNumber",TaxNumber(tax))));
    }
    public static async Task<CheckoutCustomerSave> QueryAsync(NavConnectionSecrets secrets,TaxpayerLookup input,CancellationToken ct,HttpClient? client=null)
    {
        var tax=TaxNumber(input.TaxNumber);var id="PCI"+Guid.NewGuid().ToString("N")[..25].ToUpperInvariant();
        var xml=Build(secrets,tax,DateTimeOffset.UtcNow,id);
        var host=secrets.TestMode?"https://api-test.onlineszamla.nav.gov.hu":"https://api.onlineszamla.nav.gov.hu";
        using var request=new HttpRequestMessage(HttpMethod.Post,host+"/invoiceService/v3/queryTaxpayer"){Content=new StringContent(xml.ToString(SaveOptions.DisableFormatting),Encoding.UTF8,"application/xml")};
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        using var response=await (client??Client).SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)throw new ArgumentException("A NAV elutasította a technikai felhasználót. Az admin ellenőrizze a NAV-kapcsolatot.");
        if(!response.IsSuccessStatusCode)throw new ArgumentException("A NAV-lekérdezés nem sikerült (HTTP "+(int)response.StatusCode+"). Nem mentettünk vevőt.");
        await using var stream=await response.Content.ReadAsStreamAsync(ct);using var bytes=new MemoryStream();var buffer=new byte[8192];int count;
        while((count=await stream.ReadAsync(buffer,ct))>0){if(bytes.Length+count>1_048_576)throw new ArgumentException("Túl nagy NAV-válasz.");bytes.Write(buffer,0,count);}
        return Parse(Encoding.UTF8.GetString(bytes.ToArray()),input.CompanyId,tax,id);
    }
    public static CheckoutCustomerSave Parse(string text,Guid company,string requestedTax,string? requestId=null)
    {
        if(text.Length>1_048_576)throw new ArgumentException("Túl nagy NAV-válasz.");
        using var reader=XmlReader.Create(new StringReader(text),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1_048_576});var doc=XDocument.Load(reader);
        if(doc.Root?.Name!=Api+"QueryTaxpayerResponse")throw new ArgumentException("A NAV-válasz típusa nem megfelelő.");
        string Value(XContainer node,string name)=>node.Descendants().FirstOrDefault(e=>e.Name.LocalName==name)?.Value.Trim()??"";
        if(requestId is not null&&Value(doc,"requestId")!=requestId)throw new ArgumentException("Eltérő NAV-válaszazonosító.");
        if(Value(doc,"funcCode")!="OK")throw new ArgumentException("A NAV nem teljesítette az adózólekérdezést. Hibakód: "+Value(doc,"errorCode"));
        if(Value(doc,"taxpayerValidity") is not ("true" or "1"))throw new ArgumentException("A NAV nem talált érvényes adózót. Nem mentettünk vevőt.");
        var data=doc.Descendants(Api+"taxpayerData").SingleOrDefault()??throw new ArgumentException("Hiányzó NAV-adózóadat.");
        var taxpayerId=Value(data,"taxpayerId");if(taxpayerId!=TaxNumber(requestedTax))throw new ArgumentException("A válasz adószáma nem a keresett adózóé.");
        var vat=Value(data,"vatCode");var county=Value(data,"countyCode");if(vat.Length!=1||county.Length!=2||!(vat+county).All(char.IsAsciiDigit))throw new ArgumentException("Hiányos NAV-adószám. Nem mentettünk vevőt.");
        var tax=taxpayerId+"-"+vat+"-"+county;
        var items=data.Descendants(Api+"taxpayerAddressItem").ToArray();var address=(items.FirstOrDefault(e=>Value(e,"taxpayerAddressType")=="HQ")??items.FirstOrDefault())?.Element(Api+"taxpayerAddress")??throw new ArgumentException("A NAV-válaszban nincs székhelycím.");
        var street=string.Join(" ",new[]{"streetName","publicPlaceCategory","number","building","staircase","floor","door"}.Select(key=>Value(address,key)).Where(v=>v.Length>0));
        var name=Value(data,"taxpayerShortName");if(string.IsNullOrWhiteSpace(name))name=Value(data,"taxpayerName");
        var result=new CheckoutCustomerSave(company,name,tax,Value(address,"countryCode"),Value(address,"postalCode"),Value(address,"city"),street);
        if(new[]{result.Name,result.Country,result.PostalCode,result.City,result.Address}.Any(string.IsNullOrWhiteSpace))throw new ArgumentException("A NAV-ból hiányos név vagy cím érkezett. Nem mentettünk vevőt.");
        return result;
    }
}
