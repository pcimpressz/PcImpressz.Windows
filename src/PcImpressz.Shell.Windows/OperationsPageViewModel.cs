using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;

namespace PcImpressz.Shell.Windows;

internal sealed record OperationProduct(Guid Id,string Name,string Sku,string UnitCode)
{ public string Label => Name+(Sku.Length>0 ? " · "+Sku : ""); }
internal sealed record OperationCustomer(Guid? Id,string Name);
internal sealed record SaleBasketLine(Guid ProductId,string Name,string UnitCode,decimal Quantity,decimal NetUnitPrice,decimal VatRate)
{ public decimal Gross => decimal.Round(Quantity*NetUnitPrice,2,MidpointRounding.AwayFromZero)+decimal.Round(decimal.Round(Quantity*NetUnitPrice,2,MidpointRounding.AwayFromZero)*VatRate/100m,2,MidpointRounding.AwayFromZero); }

internal sealed class OperationsPageViewModel:Observable,IWorkspacePage,IColumnList
{
    private readonly ColumnList _columns;
    private JsonElement? _listing;
    private readonly HostConnection _connection;
    private readonly Func<Guid?> _company;
    private readonly CancellationToken _shutdown;
    private bool _busy,_dirty,_filling;
    private Guid _operation=Guid.NewGuid();
    private string _productSearch="",_customerSearch="",_recordSearch="",_details="",_listNotice="";
    private string _warehouse="Bolt",_quantity="1",_price="0",_vat="27",_reason="Nyitókészlet",_message="";
    private OperationProduct? _product;
    private OperationCustomer? _customer;
    public OperationsPageViewModel(EntityPageDescriptor definition,HostConnection connection,Func<Guid?> company,CancellationToken shutdown,ListLayoutStore layouts)
    {
        (Definition,_connection,_company,_shutdown)=(definition,connection,company,shutdown);
        _columns=new ColumnList(layouts,definition);
        _columns.Changed+=()=>Changed(nameof(LayoutRevision));
        _columns.SortChanged+=PopulateRows;
        RefreshCommand=new RelayCommand(()=>_=LoadAsync());
        AddLineCommand=new RelayCommand(AddLine);
        DetailsCommand=new RelayCommand(()=>_=DetailsAsync());
        RemoveLineCommand=new RelayCommand(()=> {if(SelectedBasketLine is { } line && !IsBusy) {Basket.Remove(line);Dirty();}});
        SaveCommand=new RelayCommand(()=>_=SaveAsync());
        ReverseCommand=new RelayCommand(()=>_=ReverseAsync());
        ResetCommand=new RelayCommand(Reset);
        ExportCommand=new RelayCommand(()=>_=ExportAsync());
    }
    public EntityPageDescriptor Definition {get;}
    public int LayoutRevision=>_columns.Revision;
    public string SortColumn=>_columns.SortColumn;
    public bool SortDescending=>_columns.SortDescending;
    public IReadOnlyList<EntityColumn> VisibleColumns=>_columns.VisibleColumns;
    public bool IsColumnVisible(string id)=>_columns.IsColumnVisible(id);
    public bool SetColumnVisible(string id,bool visible)=>_columns.SetColumnVisible(id,visible);
    public void ToggleSort(string id)=>_columns.ToggleSort(id);
    public bool IsInventory=>Definition.ViewKind=="inventory";
    public bool IsSales=>!IsInventory;
    public string SaveLabel=>IsInventory ? "Készletváltozás rögzítése" : "Eladás véglegesítése";
    public bool IsBusy {get=>_busy;private set{_busy=value;Changed();Changed(nameof(IsNotBusy));}}
    public bool IsNotBusy=>!IsBusy;
    public bool IsDirty {get=>_dirty;private set{_dirty=value;Changed();}}
    public string Message {get=>_message;private set{_message=value;Changed();}}
    public string ProductSearch {get=>_productSearch;set{_productSearch=value;Changed();}}
    public string CustomerSearch {get=>_customerSearch;set{_customerSearch=value;Changed();}}
    public string RecordSearch {get=>_recordSearch;set{_recordSearch=value;Changed();}}
    public string Details {get=>_details;private set{_details=value;Changed();}}
    public string ListNotice {get=>_listNotice;private set{_listNotice=value;Changed();}}
    public string Warehouse {get=>_warehouse;set{_warehouse=value;Changed();Dirty();}}
    public string QuantityText {get=>_quantity;set{_quantity=value;Changed();Dirty();}}
    public string PriceText {get=>_price;set{_price=value;Changed();Dirty();}}
    public string VatText {get=>_vat;set{_vat=value;Changed();Dirty();}}
    public string Reason {get=>_reason;set{_reason=value;Changed();Dirty();}}
    public OperationProduct? SelectedProduct {get=>_product;set{_product=value;Changed();Dirty();}}
    public OperationCustomer? SelectedCustomer {get=>_customer;set{_customer=value;Changed();Dirty();}}
    public ObservableCollection<OperationProduct> Products {get;}=[];
    public ObservableCollection<OperationCustomer> Customers {get;}=[];
    public ObservableCollection<SaleBasketLine> Basket {get;}=[];
    public ObservableCollection<EntityRow> Rows {get;}=[];
    public ObservableCollection<EntityRow> Movements {get;}=[];
    public EntityRow? SelectedRow {get;set;}
    public EntityRow? SelectedMovement {get;set;}
    public SaleBasketLine? SelectedBasketLine {get;set;}
    public string BasketTotal=>Basket.Sum(l=>l.Gross).ToString("N2",CultureInfo.GetCultureInfo("hu-HU"))+" Ft";
    public ICommand DetailsCommand {get;}
    public ICommand RefreshCommand {get;}
    public ICommand AddLineCommand {get;}
    public ICommand RemoveLineCommand {get;}
    public ICommand SaveCommand {get;}
    public ICommand ReverseCommand {get;}
    public ICommand ResetCommand {get;}
    public ICommand ExportCommand {get;}
    private void Dirty(){if(!_filling)IsDirty=true;Changed(nameof(BasketTotal));}
    public void CompanyChanged()
    {
        Details="";ListNotice="";_filling=true;_listing=null;Rows.Clear();Movements.Clear();Basket.Clear();Products.Clear();Customers.Clear();
        SelectedRow=null;SelectedMovement=null;SelectedBasketLine=null;SelectedProduct=null;SelectedCustomer=null;
        _operation=Guid.NewGuid();_filling=false;IsDirty=false;Changed(nameof(BasketTotal));_=LoadAsync();
    }
    private async Task<JsonElement> Send(string command,object payload)=>await _connection.SendAsync("module.execute",
        new ModuleCommandRequest(_company()??throw new InvalidOperationException("Válassz céget."),Definition.ModuleId,command,JsonSerializer.SerializeToElement(payload)),_shutdown);
    private async Task LoadAsync()
    {
        if(IsBusy||_company() is null)return;
        IsBusy=true;
        try
        {
            var refs=await Send("references",new{search=ProductSearch,customerSearch=CustomerSearch});
            var selectedProduct=SelectedProduct;var selectedCustomer=SelectedCustomer;var productId=selectedProduct?.Id;var customerId=selectedCustomer?.Id;
            _filling=true;
            Products.Clear();foreach(var product in refs.GetProperty("products").EnumerateArray())
                Products.Add(new(product.GetProperty("id").GetGuid(),Text(product,"name"),Text(product,"sku"),Text(product,"unitCode")));
            if(selectedProduct is not null && Products.All(p=>p.Id!=productId))Products.Add(selectedProduct);
            SelectedProduct=Products.FirstOrDefault(p=>p.Id==productId);
            if(IsSales)
            {
                Customers.Clear();Customers.Add(new(null,"Alkalmi vevő"));
                foreach(var customer in refs.GetProperty("customers").EnumerateArray())Customers.Add(new(customer.GetProperty("id").GetGuid(),Text(customer,"name")));
                if(selectedCustomer is not null && Customers.All(c=>c.Id!=customerId))Customers.Add(selectedCustomer);
                SelectedCustomer=Customers.FirstOrDefault(c=>c.Id==customerId)??Customers.First();
            }
            _filling=false;
            await RefreshRows();
        }
        catch(Exception ex){Message=ex.Message;}
        finally{_filling=false;IsBusy=false;}
    }
    private async Task RefreshRows()
    {
        var listed=await Send("list",new{search=RecordSearch,recordSearch=RecordSearch});_listing=listed.Clone();PopulateRows();
        Movements.Clear();SelectedMovement=null;
        if(listed.TryGetProperty("movements",out var movements))foreach(var row in movements.EnumerateArray())Movements.Add(DisplayRow(row));
    }
    private void PopulateRows()
    {
        if(_listing is not { } listed)return;
        Rows.Clear();SelectedRow=null;
        var rows=listed.GetProperty("rows").EnumerateArray().Select(r=>r.Clone()).ToArray();
        if(SortColumn.Length>0)
        {
            var comparer=Comparer<JsonElement>.Create((a,b)=>
            {
                a.TryGetProperty(SortColumn,out var av);b.TryGetProperty(SortColumn,out var bv);
                if(av.ValueKind==JsonValueKind.Number && bv.ValueKind==JsonValueKind.Number)return av.GetDecimal().CompareTo(bv.GetDecimal());
                return StringComparer.CurrentCultureIgnoreCase.Compare(av.ToString(),bv.ToString());
            });
            rows=(SortDescending?rows.OrderByDescending(r=>r,comparer):rows.OrderBy(r=>r,comparer)).ToArray();
        }
        foreach(var row in rows)Rows.Add(DisplayRow(row));
        ListNotice=listed.TryGetProperty("total",out var total)&&total.GetInt32()>Rows.Count
            ? "Az első "+Rows.Count+" sor látszik a "+total.GetInt32()+" találatból. Szűkítsd a keresést."
            : Rows.Count+" sor. A termék- és vevőválasztó keresésenként legfeljebb 200 találatot mutat.";
    }
    private static EntityRow DisplayRow(JsonElement json)
    {
        var row=new EntityRow(json);
        if(json.TryGetProperty("gross",out var gross))row.Values["gross"]=gross.GetDecimal().ToString("N2",CultureInfo.GetCultureInfo("hu-HU"));
        if(json.TryGetProperty("quantity",out var quantity))row.Values["quantity"]=quantity.GetDecimal().ToString("0.####",CultureInfo.GetCultureInfo("hu-HU"));
        if(json.TryGetProperty("at",out var at)&&at.TryGetDateTimeOffset(out var date))row.Values["at"]=date.ToLocalTime().ToString("yyyy.MM.dd HH:mm");
        return row;
    }
    private SaleBasketLine DraftLine()
    {
        var p=SelectedProduct??throw new ArgumentException("Válassz terméket.");
        var quantity=Number(QuantityText,"Mennyiség");var price=Number(PriceText,"Nettó egységár");var vat=Number(VatText,"Áfa");
        if(quantity<=0||quantity>1_000_000||decimal.Round(quantity,4)!=quantity||price<0||price>1_000_000_000||decimal.Round(price,4)!=price||!new decimal[]{0,5,18,27}.Contains(vat))
            throw new ArgumentException("Ellenőrizd a pozitív mennyiséget, az árat és az áfakulcsot (0, 5, 18, 27).");
        return new(p.Id,p.Name,p.UnitCode,quantity,price,vat);
    }
    private void AddLine()
    {
        if(IsBusy)return;
        try{if(Basket.Count>=400)throw new ArgumentException("Legfeljebb 400 tétel lehet.");Basket.Add(DraftLine());Dirty();Message="A tétel bekerült az eladásba. A készlet csak véglegesítéskor változik.";}
        catch(Exception ex){Message=ex.Message;}
    }
    private async Task SaveAsync()
    {
        if(IsBusy)return;
        var committed=false;
        try
        {
            object payload;
            if(IsInventory)
            {
                var p=SelectedProduct??throw new ArgumentException("Válassz terméket.");
                payload=new{operationId=_operation,productId=p.Id,warehouse=Warehouse,quantity=Number(QuantityText,"Mennyiség"),reason=Reason};
            }
            else
            {
                if(Basket.Count==0)throw new ArgumentException("Adj legalább egy tételt az eladáshoz.");
                payload=new{operationId=_operation,customerId=SelectedCustomer?.Id,warehouse=Warehouse,lines=Basket.Select(l=>new{l.ProductId,l.Quantity,l.NetUnitPrice,l.VatRate}).ToArray()};
            }
            if(MessageBox.Show(IsInventory ? "Rögzíted a készletváltozást? Az előzmény megmarad." : "Véglegesíted az eladást? A készlet csökken; utólag visszafordítással javítható.",
                Definition.Title,MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            IsBusy=true;
            var result=await Send("save",payload);
            committed=true;
            if(IsInventory){_quantity="";_reason="";Changed(nameof(QuantityText));Changed(nameof(Reason));}
            _operation=Guid.NewGuid();Basket.Clear();IsDirty=false;Changed(nameof(BasketTotal));
            await RefreshRows();
            Message=IsInventory ? "A készletváltozás rögzítve." : "Eladás rögzítve: "+Text(result,"number")+". Ez helyi eladási bizonylat, nem adóügyi számla.";
        }
        catch(Exception ex){Message=committed ? "A rögzítés sikerült, de a lista frissítése nem: "+ex.Message+" Használd a Frissítés gombot." : ex.Message+" A szerkesztett adatok megmaradtak; ugyanaz a beküldés újrapróbálható.";}
        finally{IsBusy=false;}
    }
    private async Task ReverseAsync()
    {
        if(IsBusy)return;
        try
        {
            object payload;
            if(IsInventory)
            {
                var m=SelectedMovement?.Json??throw new ArgumentException("Válassz egy kézi mozgást a mozgásnaplóból.");
                if(Text(m,"sourceModule")!="inventory")throw new ArgumentException("A bevételezést és az eladást a saját bizonylatánál lehet helyesbíteni.");
                payload=new{id=m.GetProperty("sourceId").GetGuid()};
            }
            else
            {
                var sale=SelectedRow?.Json??throw new ArgumentException("Válassz eladást a listából.");
                payload=new{id=sale.GetProperty("id").GetGuid(),version=sale.GetProperty("version").GetInt64()};
            }
            if(MessageBox.Show("Ellenmozgással visszafordítod? Az eredeti bizonylat és az előzménye megmarad.","Visszafordítás",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            IsBusy=true;await Send("reverse",payload);await RefreshRows();Message="A visszafordítás rögzítve.";
        }
        catch(Exception ex){Message=ex.Message;}
        finally{IsBusy=false;}
    }
    private async Task DetailsAsync()
    {
        if(IsBusy||IsInventory)return;
        try
        {
            var sale=SelectedRow?.Json??throw new ArgumentException("Válassz egy eladást.");IsBusy=true;
            var response=await Send("get",new{id=sale.GetProperty("id").GetGuid()});
            Details=Text(response,"number")+" · "+Text(response,"customerName")+" · "+Text(response,"warehouse")+"\n"+
                string.Join("\n",response.GetProperty("lines").EnumerateArray().Select(l=>Text(l,"name")+" · "+l.GetProperty("quantity").GetDecimal().ToString("0.####",CultureInfo.GetCultureInfo("hu-HU"))+" "+Text(l,"unitCode")+" · nettó egységár "+l.GetProperty("netUnitPrice").GetDecimal().ToString("N2",CultureInfo.GetCultureInfo("hu-HU"))+" Ft · áfa "+l.GetProperty("vatRate").GetDecimal()+"%"));
        }
        catch(Exception ex){Message=ex.Message;}
        finally{IsBusy=false;}
    }
    private void Reset()
    {
        if(IsBusy)return;
        if(IsDirty&&MessageBox.Show("Elveted a szerkesztett adatokat? Bizonytalan beküldés után előbb frissítsd a bizonylatlistát.","Új művelet",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        Basket.Clear();_operation=Guid.NewGuid();IsDirty=false;Changed(nameof(BasketTotal));Message="Új művelet indítható.";
    }
    private async Task ExportAsync()
    {
        if(IsBusy)return;
        var file=new SaveFileDialog{Filter="CSV (*.csv)|*.csv",FileName=IsInventory?"keszlet.csv":"eladasok.csv"};if(file.ShowDialog()!=true)return;
        IsBusy=true;
        try{var response=await Send("export",new{});await File.WriteAllTextAsync(file.FileName,Text(response,"csv"),new System.Text.UTF8Encoding(true),_shutdown);Message="A lista exportálva.";}
        catch(Exception ex){Message=ex.Message;}
        finally{IsBusy=false;}
    }
    private static string Text(JsonElement e,string name)=>e.TryGetProperty(name,out var value)?value.GetString()??"":"";
    private static decimal Number(string text,string label)
    {
        if(!decimal.TryParse(text.Replace(" ","").Replace(",","."),NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var result))throw new ArgumentException(label+": számot adj meg.");
        return result;
    }
}
