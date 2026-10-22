using System.Globalization;
using System.Windows.Input;
using PcImpressz.Shell.Customization;

namespace PcImpressz.Shell.Windows;
internal sealed partial class LineEditor
{
    private bool _stockItem=true,_priceCalc;
    private void SyncProductField(string id,string value)
    {
        if(_filling||!ProductLoaded)return;var field=ProductFields.FirstOrDefault(f=>f.Definition.Id==id);if(field is not null&&field.Text!=value)field.Text=value;
    }
    public bool StockItem{get=>_stockItem;set{_stockItem=value;Changed();Touch();}}
    public bool DetailsOpen{get;set;}
    public string Margin=>ReceivingPageViewModel.TryMoney(RetailPrice,out var gross)&&ReceivingPageViewModel.TryMoney(SaleVat,out var vat)&&ReceivingPageViewModel.TryMoney(PriceText,out var buy)&&gross>0&&vat>=0
        ? ((gross/(1+vat/100)-buy)/(gross/(1+vat/100))*100).ToString("0.##",CultureInfo.GetCultureInfo("hu-HU"))+" %":"—";
    public void ApplyPrices(string retail,string markup,string vat)
    {
        _priceCalc=true;try{RetailPrice=retail;Markup=markup;SaleVat=vat;}finally{_priceCalc=false;}Changed(nameof(Margin));
    }
    private void RetailEdited()
    {
        if(_priceCalc||_filling)return;
        if(ReceivingPageViewModel.TryMoney(RetailPrice,out var gross)&&ReceivingPageViewModel.TryMoney(SaleVat,out var vat)&&ReceivingPageViewModel.TryMoney(PriceText,out var buy)&&buy>0&&vat>=0)
        {_priceCalc=true;Markup=((gross/(1+vat/100)/buy-1)*100).ToString("0.##",CultureInfo.GetCultureInfo("hu-HU"));_priceCalc=false;}
        Changed(nameof(Margin));ProductTouched();
    }
    private void MarkupEdited()
    {
        if(_priceCalc||_filling)return;
        if(ReceivingPageViewModel.TryMoney(PriceText,out var buy)&&buy is >=0 and <=1_000_000_000&&ReceivingPageViewModel.TryMoney(Markup,out var markup)&&markup is >=0 and <=10000&&ReceivingPageViewModel.TryMoney(SaleVat,out var vat)&&vat is 0 or 5 or 18 or 27)
        {_priceCalc=true;RetailPrice=RetailPricing.Suggest(buy,markup,vat).ToString("0.####",CultureInfo.GetCultureInfo("hu-HU"));_priceCalc=false;}
        Changed(nameof(Margin));ProductTouched();
    }
    public ICommand GenerateBarcodeCommand=>new RelayCommand(()=>_= _page.GenerateBarcodeAsync(this));
    public ICommand PrintBarcodeCommand=>new RelayCommand(()=>_= _page.PrintBarcodeAsync(this));
}
internal sealed partial class ReceivingPageViewModel
{
    private async Task EnsureNavSupplierAsync(NavInvoiceRow invoice,Guid company)
    {
        if(_company()!=company)return;
        var match=Suppliers.FirstOrDefault(s=>Tax(s.TaxId)==Tax(invoice.SupplierTaxNumber));
        if(match is null)
        {
            var saved=await ModuleAsync("partners","save",new{id=(Guid?)null,version=0,data=new{name=invoice.SupplierName,taxId=invoice.SupplierTaxNumber,country="HU",isSupplier=true,isActive=true}});
            if(_company()!=company)return;
            match=new(saved.GetProperty("id").GetGuid(),invoice.SupplierName,invoice.SupplierTaxNumber);Suppliers.Add(match);
        }
        _populating=true;_supplier=match;SelectedNavSupplier=match;Changed(nameof(SelectedSupplier));Changed(nameof(NavSupplierHint));_populating=false;
    }
    internal async Task GenerateBarcodeAsync(LineEditor line)
    {
        if(!IsEditable||IsBusy)return;
        try
        {
            var value=await ModuleAsync("catalog","barcode-propose",new{});
            line.Barcode=value.GetProperty("barcode").GetString()??"";
            var field=line.ProductFields.FirstOrDefault(f=>f.Definition.Id=="barcode");if(field is not null)field.Text=line.Barcode;
            line.ProductDirty=true;MarkDirty();
        }
        catch(Exception ex){Message=ex.Message;}
    }
    internal async Task PrintBarcodeAsync(LineEditor line)
    {
        try{if(InvoiceListPreferences.LabelPrinter.Equals("Niimbot D110-M Bluetooth",StringComparison.OrdinalIgnoreCase))await NiimbotLabelPrinter.PrintAsync(line.Barcode,line.Name);else BarcodeLabelPrinter.Print(line.Barcode,line.Name);Message="A vonalkódcímke elküldve a kiválasztott nyomtatóra.";}catch(Exception ex){Message=ex.Message;}

    }
}
