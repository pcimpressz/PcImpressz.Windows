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
                return CatalogJson.Element(new{cards=new[]{new{label="Saját készlet",value=Number(movements.Sum(m=>m.GetProperty("quantity").GetDecimal()))+" "+product.UnitCode},new{label="Eladott mennyiség · visszavételekkel",value=Number(sales.Sum(l=>l.GetProperty("quantity").GetDecimal()))+" "+product.UnitCode},new{label="Forgalom · bruttó",value=sales.Sum(l=>l.GetProperty("net").GetDecimal()+l.GetProperty("vat").GetDecimal()).ToString("N0",CultureInfo.GetCultureInfo("hu-HU"))+" Ft"},new{label="Utolsó készletmozgás",value=movements.Length==0?"Még nincs":movements.Max(m=>m.GetProperty("at").GetDateTimeOffset()).ToLocalTime().ToString("yyyy.MM.dd. HH:mm")}},notice=warehouse.Length==0?"Még nincs készletmozgás.":"Raktáranként\n"+warehouse});
            }));
        });
    }
}