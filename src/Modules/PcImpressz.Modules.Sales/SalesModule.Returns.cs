using System.Globalization;
using System.Text.Json;
using PcImpressz.Business;
using PcImpressz.Contracts;
namespace PcImpressz.Modules.Sales;
public sealed partial class SalesModule
{
    private static bool MatchesDay(SaleRecord sale,JsonElement input)
    {
        if(!input.TryGetProperty("date",out var date)||date.ValueKind==JsonValueKind.Null||date.GetString() is not {Length:>0} text)return true;
        if(!DateOnly.TryParseExact(text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day))throw new ArgumentException("Érvénytelen eladási nap.");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(sale.At,TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest")).DateTime)==day;
    }
    private static decimal Returned(ICompanyTransaction tx,Guid id,int index)=>Records(tx).Where(r=>r.ReturnOf==id).SelectMany(r=>r.Lines).Where(l=>l.SourceLineIndex==index).Sum(l=>-l.Quantity);
    private static decimal Share(decimal amount,decimal quantity,decimal total)=>decimal.Round(amount*quantity/total,2,MidpointRounding.AwayFromZero);
    private static void RegisterReturns(IModuleContext context)
    {
        DomainCommands.Register(context,"returnable","sales.read",CommandAccess.RecoveryReadOnly,(tx,input,_)=>
        {
            var source=tx.Read<SaleRecord>("sales","sale/"+input.GetProperty("id").GetGuid().ToString("N"))??throw new RecordNotFoundException("Az eladás nem található.");
            return DomainCommands.Json(new{source.Id,source.Number,source.Warehouse,lines=source.Lines.Select((l,i)=>new{index=i,l.ProductId,l.Name,l.UnitCode,l.Quantity,l.NetUnitPrice,l.VatRate,l.Net,l.Vat,returned=Returned(tx,source.Id,i),available=source.Status=="vegleges"&&source.ReturnOf is null?l.Quantity-Returned(tx,source.Id,i):0}).ToArray()});
        });
    }
    private static SaleRecord PostReturn(ICompanyTransaction tx,SaleRequest request,string actor,DateTimeOffset at)
    {
        if(request.PaymentMethod is not ("cash" or "card"))throw new ArgumentException("Válassz fizetési módot.");
        if(request.InvoiceRequested||request.CustomerId is not null)throw new ArgumentException("A visszavét az eredeti bizonylat vevőjéhez kapcsolódik.");
        var fingerprint=Fingerprint(request);var key="sale/"+request.OperationId.ToString("N");
        if(tx.Read<SaleRecord>("sales",key) is {} existing){if(existing.Fingerprint!=fingerprint)throw new ArgumentException("Eltérő visszavét ugyanazzal az azonosítóval.");return existing;}
        var source=tx.Read<SaleRecord>("sales","sale/"+request.ReturnOf!.Value.ToString("N"))??throw new ArgumentException("Az eredeti eladás nem található.");
        if(source.ReturnOf is not null||source.Status!="vegleges"||source.Warehouse!=StockLedger.Warehouse(request.Warehouse))throw new ArgumentException("Csak végleges eredeti eladás saját raktárába vehető vissza.");
        if(request.Lines.Select(l=>l.SourceLineIndex).Distinct().Count()!=request.Lines.Length)throw new ArgumentException("Egy eredeti tétel egyszer szerepelhet.");
        var lines=request.Lines.Select(l=>
        {
            if(l.SourceLineIndex is not int index||index<0||index>=source.Lines.Length)throw new ArgumentException("Hiányzó eredeti tétel.");
            var original=source.Lines[index];var quantity=-l.Quantity;var returned=Returned(tx,source.Id,index);
            if(StockLedger.Quantity(quantity)<=0||quantity>original.Quantity-returned||l.ProductId!=original.ProductId||l.NetUnitPrice!=original.NetUnitPrice||l.VatRate!=original.VatRate)throw new ArgumentException("A visszavét mennyisége, ára vagy eredeti tétele hibás; az eladottnál többet nem vehetsz vissza.");
            return new SaleLine(original.ProductId,original.Name,original.UnitCode,-quantity,original.NetUnitPrice,original.VatRate,-(Share(original.Net,returned+quantity,original.Quantity)-Share(original.Net,returned,original.Quantity)),-(Share(original.Vat,returned+quantity,original.Quantity)-Share(original.Vat,returned,original.Quantity)),index);
        }).ToArray();
        var year=at.Year.ToString(CultureInfo.InvariantCulture);var counter=tx.Read<SaleCounter>("sales","counter/"+year)?.Value??0;if(counter>=999999)throw new ArgumentException("Elfogyott az éves bizonylatszám.");
        var number="VV-"+year+"-"+(counter+1).ToString("D6",CultureInfo.InvariantCulture);var net=lines.Sum(l=>l.Net);var vat=lines.Sum(l=>l.Vat);var gross=net+vat;var payable=PaymentRounding.Payable(gross,request.PaymentMethod);
        StockLedger.Post(tx,"sale:"+request.OperationId.ToString("N"),"sales",request.OperationId,lines.Select(l=>new StockChange(l.ProductId,l.Name,l.UnitCode,source.Warehouse,-l.Quantity)).ToArray(),"Visszavét "+number+" · "+source.Number,actor,at);
        var record=new SaleRecord(request.OperationId,1,number,source.CustomerId,source.CustomerName,source.Warehouse,"HUF",lines,net,vat,gross,"vegleges",at,actor,fingerprint,PaymentMethod:request.PaymentMethod,InvoiceCustomer:source.InvoiceCustomer,Rounding:payable-gross,Payable:payable,ReturnOf:source.Id);
        tx.Write("sales",key,record);tx.Write("sales","counter/"+year,new SaleCounter(counter+1));return record;
    }
}