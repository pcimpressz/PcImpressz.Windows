using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PcImpressz.Modules.Receiving;

public sealed record BizonylatClient(string Name,string TaxNumber,string Country,string PostalCode,string City,string Address,string Email="");
public sealed record BizonylatLine(string Name,decimal Quantity,decimal UnitPrice,decimal VatRate,string? Sku=null);
public sealed record BizonylatIssueRequest(BizonylatClient Client,BizonylatLine[] Lines,string Currency,string PaymentMethod,string PaymentStatus,string? DueDate,string? DeliveryDate,bool SendEmail,string? InvoicePrefix=null,string? Notes=null);
public sealed record BizonylatIssueResult(string Id,string InvoiceNumber,decimal NetTotal,decimal VatTotal,decimal GrossTotal,string Currency,string PaymentStatus,string NavStatus,string PublicUrl,string PublicPdfUrl,string IssueDate,string DueDate);

public static class BizonylatInvoiceClient
{
    private static readonly HttpClient Client=new(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=TimeSpan.FromSeconds(40)};
    public static async Task<BizonylatIssueResult> IssueAsync(BizonylatConnectionSecrets secrets,string idempotencyKey,BizonylatIssueRequest request,CancellationToken ct,HttpClient? client=null)
    {
        if(string.IsNullOrWhiteSpace(idempotencyKey)||idempotencyKey.Length>120)throw new ArgumentException("Érvénytelen számlázási műveletazonosító.");
        if(request.Lines is null||request.Lines.Length is <1 or >400)throw new ArgumentException("A számlán 1–400 tétel lehet.");
        using var message=new HttpRequestMessage(HttpMethod.Post,"https://api.bizonylat.hu/public/v1/invoices");
        message.Headers.Authorization=new AuthenticationHeaderValue("Bearer",secrets.ApiKey);message.Headers.Add("Idempotency-Key",idempotencyKey);message.Headers.Add("Accept-Language","hu");
        var body=new
        {
            client=new{name=request.Client.Name,taxNumber=Empty(request.Client.TaxNumber),country=Empty(request.Client.Country),postalCode=Empty(request.Client.PostalCode),city=Empty(request.Client.City),address=Empty(request.Client.Address),email=Empty(request.Client.Email)},
            lines=request.Lines.Select(x=>new{name=x.Name,sku=Empty(x.Sku),quantity=x.Quantity,unitPrice=x.UnitPrice,vatRate=x.VatRate}).ToArray(),
            pricesIncludeVat=false,currency=request.Currency,paymentMethod=request.PaymentMethod,paymentStatus=request.PaymentStatus,dueDate=Empty(request.DueDate),deliveryDate=Empty(request.DeliveryDate),sendEmail=request.SendEmail,
            invoicePrefix=Empty(string.IsNullOrWhiteSpace(request.InvoicePrefix)?secrets.InvoicePrefix:request.InvoicePrefix),notes=Empty(request.Notes)
        };
        message.Content=new StringContent(JsonSerializer.Serialize(body,ReceivingJson.Options),Encoding.UTF8,"application/json");
        using var response=await (client??Client).SendAsync(message,HttpCompletionOption.ResponseHeadersRead,ct);await using var stream=await response.Content.ReadAsStreamAsync(ct);using var buffer=new MemoryStream();var chunk=new byte[8192];int read;while((read=await stream.ReadAsync(chunk,ct))>0){if(buffer.Length+read>1_000_000)throw new ArgumentException("Túl nagy szolgáltatói válasz; az eredmény egyeztetendő.");buffer.Write(chunk,0,read);}var text=Encoding.UTF8.GetString(buffer.ToArray());
        JsonDocument doc;try{doc=JsonDocument.Parse(text);}catch(JsonException){throw new ArgumentException("A Bizonylat API nem értelmezhető választ adott (HTTP "+(int)response.StatusCode+").");}
        using(doc)
        {
            var root=doc.RootElement;var data=root.TryGetProperty("data",out var nested)&&nested.ValueKind==JsonValueKind.Object?nested:root;
            if(!response.IsSuccessStatusCode)
            {
                throw new ArgumentException("Bizonylat API hiba (HTTP "+(int)response.StatusCode+"). Az eredmény egyeztetendő; automatikus újraküldés nincs.");
            }
            var id=Required(data,"id");var number=First(data,"invoiceNumber","number");
            if(string.IsNullOrWhiteSpace(number))throw new ArgumentException("A Bizonylat API nem adott vissza számlaszámot.");
            return new(id,number,Decimal(data,"netTotal"),Decimal(data,"vatTotal"),Decimal(data,"grossTotal"),First(data,"currency"),First(data,"paymentStatus"),First(data,"navStatus"),First(data,"publicUrl"),First(data,"publicPdfUrl","pdfUrl"),First(data,"issueDate","issuedOn"),First(data,"dueDate"));
        }
    }
    private static object? Empty(string? value)=>string.IsNullOrWhiteSpace(value)?null:value;
    private static string Required(JsonElement e,string name){var s=Text(e,name);if(s.Length==0)throw new ArgumentException("Hiányos Bizonylat API-válasz: "+name);return s;}
    private static string First(JsonElement e,params string[] names){foreach(var n in names){var s=Text(e,n);if(s.Length>0)return s;}return "";}
    private static string Text(JsonElement e,string name)=>e.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
    private static decimal Decimal(JsonElement e,string name)=>e.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetDecimal(out var n)?n:throw new ArgumentException("Hiányos számlaösszeg: "+name);
}
