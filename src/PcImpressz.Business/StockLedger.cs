using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PcImpressz.Contracts;

namespace PcImpressz.Business;

public sealed record StockChange(Guid ProductId, string ProductName, string UnitCode, string Warehouse, decimal Quantity);
public sealed record StockMovement(Guid Id, string OperationId, string SourceModule, Guid SourceId, Guid ProductId,
    string ProductName, string UnitCode, string Warehouse, decimal Quantity, string Reason, DateTimeOffset At, string Actor);
public sealed record StockOperation(string Fingerprint, StockMovement[] Movements);
public sealed record StockBalance(Guid ProductId,string ProductName,string UnitCode,string Warehouse,decimal Quantity);

public static class StockLedger
{
    public static StockMovement[] Movements(ICompanyTransaction tx) => tx.List("inventory","movement/")
        .Select(body=>JsonSerializer.Deserialize<StockMovement>(body,CompanyDocuments.Json) ?? throw new IOException("Sérült készletmozgás.")).ToArray();
    public static StockBalance[] Balances(ICompanyTransaction tx) => tx is IIndexedCompanyTransaction index ? index.StockBalances().Select(b=>new StockBalance(b.ProductId,b.ProductName,b.UnitCode,b.Warehouse,b.Quantity)).OrderBy(b=>b.ProductName,StringComparer.CurrentCultureIgnoreCase).ThenBy(b=>b.Warehouse).ToArray() : Movements(tx).GroupBy(m=>(m.ProductId,m.Warehouse))
        .Select(g=>new StockBalance(g.Key.ProductId,g.Last().ProductName,g.Last().UnitCode,g.Key.Warehouse,g.Sum(m=>m.Quantity)))
        .OrderBy(b=>b.ProductName,StringComparer.CurrentCultureIgnoreCase).ThenBy(b=>b.Warehouse).ToArray();
    public static string Warehouse(string? value)
    {
        var text=(value??"").Trim();
        if(text.Length is <1 or >80 || text.Any(char.IsControl)) throw new ArgumentException("A raktár neve 1–80 karakter legyen.");
        return text;
    }
    public static decimal Quantity(decimal value)
    {
        if(value==0 || value < -1_000_000 || value > 1_000_000 || decimal.Round(value,4)!=value)
            throw new ArgumentException("A mennyiség nem nulla, legfeljebb egymillió és legfeljebb négy tizedes lehet.");
        return value;
    }
    public static StockMovement[] Post(ICompanyTransaction tx,string operationId,string sourceModule,Guid sourceId,
        StockChange[] changes,string reason,string actor,DateTimeOffset at)
    {
        if(sourceId==Guid.Empty || string.IsNullOrWhiteSpace(actor) || operationId.Length is <1 or >150 || changes.Length>400)
            throw new ArgumentException("Hiányzó készletműveleti azonosító vagy túl sok tétel.");
        changes=changes.Select(c=>c with { Warehouse=Warehouse(c.Warehouse),Quantity=Quantity(c.Quantity) }).ToArray();
        if(changes.Any(c=>c.ProductId==Guid.Empty || string.IsNullOrWhiteSpace(c.ProductName) || string.IsNullOrWhiteSpace(c.UnitCode)))
            throw new ArgumentException("A készletmozgáshoz termék és mértékegység kell.");
        var canonical=JsonSerializer.Serialize(new {sourceModule,sourceId,changes,reason},CompanyDocuments.Json);
        var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var key="operation/"+operationId;
        if(tx.Read<StockOperation>("inventory",key) is { } previous)
        {
            if(previous.Fingerprint!=fingerprint) throw new ArgumentException("Ezzel a műveletazonosítóval már másik készletmozgás készült.");
            return previous.Movements;
        }
        var current=tx is IIndexedCompanyTransaction index ? changes.Select(c=>c.ProductId).Distinct().SelectMany(id=>index.StockBalances(id)).Select(b=>new StockBalance(b.ProductId,b.ProductName,b.UnitCode,b.Warehouse,b.Quantity)).ToArray() : Balances(tx);
        var balances=current.ToDictionary(b=>(b.ProductId,b.Warehouse),b=>b.Quantity);
        foreach(var group in changes.GroupBy(c=>(c.ProductId,c.Warehouse)))
            if(balances.GetValueOrDefault(group.Key)+group.Sum(c=>c.Quantity)<(group.Sum(c=>c.Quantity)<0?StockReservations.Quantity(tx,group.Key.ProductId,group.Key.Warehouse,at):0))
                throw new ArgumentException("Nincs elegendő szabad készlet (más rendelés foglalását is figyelembe véve): "+group.First().ProductName+" · "+group.Key.Warehouse);
        var movements=changes.Select(c=>new StockMovement(Guid.NewGuid(),operationId,sourceModule,sourceId,c.ProductId,
            c.ProductName,c.UnitCode,c.Warehouse,c.Quantity,reason,at,actor)).ToArray();
        foreach(var movement in movements) tx.Write("inventory","movement/"+movement.Id.ToString("N"),movement);
        tx.Write("inventory",key,new StockOperation(fingerprint,movements));
        return movements;
    }
}
