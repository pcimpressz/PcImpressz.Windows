using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
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
internal sealed class OfferCandidate
{
    private static readonly CultureInfo Hu=CultureInfo.GetCultureInfo("hu-HU");
    public static SolidColorBrush OwnStripe{get;}=Freeze(Color.FromRgb(0x1F,0x8A,0x5B));
    public static SolidColorBrush CompMarketStripe{get;}=Freeze(Color.FromRgb(0xD9,0x77,0x06));
    private static SolidColorBrush Freeze(Color color){var brush=new SolidColorBrush(color);brush.Freeze();return brush;}
    public OfferCandidate(string sourceId,string sourceLabel,Brush stripe,Guid productId,string name,string sku,string stock,decimal price,decimal vat,string lineName)
    {SourceId=sourceId;SourceLabel=sourceLabel;Stripe=stripe;ProductId=productId;Name=name;Sku=sku;Stock=stock;Price=price;Vat=vat;LineName=lineName;}
    public string SourceId{get;}
    public string SourceLabel{get;}
    public Brush Stripe{get;}
    public Guid ProductId{get;}
    public string Name{get;}
    public string Sku{get;}
    public string Stock{get;}
    public decimal Price{get;}
    public decimal Vat{get;}
    public string LineName{get;}
    public string Summary=>string.Join(" · ",new[]{Sku,Stock,Price.ToString("N2",Hu)+" Ft nettó",SourceLabel}.Where(part=>!string.IsNullOrWhiteSpace(part)));
}
internal sealed record QuoteListItem(Guid Id,long Version,string Number,string CustomerName,string Status,decimal Gross,JsonElement Data);
internal sealed class QuotesPageViewModel:Observable,IWorkspacePage
{
    private readonly HostConnection _connection;private readonly Func<Guid?> _company;private readonly CancellationToken _shutdown;
    private Guid? _customerId;private Guid _id=Guid.NewGuid();private long _version;private bool _busy,_dirty,_filling,_partners,_catalog,_accepted,_own=true,_compMarket=true,_availableOnly=true,_compMarketBlocked;
    private string _message="",_name="",_notes="",_until=DateTime.Today.AddDays(30).ToString("yyyy-MM-dd"),_offerQuery="",_margin="20",_compMarketHint="";
    private JsonElement? _pending;private CancellationTokenSource? _customerCts,_offerCts;private int _customerGeneration,_offerGeneration;private bool _offerOnNextLoad;
    public QuotesPageViewModel(EntityPageDescriptor definition,HostConnection connection,Func<Guid?> company,CancellationToken shutdown)
    {
        Definition=definition;_connection=connection;_company=company;_shutdown=shutdown;
        Customers.CollectionChanged+=(_,_)=>Changed(nameof(ShowCustomerSuggestions));
        RefreshCommand=new RelayCommand(()=>{_offerOnNextLoad=true;_=LoadAsync();});NewCommand=new RelayCommand(New);SaveCommand=new RelayCommand(()=>_=SaveAsync());AcceptCommand=new RelayCommand(()=>_=AcceptAsync());
        AddCommand=new RelayCommand(()=>{if(CanEdit){Lines.Add(new(Dirty));Dirty();}});RemoveCommand=new RelayCommand(()=>{if(CanEdit&&SelectedLine is {} line){Lines.Remove(line);Dirty();}});
        OpenCommand=new RelayCommand(Open);ImportCompMarketCommand=new RelayCommand(()=>_=ImportCompMarketAsync());InsertOfferCommand=new RelayCommand(InsertSelectedOffer);
    }
    public EntityPageDescriptor Definition{get;}
    public ObservableCollection<QuoteListItem> Rows{get;}=[];public ObservableCollection<QuoteEditLine> Lines{get;}=[];
    public ObservableCollection<QuoteReference> Customers{get;}=[];public ObservableCollection<OfferCandidate> Offers{get;}=[];
    public OfferCandidate? SelectedOffer{get;set;}
    public bool PartnersAvailable{get=>_partners;private set{if(_partners==value)return;_partners=value;Changed();Changed(nameof(ShowManualCustomer));Changed(nameof(ShowCustomerSuggestions));}}
    public bool CatalogAvailable{get=>_catalog;private set{if(_catalog==value)return;_catalog=value;Changed();}}
    public bool ShowManualCustomer=>!PartnersAvailable;
    public bool ShowCustomerSuggestions=>PartnersAvailable&&Customers.Count>0;
    public bool IncludeOwn{get=>_own;set{if(_own==value)return;_own=value;Changed();ScheduleOfferSearch();}}
    public bool IncludeCompMarket{get=>_compMarket;set{if(_compMarket==value)return;if(value)_compMarketBlocked=false;_compMarket=value;Changed();ScheduleOfferSearch();}}
    public bool AvailableOnly{get=>_availableOnly;set{if(_availableOnly==value)return;_availableOnly=value;Changed();ScheduleOfferSearch();}}
    public string OfferQuery{get=>_offerQuery;set{if(_offerQuery==value)return;_offerQuery=value;Changed();ScheduleOfferSearch();}}
    public string CompMarketMargin{get=>_margin;set{if(_margin==value)return;_margin=value;Changed();ScheduleOfferSearch();}}
    public ICommand ImportCompMarketCommand{get;}public ICommand InsertOfferCommand{get;}
    public QuoteListItem? Selected{get;set;}public QuoteEditLine? SelectedLine{get;set;}public string Search{get;set;}="";
    public string CustomerName{get=>_name;set{if(_name==value)return;_name=value;if(!_filling)_customerId=null;Changed();Dirty();ScheduleCustomerSearch();}}
    public string Notes{get=>_notes;set{_notes=value;Changed();Dirty();}}
    public string ValidUntil{get=>_until;set{_until=value;Changed();Dirty();}}
    public string Message{get=>_message;private set{_message=value;Changed();}}
    public bool IsBusy{get=>_busy;private set{_busy=value;Changed();Changed(nameof(CanEdit));}}
    public bool IsDirty=>_dirty;public bool HasUnresolvedOperation=>_pending is not null;
    public bool CanEdit=>!IsBusy&&!HasUnresolvedOperation&&!_accepted&&_connection.Allows("quotes.write");
    public ICommand RefreshCommand{get;}public ICommand NewCommand{get;}public ICommand SaveCommand{get;}public ICommand AcceptCommand{get;}public ICommand AddCommand{get;}public ICommand RemoveCommand{get;}public ICommand OpenCommand{get;}
    private static string Str(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
    private static bool Unavailable(Exception ex)=>ex.Message.Contains("ki van kapcsolva",StringComparison.Ordinal)||ex.Message.Contains("nem érhető el",StringComparison.Ordinal);
    private static bool BlocksCompMarket(Exception ex)=>ex.Message.Contains("Nincs CompMarket-kapcsolat",StringComparison.Ordinal)||ex.Message.Contains("ki van kapcsolva a beállításokban",StringComparison.Ordinal)||ex.Message.Contains("Nincs VPS-proxy",StringComparison.Ordinal);
    private static string? JoinNote(string? left,string? right)=>string.IsNullOrWhiteSpace(left)?(string.IsNullOrWhiteSpace(right)?null:right):string.IsNullOrWhiteSpace(right)?left:left.TrimEnd('.')+". "+right;
    private static string TrimName(string name)=>name.Length<=300?name:name[..300];
    private async Task<JsonElement> Module(string module,string command,object input,CancellationToken ct)=>await _connection.SendAsync("module.execute",new ModuleCommandRequest(_company()??throw new InvalidOperationException("Nincs cég."),module,command,JsonSerializer.SerializeToElement(input,CompanyDocuments.Json)),ct);
    public void IntegrationsChanged(Func<string,bool> available)
    {
        var partners=available("partners");var catalog=available("catalog");
        if(!partners){CancelCustomerSearch();Customers.Clear();_customerId=null;}
        if(!catalog)foreach(var line in Lines)line.ProductId=Guid.Empty;
        PartnersAvailable=partners;CatalogAvailable=catalog;
        if(partners&&!_filling&&CustomerName.Trim().Length>0)ScheduleCustomerSearch();
        if(!_filling)ScheduleOfferSearch();
    }
    public void PickCustomer(QuoteReference partner)
    {
        CancelCustomerSearch();_filling=true;_customerId=partner.Id;_name=partner.Name;Changed(nameof(CustomerName));Customers.Clear();_filling=false;_dirty=true;Changed(nameof(IsDirty));
    }
    private void ScheduleCustomerSearch()
    {
        CancelCustomerSearch();if(_filling||!PartnersAvailable)return;var text=CustomerName.Trim();
        if(text.Length==0){Customers.Clear();return;}if(_company() is not Guid company)return;
        var cts=CancellationTokenSource.CreateLinkedTokenSource(_shutdown);_customerCts=cts;_=RunCustomerSearch(text,company,_customerGeneration,cts.Token);
    }
    private void CancelCustomerSearch(){_customerGeneration++;_customerCts?.Cancel();_customerCts?.Dispose();_customerCts=null;}
    private async Task RunCustomerSearch(string text,Guid company,int generation,CancellationToken ct)
    {
        try
        {
            await Task.Delay(250,ct);if(generation!=_customerGeneration||!PartnersAvailable||_company()!=company||CustomerName.Trim()!=text)return;
            var response=await Module("partners","list",new{companyId=company,search=text.Length>200?text[..200]:text,filter="all",page=0,pageSize=8},ct);
            if(generation!=_customerGeneration||CustomerName.Trim()!=text)return;
            Customers.Clear();
            foreach(var p in response.GetProperty("rows").EnumerateArray())
                if(!p.TryGetProperty("isActive",out var active)||active.ValueKind!=JsonValueKind.False)
                    Customers.Add(new(p.GetProperty("id").GetGuid(),Str(p,"name"),Str(p,"taxId"),p.Clone()));
        }
        catch(Exception ex) when(ex is OperationCanceledException or ObjectDisposedException){}
        catch(Exception ex) when(Unavailable(ex)){if(generation!=_customerGeneration)return;PartnersAvailable=false;Customers.Clear();_customerId=null;}
        catch(Exception ex){if(generation==_customerGeneration)Message="Partnerkeresés: "+ex.Message;}
    }
    private void ScheduleOfferSearch()
    {
        CancelOfferSearch();if(_filling||_company() is not Guid company)return;
        var cts=CancellationTokenSource.CreateLinkedTokenSource(_shutdown);_offerCts=cts;_=RunOfferSearch(OfferQuery,company,_offerGeneration,cts.Token);
    }
    private void CancelOfferSearch(){_offerGeneration++;_offerCts?.Cancel();_offerCts?.Dispose();_offerCts=null;}
    private async Task RunOfferSearch(string query,Guid company,int generation,CancellationToken ct)
    {
        try
        {
            await Task.Delay(320,ct);if(Stale(generation,company,query))return;
            var text=query.Trim();if(text.Length>200)text=text[..200];
            var ownOn=CatalogAvailable&&IncludeOwn;
            if(!ownOn&&!IncludeCompMarket){Offers.Clear();Message=CatalogAvailable?"Kapcsold be legalább egy forrást a listához.":"A cikktörzs ki van kapcsolva. CompMarket-tételhez kapcsold be a CompMarket jelölőt, és írj legalább 2 karaktert.";return;}
            var items=new List<OfferCandidate>();var ownCount=0;var cmCount=0;string? note=null;var hiddenUnproven=0;
            if(ownOn)
            {
                try
                {
                    var response=await Module("catalog","products-list",new{companyId=company,search=text,filter=AvailableOnly?"in-stock":"salable",page=0,pageSize=40},ct);
                    if(Stale(generation,company,query))return;
                    foreach(var p in response.GetProperty("rows").EnumerateArray())
                    {
                        if(p.TryGetProperty("isActive",out var active)&&active.ValueKind==JsonValueKind.False)continue;
                        decimal? stock=p.TryGetProperty("quantity",out var qty)&&qty.ValueKind==JsonValueKind.Number?qty.GetDecimal():null;
                        if(AvailableOnly&&stock is not >0){if(stock is null)hiddenUnproven++;continue;}
                        var name=Str(p,"name");var price=NumberOr(p,"saleNetUnitPrice",0m);var vat=NumberOr(p,"saleVatRate",27m);
                        items.Add(new("own","Saját készlet",OfferCandidate.OwnStripe,p.GetProperty("id").GetGuid(),name,Str(p,"sku"),stock is null?"készlet nélkül":stock.Value.ToString("0.##",CultureInfo.GetCultureInfo("hu-HU"))+" raktáron",price,vat,TrimName(name)));
                        ownCount++;
                    }
                    if(AvailableOnly&&ownCount==0&&hiddenUnproven>0)note="A saját készlet most nem ellenőrizhető, ezért a csak elérhető szűrő nem mutatja.";
                }
                catch(Exception ex) when(ex is OperationCanceledException or ObjectDisposedException){return;}
                catch(Exception ex) when(Unavailable(ex)){CatalogAvailable=false;ownOn=false;}
                catch(Exception ex){note="Cikktörzs: "+ex.Message;}
            }
            if(Stale(generation,company,query))return;
            if(IncludeCompMarket)
            {
                if(text.Length<2){if(text.Length>0||!ownOn)note=JoinNote(note,"A CompMarkethez legalább 2 karakter kell.");}
                else if(_compMarketBlocked)note=JoinNote(note,_compMarketHint);
                else if(!TryMargin(out var margin))note=JoinNote(note,"A felár 0 és 1000 közötti szám legyen.");
                else
                {
                    try
                    {
                        var result=await Module("quotes","compmarket-search",new{query=text,marginPercent=margin,vatRate=27m},ct);
                        if(Stale(generation,company,query))return;
                        foreach(var item in result.EnumerateArray())
                        {
                            var availability=Str(item,"availability");
                            if(AvailableOnly&&availability.Contains("nincs",StringComparison.OrdinalIgnoreCase))continue;
                            var sku=Str(item,"supplierSku");var name=Str(item,"name");
                            items.Add(new("compmarket","CompMarket",OfferCandidate.CompMarketStripe,Guid.Empty,name,sku,availability,NumberOr(item,"saleNet",0m),NumberOr(item,"vatRate",27m),TrimName((sku+" "+name).Trim())));
                            cmCount++;
                        }
                    }
                    catch(Exception ex) when(ex is OperationCanceledException or ObjectDisposedException){return;}
                    catch(Exception ex) when(BlocksCompMarket(ex)){_compMarketBlocked=true;_compMarketHint="A CompMarket nincs bekötve. A Beállítások kártyáján add meg a VPS-proxyt és a belépést.";note=JoinNote(note,_compMarketHint);}
                    catch(Exception ex){note=JoinNote(note,"CompMarket: "+ex.Message);}
                }
            }
            if(Stale(generation,company,query))return;
            items.Sort((a,b)=>string.Compare(a.Name,b.Name,StringComparison.CurrentCultureIgnoreCase));
            Offers.Clear();foreach(var item in items)Offers.Add(item);
            Message=Offers.Count==0?JoinNote("Nincs ilyen tétel a bekapcsolt forrásokban.",note)??"Nincs ilyen tétel a bekapcsolt forrásokban.":JoinNote($"{Offers.Count} tétel: {ownCount} saját, {cmCount} CompMarket.",note)!;
        }
        catch(Exception ex) when(ex is OperationCanceledException or ObjectDisposedException){}
    }
    private bool Stale(int generation,Guid company,string query)=>generation!=_offerGeneration||_company()!=company||OfferQuery!=query;
    private static decimal NumberOr(JsonElement element,string key,decimal fallback)=>element.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.Number?value.GetDecimal():fallback;
    private bool TryMargin(out decimal margin)=>decimal.TryParse(CompMarketMargin.Replace(',','.'),NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out margin)&&margin is >=0 and <=1000;
    public void InsertSelectedOffer()
    {
        if(!CanEdit||SelectedOffer is not {} item)return;
        Lines.Add(new(Dirty){ProductId=item.SourceId=="own"&&CatalogAvailable?item.ProductId:Guid.Empty,Name=item.LineName,Quantity="1",Price=item.Price.ToString(CultureInfo.InvariantCulture),Vat=item.Vat.ToString(CultureInfo.InvariantCulture)});
        Dirty();
    }
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
                Lines.Add(new(Dirty){Name=Str(item,"name"),Quantity="1",Price=item.GetProperty("saleNet").GetDecimal().ToString(CultureInfo.InvariantCulture),Vat="27"});
            }
            Dirty();Message=$"CompMarket fájl: {Math.Min(count,200)} tétel. Az ár a fájl letöltésekor volt érvényes.";
        }
        catch(Exception ex){Message="CompMarket import: "+ex.Message;}
    }
    private void Dirty(){if(!_filling){_dirty=true;Changed(nameof(IsDirty));}}
    public void PermissionsChanged()=>Changed(nameof(CanEdit));
    public void CompanyChanged()
    {
        CancelCustomerSearch();CancelOfferSearch();_compMarketBlocked=false;_compMarketHint="";
        _filling=true;_id=Guid.NewGuid();_version=0;Rows.Clear();Lines.Clear();Offers.Clear();Customers.Clear();_dirty=false;_accepted=false;_pending=null;_customerId=null;_name="";Changed(nameof(CustomerName));_offerQuery="";Changed(nameof(OfferQuery));Notes="";_dirty=false;_filling=false;Changed(nameof(IsDirty));Changed(nameof(CanEdit));_offerOnNextLoad=true;_=LoadAsync();
    }
    private async Task<JsonElement> Send(string command,object input)=>await Module("quotes",command,input,_shutdown);
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
        if(_offerOnNextLoad){_offerOnNextLoad=false;if(!_filling)ScheduleOfferSearch();}
    }
    private async Task<bool> ModuleOn(string module,string command,object input)
    {
        try{await Module(module,command,input,_shutdown);return true;}
        catch(Exception ex) when(Unavailable(ex)){return false;}
        catch(Exception ex){Message=ex.Message;return false;}
    }
    private bool CanLeave()=>!IsDirty||UnsavedChanges.Confirm("Elveted a nem mentett ajánlatot?","Árajánlat");
    private void New(){if(IsBusy||HasUnresolvedOperation||!CanLeave())return;_id=Guid.NewGuid();_version=0;_accepted=false;_filling=true;CustomerName="";_customerId=null;Customers.Clear();Notes="";Lines.Clear();_filling=false;_dirty=false;Changed(nameof(IsDirty));Changed(nameof(CanEdit));}
    private void Open(){if(IsBusy||HasUnresolvedOperation||Selected is not {} selected||!CanLeave())return;Apply(selected.Data);}
    private void Apply(JsonElement row)
    {_filling=true;_id=row.GetProperty("id").GetGuid();_version=row.GetProperty("version").GetInt64();_accepted=row.GetProperty("status").GetString()=="accepted";ValidUntil=row.GetProperty("validUntil").GetString()??"";Notes=row.GetProperty("notes").GetString()??"";_customerId=row.TryGetProperty("customerId",out var cid)&&cid.ValueKind==JsonValueKind.String?cid.GetGuid():null;CustomerName=row.TryGetProperty("customer",out var customer)&&customer.ValueKind==JsonValueKind.Object?customer.GetProperty("name").GetString()??"":"";Customers.Clear();Lines.Clear();foreach(var line in row.GetProperty("lines").EnumerateArray())Lines.Add(new(Dirty){ProductId=line.TryGetProperty("productId",out var pid)&&pid.ValueKind==JsonValueKind.String?pid.GetGuid():Guid.Empty,Name=line.GetProperty("description").GetString()??"",Quantity=line.GetProperty("quantity").GetDecimal().ToString(CultureInfo.InvariantCulture),Price=line.GetProperty("netUnitPrice").GetDecimal().ToString(CultureInfo.InvariantCulture),Vat=line.GetProperty("vatRate").GetDecimal().ToString(CultureInfo.InvariantCulture)});_filling=false;_dirty=false;Changed(nameof(IsDirty));Changed(nameof(CanEdit));}
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
        editor.Children.Add(Label("Vevő neve"));
        var customerBox=Input("CustomerName");customerBox.ToolTip="Ha a partnertörzs be van kapcsolva, gépelés közben szűri a partnereket.";editor.Children.Add(customerBox);
        var suggestions=List("Customers");suggestions.MaxHeight=160;suggestions.SetBinding(UIElement.VisibilityProperty,new Binding("ShowCustomerSuggestions"){Converter=Flag.Instance});
        suggestions.PreviewMouseLeftButtonUp+=(_,e)=>{if(DataContext is QuotesPageViewModel page&&ItemsControl.ContainerFromElement(suggestions,e.OriginalSource as DependencyObject) is ListBoxItem{DataContext:QuoteReference partner}){page.PickCustomer(partner);customerBox.Focus();e.Handled=true;}};
        suggestions.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter&&DataContext is QuotesPageViewModel page&&suggestions.SelectedItem is QuoteReference partner){page.PickCustomer(partner);customerBox.Focus();e.Handled=true;}};
        customerBox.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Down&&suggestions.Items.Count>0){suggestions.SelectedIndex=0;suggestions.Focus();e.Handled=true;}};
        editor.Children.Add(suggestions);
        var manualHint=new TextBlock{Text="A partnertörzs ki van kapcsolva. A vevő neve csak ezen az ajánlaton marad meg.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,0)};manualHint.SetResourceReference(StyleProperty,"Muted");manualHint.SetBinding(UIElement.VisibilityProperty,new Binding("ShowManualCustomer"){Converter=Flag.Instance});editor.Children.Add(manualHint);
        foreach(var (label,path) in new[]{("Érvényesség · ÉÉÉÉ-HH-NN","ValidUntil"),("Megjegyzés","Notes")}){editor.Children.Add(Label(label));editor.Children.Add(Input(path));}
        editor.Children.Add(Label("Tétel keresése"));
        var offerQuery=Input("OfferQuery");offerQuery.MaxLength=200;offerQuery.ToolTip="Név, cikkszám vagy vonalkód. A saját készlet és a bekötött nagykerek egy listában jelennek meg.";editor.Children.Add(offerQuery);
        var filters=new WrapPanel{Margin=new Thickness(0,4,0,0)};
        filters.Children.Add(Check("Saját készlet","IncludeOwn","CatalogAvailable"));filters.Children.Add(Check("CompMarket","IncludeCompMarket",null));filters.Children.Add(Check("Csak elérhető","AvailableOnly",null));editor.Children.Add(filters);
        editor.Children.Add(Label("Felár % a CompMarket beszerzési nettó árára"));
        var margin=Input("CompMarketMargin");margin.Width=120;margin.HorizontalAlignment=HorizontalAlignment.Left;editor.Children.Add(margin);
        var legend=new TextBlock{Text="Zöld csík: saját készlet. Borostyán csík: CompMarket. A később bekötött nagykerek ugyanebbe a listába kerülnek.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,4)};legend.SetResourceReference(StyleProperty,"Muted");editor.Children.Add(legend);
        var offers=List("Offers");offers.Height=240;offers.SetBinding(Selector.SelectedItemProperty,new Binding("SelectedOffer"){Mode=BindingMode.TwoWay});offers.ItemTemplate=OfferTemplate();
        offers.MouseDoubleClick+=(_,e)=>{if(DataContext is QuotesPageViewModel page&&ItemsControl.ContainerFromElement(offers,e.OriginalSource as DependencyObject) is ListBoxItem)page.InsertSelectedOffer();};
        offers.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter&&DataContext is QuotesPageViewModel page){page.InsertSelectedOffer();e.Handled=true;}};
        editor.Children.Add(offers);
        var rowButtons=new WrapPanel();rowButtons.Children.Add(Button("Kijelölt tétel beszúrása","InsertOfferCommand"));rowButtons.Children.Add(Button("XML fájl betöltése","ImportCompMarketCommand"));rowButtons.Children.Add(Button("Kézi tétel hozzáadása","AddCommand"));rowButtons.Children.Add(Button("Kijelölt törlése","RemoveCommand"));editor.Children.Add(rowButtons);
        var lines=new DataGrid{AutoGenerateColumns=false,CanUserAddRows=false,Height=230};lines.SetBinding(ItemsControl.ItemsSourceProperty,"Lines");lines.SetBinding(DataGrid.SelectedItemProperty,new Binding("SelectedLine"){Mode=BindingMode.TwoWay});foreach(var (label,path) in new[]{("Megnevezés","Name"),("Mennyiség","Quantity"),("Nettó egységár","Price"),("Áfa %","Vat")})lines.Columns.Add(new DataGridTextColumn{Header=label,Binding=new Binding(path){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=new DataGridLength(1,DataGridLengthUnitType.Star)});editor.Children.Add(lines);
        var actions=new WrapPanel();actions.Children.Add(Button("Mentés / újraellenőrzés","SaveCommand"));actions.Children.Add(Button("Elfogadás","AcceptCommand"));root.Children.Add(actions);var message=new TextBlock{TextWrapping=TextWrapping.Wrap};message.SetBinding(TextBlock.TextProperty,"Message");root.Children.Add(message);Content=scroll;
    }
    private static TextBlock Label(string text)=>new(){Text=text,Margin=new Thickness(0,8,0,4)};
    private static TextBox Input(string path){var input=new TextBox{MinHeight=34};input.SetBinding(TextBox.TextProperty,new Binding(path){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});return input;}
    private static ListBox List(string path){var list=new ListBox{Margin=new Thickness(0,6,0,0),HorizontalContentAlignment=HorizontalAlignment.Stretch};list.SetBinding(ItemsControl.ItemsSourceProperty,path);list.SetResourceReference(Control.BackgroundProperty,"SurfaceBrush");list.SetResourceReference(Control.ForegroundProperty,"TextBrush");list.SetResourceReference(Control.BorderBrushProperty,"StrokeBrush");return list;}
    private static CheckBox Check(string caption,string path,string? visible)
    {
        var box=new CheckBox{Content=caption,Margin=new Thickness(0,8,16,0),VerticalAlignment=VerticalAlignment.Center};box.SetBinding(ToggleButton.IsCheckedProperty,new Binding(path){Mode=BindingMode.TwoWay});
        if(visible is not null)box.SetBinding(UIElement.VisibilityProperty,new Binding(visible){Converter=Flag.Instance});return box;
    }
    private static DataTemplate OfferTemplate()
    {
        var template=new DataTemplate();var dock=new FrameworkElementFactory(typeof(DockPanel));dock.SetValue(FrameworkElement.MarginProperty,new Thickness(4,3,4,3));
        var stripe=new FrameworkElementFactory(typeof(Border));stripe.SetValue(FrameworkElement.WidthProperty,8d);stripe.SetValue(DockPanel.DockProperty,Dock.Left);stripe.SetValue(FrameworkElement.MarginProperty,new Thickness(0,2,10,2));stripe.SetBinding(Border.BackgroundProperty,new Binding("Stripe"));dock.AppendChild(stripe);
        var stack=new FrameworkElementFactory(typeof(StackPanel));var title=new FrameworkElementFactory(typeof(TextBlock));title.SetBinding(TextBlock.TextProperty,new Binding("Name"));title.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);title.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);stack.AppendChild(title);
        var sub=new FrameworkElementFactory(typeof(TextBlock));sub.SetBinding(TextBlock.TextProperty,new Binding("Summary"));sub.SetValue(TextBlock.OpacityProperty,0.8);sub.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);stack.AppendChild(sub);dock.AppendChild(stack);template.VisualTree=dock;return template;
    }
    private static Button Button(string text,string command){var b=new Button{Content=text,Margin=new Thickness(0,8,8,8)};b.SetBinding(System.Windows.Controls.Button.CommandProperty,command);return b;}
    private sealed class Flag:IValueConverter
    {
        public static Flag Instance{get;}=new();
        public object Convert(object? value,Type targetType,object? parameter,CultureInfo culture)=>value is true?Visibility.Visible:Visibility.Collapsed;
        public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture)=>throw new NotSupportedException();
    }
}
