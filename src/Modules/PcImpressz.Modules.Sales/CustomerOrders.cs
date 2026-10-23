using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using PcImpressz.Business;
using PcImpressz.Contracts;
using PcImpressz.Modules.Partners;
namespace PcImpressz.Modules.Sales;

public static class CustomerOrders
{
    private sealed record Result(string Fingerprint,CustomerOrder Order);
    private static string Key(Guid id)=>"order/"+id.ToString("N");
    public static CustomerOrder Get(ICompanyTransaction tx,Guid id,DateTimeOffset at)
    {
        var order=tx.Read<CustomerOrder>("sales",Key(id))??throw new RecordNotFoundException("A rendelés nem található.");
        if(order.ReservationUntil is {} until&&until<=at)
        {
            order=order with{Lines=order.Lines.Select(l=>l with{Reserved=0}).ToArray()};
            if(order.Status=="reserved")order=order with{Status=order.Lines.Any(l=>l.Delivered>0)?"partial":"open"};
        }
        return order;
    }
    private static CustomerOrder Version(ICompanyTransaction tx,Guid id,long version,DateTimeOffset at)
    {
        var order=Get(tx,id,at);if(order.Version!=version)throw new ArgumentException("A rendelés közben módosult. Frissítsd és nyisd meg újra.");
        if(order.Status is "cancelled" or "completed")throw new ArgumentException("A lezárt rendelés már nem módosítható.");return order;
    }
    private static CustomerOrder Once<T>(ICompanyTransaction tx,Guid requestId,string action,T input,Func<CustomerOrder> apply)
    {
        if(requestId==Guid.Empty)throw new ArgumentException("Műveletazonosító szükséges.");
        var fingerprint=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{action,input},CompanyDocuments.Json)));
        var key="order-request/"+requestId.ToString("N");
        if(tx.Read<Result>("sales",key) is {} previous)
        {if(previous.Fingerprint!=fingerprint)throw new ArgumentException("Ezzel a műveletazonosítóval eltérő kérés már lezajlott.");return previous.Order;}
        var result=apply();tx.Write("sales",key,new Result(fingerprint,result));return result;
    }
    private static CustomerOrder Store(ICompanyTransaction tx,CustomerOrder order,string action,string actor,DateTimeOffset at)
    {
        order=order with{Version=order.Version+1,UpdatedAt=at,Actor=actor};tx.Write("sales",Key(order.Id),order);
        tx.Write("sales","order-audit/"+order.Id.ToString("N")+"/"+Guid.NewGuid().ToString("N"),new{order.Id,order.Version,action,actor,at});return order;
    }
    private static string Text(string? text,int max,string label)
    {text=(text??"").Trim();if(text.Length>max||text.Any(c=>char.IsControl(c)&&c is not '\r' and not '\n' and not '\t'))throw new ArgumentException(label+": túl hosszú vagy érvénytelen szöveg.");return text;}
    public static CustomerOrder Save(ICompanyTransaction tx,OrderSave request,string actor,DateTimeOffset at)=>Once(tx,request.RequestId,"save",request,()=>
    {
        if(request.Id==Guid.Empty||request.Lines is null||request.Lines.Length is <1 or >200||request.Lines.Any(l=>l is null||l.Id==Guid.Empty)||request.Lines.Select(l=>l.Id).Distinct().Count()!=request.Lines.Length)throw new ArgumentException("A rendeléshez 1–200, egyedi azonosítójú tétel szükséges.");
        var previous=tx.Read<CustomerOrder>("sales",Key(request.Id));
        if(previous is null&&request.Version!=0)throw new ArgumentException("A rendelés nem található.");
        if(previous is not null)
        {
            previous=Version(tx,request.Id,request.Version,at);
            if(previous.Lines.Any(l=>l.Delivered>0||l.Reserved>0))throw new ArgumentException("Szerkesztés előtt oldd fel a foglalást. Részben átadott rendelés tételei már nem szerkeszthetők.");
        }
        if(!OrderWorkflow.Editable(request.WorkflowStatus))throw new ArgumentException("Érvénytelen rendelési állapot.");
        var customer=ResolveCustomer(tx,request,actor,at);
        var warehouse=StockLedger.Warehouse(request.Warehouse);var notes=Text(request.Notes,2000,"Megjegyzés");var due=Text(request.DueDate,10,"Határidő");
        if(due.Length>0&&!DateOnly.TryParseExact(due,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _))throw new ArgumentException("A határidő formátuma ÉÉÉÉ-HH-NN.");
        var products=CompanyReferenceQueries.Products(tx,request.Lines.Select(l=>l.ProductId)).ToDictionary(p=>p.Id);
        var lines=request.Lines.Select(l=>
        {
            if(!products.TryGetValue(l.ProductId,out var p)||!p.IsActive||!p.IsSalable)throw new ArgumentException("Csak aktív, eladható termék rendelhető.");
            if(StockLedger.Quantity(l.Quantity)<=0||l.NetUnitPrice is <0 or >1_000_000_000||VatMath.Round(l.NetUnitPrice,4)!=l.NetUnitPrice||!VatMath.IsSupportedRate(l.VatRate))throw new ArgumentException("Érvénytelen mennyiség, ár vagy áfakulcs.");
            return new CustomerOrderLine(l.Id,p.Id,p.Name,p.Sku,p.UnitCode,l.Quantity,l.NetUnitPrice,l.VatRate);
        }).ToArray();
        var amounts=lines.Select(l=>VatMath.Line(l.Quantity,l.NetUnitPrice,l.VatRate)).ToArray();
        var number=previous?.Number;
        if(number is null)
        {
            var key="order-counter/"+at.Year;var value=(tx.Read<SaleCounter>("sales",key)?.Value??0)+1;
            if(value>999999)throw new ArgumentException("Az éves rendelésszámtartomány megtelt.");tx.Write("sales",key,new SaleCounter(value));number="VR-"+at.Year+"-"+value.ToString("D6",CultureInfo.InvariantCulture);
        }
        if(previous is not null)foreach(var line in previous.Lines)tx.Delete("inventory",StockReservations.Key(previous.Id,line.Id));
        var order=new CustomerOrder(request.Id,previous?.Version??0,number,customer.Id,customer.Name,warehouse,due,notes,"open",lines,amounts.Sum(a=>a.Net),amounts.Sum(a=>a.Vat),amounts.Sum(a=>a.Gross),previous?.CreatedAt??at,at,actor,Customer:customer.Snapshot,WorkflowStatus:request.WorkflowStatus);
        return Store(tx,order,previous is null?"create":"edit",actor,at);
    });
    public static CustomerOrder Reserve(ICompanyTransaction tx,OrderAction request,string actor,DateTimeOffset at)=>Once(tx,request.RequestId,"reserve",request,()=>
    {
        if(request.ReservationDays is <1 or >365)throw new ArgumentException("A foglalás 1–365 nap lehet.");
        var order=Version(tx,request.Id,request.Version,at);var until=at.AddDays(request.ReservationDays);
        foreach(var line in order.Lines)tx.Delete("inventory",StockReservations.Key(order.Id,line.Id));
        var lines=order.Lines.Select(line=>
        {
            var free=Math.Max(0,StockReservations.Free(tx,line.ProductId,order.Warehouse,at));var take=Math.Min(free,line.Quantity-line.Delivered);
            if(take>0)tx.Write("inventory",StockReservations.Key(order.Id,line.Id),new InventoryReservation(order.Id,line.Id,line.ProductId,order.Warehouse,take,until));
            return line with{Reserved=take};
        }).ToArray();
        var status=lines.Any(l=>l.Delivered>0)?"partial":lines.All(l=>l.Reserved==l.Quantity)?"reserved":"open";
        return Store(tx,order with{Lines=lines,ReservationUntil=until,Status=status,WorkflowStatus=lines.All(l=>l.Reserved+l.Delivered==l.Quantity)?"arrived":order.WorkflowStatus},"reserve",actor,at);
    });
    public static CustomerOrder Release(ICompanyTransaction tx,OrderAction request,string actor,DateTimeOffset at,bool cancel=false)=>Once(tx,request.RequestId,cancel?"cancel":"release",request,()=>
    {
        var order=Version(tx,request.Id,request.Version,at);
        foreach(var line in order.Lines)tx.Delete("inventory",StockReservations.Key(order.Id,line.Id));
        return Store(tx,order with{Lines=order.Lines.Select(l=>l with{Reserved=0}).ToArray(),ReservationUntil=null,Status=cancel?"cancelled":order.Lines.Any(l=>l.Delivered>0)?"partial":"open"},cancel?"cancel":"release",actor,at);
    });
    public static CustomerOrder Deliver(ICompanyTransaction tx,OrderDeliver request,string actor,DateTimeOffset at)=>Once(tx,request.RequestId,"deliver",request,()=>
    {
        var order=Version(tx,request.Id,request.Version,at);
        if(request.Lines is null||request.Lines.Length is <1 or >200||request.Lines.Any(l=>l is null)||request.Lines.Select(l=>l.LineId).Distinct().Count()!=request.Lines.Length)throw new ArgumentException("Válassz egyedi rendelési sorokat az átadáshoz.");
        var quantities=request.Lines.ToDictionary(l=>l.LineId,l=>l.Quantity);
        if(quantities.Keys.Any(id=>!order.Lines.Any(l=>l.Id==id)))throw new ArgumentException("Ismeretlen rendelési sor.");
        var saleLines=new List<SaleLineDraft>();
        var lines=order.Lines.Select(line=>
        {
            if(!quantities.TryGetValue(line.Id,out var quantity))return line;
            if(StockLedger.Quantity(quantity)<=0||quantity>line.Quantity-line.Delivered||quantity>line.Reserved)throw new ArgumentException(line.Name+": csak a lefoglalt, még át nem adott mennyiség adható át.");
            var remaining=line.Reserved-quantity;var key=StockReservations.Key(order.Id,line.Id);
            if(remaining==0)tx.Delete("inventory",key);else tx.Write("inventory",key,new InventoryReservation(order.Id,line.Id,line.ProductId,order.Warehouse,remaining,order.ReservationUntil!.Value));
            saleLines.Add(new(line.ProductId,quantity,line.NetUnitPrice,line.VatRate));return line with{Delivered=line.Delivered+quantity,Reserved=remaining};
        }).ToArray();
        var sale=SalesModule.Post(tx,new(request.CompanyId,request.RequestId,order.CustomerId==Guid.Empty?null:order.CustomerId,order.Warehouse,saleLines.ToArray(),request.PaymentMethod,request.InvoiceRequested),actor,at,order.CustomerId==Guid.Empty&&order.Customer is {} c?new BillingCustomer(Guid.Empty,c.Name,c.TaxId,c.Country,c.PostalCode,c.City,c.Address,true,true):null);
        var status=lines.All(l=>l.Delivered==l.Quantity)?"completed":"partial";
        return Store(tx,order with{Lines=lines,Status=status,Deliveries=(order.Deliveries??[]).Append(new(sale.Id,sale.Number,at,request.Lines.Select(l=>l.LineId).ToArray())).ToArray()},"deliver",actor,at);
    });
    public static CustomerOrder ChangeWorkflow(ICompanyTransaction tx,OrderWorkflowChange request,string actor,DateTimeOffset at)=>Once(tx,request.RequestId,"workflow",request,()=>
    {
        if(!OrderWorkflow.Editable(request.Status))throw new ArgumentException("Az Átadva állapot csak véglegesített átadással, a Lemondva csak lemondással állítható be.");
        var order=Version(tx,request.Id,request.Version,at);
        return Store(tx,order with{WorkflowStatus=request.Status},"workflow:"+request.Status,actor,at);
    });
    private sealed class OrderClock(DateTimeOffset now):TimeProvider{public override DateTimeOffset GetUtcNow()=>now;}
    private sealed class BoundDatabase(ICompanyTransaction tx):ICompanyDatabase{public T Execute<T>(Guid companyId,Func<ICompanyTransaction,T> action)=>action(tx);}
    private static (Guid Id,string Name,OrderCustomerDraft Snapshot) ResolveCustomer(ICompanyTransaction tx,OrderSave request,string actor,DateTimeOffset at)
    {
        var repository=new PartnerRepository(_=>"",new OrderClock(at),new BoundDatabase(tx));
        OrderCustomerDraft Snapshot(PartnerData d)=>new(d.Name,d.Phone,d.Email,d.TaxId,d.Country,d.PostalCode,d.City,d.Address);
        if(request.CustomerId!=Guid.Empty)
        {
            var selected=repository.Snapshot(request.CompanyId).GetAwaiter().GetResult().Partners.SingleOrDefault(p=>p.Id==request.CustomerId&&p.Data.IsActive&&p.Data.IsCustomer)??throw new ArgumentException("Válassz aktív vevőt.");
            return(selected.Id,selected.Data.Name,Snapshot(selected.Data));
        }
        var input=request.Customer??throw new ArgumentException("Válassz vevőt vagy add meg az új vevő nevét.");
        var data=PartnerValidation.Normalize(new PartnerData{Name=input.Name,Phone=input.Phone,Email=input.Email,TaxId=input.TaxId,Country=input.Country,PostalCode=input.PostalCode,City=input.City,Address=input.Address,IsCustomer=true,IsActive=true});
        if(!request.SaveCustomerToPartners)return(Guid.Empty,data.Name,Snapshot(data));
        PartnerRecord? match=null;
        if(data.TaxId.Length>0)
        {
            var matches=repository.Snapshot(request.CompanyId).GetAwaiter().GetResult().Partners.Where(p=>p.Data.IsActive&&p.Data.Country==data.Country&&PartnerValidation.TaxKey(p.Data.TaxId)==PartnerValidation.TaxKey(data.TaxId)).ToArray();
            if(matches.Length>1)throw new ArgumentException("Több partner tartozik az adószámhoz; válassz a meglévő vevők közül.");match=matches.SingleOrDefault();
        }
        var saved=match is null?repository.Save(new(request.CompanyId,null,0,data),actor).GetAwaiter().GetResult():match.Data.IsCustomer?match:repository.Save(new(request.CompanyId,match.Id,match.Version,match.Data with{IsCustomer=true}),actor).GetAwaiter().GetResult();
        return(saved.Id,saved.Data.Name,Snapshot(saved.Data));
    }
    public static (CustomerOrder[] Rows,int Total) List(ICompanyTransaction tx,OrderQuery query,DateTimeOffset at)
    {
        if(query.Search is null||query.Search.Length>200||query.Search.Any(char.IsControl)||query.Status is not ("all" or "open" or "reserved" or "partial" or "completed" or "cancelled" or "new" or "ordered" or "arrived" or "done")||query.Page<0||query.PageSize is <1 or >100)throw new ArgumentException("Érvénytelen rendeléslista-szűrés.");
        if(tx is IIndexedCompanyTransaction index)
        {var page=index.Orders(query,at);return (page.Records.Select(s=>JsonSerializer.Deserialize<CustomerOrder>(s,CompanyDocuments.Json)!).Select(o=>Get(tx,o.Id,at)).ToArray(),page.Total);}
        var rows=tx.List("sales","order/").Select(s=>JsonSerializer.Deserialize<CustomerOrder>(s,CompanyDocuments.Json)!).Where(o=>query.Status=="all"||(query.Status=="open"?o.Status is "open" or "reserved" or "partial":query.Status is "new" or "ordered" or "arrived" or "done"?OrderWorkflow.Effective(o)==query.Status:o.Status==query.Status)).Where(o=>SearchText.Fold(o.Number+" "+o.CustomerName+" "+o.Notes).Contains(SearchText.Fold(query.Search),StringComparison.Ordinal)).OrderByDescending(o=>o.UpdatedAt).ThenBy(o=>o.Id).ToArray();
        return (rows.Skip((int)Math.Min(int.MaxValue,(long)query.Page*query.PageSize)).Take(query.PageSize).Select(o=>Get(tx,o.Id,at)).ToArray(),rows.Length);
    }
}

