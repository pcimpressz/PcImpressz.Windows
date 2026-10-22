using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;

namespace PcImpressz.Shell.Windows;

internal sealed partial class LineEditor
{
    private string _retail="",_markup="30",_saleVat="27";
    public ObservableCollection<EntityFieldViewModel> ProductFields {get;}=[];
    public bool ProductLoading {get;set;}
    public bool ProductDirty {get;set;}
    public long ProductEditRevision {get;set;}
    private void ProductTouched(){if(ProductLoaded){ProductEditRevision++;ProductDirty=true;_page.MarkDirty();}}
    public long ProductVersion {get;set;}
    public Guid? LoadedProductId {get;set;}
    public bool ProductLoaded {get;set;}
    public string RetailPrice {get=>_retail;set{_retail=value;Changed();if(ProductLoaded)RetailEdited();}}
    public string Markup {get=>_markup;set{_markup=value;Changed();if(ProductLoaded)MarkupEdited();}}
    public string SaleVat {get=>_saleVat;set{_saleVat=value;Changed();if(ProductLoaded)MarkupEdited();}}
    public ICommand CalculateRetailCommand=>new RelayCommand(()=>
    {
        if(!ReceivingPageViewModel.TryMoney(PriceText,out var buy)||buy is <0 or >1_000_000_000||!ReceivingPageViewModel.TryMoney(Markup,out var markup)||markup<0||markup>10000||!ReceivingPageViewModel.TryMoney(SaleVat,out var vat)||vat is not (0 or 5 or 18 or 27))
        { _page.ProductMessage("Az árképzéshez érvényes beszerzési ár, 0–10000% haszonkulcs és 0/5/18/27% áfa kell.");return; }
        RetailPrice=RetailPricing.Suggest(buy,markup,vat).ToString("0.####",CultureInfo.GetCultureInfo("hu-HU"));
    });
    public ICommand SaveProductCommand=>new RelayCommand(()=>_= _page.SaveLineProductAsync(this));
    public ICommand LoadProductCommand=>new RelayCommand(()=>_= _page.LoadLineProductAsync(this));
}

