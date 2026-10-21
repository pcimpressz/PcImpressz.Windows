using System.Globalization;
using System.Windows.Input;
using PcImpressz.Shell.Customization;

namespace PcImpressz.Shell.Windows;
internal sealed partial class OperationsPageViewModel
{
    private string? _stockWarehouse;
    public string SelectedStockInfo=>!IsSales?"":_stockWarehouse!=Warehouse?"A raktárhoz nincs friss készletadat. Frissítsd a választókat.":
        SelectedProduct?.AvailableQuantity is decimal quantity ? "Tájékoztató saját készlet: "+quantity.ToString("0.####",CultureInfo.GetCultureInfo("hu-HU"))+" "+SelectedProduct.UnitCode+" · "+Warehouse+". A végső ellenőrzés mentéskor történik." : "Válassz terméket a készlet megtekintéséhez.";
    public ICommand EditLineCommand{get;private set;}=null!;
    public ICommand UpdateLineCommand{get;private set;}=null!;
    private void InitializeBasketEditor()
    {
        EditLineCommand=new RelayCommand(()=>
        {
            if(IsBusy||SelectedBasketLine is not {} line)return;
            SelectedProduct=Products.FirstOrDefault(p=>p.Id==line.ProductId);
            if(SelectedProduct is null){var product=new OperationProduct(line.ProductId,line.Name,"",line.UnitCode);Products.Add(product);SelectedProduct=product;}
            QuantityText=line.Quantity.ToString(CultureInfo.InvariantCulture);PriceText=line.NetUnitPrice.ToString(CultureInfo.InvariantCulture);VatText=line.VatRate.ToString(CultureInfo.InvariantCulture);
            Message="A kijelölt kosártétel adatai betöltve. A változtatásokat a Kijelölt tétel cseréje gomb menti a kosárba.";
        });
        UpdateLineCommand=new RelayCommand(()=>
        {
            if(IsBusy)return;
            try{if(SelectedBasketLine is not {} selected)throw new ArgumentException("Jelölj ki egy kosártételt.");var index=Basket.IndexOf(selected);if(index<0)throw new ArgumentException("A tétel már nem található.");var updated=DraftLine();CheckBasketStock(updated,index);Basket[index]=updated;SelectedBasketLine=updated;Dirty();Message="A kijelölt kosártétel frissítve.";}
            catch(Exception ex){Message=ex.Message;}
        });
    }
    private async Task RefreshSalesStockAsync()
    {
        if(!IsSales)return;
        _stockWarehouse=null;Changed(nameof(SelectedStockInfo));
        var reference=await Send("references",new{search=ProductSearch,customerSearch=CustomerSearch,warehouse=Warehouse});
        var selected=SelectedProduct;_filling=true;
        try
        {
            Products.Clear();foreach(var product in reference.GetProperty("products").EnumerateArray())Products.Add(new(product.GetProperty("id").GetGuid(),Text(product,"name"),Text(product,"sku"),Text(product,"unitCode"),product.GetProperty("availableQuantity").GetDecimal()));
            if(selected is not null&&Products.All(p=>p.Id!=selected.Id))Products.Add(selected with{AvailableQuantity=null});
            SelectedProduct=Products.FirstOrDefault(p=>p.Id==selected?.Id);_stockWarehouse=Warehouse;Changed(nameof(SelectedStockInfo));
        }
        finally{_filling=false;}
    }

    private void CheckBasketStock(SaleBasketLine draft,int excluding=-1)
    {
        if(_stockWarehouse!=Warehouse||SelectedProduct?.AvailableQuantity is not decimal available)return;
        var requested=draft.Quantity+Basket.Where((_,index)=>index!=excluding).Where(line=>line.ProductId==draft.ProductId).Sum(line=>line.Quantity);
        if(requested>available)throw new ArgumentException("A kosár mennyisége több a jelzett saját készletnél ("+available+"). Ellenőrizd a raktárt vagy frissítsd a választókat.");
    }
}
