using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
namespace PcImpressz.Shell.Windows;
internal sealed record HistorySaleLine(Guid SaleId,string Warehouse,int Index,Guid ProductId,string Name,string UnitCode,decimal Quantity,decimal NetUnitPrice,decimal VatRate,decimal Net,decimal Vat,decimal Returned,decimal Available)
{public string Amount=>MoneyDisplay.Format(Net+Vat)+" Ft";}
internal sealed partial class OperationsPageViewModel
{
    private DateTime? _historyDate,_appliedHistoryDate;private int _historyGeneration;
    public DateTime? HistoryDate{get=>_historyDate;set{if(IsBusy){Changed();return;}_historyDate=value?.Date;Changed();if(!_filling)_=LoadAsync();}}
    public ObservableCollection<HistorySaleLine> HistoryLines{get;}=[];
    public HistorySaleLine? SelectedHistoryLine{get;set;}
    public ICommand ClearHistoryDateCommand=>new RelayCommand(()=>HistoryDate=null);
    public ICommand ReturnSelectedLineCommand=>new RelayCommand(()=>{if(SelectedHistoryLine is {} line)AddReturnLine(line);});
    internal async Task LoadHistorySelectionAsync()
    {
        var generation=++_historyGeneration;HistoryLines.Clear();SelectedHistoryLine=null;Details="";
        if(SelectedRow is not {} row||_company() is not Guid company)return;var id=row.Json.GetProperty("id").GetGuid();
        try
        {
            var sale=await Send("get",new{id});var detail=await Send("returnable",new{id});
            if(generation!=_historyGeneration||_company()!=company)return;
            var gross=sale.GetProperty("gross").GetDecimal();var payable=sale.TryGetProperty("payable",out var p)&&p.ValueKind==JsonValueKind.Number?p.GetDecimal():gross;
            Details=Text(sale,"number")+" · "+Text(sale,"customerName")+" · "+Text(sale,"warehouse")+" · "+sale.GetProperty("at").GetDateTimeOffset().ToLocalTime().ToString("yyyy.MM.dd. HH:mm",CultureInfo.GetCultureInfo("hu-HU"))+" · Fizetendő: "+MoneyDisplay.Format(payable)+" Ft";
            foreach(var l in detail.GetProperty("lines").EnumerateArray())HistoryLines.Add(new(id,Text(detail,"warehouse"),l.GetProperty("index").GetInt32(),l.GetProperty("productId").GetGuid(),Text(l,"name"),Text(l,"unitCode"),l.GetProperty("quantity").GetDecimal(),l.GetProperty("netUnitPrice").GetDecimal(),l.GetProperty("vatRate").GetDecimal(),l.GetProperty("net").GetDecimal(),l.GetProperty("vat").GetDecimal(),l.GetProperty("returned").GetDecimal(),l.GetProperty("available").GetDecimal()));
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){if(generation==_historyGeneration)Details=ex.Message;}
    }
    internal void AddReturnLine(HistorySaleLine line)
    {
        if(!CanEditSale)return;
        try
        {
            if(_pendingScans>0)throw new ArgumentException("Várd meg a beolvasást.");
            if(line.Available<=0)throw new ArgumentException("Ebből a tételből már nincs visszavehető mennyiség.");
            if(Basket.Any(l=>!l.IsReturn||l.ReturnOf!=line.SaleId))throw new ArgumentException("Egy kosárban egy eredeti eladás tételeit veheted vissza. Előbb zárd le vagy ürítsd ki a kosarat.");
            var existing=Basket.FirstOrDefault(l=>l.SourceLineIndex==line.Index);var quantity=Math.Min(1,line.Available);
            if(existing is not null){if(-existing.Quantity+quantity>line.Available)throw new ArgumentException("Az eladottnál többet nem vehetsz vissza.");existing.QuantityText=(existing.Quantity-quantity).ToString(CultureInfo.InvariantCulture);}
            else
            {
                if(Basket.Count>=400)throw new ArgumentException("Legfeljebb 400 kosársor lehet.");
                Warehouse=line.Warehouse;CancelQuickSearch();QuickMatches.Clear();
                Basket.Add(new(line.ProductId,line.Name,line.UnitCode,-quantity,line.NetUnitPrice,line.VatRate){ReturnOf=line.SaleId,SourceLineIndex=line.Index,SourceQuantity=line.Quantity,SourceNet=line.Net,SourceVat=line.Vat,AlreadyReturned=line.Returned});
            }
            Message="Visszavét a kosárban: "+line.Name+". A mennyiség negatív; véglegesítéskor kerül vissza készletre.";
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){Message=ex.Message;}
    }
}