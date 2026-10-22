using System.Globalization;
using System.Text.Json;
using PcImpressz.Contracts;
namespace PcImpressz.Modules.Catalog;
public sealed partial class CatalogModule
{
    private static void RegisterProductOverview(IModuleContext context)
    {
        Register(context,"products-overview","catalog.read",CommandAccess.RecoveryReadOnly,(input,_,ct)=>
        {
            ct.ThrowIfCancellationRequested();var company=input.GetProperty("companyId").GetGuid();var id=input.GetProperty("id").GetGuid();
            var db=context.Database??throw new InvalidOperationException("A termékösszesítőhöz céges adatbázis kell.");
            return Task.FromResult(db.Execute(company,tx=>
            {
                var product=CompanyReferenceQueries.Products(tx).SingleOrDefault(p=>p.Id==id)??throw new ArgumentException("A termék nem található.");
                var movements=tx.List("inventory","movement/").Select(body=>JsonSerializer.Deserialize<JsonElement>(body)).Where(m=>m.GetProperty("productId").GetGuid()==id).ToArray();
                var sales=tx.List("sales","sale/").Select(body=>JsonSerializer.Deserialize<JsonElement>(body)).Where(s=>s.GetProperty("status").GetString()=="vegleges").SelectMany(s=>s.GetProperty("lines").EnumerateArray()).Where(l=>l.GetProperty("productId").GetGuid()==id).ToArray();
                string Number(decimal number)=>number.ToString("0.####",CultureInfo.GetCultureInfo("hu-HU"));
                var warehouse=string.Join("\n",movements.GroupBy(m=>m.GetProperty("warehouse").GetString()).OrderBy(g=>g.Key).Select(g=>g.Key+": "+Number(g.Sum(m=>m.GetProperty("quantity").GetDecimal()))+" "+product.UnitCode));
                var purchases=Purchases(tx,id).ToArray();
                var last=purchases.OrderByDescending(p=>p.At).FirstOrDefault();
                var usual=purchases.GroupBy(p=>p.Supplier).OrderByDescending(g=>g.Count()).ThenByDescending(g=>g.Max(p=>p.At)).Select(g=>g.Key).FirstOrDefault();
                string Ft(decimal? amount)=>amount is null?"—":amount.Value.ToString(amount.Value==decimal.Round(amount.Value,0)?"N0":"N2",CultureInfo.GetCultureInfo("hu-HU"))+" Ft";
                decimal? gross=product.SaleNetUnitPrice is null?null:decimal.Round(product.SaleNetUnitPrice.Value*(1+product.SaleVatRate/100m),2,MidpointRounding.AwayFromZero);
                var bought=purchases.Length>0;
                var cards=new[]{new{label="Saját készlet",value=Number(movements.Sum(m=>m.GetProperty("quantity").GetDecimal()))+" "+product.UnitCode},new{label="Eladott mennyiség · visszavételekkel",value=Number(sales.Sum(l=>l.GetProperty("quantity").GetDecimal()))+" "+product.UnitCode},new{label="Forgalom · bruttó",value=sales.Sum(l=>l.GetProperty("net").GetDecimal()+l.GetProperty("vat").GetDecimal()).ToString("N0",CultureInfo.GetCultureInfo("hu-HU"))+" Ft"},new{label="Utolsó készletmozgás",value=movements.Length==0?"Még nincs":movements.Max(m=>m.GetProperty("at").GetDateTimeOffset()).ToLocalTime().ToString("yyyy.MM.dd. HH:mm")},new{label="Nettó eladási ár",value=Ft(product.SaleNetUnitPrice)},new{label="Bruttó eladási ár",value=Ft(gross)},new{label="Utolsó beszerzési ár",value=bought?Ft(last.Net)+" nettó":"Még nincs bevételezve"},new{label="Szokásos szállító",value=string.IsNullOrWhiteSpace(usual)?"Még nincs bevételezve":usual},new{label="Utolsó bevételezés",value=bought?last.At.ToLocalTime().ToString("yyyy.MM.dd."):"Még nincs"}};
                return CatalogJson.Element(new{cards,notice=warehouse.Length==0?"Még nincs készletmozgás.":"Raktáranként\n"+warehouse});
            }));
        });
    }
    private readonly record struct PurchaseFact(string Supplier, decimal Net, DateTimeOffset At);
    private static IEnumerable<PurchaseFact> Purchases(ICompanyTransaction tx, Guid productId)
    {
        if (tx.Get("receiving", "state") is not { } body) yield break;
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("receipts", out var receipts) || receipts.ValueKind != JsonValueKind.Array) yield break;
        foreach (var row in receipts.EnumerateArray())
        {
            if (!row.TryGetProperty("data", out var data)) continue;
            if (data.TryGetProperty("status", out var status) && status.GetString() != "bevetelezve") continue;
            if (data.TryGetProperty("reversedAt", out var reversed) && reversed.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined) continue;
            var supplier = data.TryGetProperty("supplierName", out var name) ? name.GetString() ?? "" : "";
            var at = data.TryGetProperty("receivedAt", out var received) && received.ValueKind == JsonValueKind.String && received.TryGetDateTimeOffset(out var stamped)
                ? stamped
                : data.TryGetProperty("issuedOn", out var issued) && DateTime.TryParse(issued.GetString(), out var day) ? new DateTimeOffset(day) : DateTimeOffset.MinValue;
            if (!data.TryGetProperty("lines", out var lines) || lines.ValueKind != JsonValueKind.Array) continue;
            foreach (var line in lines.EnumerateArray())
            {
                if (!line.TryGetProperty("productId", out var id) || id.ValueKind != JsonValueKind.String || !Guid.TryParse(id.GetString(), out var lineProduct) || lineProduct != productId) continue;
                if (!line.TryGetProperty("netUnitPrice", out var price) || price.ValueKind != JsonValueKind.Number) continue;
                yield return new(supplier, price.GetDecimal(), at);
            }
        }
    }
}