internal sealed partial class ReceivingPageViewModel
{
    private EntityField[]? _productSchema;
    public void ProductMessage(string text)=>Message=text;
    internal async Task LoadLineProductsAsync(Guid company)
    {
        foreach(var line in Lines.ToArray())
        {
            if(_company()!=company)return;
            if(line.ProductId is null)await SuggestLineProductsAsync(line);
            await LoadLineProductAsync(line);
        }
    }
    internal async Task LoadLineProductAsync(LineEditor line)
    {
        if(_company() is not Guid company||line.ProductLoading||!Lines.Contains(line)||line.ProductLoaded&&line.LoadedProductId==line.ProductId)return;
        line.ProductLoading=true;
        try
        {
            _productSchema??=(await ModuleAsync("catalog","products-schema",new{})).Deserialize<EntityField[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web))??[];
            if(_company()!=company||!Lines.Contains(line))return;
            var id=line.ProductId;
            var row=id is Guid product?await ModuleAsync("catalog","products-get",new{id=product}):default;
            if(_company()!=company||line.ProductId!=id||!Lines.Contains(line))return;
            var pendingPrice=line.ProductDirty?(line.RetailPrice,line.Markup,line.SaleVat):((string,string,string)?)null;
            line.ProductLoaded=false;line.ProductFields.Clear();
            foreach(var definition in _productSchema)
            {
                var field=new EntityFieldViewModel(definition);
                if(row.ValueKind==JsonValueKind.Object&&row.TryGetProperty(definition.Id,out var value))
                {
                    if(definition.Kind=="boolean")field.Checked=value.ValueKind==JsonValueKind.True;
                    else field.Text=value.ValueKind==JsonValueKind.String?value.GetString()??"":value.ValueKind==JsonValueKind.Number?value.GetRawText():"";
                }
                else if(definition.Id is "isSalable" or "isPurchaseable")field.Checked=true;
                else if(id is null)field.Text=definition.Id switch{"name"=>line.Name,"sku"=>line.Sku,"barcode"=>line.Barcode,"unitCode"=>line.UnitCode,_=>field.Text};
                field.Edited=()=>{if(line.ProductLoaded){line.ProductEditRevision++;line.ProductDirty=true;MarkDirty();}};
                line.ProductFields.Add(field);
            }
            line.ProductVersion=id is not null?row.GetProperty("version").GetInt64():0;
            line.LoadedProductId=id;
            if(id is not null)
            {
                var vat=row.GetProperty("saleVatRate").GetDecimal();line.SaleVat=vat.ToString(CultureInfo.InvariantCulture);
                line.RetailPrice=row.TryGetProperty("saleNetUnitPrice",out var price)&&price.ValueKind==JsonValueKind.Number?(price.GetDecimal()*(1+vat/100m)).ToString("0.####",CultureInfo.GetCultureInfo("hu-HU")):"";
            }
            line.ProductLoaded=true;line.ProductDirty=false;if(pendingPrice is {} pending){line.ApplyPrices(pending.Item1,pending.Item2,pending.Item3);line.ProductDirty=true;}
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){Message="A termékadatok nem tölthetők be: "+ex.Message;}
        finally{line.ProductLoading=false;}
    }
    internal async Task SaveLineProductAsync(LineEditor line,bool fromReceipt=false,bool fromPreparation=false)
    {
        if(IsBusy&&!fromReceipt||!IsEditable||line.IsService||_company() is not Guid company)return;
        IsBusy=true;
        try
        {
            if(line.ProductId is null)await SuggestLineProductsAsync(line);
            await LoadLineProductAsync(line);
            if(!line.ProductLoaded||line.LoadedProductId!=line.ProductId)throw new ArgumentException("Előbb töltsd be a termék adatait.");
            var editRevision=line.ProductEditRevision;
            var data=new Dictionary<string,object?>();
            foreach(var field in line.ProductFields)
            {
                if(field.Definition.Kind=="boolean")data[field.Definition.Id]=field.Checked;
                else if(field.Definition.Kind=="decimal")
                {
                    if(string.IsNullOrWhiteSpace(field.Text))data[field.Definition.Id]=null;
                    else if(TryMoney(field.Text,out var amount))data[field.Definition.Id]=amount;
                    else throw new ArgumentException(field.Label+": számot adj meg.");
                }
                else data[field.Definition.Id]=field.Text;
            }
            if(line.ProductId is null){foreach(var pair in new[]{("name",line.Name),("sku",line.Sku),("barcode",line.Barcode),("unitCode",line.UnitCode)})if(!data.TryGetValue(pair.Item1,out var value)||string.IsNullOrWhiteSpace(value?.ToString()))data[pair.Item1]=pair.Item2;}
            if(!TryMoney(line.SaleVat,out var vat)||vat is not (0 or 5 or 18 or 27))throw new ArgumentException("Az eladási áfa 0, 5, 18 vagy 27% lehet.");
            data["saleVatRate"]=vat;
            if(string.IsNullOrWhiteSpace(line.RetailPrice))data["saleNetUnitPrice"]=null;
            else if(TryMoney(line.RetailPrice,out var gross)&&gross>=0)data["saleNetUnitPrice"]=decimal.Round(gross/(1+vat/100m),4,MidpointRounding.AwayFromZero);
            else throw new ArgumentException("Érvényes, nem negatív kisker árat adj meg.");
            if(_company()!=company)throw new ArgumentException("A cég közben megváltozott; töltsd be újra a számlát.");
            if(line.ProductId is null&&line.UnitCode.Equals("DB",StringComparison.OrdinalIgnoreCase))await EnsurePieceAsync();
            var saved=await ModuleAsync("catalog","products-save",new{id=line.ProductId,version=line.ProductVersion,data});
            if(_company()!=company)return;
            if(editRevision==line.ProductEditRevision)line.FillQuiet(()=>{line.Sku=Get(saved,"sku");line.Barcode=Get(saved,"barcode");line.UnitCode=Get(saved,"unitCode");});
            line.ProductId=saved.GetProperty("id").GetGuid();line.LoadedProductId=line.ProductId;line.ProductVersion=saved.GetProperty("version").GetInt64();
            line.ProductDirty=editRevision!=line.ProductEditRevision;line.ShowMatch(line.ProductDirty?"Új módosítás mentésre vár":"Terméktörzsben mentve");if(!fromPreparation)MarkDirty();
            foreach(var other in Lines.Where(l=>l!=line&&l.ProductId==line.ProductId&&!l.ProductDirty).ToArray()){other.ProductLoaded=false;await LoadLineProductAsync(other);}
            Message="Termék és eladási ár mentve a terméktörzsbe. A számla és a készlet még nem változott.";
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){Message=ex.Message;if(fromReceipt)throw;}
        finally{if(!fromReceipt)IsBusy=false;}
    }
    private Task RefreshNavReceiptFlagsAsync(JsonElement saved)
    {
        var number=Get(saved,"originalNavNumber");if(number.Length==0)number=Get(saved,"number");
        var tax=Get(saved,"originalNavSupplierTaxId");if(tax.Length==0)tax=Get(saved,"supplierTaxId");
        NavInvoiceRow Update(NavInvoiceRow row)=>row with{ReceiptId=saved.GetProperty("id").GetGuid(),Received=Get(saved,"status")=="bevetelezve"&&saved.GetProperty("stockPosted").GetBoolean()};
        for(var i=0;i<_navAll.Count;i++)if(_navAll[i].Number==number&&Tax(_navAll[i].SupplierTaxNumber)==Tax(tax))_navAll[i]=Update(_navAll[i]);
        for(var i=0;i<NavInvoices.Count;i++)
        {
            var row=NavInvoices[i];
            if(number!=row.Number||Tax(tax)!=Tax(row.SupplierTaxNumber))continue;
            var updated=Update(row);
            NavInvoices[i]=updated;if(_navSelected==row){_navSelected=updated;Changed(nameof(SelectedNavInvoice));}
        }
        return Task.CompletedTask;
    }
}
