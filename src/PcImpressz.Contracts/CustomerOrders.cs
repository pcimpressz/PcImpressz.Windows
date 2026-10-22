namespace PcImpressz.Contracts;

public sealed record CustomerOrderLineDraft(Guid Id,Guid ProductId,decimal Quantity,decimal NetUnitPrice,decimal VatRate);
public sealed record CustomerOrderLine(Guid Id,Guid ProductId,string Name,string Sku,string UnitCode,decimal Quantity,
    decimal NetUnitPrice,decimal VatRate,decimal Delivered=0,decimal Reserved=0);
public sealed record OrderDelivery(Guid SaleId,string SaleNumber,DateTimeOffset At,Guid[] LineIds);
public sealed record CustomerOrder(Guid Id,long Version,string Number,Guid CustomerId,string CustomerName,string Warehouse,
    string DueDate,string Notes,string Status,CustomerOrderLine[] Lines,decimal Net,decimal Vat,decimal Gross,
    DateTimeOffset CreatedAt,DateTimeOffset UpdatedAt,string Actor,DateTimeOffset? ReservationUntil=null,OrderDelivery[]? Deliveries=null);
public sealed record OrderSave(Guid CompanyId,Guid RequestId,Guid Id,long Version,Guid CustomerId,string Warehouse,
    string DueDate,string Notes,CustomerOrderLineDraft[] Lines);
public sealed record OrderAction(Guid CompanyId,Guid RequestId,Guid Id,long Version,int ReservationDays=7);
public sealed record OrderDeliveryLine(Guid LineId,decimal Quantity);
public sealed record OrderDeliver(Guid CompanyId,Guid RequestId,Guid Id,long Version,OrderDeliveryLine[] Lines,string PaymentMethod="cash",bool InvoiceRequested=false);
public sealed record OrderQuery(Guid CompanyId,string Search="",string Status="open",int Page=0,int PageSize=20);
public sealed record OrderIndexPage(string[] Records,int Total);
public sealed record InventoryReservation(Guid SourceId,Guid LineId,Guid ProductId,string Warehouse,decimal Quantity,DateTimeOffset ExpiresAt);
public static class OrderStatus
{
    public static string Label(string status)=>status switch{"open"=>"Nyitott","reserved"=>"Lefoglalva","partial"=>"Részben átadva","completed"=>"Átadva","cancelled"=>"Lemondva",_=>status};
}
