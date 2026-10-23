using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using PcImpressz.Contracts;
namespace PcImpressz.Shell.Windows;

internal sealed record OrderProductOption(Guid Id,string Name,string Sku,string UnitCode,decimal? SaleNetUnitPrice,decimal SaleVatRate,decimal Physical,decimal Reserved,decimal Free)
{public string Label=>$"{Name} · {Sku} · szabad: {Free:0.####} {UnitCode}";}
internal sealed record OrderCustomerOption(Guid Id,string Name,string TaxId=""){public string Label=>Name+(TaxId.Length>0?" · "+TaxId:"");}
internal sealed record OrderListRow(CustomerOrder Value)
{public string Number=>Value.Number;public string Customer=>Value.CustomerName;public string Due=>Value.DueDate;public string Status=>OrderWorkflow.Label(OrderWorkflow.Effective(Value));public string Fulfilment=>OrderStatus.Label(Value.Status);public decimal Gross=>Value.Gross;}
internal sealed class OrderEditLine:Observable
{
    private readonly Action _dirty;private string _quantity,_price,_vat;
    public OrderEditLine(CustomerOrderLine line,Action dirty){Value=line;_dirty=dirty;_quantity=Show(line.Quantity);_price=Show(line.NetUnitPrice);_vat=Show(line.VatRate);DeliveryText=Show(line.Reserved);}
    public CustomerOrderLine Value{get;}
    public string Name=>Value.Name;public string Sku=>Value.Sku;
    public string QuantityText{get=>_quantity;set{_quantity=value;Changed();_dirty();}}
    public string PriceText{get=>_price;set{_price=value;Changed();_dirty();}}
    public string VatText{get=>_vat;set{_vat=value;Changed();_dirty();}}
    public decimal Delivered=>Value.Delivered;public decimal Reserved=>Value.Reserved;
    public decimal Missing=>Math.Max(0,Value.Quantity-Value.Delivered-Value.Reserved);
    public string DeliveryText{get;set;}="0";
    internal static string Show(decimal value)=>value.ToString("0.####",CultureInfo.GetCultureInfo("hu-HU"));
}
internal sealed class OrdersPageViewModel:Observable,IWorkspacePage
{
    private readonly HostConnection _connection;private readonly Func<Guid?> _company;private readonly CancellationToken _shutdown;private readonly Func<bool> _saveCustomersDefault;
    private CustomerOrder? _order;private Guid _id=Guid.NewGuid();private bool _busy,_dirty,_filling;private int _page,_total;
    private Func<Task<JsonElement>>? _pending;private string _pendingLabel="";
    private string _message="",_warehouse="Bolt",_due="",_notes="";private OrderListRow? _selected;private OrderCustomerOption? _customer;
    public OrdersPageViewModel(EntityPageDescriptor definition,HostConnection connection,Func<Guid?> company,CancellationToken shutdown,Func<bool>? saveCustomersDefault=null)
    {
        (Definition,_connection,_company,_shutdown)=(definition,connection,company,shutdown);_saveCustomersDefault=saveCustomersDefault??(()=>true);_saveCustomer=_saveCustomersDefault();
        RefreshCommand=new RelayCommand(()=>{_page=0;_=LoadAsync();});ReferencesCommand=new RelayCommand(()=>_=ReferencesAsync());
        NewCommand=new RelayCommand(New);AddLineCommand=new RelayCommand(Add);RemoveLineCommand=new RelayCommand(()=>{if(CanEdit&&SelectedLine is {} line){Lines.Remove(line);Dirty();}});
        SaveCommand=new RelayCommand(()=>_=SaveAsync());ReserveCommand=new RelayCommand(()=>_=ActionAsync("reserve"));ReleaseCommand=new RelayCommand(()=>_=ActionAsync("release"));CancelCommand=new RelayCommand(()=>_=ActionAsync("cancel"));DeliverCommand=new RelayCommand(()=>_=DeliverAsync());RetryCommand=new RelayCommand(()=>_=RunPendingAsync());StatusCommand=new RelayCommand(()=>_=SetStatusAsync());
        PreviousCommand=new RelayCommand(()=>{if(!IsUsable||_page==0)return;_page--;_=LoadAsync();});NextCommand=new RelayCommand(()=>{if(!IsUsable||(_page+1)*20>=_total)return;_page++;_=LoadAsync();});
    }
    public EntityPageDescriptor Definition{get;}
    public ObservableCollection<OrderListRow> Rows{get;}=[];
    public ObservableCollection<OrderProductOption> Products{get;}=[];
    public ObservableCollection<OrderCustomerOption> Customers{get;}=[];
    public ObservableCollection<OrderEditLine> Lines{get;}=[];
    public OrderProductOption? SelectedProduct{get;set;}public OrderEditLine? SelectedLine{get;set;}
    public EntityFilterOption[] StatusFilters{get;}=[new("open","Nyitott rendelések"),new("all","Minden rendelés"),new("new","Új"),new("ordered","Megrendelve"),new("arrived","Beérkezett"),new("partial","Részben átadva"),new("done","Átadva"),new("cancelled","Lemondva")];
    public EntityFilterOption[] WorkflowChoices{get;}=[new("new","Új"),new("ordered","Megrendelve"),new("arrived","Beérkezett")];
    public string Search{get;set;}="";private string _statusFilter="open";
    public string StatusFilter{get=>_statusFilter;set{if(value is null||value==_statusFilter)return;_statusFilter=value;_page=0;Changed();_=LoadAsync();}}
    private bool _newCustomer,_saveCustomer;private string _customerName="",_phone="",_email="",_taxId="",_postal="",_city="",_address="",_workflow="new";
    public bool NewCustomer{get=>_newCustomer;set{_newCustomer=value;Changed();Changed(nameof(ExistingCustomer));Dirty();}}
    public bool ExistingCustomer=>!NewCustomer;
    public bool SaveCustomerToPartners{get=>_saveCustomer;set{_saveCustomer=value;Changed();Dirty();}}
    public string CustomerName{get=>_customerName;set{_customerName=value;Changed();Dirty();}}
    public string CustomerPhone{get=>_phone;set{_phone=value;Changed();Dirty();}}
    public string CustomerEmail{get=>_email;set{_email=value;Changed();Dirty();}}
    public string CustomerTaxId{get=>_taxId;set{_taxId=value;Changed();Dirty();}}
    public string CustomerPostalCode{get=>_postal;set{_postal=value;Changed();Dirty();}}
    public string CustomerCity{get=>_city;set{_city=value;Changed();Dirty();}}
    public string CustomerAddress{get=>_address;set{_address=value;Changed();Dirty();}}
    public string WorkflowStatus{get=>_workflow;set{_workflow=value;Changed();}}

