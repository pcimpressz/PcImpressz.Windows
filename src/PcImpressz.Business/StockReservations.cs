using System.Text.Json;
using PcImpressz.Contracts;
namespace PcImpressz.Business;
public static class StockReservations
{
    public static string Key(Guid source,Guid line)=>"reservation/"+source.ToString("N")+"/"+line.ToString("N");
    public static decimal Quantity(ICompanyTransaction tx,Guid product,string warehouse,DateTimeOffset at)=>tx is IIndexedCompanyTransaction index
        ?index.ReservedQuantity(product,warehouse,at)
        :tx.List("inventory","reservation/").Select(x=>JsonSerializer.Deserialize<InventoryReservation>(x,CompanyDocuments.Json)!).Where(r=>r.ProductId==product&&r.Warehouse==warehouse&&r.ExpiresAt>at).Sum(r=>r.Quantity);
    public static decimal Physical(ICompanyTransaction tx,Guid product,string warehouse)=>tx is IIndexedCompanyTransaction index
        ?index.StockBalances(product).Where(b=>b.Warehouse==warehouse).Sum(b=>b.Quantity)
        :StockLedger.Balances(tx).Where(b=>b.ProductId==product&&b.Warehouse==warehouse).Sum(b=>b.Quantity);
    public static decimal Free(ICompanyTransaction tx,Guid product,string warehouse,DateTimeOffset at)=>Physical(tx,product,warehouse)-Quantity(tx,product,warehouse,at);
}