public sealed partial class SalesModule
{
    private static void RegisterOrders(IModuleContext context)
    {
        DomainCommands.Register(context,"orders-list","sales.read",CommandAccess.RecoveryReadOnly,(tx,input,_)=>{var result=CustomerOrders.List(tx,DomainCommands.Read<OrderQuery>(input),context.TimeProvider.GetUtcNow());return DomainCommands.Json(new{rows=result.Rows,total=result.Total});});
        DomainCommands.Register(context,"orders-get","sales.read",CommandAccess.RecoveryReadOnly,(tx,input,_)=>DomainCommands.Json(CustomerOrders.Get(tx,input.GetProperty("id").GetGuid(),context.TimeProvider.GetUtcNow())));
        DomainCommands.Register(context,"orders-save","sales.write",CommandAccess.Licensed,(tx,input,actor)=>DomainCommands.Json(CustomerOrders.Save(tx,DomainCommands.Read<OrderSave>(input),actor,context.TimeProvider.GetUtcNow())));
        DomainCommands.Register(context,"orders-status","sales.write",CommandAccess.Licensed,(tx,input,actor)=>DomainCommands.Json(CustomerOrders.ChangeWorkflow(tx,DomainCommands.Read<OrderWorkflowChange>(input),actor,context.TimeProvider.GetUtcNow())));
        DomainCommands.Register(context,"orders-reserve","sales.write",CommandAccess.Licensed,(tx,input,actor)=>DomainCommands.Json(CustomerOrders.Reserve(tx,DomainCommands.Read<OrderAction>(input),actor,context.TimeProvider.GetUtcNow())));
        DomainCommands.Register(context,"orders-release","sales.write",CommandAccess.Licensed,(tx,input,actor)=>DomainCommands.Json(CustomerOrders.Release(tx,DomainCommands.Read<OrderAction>(input),actor,context.TimeProvider.GetUtcNow())));
        DomainCommands.Register(context,"orders-cancel","sales.write",CommandAccess.Licensed,(tx,input,actor)=>DomainCommands.Json(CustomerOrders.Release(tx,DomainCommands.Read<OrderAction>(input),actor,context.TimeProvider.GetUtcNow(),cancel:true)));
        DomainCommands.Register(context,"orders-deliver","sales.write",CommandAccess.Licensed,(tx,input,actor)=>DomainCommands.Json(CustomerOrders.Deliver(tx,DomainCommands.Read<OrderDeliver>(input),actor,context.TimeProvider.GetUtcNow())));
        DomainCommands.Register(context,"orders-references","sales.read",CommandAccess.RecoveryReadOnly,(tx,input,_)=>
        {
            var warehouse=StockLedger.Warehouse(input.GetProperty("warehouse").GetString());var now=context.TimeProvider.GetUtcNow();
            var products=CompanyReferenceQueries.SearchProducts(tx,DomainCommands.Search(input),"salable",checkout:true);
            return DomainCommands.Json(new{products=products.Select(p=>new{p.Id,p.Name,p.Sku,p.UnitCode,p.SaleNetUnitPrice,p.SaleVatRate,physical=StockReservations.Physical(tx,p.Id,warehouse),reserved=StockReservations.Quantity(tx,p.Id,warehouse,now),free=StockReservations.Free(tx,p.Id,warehouse,now)}),customers=CompanyReferenceQueries.Partners(tx).Where(c=>c.IsActive&&c.IsCustomer&&DomainCommands.Matches(c.Name+" "+c.TaxId,DomainCommands.Search(input,"customerSearch"))).Take(100)});
        });
        context.RegisterPage(new("orders",context.ModuleId,"Vevői rendelések","Mentett rendelések, lejáró készletfoglalás és részátadás eladási bizonylattal.","\uE8A5",[new("number","Rendelés")],[new("number","Rendelés"),new("customerName","Vevő")],new("orders-list","orders-save","orders-cancel","orders-list")){ViewKind="orders"});
    }
}