    public string ProductSearch{get;set;}="";public string CustomerSearch{get;set;}="";
    public string ReservationDays{get;set;}="7";public bool CardPayment{get;set;}public bool InvoiceRequested{get;set;}
    public string Warehouse{get=>_warehouse;set{_warehouse=value;Changed();Dirty();}}
    public string DueDate{get=>_due;set{_due=value;Changed();Dirty();}}
    public string Notes{get=>_notes;set{_notes=value;Changed();Dirty();}}
    public OrderCustomerOption? SelectedCustomer{get=>_customer;set{_customer=value;Changed();Dirty();}}
    public OrderListRow? SelectedOrder{get=>_selected;set{if(value==_selected||!IsUsable)return;if(IsDirty&&!UnsavedChanges.Confirm("A rendelés nem mentett módosításait elveted?","Másik rendelés")){Changed();return;}_selected=value;Changed();if(value is not null)_=OpenAsync(value.Value.Id);}}
    public bool IsBusy{get=>_busy;private set{_busy=value;Changed();Notify();}}
    public bool IsDirty{get=>_dirty;private set{_dirty=value;Changed();Notify();}}
    public bool HasUnresolvedOperation=>_pending is not null;
    public bool IsUsable=>!IsBusy&&!HasUnresolvedOperation;
    public bool CanWrite=>_connection.Allows("sales.write");
    public bool CanEdit=>IsUsable&&CanWrite&&(_order is null||_order.Status=="open"&&!_order.Lines.Any(l=>l.Reserved>0||l.Delivered>0));
    public bool CanAct=>IsUsable&&CanWrite&&!IsDirty&&_order is {Status:not "completed" and not "cancelled"};
    public bool CanDeliver=>CanAct&&_order!.Lines.Any(l=>l.Reserved>0);
    public bool CanRetry=>!IsBusy&&HasUnresolvedOperation;
    public string Header=>_order is null?"Új vevői rendelés":_order.Number+" · "+OrderWorkflow.Label(OrderWorkflow.Effective(_order))+" · "+OrderStatus.Label(_order.Status);
    public string Summary=>_order is null?"A rendelés mentése még nem foglal és nem csökkent készletet.":$"Tervezett bruttó: {MoneyDisplay.Format(_order.Gross)} Ft · Részátadások: {_order.Deliveries?.Length??0} · Foglalás: {(_order.ReservationUntil?.ToLocalTime().ToString("yyyy-MM-dd HH:mm")??"nincs")}";
    public string DeliveryHistory=>string.Join("\n",(_order?.Deliveries??[]).Select(d=>d.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm")+" · "+d.SaleNumber));
    public string PageLabel=>$"{_page+1}. oldal · {_total} rendelés";
    public string Message{get=>_message;private set{_message=value;Changed();}}
    public ICommand StatusCommand{get;}public ICommand RefreshCommand{get;}public ICommand ReferencesCommand{get;}public ICommand NewCommand{get;}public ICommand AddLineCommand{get;}public ICommand RemoveLineCommand{get;}
    public ICommand SaveCommand{get;}public ICommand ReserveCommand{get;}public ICommand ReleaseCommand{get;}public ICommand CancelCommand{get;}public ICommand DeliverCommand{get;}public ICommand RetryCommand{get;}public ICommand PreviousCommand{get;}public ICommand NextCommand{get;}
    private void Notify(){foreach(var property in new[]{nameof(IsUsable),nameof(CanEdit),nameof(CanAct),nameof(CanDeliver),nameof(CanRetry),nameof(HasUnresolvedOperation),nameof(Header),nameof(Summary),nameof(DeliveryHistory)})Changed(property);}
    private void Dirty(){if(!_filling)IsDirty=true;}
    public void PermissionsChanged(){Changed(nameof(CanWrite));Notify();}
    public void CompanyChanged(){if(IsBusy||HasUnresolvedOperation)return;_order=null;_selected=null;_id=Guid.NewGuid();Rows.Clear();Products.Clear();Customers.Clear();Lines.Clear();_customer=null;_filling=true;ResetCustomer();WorkflowStatus="new";Warehouse="Bolt";DueDate="";Notes="";_filling=false;IsDirty=false;Changed(nameof(SelectedCustomer));Notify();_=LoadAsync();}
    private async Task<JsonElement> Send(string command,object input)=>await _connection.SendAsync("module.execute",new ModuleCommandRequest(_company()??throw new InvalidOperationException("Jelentkezz be a cégbe."),Definition.ModuleId,command,JsonSerializer.SerializeToElement(input,CompanyDocuments.Json)),_shutdown);
    internal async Task LoadAsync()
    {
        if(!IsUsable||_company() is not {} company)return;IsBusy=true;
        try{await ReadListAsync(company);Message="Rendelések betöltve. A foglalás nem készletmozgás.";}
        catch(Exception ex){Message=ex.Message;}finally{IsBusy=false;}
    }
    private async Task ReadListAsync(Guid company)
    {
        var result=await Send("orders-list",new OrderQuery(company,Search,StatusFilter,_page));Rows.Clear();
        foreach(var order in result.GetProperty("rows").Deserialize<CustomerOrder[]>(CompanyDocuments.Json)??[])Rows.Add(new(order));
        _total=result.GetProperty("total").GetInt32();Changed(nameof(PageLabel));
    }
    internal async Task ReferencesAsync()
    {
        if(!IsUsable||_company() is null)return;IsBusy=true;
        try
        {
            var result=await Send("orders-references",new{warehouse=Warehouse,search=ProductSearch,customerSearch=CustomerSearch});
            Products.Clear();foreach(var product in result.GetProperty("products").Deserialize<OrderProductOption[]>(CompanyDocuments.Json)??[])Products.Add(product);
            var keep=SelectedCustomer;Customers.Clear();foreach(var customer in result.GetProperty("customers").Deserialize<OrderCustomerOption[]>(CompanyDocuments.Json)??[])Customers.Add(customer);
            if(keep is not null&&!Customers.Any(c=>c.Id==keep.Id))Customers.Insert(0,keep);
            _filling=true;SelectedCustomer=Customers.FirstOrDefault(c=>c.Id==keep?.Id);_filling=false;Changed(nameof(SelectedCustomer));Message="Termékek és vevők frissítve. A szabad készlet a más rendelések foglalását is figyelembe veszi.";
        }
        catch(Exception ex){Message=ex.Message;}finally{_filling=false;IsBusy=false;}
    }
    private void New()
    {
        if(!IsUsable||!CanWrite)return;if(IsDirty&&!UnsavedChanges.Confirm("A nem mentett rendelést elveted?","Új rendelés"))return;
        _order=null;_selected=null;_id=Guid.NewGuid();_filling=true;Lines.Clear();DueDate="";Notes="";SelectedCustomer=null;ResetCustomer();WorkflowStatus="new";_filling=false;IsDirty=false;Changed(nameof(SelectedOrder));Notify();_=ReferencesAsync();
    }
    private void Add()
    {
        if(!CanEdit||SelectedProduct is not {} product)return;
        if(Lines.Count>=200){Message="Legfeljebb 200 tétel adható meg.";return;}
        Lines.Add(new(new(Guid.NewGuid(),product.Id,product.Name,product.Sku,product.UnitCode,1,product.SaleNetUnitPrice??0,product.SaleVatRate),Dirty));Dirty();
    }
    private async Task OpenAsync(Guid id)
    {
        if(!IsUsable)return;IsBusy=true;
        try{Apply((await Send("orders-get",new{id})).Deserialize<CustomerOrder>(CompanyDocuments.Json)!);Message="Mentett rendelés. Foglalással készítheted elő az átadást.";}
        catch(Exception ex){Message=ex.Message;}finally{IsBusy=false;}
    }
    private void Apply(CustomerOrder order)
    {
        _order=order;_id=order.Id;_filling=true;Warehouse=order.Warehouse;DueDate=order.DueDate;Notes=order.Notes;
        WorkflowStatus=order.WorkflowStatus;
        var customer=Customers.FirstOrDefault(c=>c.Id==order.CustomerId);if(customer is null&&order.CustomerId!=Guid.Empty){customer=new(order.CustomerId,order.CustomerName);Customers.Add(customer);}SelectedCustomer=customer;
        NewCustomer=order.CustomerId==Guid.Empty;var draft=order.Customer;CustomerName=draft?.Name??order.CustomerName;CustomerPhone=draft?.Phone??"";CustomerEmail=draft?.Email??"";CustomerTaxId=draft?.TaxId??"";CustomerPostalCode=draft?.PostalCode??"";CustomerCity=draft?.City??"";CustomerAddress=draft?.Address??"";SaveCustomerToPartners=order.CustomerId!=Guid.Empty;
        Lines.Clear();foreach(var line in order.Lines)Lines.Add(new(line,Dirty));_filling=false;IsDirty=false;Notify();
    }
    private void ResetCustomer(){NewCustomer=false;SaveCustomerToPartners=_saveCustomersDefault();CustomerName="";CustomerPhone="";CustomerEmail="";CustomerTaxId="";CustomerPostalCode="";CustomerCity="";CustomerAddress="";}
    private async Task SetStatusAsync()
    {
        if(!CanAct||_company() is not {} company)return;
        await WriteAsync("orders-status",new OrderWorkflowChange(company,Guid.NewGuid(),_id,_order!.Version,WorkflowStatus),"Rendelési állapot mentése");
    }
    private static decimal Number(string value,string label)
    {
        if(!decimal.TryParse(value.Replace(" ","").Replace(',','.'),NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var amount))throw new ArgumentException(label+": érvényes szám szükséges.");return amount;
    }
    private async Task SaveAsync()
    {
        if(!CanEdit||_company() is not {} company)return;
        try
        {
            if(!NewCustomer&&SelectedCustomer is null)throw new ArgumentException("Válassz vevőt vagy jelöld az Új vevő mezőt.");
            var request=new OrderSave(company,Guid.NewGuid(),_id,_order?.Version??0,NewCustomer?Guid.Empty:SelectedCustomer!.Id,Warehouse,DueDate,Notes,Lines.Select(l=>new CustomerOrderLineDraft(l.Value.Id,l.Value.ProductId,Number(l.QuantityText,"Mennyiség"),Number(l.PriceText,"Nettó ár"),Number(l.VatText,"Áfa"))).ToArray(),NewCustomer?new OrderCustomerDraft(CustomerName,CustomerPhone,CustomerEmail,CustomerTaxId,"HU",CustomerPostalCode,CustomerCity,CustomerAddress):null,SaveCustomerToPartners,WorkflowStatus);
            await WriteAsync("orders-save",request,"Rendelés mentése");
        }
        catch(Exception ex){Message=ex.Message;}
    }
    private async Task ActionAsync(string action)
    {
        if(!CanAct||_company() is not {} company)return;
        if(action=="cancel"&&!UnsavedChanges.Confirm("Lemondod a még át nem adott részt és feloldod a foglalást? A korábbi átadások megmaradnak.","Rendelés lemondása"))return;
        var days=7;if(action=="reserve"&&!int.TryParse(ReservationDays,out days)){Message="A foglalás napjainak egész számot adj meg.";return;}
        await WriteAsync("orders-"+action,new OrderAction(company,Guid.NewGuid(),_id,_order!.Version,days),action=="reserve"?"Szabad készlet foglalása":action=="release"?"Foglalás feloldása":"Rendelés lemondása");
    }
    private async Task DeliverAsync()
    {
        if(!CanDeliver||_company() is not {} company)return;
        try
        {
            var lines=Lines.Select(l=>new OrderDeliveryLine(l.Value.Id,Number(l.DeliveryText,"Átadási mennyiség"))).Where(l=>l.Quantity!=0).ToArray();
            if(lines.Length==0)throw new ArgumentException("Adj meg legalább egy pozitív átadási mennyiséget.");
            if(!UnsavedChanges.Confirm("Véglegesíted az átadást? Eladási bizonylat készül és csökken a készlet.","Részátadás"))return;
            await WriteAsync("orders-deliver",new OrderDeliver(company,Guid.NewGuid(),_id,_order!.Version,lines,CardPayment?"card":"cash",InvoiceRequested),"Átadás véglegesítése");
        }
        catch(Exception ex){Message=ex.Message;}
    }
    private async Task WriteAsync(string command,object request,string label)
    {if(!IsUsable||!CanWrite)return;_pending=()=>Send(command,request);_pendingLabel=label;Notify();await RunPendingAsync();}
    internal async Task RunPendingAsync()
    {
        if(IsBusy||_pending is null)return;IsBusy=true;
        try
        {
            var result=await _pending();var order=result.Deserialize<CustomerOrder>(CompanyDocuments.Json)??throw new InvalidOperationException("Hiányzó rendelésválasz.");_pending=null;Apply(order);Message=_pendingLabel+" sikeres. "+(order.Status=="completed"?"Minden tétel átadva.":"A mentett állapot frissült.");
            if(_company() is {} company){try{await ReadListAsync(company);}catch(Exception ex){Message+=" A lista frissítése nem sikerült: "+ex.Message;}}
        }
        catch(HostCommandException ex) when(ex.Code is "validation" or "denied" or "not-found"){_pending=null;Message=ex.Message;}
        catch(Exception ex){Message=ex.Message+" Az eredmény bizonytalan. Az Újraellenőrzés gomb ugyanazzal a műveletazonosítóval folytatja.";}
        finally{IsBusy=false;Notify();}
    }
}
