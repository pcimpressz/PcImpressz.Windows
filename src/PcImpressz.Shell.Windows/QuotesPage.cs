using Microsoft.Win32;
using System.IO;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using PcImpressz.Contracts;
namespace PcImpressz.Shell.Windows;

internal sealed class QuoteEditLine:Observable
{
    private readonly Action _dirty;public Guid ProductId{get;set;}private string _name="",_quantity="1",_price="0",_vat="27";
    public QuoteEditLine(Action dirty)=>_dirty=dirty;
    public string Name{get=>_name;set{_name=value;Changed();_dirty();}}
    public string Quantity{get=>_quantity;set{_quantity=value;Changed();_dirty();}}
    public string Price{get=>_price;set{_price=value;Changed();_dirty();}}
    public string Vat{get=>_vat;set{_vat=value;Changed();_dirty();}}
}
internal sealed record QuoteReference(Guid Id,string Name,string Detail,JsonElement Data) { public override string ToString()=>Name+ (string.IsNullOrWhiteSpace(Detail)?"":" · "+Detail); }
internal sealed record QuoteListItem(Guid Id,long Version,string Number,string CustomerName,string Status,decimal Gross,JsonElement Data);
internal sealed class QuotesPageViewModel:Observable,IWorkspacePage
{
    private readonly HostConnection _connection;private readonly Func<Guid?> _company;private readonly CancellationToken _shutdown;
    private Guid? _customerId;private QuoteReference? _selectedCustomer,_selectedProduct,_selectedWholesale;private Guid _id=Guid.NewGuid();private long _version;private bool _busy,_dirty,_filling,_partners,_catalog;private string _message="",_name="",_notes="",_until=DateTime.Today.AddDays(30).ToString("yyyy-MM-dd"),_productQuery="",_wholesaleQuery="";private JsonElement? _pending;
    public QuotesPageViewModel(EntityPageDescriptor definition,HostConnection connection,Func<Guid?> company,CancellationToken shutdown)
    {Definition=definition;_connection=connection;_company=company;_shutdown=shutdown;RefreshCommand=new RelayCommand(()=>_=LoadAsync());NewCommand=new RelayCommand(New);SaveCommand=new RelayCommand(()=>_=SaveAsync());AcceptCommand=new RelayCommand(()=>_=AcceptAsync());AddCommand=new RelayCommand(()=>{if(CanEdit){Lines.Add(new(Dirty));Dirty();}});RemoveCommand=new RelayCommand(()=>{if(CanEdit&&SelectedLine is {} line){Lines.Remove(line);Dirty();}});OpenCommand=new RelayCommand(Open);FindCustomersCommand=new RelayCommand(()=>_=FindCustomersAsync());FindProductsCommand=new RelayCommand(()=>_=FindProductsAsync());InsertProductCommand=new RelayCommand(InsertProduct);ImportCompMarketCommand=new RelayCommand(()=>_=ImportCompMarketAsync());SearchCompMarketCommand=new RelayCommand(()=>_=SearchCompMarketAsync());InsertWholesaleCommand=new RelayCommand(InsertWholesale);}
    public EntityPageDescriptor Definition{get;}
    public ObservableCollection<QuoteListItem> Rows{get;}=[];public ObservableCollection<QuoteEditLine> Lines{get;}=[];
    public ObservableCollection<QuoteReference> Customers{get;}=[];public ObservableCollection<QuoteReference> Products{get;}=[];public ObservableCollection<QuoteReference> Wholesale{get;}=[];
    public QuoteReference? SelectedCustomer{get=>_selectedCustomer;set{_selectedCustomer=value;Changed();if(value is not null){_customerId=value.Id;_filling=true;CustomerName=value.Name;_filling=false;}}}
    public QuoteReference? SelectedProduct{get=>_selectedProduct;set{_selectedProduct=value;Changed();}}
    public QuoteReference? SelectedWholesale{get=>_selectedWholesale;set{_selectedWholesale=value;Changed();}}
    public bool PartnersAvailable{get=>_partners;private set{_partners=value;Changed();Changed(nameof(ShowManualCustomer));}}
    public bool CatalogAvailable{get=>_catalog;private set{_catalog=value;Changed();}}
    public bool ShowManualCustomer=>!PartnersAvailable;
    public string ProductQuery{get=>_productQuery;set{_productQuery=value;Changed();}}
    public string WholesaleQuery{get=>_wholesaleQuery;set{_wholesaleQuery=value;Changed();}}
    public string CompMarketMargin{get;set;}="20";public ICommand ImportCompMarketCommand{get;}public ICommand SearchCompMarketCommand{get;}public ICommand InsertWholesaleCommand{get;}
    public ICommand FindCustomersCommand{get;}public ICommand FindProductsCommand{get;}public ICommand InsertProductCommand{get;}
    public QuoteListItem? Selected{get;set;}public QuoteEditLine? SelectedLine{get;set;}public string Search{get;set;}="";
    public string CustomerName{get=>_name;set{if(_name==value)return;_name=value;if(!_filling&&(_selectedCustomer is null||_selectedCustomer.Name!=value)){_customerId=null;if(_selectedCustomer is not null){_selectedCustomer=null;Changed(nameof(SelectedCustomer));}}Changed();Dirty();}}
    public string Notes{get=>_notes;set{_notes=value;Changed();Dirty();}}
    public string ValidUntil{get=>_until;set{_until=value;Changed();Dirty();}}
    public string Message{get=>_message;private set{_message=value;Changed();}}
    public bool IsBusy{get=>_busy;private set{_busy=value;Changed();Changed(nameof(CanEdit));}}
    public bool IsDirty=>_dirty;public bool HasUnresolvedOperation=>_pending is not null;private bool _accepted;
    public bool CanEdit=>!IsBusy&&!HasUnresolvedOperation&&!_accepted&&_connection.Allows("quotes.write");
    public ICommand RefreshCommand{get;}public ICommand NewCommand{get;}public ICommand SaveCommand{get;}public ICommand AcceptCommand{get;}public ICommand AddCommand{get;}public ICommand RemoveCommand{get;}public ICommand OpenCommand{get;}
    private static string Str(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
    private async Task<JsonElement> Reference(string module,string command,object input)=>await _connection.SendAsync("module.execute",new ModuleCommandRequest(_company()??throw new InvalidOperationException("Nincs cég."),module,command,JsonSerializer.SerializeToElement(input,CompanyDocuments.Json)),_shutdown);
    public void IntegrationsChanged(Func<string,bool> available)
    {
        var partners=available("partners");var catalog=available("catalog");
        if(!partners){Customers.Clear();SelectedCustomer=null;_customerId=null;}
        if(!catalog){Products.Clear();SelectedProduct=null;foreach(var line in Lines)line.ProductId=Guid.Empty;}
        PartnersAvailable=partners;CatalogAvailable=catalog;
        if(partners)_=FindCustomersAsync();
    }
    private async Task FindCustomersAsync(){if(!PartnersAvailable){Message="A partnertörzs ki van kapcsolva. A vevő nevét kézzel írd be.";return;}Customers.Clear();if(_company() is not {} company)return;var keep=_customerId;try{var response=await Reference("partners","list",new{companyId=company,search=CustomerName,filter="all",page=0,pageSize=50});foreach(var p in response.GetProperty("rows").EnumerateArray())if(!p.TryGetProperty("isActive",out var active)||active.ValueKind!=JsonValueKind.False)Customers.Add(new(p.GetProperty("id").GetGuid(),Str(p,"name"),Str(p,"taxId"),p.Clone()));SelectedCustomer=Customers.FirstOrDefault(c=>c.Id==keep);Message=Customers.Count==0?"Nincs ilyen partner a törzsben. A nevet kézzel is beírhatod.":$"{Customers.Count} partner található.";}catch(Exception ex){Customers.Clear();_customerId=null;Message="Partnerlista betöltési hiba: "+ex.Message+" (kézi vevő továbbra is megadható)";}}
    private async Task FindProductsAsync(){if(!CatalogAvailable){Message="A cikktörzs ki van kapcsolva. A tételt kézzel írd be.";return;}Products.Clear();SelectedProduct=null;if(_company() is not {} company)return;try{var response=await Reference("catalog","products-list",new{companyId=company,search=ProductQuery,filter="salable",page=0,pageSize=50});foreach(var p in response.GetProperty("rows").EnumerateArray())if(!p.TryGetProperty("isActive",out var active)||active.ValueKind!=JsonValueKind.False)Products.Add(new(p.GetProperty("id").GetGuid(),Str(p,"name"),Str(p,"sku"),p.Clone()));Message=Products.Count==0?"Nincs ilyen cikk. A megnevezést kézzel is felviheted.":$"{Products.Count} cikk található.";}catch(Exception ex){Products.Clear();Message="Cikklista betöltési hiba: "+ex.Message+" (kézi tétel továbbra is felvihető)";}}
    private async Task ImportCompMarketAsync()
    {
        if(!CanEdit)return;
        var picker=new OpenFileDialog{Title="CompMarket XML pillanatkép",Filter="XML fájlok (*.xml)|*.xml"};
        if(picker.ShowDialog()!=true)return;
        try
        {
            var info=new FileInfo(picker.FileName);
            if(info.Length>4_000_000)throw new ArgumentException("Az XML túl nagy. Szűrt CompMarket XML szükséges (maximum 4 MB).");
            var margin=Number(CompMarketMargin);
            var xml=await File.ReadAllTextAsync(picker.FileName,_shutdown);
            var result=await Send("compmarket-xml-preview",new{xml,marginPercent=margin,vatRate=27m});
            var count=0;
            foreach(var item in result.EnumerateArray())
            {
                if(count++>=200)break;
                Lines.Add(new(Dirty){Name=Str(item,"name"),Quantity="1",
                    Price=item.GetProperty("saleNet").GetDecimal().ToString(CultureInfo.InvariantCulture),Vat="27"});
            }
            Dirty();Message=$"CompMarket fájl: {Math.Min(count,200)} tétel. Az ár a fájl letöltésekor volt érvényes.";
        }
        catch(Exception ex){Message="CompMarket import: "+ex.Message;}
    }
    private async Task SearchCompMarketAsync()
    {
        if(!CanEdit)return;
        Wholesale.Clear();SelectedWholesale=null;
        try
        {
            IsBusy=true;Message="CompMarket készletlista lekérése a VPS-proxyn (217.113.49.52). Közvetlen kapcsolat nem indul.";
            var result=await Send("compmarket-search",new{query=WholesaleQuery.Trim(),marginPercent=Number(CompMarketMargin),vatRate=27m});
            foreach(var item in result.EnumerateArray())Wholesale.Add(new(Guid.Empty,Str(item,"name"),Str(item,"supplierSku")+" · "+Str(item,"availability"),item.Clone()));
            Message=Wholesale.Count==0?"Nincs CompMarket-találat.":$"{Wholesale.Count} CompMarket-tétel. Az ár a készletlistából származik, felárral.";
        }
        catch(Exception ex){Message="CompMarket: "+ex.Message;}
        finally{IsBusy=false;}
    }
    private void InsertWholesale(){if(!CanEdit||SelectedWholesale is not {} item)return;var price=item.Data.TryGetProperty("saleNet",out var net)&&net.ValueKind==JsonValueKind.Number?net.GetDecimal():0m;var name=string.IsNullOrWhiteSpace(item.Detail)?item.Name:(item.Detail.Split('·')[0].Trim()+" "+item.Name).Trim();if(name.Length>300)name=name[..300];Lines.Add(new(Dirty){Name=name,Quantity="1",Price=price.ToString(CultureInfo.InvariantCulture),Vat="27"});Dirty();}
    private void InsertProduct(){if(!CanEdit||SelectedProduct is not {} p)return;var d=p.Data;var price=d.TryGetProperty("saleNetUnitPrice",out var v)&&v.ValueKind==JsonValueKind.Number?v.GetDecimal():0m;var vat=d.TryGetProperty("saleVatRate",out var a)&&a.ValueKind==JsonValueKind.Number?a.GetDecimal():27m;Lines.Add(new(Dirty){ProductId=p.Id,Name=p.Name,Quantity="1",Price=price.ToString(CultureInfo.InvariantCulture),Vat=vat.ToString(CultureInfo.InvariantCulture)});Dirty();}
    private void Dirty(){if(!_filling){_dirty=true;Changed(nameof(IsDirty));}}
    public void PermissionsChanged()=>Changed(nameof(CanEdit));
    public void CompanyChanged(){_filling=true;_id=Guid.NewGuid();_version=0;Rows.Clear();Lines.Clear();_dirty=false;_accepted=false;_pending=null;CustomerName="";_customerId=null;Customers.Clear();Products.Clear();Notes="";_dirty=false;_filling=false;Changed(nameof(IsDirty));_=LoadAsync();}
    private async Task<JsonElement> Send(string command,object input)=>await _connection.SendAsync("module.execute",new ModuleCommandRequest(_company()??throw new InvalidOperationException("Jelentkezz be."),"quotes",command,JsonSerializer.SerializeToElement(input,CompanyDocuments.Json)),_shutdown);
    internal async Task LoadAsync()
    {
        if(IsBusy||_company() is null)return;IsBusy=true;
        try{var result=await Send("list",new{search=Search});Rows.Clear();foreach(var row in result.EnumerateArray())Rows.Add(new(row.GetProperty("id").GetGuid(),row.GetProperty("version").GetInt64(),row.GetProperty("number").GetString()??"",row.TryGetProperty("customer",out var customer)&&customer.ValueKind==JsonValueKind.Object?customer.GetProperty("name").GetString()??"":"",row.GetProperty("status").GetString()=="accepted"?"Elfogadva":"Piszkozat",row.GetProperty("gross").GetDecimal(),row.Clone()));}
        catch(Exception ex){Message="Árajánlat-lista betöltési hiba: "+ex.Message;}
        try{await RefreshAvailabilityAsync();}
        catch(Exception ex){Message=ex.Message;}
        finally{IsBusy=false;}
    }
    private async Task RefreshAvailabilityAsync()
    {
        PartnersAvailable=await ModuleOn("partners","list",new{search="",filter="all",page=0,pageSize=1});
        CatalogAvailable=await ModuleOn("catalog","products-list",new{search="",filter="salable",page=0,pageSize=1});
        if(PartnersAvailable)await FindCustomersAsync();
    }
    private async Task<bool> ModuleOn(string module,string command,object input)
    {
        try{await Reference(module,command,input);return true;}
        catch(Exception ex) when(ex.Message.Contains("ki van kapcsolva",StringComparison.Ordinal)||ex.Message.Contains("nem érhető el",StringComparison.Ordinal)){return false;}
        catch(Exception ex){Message=ex.Message;return false;}
    }
    private bool CanLeave()=>!IsDirty||UnsavedChanges.Confirm("Elveted a nem mentett ajánlatot?","Árajánlat");
    private void New(){if(IsBusy||HasUnresolvedOperation||!CanLeave())return;_id=Guid.NewGuid();_version=0;_accepted=false;_filling=true;CustomerName="";_customerId=null;SelectedCustomer=null;Notes="";Lines.Clear();_filling=false;_dirty=false;Changed(nameof(IsDirty));Changed(nameof(CanEdit));}
    private void Open(){if(IsBusy||HasUnresolvedOperation||Selected is not {} selected||!CanLeave())return;Apply(selected.Data);}
    private void Apply(JsonElement row)
    {_filling=true;_id=row.GetProperty("id").GetGuid();_version=row.GetProperty("version").GetInt64();_accepted=row.GetProperty("status").GetString()=="accepted";ValidUntil=row.GetProperty("validUntil").GetString()??"";Notes=row.GetProperty("notes").GetString()??"";_customerId=row.TryGetProperty("customerId",out var cid)&&cid.ValueKind==JsonValueKind.String?cid.GetGuid():null;CustomerName=row.TryGetProperty("customer",out var customer)&&customer.ValueKind==JsonValueKind.Object?customer.GetProperty("name").GetString()??"":"";Lines.Clear();foreach(var line in row.GetProperty("lines").EnumerateArray())Lines.Add(new(Dirty){ProductId=line.TryGetProperty("productId",out var pid)&&pid.ValueKind==JsonValueKind.String?pid.GetGuid():Guid.Empty,Name=line.GetProperty("description").GetString()??"",Quantity=line.GetProperty("quantity").GetDecimal().ToString(CultureInfo.InvariantCulture),Price=line.GetProperty("netUnitPrice").GetDecimal().ToString(CultureInfo.InvariantCulture),Vat=line.GetProperty("vatRate").GetDecimal().ToString(CultureInfo.InvariantCulture)});_filling=false;_dirty=false;Changed(nameof(IsDirty));Changed(nameof(CanEdit));}
    private static decimal Number(string s)=>decimal.Parse(s.Replace(',','.'),NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture);
    internal async Task SaveAsync()
    {
        if(IsBusy||_company() is not {} company)return;
        try
        {
            if(_pending is null){if(!CanEdit)return;if(!DateOnly.TryParseExact(ValidUntil,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var until))throw new ArgumentException("Érvényesség: ÉÉÉÉ-HH-NN.");var customerId=PartnersAvailable?_customerId:null;_pending=JsonSerializer.SerializeToElement(new{companyId=company,requestId=Guid.NewGuid(),id=_id,version=_version,customerId,customer=string.IsNullOrWhiteSpace(CustomerName)?null:new BillingCustomer(Guid.Empty,CustomerName.Trim(),"","HU","","","",true,true),validUntil=until,notes=Notes,lines=Lines.Select(l=>new{productId=CatalogAvailable?l.ProductId:Guid.Empty,description=l.Name,quantity=Number(l.Quantity),netUnitPrice=Number(l.Price),vatRate=Number(l.Vat)}).ToArray()},CompanyDocuments.Json);Changed(nameof(HasUnresolvedOperation));}
            IsBusy=true;var result=await Send("save",_pending.Value);_pending=null;Apply(result);Message="Az árajánlat mentve.";
        }
        catch(HostCommandException ex) when(ex.Code is "validation" or "denied" or "not-found"){_pending=null;Message=ex.Message;}
        catch(Exception ex){Message=ex.Message;}
        finally{IsBusy=false;Changed(nameof(HasUnresolvedOperation));}
        if(_pending is null)await LoadAsync();
    }
    private async Task AcceptAsync()
    {if(IsBusy||HasUnresolvedOperation||IsDirty||_version==0||!CanEdit)return;IsBusy=true;try{Apply(await Send("accept",new{requestId=Guid.NewGuid(),id=_id,version=_version}));Message="Az árajánlat elfogadva.";}catch(Exception ex){Message=ex.Message;}finally{IsBusy=false;}await LoadAsync();}
}
internal sealed class QuotesPageView:UserControl
{
    public QuotesPageView()
    {
        var root=new StackPanel{Margin=new Thickness(12)};var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=root};var buttons=new WrapPanel();foreach(var (label,command) in new[]{("Frissítés","RefreshCommand"),("Új ajánlat","NewCommand"),("Megnyitás","OpenCommand")})buttons.Children.Add(Button(label,command));root.Children.Add(buttons);
        var grid=new DataGrid{AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,Height=230,Margin=new Thickness(0,10,0,16)};grid.SetBinding(ItemsControl.ItemsSourceProperty,"Rows");grid.SetBinding(DataGrid.SelectedItemProperty,new Binding("Selected"){Mode=BindingMode.TwoWay});foreach(var (label,path) in new[]{("Ajánlat","Number"),("Vevő","CustomerName"),("Állapot","Status"),("Bruttó","Gross")})grid.Columns.Add(new DataGridTextColumn{Header=label,Binding=new Binding(path),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});root.Children.Add(grid);
        var editor=new StackPanel();root.Children.Add(editor);
        foreach(var (label,path) in new[]{("Vevő neve","CustomerName"),("Érvényesség · ÉÉÉÉ-HH-NN","ValidUntil"),("Megjegyzés","Notes")}){editor.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,8,0,4)});var input=new TextBox{MinHeight=34};input.SetBinding(TextBox.TextProperty,new Binding(path){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});editor.Children.Add(input);}
        var manualHint=new TextBlock{Text="A partnertörzs ki van kapcsolva. A vevő neve csak ezen az ajánlaton marad meg.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,0)};manualHint.SetBinding(UIElement.VisibilityProperty,new Binding("ShowManualCustomer"){Converter=Flag.Instance});editor.Children.Add(manualHint);
        var partners=new StackPanel();partners.SetBinding(UIElement.VisibilityProperty,new Binding("PartnersAvailable"){Converter=Flag.Instance});
        partners.Children.Add(new TextBlock{Text="Partnertörzs",Margin=new Thickness(0,8,0,4)});partners.Children.Add(Button("Partnerek keresése","FindCustomersCommand"));var customerPicker=new ComboBox{MinHeight=32,DisplayMemberPath="Name"};customerPicker.SetBinding(ItemsControl.ItemsSourceProperty,"Customers");customerPicker.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,new Binding("SelectedCustomer"){Mode=BindingMode.TwoWay});partners.Children.Add(customerPicker);editor.Children.Add(partners);
        var catalog=new StackPanel();catalog.SetBinding(UIElement.VisibilityProperty,new Binding("CatalogAvailable"){Converter=Flag.Instance});
        catalog.Children.Add(new TextBlock{Text="Cikktörzs",Margin=new Thickness(0,8,0,4)});var productQuery=new TextBox{MinHeight=32};productQuery.SetBinding(TextBox.TextProperty,new Binding("ProductQuery"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});catalog.Children.Add(productQuery);catalog.Children.Add(Button("Cikkek keresése","FindProductsCommand"));var productPicker=new ComboBox{MinHeight=32};productPicker.SetBinding(ItemsControl.ItemsSourceProperty,"Products");productPicker.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,new Binding("SelectedProduct"){Mode=BindingMode.TwoWay});catalog.Children.Add(productPicker);catalog.Children.Add(Button("Kiválasztott cikk beszúrása","InsertProductCommand"));editor.Children.Add(catalog);
        editor.Children.Add(new TextBlock{Text="CompMarket nagyker. A lekérdezés csak a Beállításokban megadott VPS-proxyn (217.113.49.52) megy ki.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,4)});
        var cmQuery=new TextBox{MinHeight=32};cmQuery.SetBinding(TextBox.TextProperty,new Binding("WholesaleQuery"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});editor.Children.Add(cmQuery);
        var cmMargin=new TextBox{MinHeight=30,ToolTip="Beszerzési nettó árra számított százalékos felár"};
        cmMargin.SetBinding(TextBox.TextProperty,new Binding("CompMarketMargin"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
        editor.Children.Add(new TextBlock{Text="Felár % (például 20)",Margin=new Thickness(0,4,0,2)});
        editor.Children.Add(cmMargin);editor.Children.Add(Button("Keresés a CompMarketnél","SearchCompMarketCommand"));var wholesalePicker=new ComboBox{MinHeight=32};wholesalePicker.SetBinding(ItemsControl.ItemsSourceProperty,"Wholesale");wholesalePicker.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,new Binding("SelectedWholesale"){Mode=BindingMode.TwoWay});editor.Children.Add(wholesalePicker);editor.Children.Add(Button("Kiválasztott nagykeres tétel beszúrása","InsertWholesaleCommand"));editor.Children.Add(Button("XML fájl betöltése","ImportCompMarketCommand"));
        var rowButtons=new WrapPanel();rowButtons.Children.Add(Button("Kézi tétel hozzáadása","AddCommand"));rowButtons.Children.Add(Button("Kijelölt törlése","RemoveCommand"));editor.Children.Add(rowButtons);
        var lines=new DataGrid{AutoGenerateColumns=false,CanUserAddRows=false,Height=230};lines.SetBinding(ItemsControl.ItemsSourceProperty,"Lines");lines.SetBinding(DataGrid.SelectedItemProperty,new Binding("SelectedLine"){Mode=BindingMode.TwoWay});foreach(var (label,path) in new[]{("Megnevezés","Name"),("Mennyiség","Quantity"),("Nettó egységár","Price"),("Áfa %","Vat")})lines.Columns.Add(new DataGridTextColumn{Header=label,Binding=new Binding(path){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=new DataGridLength(1,DataGridLengthUnitType.Star)});editor.Children.Add(lines);
        var actions=new WrapPanel();actions.Children.Add(Button("Mentés / újraellenőrzés","SaveCommand"));actions.Children.Add(Button("Elfogadás","AcceptCommand"));root.Children.Add(actions);var message=new TextBlock{TextWrapping=TextWrapping.Wrap};message.SetBinding(TextBlock.TextProperty,"Message");root.Children.Add(message);Content=scroll;
    }
    private static Button Button(string text,string command){var b=new Button{Content=text,Margin=new Thickness(0,8,8,8)};b.SetBinding(System.Windows.Controls.Button.CommandProperty,command);return b;}
    private sealed class Flag:IValueConverter
    {
        public static Flag Instance{get;}=new();
        public object Convert(object? value,Type targetType,object? parameter,CultureInfo culture)=>value is true?Visibility.Visible:Visibility.Collapsed;
        public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture)=>throw new NotSupportedException();
    }
}
