using System.Globalization;
using PcImpressz.Business;
using PcImpressz.Contracts;

namespace PcImpressz.Modules.Inventory;
public sealed record StockAdjustment(Guid CompanyId,Guid OperationId,Guid ProductId,string Warehouse,decimal Quantity,string Reason);
public sealed record StockReverse(Guid CompanyId,Guid Id);
public sealed class InventoryModule:IModule
{
    public ValueTask StartAsync(IModuleContext context,CancellationToken cancellationToken)
    {
        DomainCommands.Register(context,"list","inventory.read",CommandAccess.RecoveryReadOnly,(tx,input,_)=>
        {
            var search=DomainCommands.Search(input);var productId=ProductId(input);var rows=StockLedger.Balances(tx).Where(b=>productId is null || b.ProductId==productId).Where(b=>DomainCommands.Matches(b.ProductName+" "+b.Warehouse,search)).ToArray();
            return DomainCommands.Json(new {rows=rows.Take(500),total=rows.Length,movements=StockLedger.Movements(tx).Where(m=>productId is null || m.ProductId==productId).Where(m=>DomainCommands.Matches(m.ProductName+" "+m.Warehouse+" "+m.Reason,search)).OrderByDescending(m=>m.At).Take(200)});
        });
        DomainCommands.Register(context,"references","inventory.read",CommandAccess.RecoveryReadOnly,(tx,input,_)=>
            DomainCommands.Json(new { products=CompanyReferenceQueries.Products(tx).Where(p=>p.IsActive&&(ProductId(input) is null || p.Id==ProductId(input))&&DomainCommands.Matches(p.Name+" "+p.Sku,DomainCommands.Search(input))).Take(200) }));
        DomainCommands.Register(context,"save","inventory.write",CommandAccess.Licensed,(tx,input,actor)=>
        {
            var request=DomainCommands.Read<StockAdjustment>(input);
            if(request.OperationId==Guid.Empty)throw new ArgumentException("Hiányzó műveletazonosító.");
            var reason=(request.Reason??"").Trim();
            if(reason.Length is <2 or >200 || reason.Any(char.IsControl))throw new ArgumentException("A készletváltozás indoklása kötelező (2–200 karakter).");
            var product=CompanyReferenceQueries.Products(tx,[request.ProductId]).SingleOrDefault(p=>p.Id==request.ProductId&&p.IsActive) ?? throw new ArgumentException("Válassz aktív terméket.");
            return DomainCommands.Json(StockLedger.Post(tx,"manual:"+request.OperationId.ToString("N"),"inventory",request.OperationId,
                [new(product.Id,product.Name,product.UnitCode,request.Warehouse,request.Quantity)],reason,actor,context.TimeProvider.GetUtcNow()));
        });
        DomainCommands.Register(context,"reverse","inventory.write",CommandAccess.Licensed,(tx,input,actor)=>
        {
            var request=DomainCommands.Read<StockReverse>(input);
            var original=tx.Read<StockOperation>("inventory","operation/manual:"+request.Id.ToString("N")) ?? throw new ArgumentException("Csak kézi készletművelet fordítható vissza itt.");
            return DomainCommands.Json(StockLedger.Post(tx,"manual-reverse:"+request.Id.ToString("N"),"inventory",request.Id,
                original.Movements.Select(m=>new StockChange(m.ProductId,m.ProductName,m.UnitCode,m.Warehouse,-m.Quantity)).ToArray(),
                "Kézi mozgás visszafordítása",actor,context.TimeProvider.GetUtcNow()));
        });
        DomainCommands.Register(context,"export","inventory.export",CommandAccess.RecoveryReadOnly,(tx,input,_)=>
            DomainCommands.Json(new {csv=DomainCommands.Csv(new[]{new[]{"Termék","Raktár","Mennyiség","Egység"}}.Concat(StockLedger.Balances(tx).Where(b=>(ProductId(input) is null || b.ProductId==ProductId(input))&&DomainCommands.Matches(b.ProductName+" "+b.Warehouse,DomainCommands.Search(input))).Select(b=>new[]{b.ProductName,b.Warehouse,b.Quantity.ToString(CultureInfo.InvariantCulture),b.UnitCode})))}));
        context.RegisterPage(new("inventory",context.ModuleId,"Készlet","Készletegyenleg, nyitókészlet és naplózott korrekciók.","\uE7B8",
            [new("reason","Indoklás")],[new("productName","Termék"),new("warehouse","Raktár"),new("quantity","Készlet"),new("unitCode","Egység")],new("list","save","reverse","export")) { ViewKind="inventory" });
        return ValueTask.CompletedTask;
    }
    private static Guid? ProductId(System.Text.Json.JsonElement input)
    {
        if(!input.TryGetProperty("productId",out var id)||id.ValueKind==System.Text.Json.JsonValueKind.Null)return null;
        if(id.ValueKind!=System.Text.Json.JsonValueKind.String||!id.TryGetGuid(out var guid)||guid==Guid.Empty)throw new ArgumentException("Érvénytelen termékazonosító.");
        return guid;
    }
    public ValueTask StopAsync(CancellationToken cancellationToken)=>ValueTask.CompletedTask;
}
