using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using PcImpressz.Contracts;
namespace PcImpressz.Shell.Windows;
internal sealed partial class OperationsPageViewModel
{
    private CancellationTokenSource? _quickSearch;private int _quickGeneration;private bool _searching;
    private readonly Dictionary<Guid,OperationProduct> _knownProducts=[];
    private JsonElement? _pendingSale;
    private readonly SemaphoreSlim _scanLock=new(1,1);private int _pendingScans,_scanEpoch;
    public bool CanBeginCheckout=>CanWrite&&_pendingScans==0;
    public ObservableCollection<OperationProduct> QuickMatches{get;}=[];
    public bool HasQuickMatches=>QuickMatches.Count>0;
    public bool IsSearching{get=>_searching;private set{_searching=value;Changed();}}
    public bool HasUnresolvedOperation=>_pendingSale is not null;
    public bool CanEditSale=>CanWrite&&_pendingSale is null;
    public string CheckoutLabel=>_pendingSale is null?"Eladás véglegesítése":"Eladás eredményének ellenőrzése / újrapróbálás";
    public event Action? SearchFocusRequested;
    private void InitializeQuickCheckout()
    {
        QuickMatches.CollectionChanged+=(_,_)=>Changed(nameof(HasQuickMatches));
        Basket.CollectionChanged+=(_,e)=>
        {
            if(e.OldItems is not null)foreach(SaleBasketLine line in e.OldItems)line.PropertyChanged-=LineChanged;
            if(e.NewItems is not null)foreach(SaleBasketLine line in e.NewItems){line.PropertyChanged+=LineChanged;line.RemoveCommand=new RelayCommand(()=>{if(CanEditSale)Basket.Remove(line);});}
            Dirty();
        };
    }
    private void LineChanged(object? sender,PropertyChangedEventArgs e){if(e.PropertyName==nameof(SaleBasketLine.Gross))Dirty();}
    private void CancelQuickSearch(){_quickGeneration++;_quickSearch?.Cancel();_quickSearch?.Dispose();_quickSearch=null;IsSearching=false;}
    private void ResetQuickCheckout(){_scanEpoch++;CancelQuickSearch();QuickMatches.Clear();_knownProducts.Clear();_productSearch="";Changed(nameof(ProductSearch));}
    private void ScheduleQuickSearch()
    {
        if(!IsSales||_filling)return;CancelQuickSearch();QuickMatches.Clear();
        if(string.IsNullOrWhiteSpace(ProductSearch)||_company() is null)return;
        var cts=CancellationTokenSource.CreateLinkedTokenSource(_shutdown);_quickSearch=cts;var generation=_quickGeneration;var query=ProductSearch.Trim();var company=_company();var warehouse=Warehouse;
        _=SearchQuickAsync(query,warehouse,company!.Value,generation,cts.Token,true);
    }
    private async Task SearchQuickAsync(string query,string warehouse,Guid company,int generation,CancellationToken ct,bool delay)
    {
        try
        {
            if(delay)await Task.Delay(220,ct);if(generation!=_quickGeneration)return;IsSearching=true;
            var response=await _connection.SendAsync("module.execute",new ModuleCommandRequest(company,"sales","references",JsonSerializer.SerializeToElement(new{search=query,warehouse})),ct);
            if(generation!=_quickGeneration||_company()!=company||Warehouse!=warehouse||ProductSearch.Trim()!=query)return;
            QuickMatches.Clear();foreach(var row in response.GetProperty("products").EnumerateArray()){var product=ReadProduct(row);QuickMatches.Add(product);_knownProducts[product.Id]=product;}
            if(QuickMatches.Count==1&&QuickMatches[0].Barcode.Length>0&&QuickMatches[0].Barcode.Equals(query,StringComparison.OrdinalIgnoreCase))AddQuickProduct(QuickMatches[0]);
            else if(QuickMatches.Count==0)Message="Nincs ilyen aktív, eladható termék.";
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){}
        catch(Exception ex) when(ex is not OutOfMemoryException){if(generation==_quickGeneration)Message=ex.Message;}
        finally{if(generation==_quickGeneration)IsSearching=false;}
    }
    public async Task EnterQuickProductAsync()
    {
        var query=ProductSearch.Trim();if(query.Length==0||!CanEditSale||_company() is not Guid company)return;
        var warehouse=Warehouse;var epoch=_scanEpoch;CancelQuickSearch();QuickMatches.Clear();_productSearch="";Changed(nameof(ProductSearch));_pendingScans++;Changed(nameof(CanBeginCheckout));var entered=false;
        try
        {
            await _scanLock.WaitAsync(_shutdown);entered=true;
            if(epoch!=_scanEpoch||_company()!=company||Warehouse!=warehouse)return;
            var response=await _connection.SendAsync("module.execute",new ModuleCommandRequest(company,"sales","references",JsonSerializer.SerializeToElement(new{search=query,warehouse})),_shutdown);
            if(epoch!=_scanEpoch||_company()!=company||Warehouse!=warehouse)return;
            var products=response.GetProperty("products").EnumerateArray().Select(ReadProduct).ToArray();
            if(products.Length==1)AddQuickProduct(products[0],false);
            else
            {
                Message=products.Length==0?"Nincs ilyen termék: "+query:"Több találat van erre: "+query+". Válaszd ki a terméket.";
                if(ProductSearch.Length==0&&_pendingScans==1){_productSearch=query;Changed(nameof(ProductSearch));foreach(var p in products){QuickMatches.Add(p);_knownProducts[p.Id]=p;}}
            }
        }
        catch(OperationCanceledException) when(_shutdown.IsCancellationRequested){}
        catch(Exception ex) when(ex is not OutOfMemoryException){if(epoch==_scanEpoch)Message=ex.Message;}
        finally{if(entered)_scanLock.Release();_pendingScans--;Changed(nameof(CanBeginCheckout));}
    }
    public bool AddQuickProduct(OperationProduct product,bool clearInput=true)
    {
        if(!CanEditSale)return false;
        try
        {
            var current=Basket.Where(l=>l.ProductId==product.Id).Sum(l=>l.Quantity);
            if(product.AvailableQuantity is decimal available&&current+1>available)throw new ArgumentException("Nincs elegendő készlet ebben a raktárban: "+Warehouse+".");
            var same=Basket.FirstOrDefault(l=>l.ProductId==product.Id&&l.NetUnitPrice==product.NetUnitPrice&&l.VatRate==product.VatRate&&l.Error.Length==0);
            if(same is not null)same.QuantityText=(same.Quantity+1).ToString(CultureInfo.InvariantCulture);
            else
            {
                if(Basket.Count>=400)throw new ArgumentException("Legfeljebb 400 kosársor lehet.");
                var line=new SaleBasketLine(product.Id,product.Name,product.UnitCode,1,product.NetUnitPrice??0,product.VatRate);if(product.NetUnitPrice is null)line.RequirePrice();Basket.Add(line);
            }
            _knownProducts[product.Id]=product;if(clearInput){CancelQuickSearch();QuickMatches.Clear();_productSearch="";Changed(nameof(ProductSearch));SearchFocusRequested?.Invoke();}Dirty();Message="Kosárba került: "+product.Name;return true;
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){Message=ex.Message;return false;}
    }
    internal void ValidateCheckout()
    {
        if(_pendingScans>0)throw new ArgumentException("Várd meg a beolvasott termék kosárba helyezését.");
        if(Basket.Count==0)throw new ArgumentException("Előbb tegyél terméket a kosárba.");
        var error=Basket.FirstOrDefault(l=>l.Error.Length>0);if(error is not null)throw new ArgumentException(error.Name+": "+error.Error);
        foreach(var group in Basket.GroupBy(l=>l.ProductId))if(_knownProducts.TryGetValue(group.Key,out var p)&&p.AvailableQuantity is decimal q&&group.Sum(l=>l.Quantity)>q)throw new ArgumentException(p.Name+": a kosár több, mint a jelzett készlet.");
    }
    internal void ShowCheckoutError(string text)=>Message=text;
    internal async Task<bool> CommitCheckoutAsync(string payment,bool invoice,OperationCustomer? customer)
    {
        if(IsBusy)return false;if(_company() is null||!_connection.Allows("sales.write")){Message="Az eladás rögzítéséhez jelentkezz be eladóként vagy adminként.";return false;}var committed=false;var hadPending=_pendingSale is not null;var confirmedAbsent=false;
        try
        {
            if(_pendingSale is null)
            {
                ValidateCheckout();if(invoice&&customer?.Id is null)throw new ArgumentException("Számlaigényhez válassz vevőt.");
                _pendingSale=JsonSerializer.SerializeToElement(new{operationId=_operation,customerId=invoice?customer?.Id:null,warehouse=Warehouse.Trim(),lines=Basket.Select(l=>new{productId=l.ProductId,quantity=l.Quantity,netUnitPrice=l.NetUnitPrice,vatRate=l.VatRate}).ToArray(),paymentMethod=payment,invoiceRequested=invoice});
            }
            CancelQuickSearch();QuickMatches.Clear();IsBusy=true;PermissionsChanged();
            JsonElement? recovered=null;
            if(hadPending)
            {
                try{var stored=await Send("get",new{id=_operation});VerifyRecovered(stored,_pendingSale.Value);recovered=stored;}
                catch(HostCommandException ex) when(ex.Code=="not-found"){confirmedAbsent=true;}
            }
            var result=recovered??await Send("save",_pendingSale.Value);committed=true;_pendingSale=null;_operation=Guid.NewGuid();Basket.Clear();IsDirty=false;SelectedCustomer=null;Changed(nameof(BasketTotal));
            Message="Eladás rögzítve: "+Text(result,"number")+" · "+(Text(result,"paymentMethod")=="card"?"Kártya":"Készpénz")+(result.TryGetProperty("invoiceRequested",out var requested)&&requested.GetBoolean()?" · Számlaigény rögzítve.":".");
            try{await RefreshRows();await RefreshSalesStockAsync();}catch(Exception ex) when(ex is not OutOfMemoryException){Message+=" A nézet frissítése nem sikerült: "+ex.Message;}
            SearchFocusRequested?.Invoke();return true;
        }
        catch(Exception ex) when(ex is not OutOfMemoryException)
        {
            if(ex is not (System.IO.IOException or OperationCanceledException)&&_pendingSale is {} pending&&pending.GetProperty("operationId").GetGuid()==_operation)
            { // Explicit server denial/validation means this attempt did not commit.
                if((!hadPending||confirmedAbsent)&&ex is HostCommandException {Code: "validation" or "denied"})_pendingSale=null;
            }
            Message=committed?"Az eladás rögzítve, a nézet nem frissült.":ex.Message+(_pendingSale is not null?" Az eredmény még bizonytalan: a véglegesítés gomb ugyanazt a műveletet próbálja újra, a kosár addig zárolt.":" A kosár megmaradt.");return false;
        }
        finally{IsBusy=false;PermissionsChanged();}
    }
    private static void VerifyRecovered(JsonElement stored,JsonElement pending)
    {
        bool Same(string key)=>stored.GetProperty(key).ToString()==pending.GetProperty(key).ToString();
        var expected=pending.GetProperty("lines").EnumerateArray().ToArray();var actual=stored.GetProperty("lines").EnumerateArray().ToArray();
        if(stored.GetProperty("id").GetGuid()!=pending.GetProperty("operationId").GetGuid()||!Same("customerId")||!Same("warehouse")||!Same("paymentMethod")||!Same("invoiceRequested")||expected.Length!=actual.Length
            ||expected.Where((line,i)=>new[]{"productId","quantity","netUnitPrice","vatRate"}.Any(key=>line.GetProperty(key).ValueKind==JsonValueKind.Number?line.GetProperty(key).GetDecimal()!=actual[i].GetProperty(key).GetDecimal():line.GetProperty(key).ToString()!=actual[i].GetProperty(key).ToString())).Any())
            throw new InvalidOperationException("A tárolt eladás eltér a függő művelettől. A kosár zárolva marad, adminisztrátori ellenőrzés szükséges.");
    }
    internal async Task OpenCheckoutAsync()
    {
        if(_pendingSale is not null){await CommitCheckoutAsync("cash",false,null);return;}
        try{ValidateCheckout();CancelQuickSearch();QuickMatches.Clear();CheckoutDialog.Show(this);}catch(Exception ex) when(ex is not OutOfMemoryException){Message=ex.Message;}
    }
    internal async Task<OperationCustomer[]> FindCheckoutCustomersAsync(string query,CancellationToken ct)
    {
        var company=_company()??throw new InvalidOperationException("Válassz céget.");
        var response=await _connection.SendAsync("module.execute",new ModuleCommandRequest(company,"sales","references",JsonSerializer.SerializeToElement(new{customerSearch=query,warehouse=Warehouse,allPartners=true})),ct);
        return response.GetProperty("customers").EnumerateArray().Select(ReadCustomer).ToArray();
    }
    internal async Task<OperationCustomer> SaveCheckoutCustomerAsync(string name,string tax,string country,string postal,string city,string address)
        =>ReadCustomer(await Send("customer-save",new CheckoutCustomerSave(_company()!.Value,name,tax,country,postal,city,address)));
    internal async Task<OperationCustomer> LookupCheckoutCustomerAsync(string tax)
        =>ReadCustomer(await _connection.SendAsync("sales.customer-nav",new TaxpayerLookup(_company()!.Value,tax),_shutdown));
    internal static OperationCustomer ReadCustomer(JsonElement row)=>new(row.GetProperty("id").GetGuid(),Text(row,"name"),Text(row,"taxId"),Text(row,"country"),Text(row,"postalCode"),Text(row,"city"),Text(row,"address"),!row.TryGetProperty("isCustomer",out var flag)||flag.GetBoolean());
    internal static OperationProduct ReadProduct(JsonElement row)=>new(row.GetProperty("id").GetGuid(),Text(row,"name"),Text(row,"sku"),Text(row,"unitCode"),row.TryGetProperty("availableQuantity",out var quantity)&&quantity.ValueKind==JsonValueKind.Number?quantity.GetDecimal():null,Text(row,"barcode"),Text(row,"manufacturerPartNumber"),row.TryGetProperty("netUnitPrice",out var price)&&price.ValueKind==JsonValueKind.Number?price.GetDecimal():null,row.TryGetProperty("vatRate",out var vat)?vat.GetDecimal():27,Text(row,"priceSource"));
}
