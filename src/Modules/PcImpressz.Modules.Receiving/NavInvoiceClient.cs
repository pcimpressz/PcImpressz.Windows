using System.Globalization;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Xml;
using System.Xml.Linq;
namespace PcImpressz.Modules.Receiving;
public sealed record NavInvoiceQuery(Guid CompanyId,string From,string To,int Page=1);
public sealed record NavInvoiceImport(Guid CompanyId,string Number,string SupplierTaxNumber,Guid? SupplierId=null,int? BatchIndex=null);
public sealed record NavInvoiceDigest(string Number,string SupplierName,string SupplierTaxNumber,string IssuedOn,string Currency,string Operation,decimal? Net,int? BatchIndex=null);
public sealed record NavInvoicePage(int CurrentPage,int AvailablePage,NavInvoiceDigest[] Rows);
public static class NavInvoiceClient
{
    private static readonly XNamespace Api="http://schemas.nav.gov.hu/OSA/3.0/api";
    private static readonly HttpClient Client=new(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=TimeSpan.FromSeconds(40)};
    public static void Validate(NavInvoiceQuery query)
    {
        if(!DateOnly.TryParseExact(query.From,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var from)||!DateOnly.TryParseExact(query.To,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var to)||from>to||to.DayNumber-from.DayNumber>34||query.Page is <1 or >10000)throw new ArgumentException("Válassz legfeljebb 35 napos időszakot és érvényes oldalt.");
    }
    public static async Task<NavInvoicePage> ListAsync(NavConnectionSecrets secrets,NavInvoiceQuery query,CancellationToken ct,HttpClient? client=null)
    {
        Validate(query);var xml=await Request(secrets,"queryInvoiceDigest",new[]{new XElement(Api+"page",query.Page),new XElement(Api+"invoiceDirection","INBOUND"),new XElement(Api+"invoiceQueryParams",new XElement(Api+"mandatoryQueryParams",new XElement(Api+"invoiceIssueDate",new XElement(Api+"dateFrom",query.From),new XElement(Api+"dateTo",query.To))))},ct,client);
        int Page(string name){if(!int.TryParse(Value(xml,name),out var value)||value is <0 or >10000)throw new ArgumentException("Hibás NAV-lapozási adat.");return value;}
        var current=Page("currentPage");var available=Page("availablePage");if(current!=query.Page||available>0&&available<current)throw new ArgumentException("Eltérő NAV-válaszoldal.");
        var rows=xml.Descendants(Api+"invoiceDigest").Select(d=>new NavInvoiceDigest(Required(d,"invoiceNumber"),Value(d,"supplierName"),NavTaxpayerClient.TaxNumber(Required(d,"supplierTaxNumber")),Required(d,"invoiceIssueDate"),Value(d,"currency"),Value(d,"invoiceOperation"),decimal.TryParse(Value(d,"invoiceNetAmount"),NumberStyles.Number,CultureInfo.InvariantCulture,out var net)?net:null,int.TryParse(Value(d,"batchIndex"),out var batch)?batch:null)).ToArray();
        if(rows.Length>100)throw new ArgumentException("Túl sok számla egy NAV-válaszoldalon.");return new(current,available,rows);
    }
    public static async Task<ImportedDocument> DownloadAsync(NavConnectionSecrets secrets,NavInvoiceImport input,CancellationToken ct,HttpClient? client=null)
    {
        if(string.IsNullOrWhiteSpace(input.Number)||input.Number.Length>100||input.BatchIndex is <1)throw new ArgumentException("Érvénytelen számlaazonosító.");
        var supplier=NavTaxpayerClient.TaxNumber(input.SupplierTaxNumber);var key=new XElement(Api+"invoiceNumberQuery",new XElement(Api+"invoiceNumber",input.Number),new XElement(Api+"invoiceDirection","INBOUND"));if(input.BatchIndex is int batch)key.Add(new XElement(Api+"batchIndex",batch));key.Add(new XElement(Api+"supplierTaxNumber",supplier));
        var response=await Request(secrets,"queryInvoiceData",new[]{key},ct,client);var operation=Value(response,"invoiceOperation");if(operation.Length>0&&operation!="CREATE")throw new ArgumentException("Módosító vagy érvénytelenített számla külön ellenőrzést igényel.");var encoded=Value(response,"invoiceData");if(encoded.Length==0)throw new ArgumentException("A NAV nem adott vissza számlaadatot.");
        byte[] bytes;try{bytes=Convert.FromBase64String(encoded);}catch(FormatException){throw new ArgumentException("Hibás NAV-számlakódolás.");}
        var compressed=Value(response,"compressedContentIndicator");if(compressed is not ("true" or "false" or "1" or "0"))throw new ArgumentException("Hiányzó NAV-tömörítési jelző.");
        if(compressed is "true" or "1"){using var source=new MemoryStream(bytes);using var gzip=new GZipStream(source,CompressionMode.Decompress);bytes=await ReadLimited(gzip,ct,4*1024*1024);}
        if(bytes.Length>4*1024*1024)throw new ArgumentException("Túl nagy NAV-számla.");
        var text=Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');var document=Parse(text);if(document.Root?.Name.LocalName!="InvoiceData")throw new ArgumentException("A letöltött XML nem NAV-számla.");
        if(Value(document,"invoiceNumber")!=input.Number||NavTaxpayerClient.TaxNumber(Required(document,"supplierTaxNumber").Replace("\n","").Replace("\t",""))!=supplier)throw new ArgumentException("A letöltött számla nem a kiválasztott bizonylat.");
        var buyer=document.Descendants().FirstOrDefault(e=>e.Name.LocalName=="customerTaxNumber");var taxpayer=buyer?.Descendants().FirstOrDefault(e=>e.Name.LocalName=="taxpayerId")?.Value.Trim();if(taxpayer!=NavTaxpayerClient.TaxNumber(secrets.TaxNumber))throw new ArgumentException("A számla vevőjének adószáma nem a kiválasztott cégé.");
        if(document.Descendants().Any(e=>e.Name.LocalName=="invoiceReference"))throw new ArgumentException("Módosító vagy sztornó számlát nem könyvelünk normál bevételezésként. Külön ellenőrzést igényel.");
        return ReceivingImport.FromNav(text);
    }
    private static async Task<XDocument> Request(NavConnectionSecrets secrets,string operation,IEnumerable<XElement> body,CancellationToken ct,HttpClient? client)
    {
        var id="PCI"+Guid.NewGuid().ToString("N")[..25].ToUpperInvariant();var request=NavTaxpayerClient.Build(secrets,secrets.TaxNumber,DateTimeOffset.UtcNow,id);var root=request.Root!;var name=char.ToUpperInvariant(operation[0])+operation[1..];root.Name=Api+(name+"Request");root.Element(Api+"taxNumber")!.Remove();root.Add(body);
        var host=secrets.TestMode?"https://api-test.onlineszamla.nav.gov.hu":"https://api.onlineszamla.nav.gov.hu";using var message=new HttpRequestMessage(HttpMethod.Post,host+"/invoiceService/v3/"+operation){Content=new StringContent(request.ToString(SaveOptions.DisableFormatting),Encoding.UTF8,"application/xml")};
        using var response=await (client??Client).SendAsync(message,HttpCompletionOption.ResponseHeadersRead,ct);if(!response.IsSuccessStatusCode)throw new ArgumentException("A NAV-számlalekérdezés sikertelen (HTTP "+(int)response.StatusCode+"). Nem történt bevételezés.");
        await using var stream=await response.Content.ReadAsStreamAsync(ct);var xml=Parse(Encoding.UTF8.GetString(await ReadLimited(stream,ct,8*1024*1024)));
        if(xml.Root?.Name!=Api+(name+"Response")||Value(xml,"requestId")!=id)throw new ArgumentException("Eltérő NAV-válaszazonosító vagy válaszfajta.");if(Value(xml,"funcCode")!="OK")throw new ArgumentException("A NAV nem teljesítette a kérést. Hibakód: "+Value(xml,"errorCode"));return xml;
    }
    private static XDocument Parse(string text){using var reader=XmlReader.Create(new StringReader(text),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=8*1024*1024});return XDocument.Load(reader);}
    private static string Value(XContainer node,string name)=>node.Descendants().FirstOrDefault(e=>e.Name.LocalName==name)?.Value.Trim()??"";
    private static string Required(XContainer node,string name){var value=Value(node,name);if(value.Length==0)throw new ArgumentException("Hiányos NAV-válasz: "+name);return value;}
    private static async Task<byte[]> ReadLimited(Stream stream,CancellationToken ct,int limit){using var result=new MemoryStream();var buffer=new byte[8192];int count;while((count=await stream.ReadAsync(buffer,ct))>0){if(result.Length+count>limit)throw new ArgumentException("Túl nagy NAV-válasz.");result.Write(buffer,0,count);}return result.ToArray();}
}