using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
namespace PcImpressz.Shell.Windows;
public sealed record NavInvoiceRow(string Number,string SupplierName,string SupplierTaxNumber,string IssuedOn,string Currency,string Operation,string Net,int? BatchIndex);
internal sealed partial class ReceivingPageViewModel
{
    private SupplierOption? _navSupplier;
    public SupplierOption? SelectedNavSupplier{get=>_navSupplier;set{_navSupplier=value;Changed();}}
    public ICommand NewNavSupplierCommand=>new RelayCommand(()=>_=CreateSupplierAsync(true));
    private bool _navExpanded;
    private DateTime? _navFrom=DateTime.Today.AddDays(-30),_navTo=DateTime.Today;
    private DateTime? _navAppliedFrom,_navAppliedTo;private int _navPage,_navPages;private NavInvoiceRow? _navSelected;
    public DateTime? NavFrom{get=>_navFrom;set{if(IsBusy)return;_navFrom=value;ResetNavList();Changed();}}
    public DateTime? NavTo{get=>_navTo;set{if(IsBusy)return;_navTo=value;ResetNavList();Changed();}}
    public ObservableCollection<NavInvoiceRow> NavInvoices{get;}=[];
    public string NavPageLabel=>_navPage==0?"Válassz időszakot és kérd le a bejövő számlákat.":_navPage+" / "+Math.Max(_navPages,1)+". oldal · "+NavInvoices.Count+" számla";
    public NavInvoiceRow? SelectedNavInvoice{get=>_navSelected;set{_navSelected=value;Changed();if(value is not null){_populating=true;SelectedNavSupplier=Suppliers.FirstOrDefault(s=>Tax(s.TaxId)==Tax(value.SupplierTaxNumber));_populating=false;}}}
    public ICommand FetchNavCommand=>new RelayCommand(()=>_=FetchNavAsync(1));
    public ICommand NextNavCommand=>new RelayCommand(()=>{if(_navPage>0&&_navPage<_navPages)_=FetchNavAsync(_navPage+1);});
    public ICommand PreviousNavCommand=>new RelayCommand(()=>{if(_navPage>1)_=FetchNavAsync(_navPage-1);});
    public ICommand DownloadNavCommand=>new RelayCommand(()=>_=DownloadNavAsync());
    private static string Tax(string value)=>new string(value.Where(char.IsAsciiDigit).Take(8).ToArray());
    private void ResetNavList(){NavInvoices.Clear();_navPage=_navPages=0;_navAppliedFrom=_navAppliedTo=null;SelectedNavInvoice=null;SelectedNavSupplier=null;Changed(nameof(NavPageLabel));}
    internal async Task FetchNavAsync(int page)
    {
        if(IsBusy||_company() is not Guid company)return;IsBusy=true;
        try
        {
            var from=page==1?NavFrom:_navAppliedFrom;var to=page==1?NavTo:_navAppliedTo;
            if(from is null||to is null||from>to||(to.Value-from.Value).TotalDays>34)throw new ArgumentException("Legfeljebb 35 napos kezdő és záró dátum kell.");
            var result=await ModuleAsync("receiving","nav-list",new{from=from.Value.ToString("yyyy-MM-dd"),to=to.Value.ToString("yyyy-MM-dd"),page});if(_company()!=company)return;
            NavInvoices.Clear();SelectedNavInvoice=null;foreach(var row in result.GetProperty("rows").EnumerateArray())NavInvoices.Add(new(Get(row,"number"),Get(row,"supplierName"),Get(row,"supplierTaxNumber"),Get(row,"issuedOn"),Get(row,"currency"),Get(row,"operation"),row.TryGetProperty("net",out var net)&&net.ValueKind==JsonValueKind.Number?MoneyDisplay.Format(net.GetDecimal()):"—",row.TryGetProperty("batchIndex",out var batch)&&batch.ValueKind==JsonValueKind.Number?batch.GetInt32():null));
            _navAppliedFrom=from;_navAppliedTo=to;_navPage=result.GetProperty("currentPage").GetInt32();_navPages=result.GetProperty("availablePage").GetInt32();Changed(nameof(NavPageLabel));Message=NavInvoices.Count==0?"Ebben az időszakban nincs bejövő számla.":"Válassz számlát, majd töltsd be a tételeit. A készlet még nem változik.";
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){Message=ex.Message;}
        finally{IsBusy=false;}
    }
    internal async Task DownloadNavAsync()
    {
        if(IsBusy||_company() is not Guid company)return;
        if(!CanLeave())return;
        if(SelectedNavInvoice is not {} invoice){Message="Válassz NAV-számlát a listából.";return;}
        if(invoice.Operation!="CREATE"){Message="Módosító/sztornó számla külön ellenőrzést igényel, nem normál bevételezés.";return;}
        if(SelectedNavSupplier is null){Message="A szállító nincs kiválasztva. Vedd fel az Új szállító gombbal, vagy válaszd ki a partnertörzsből.";return;}
        if(Tax(SelectedNavSupplier.TaxId)!=Tax(invoice.SupplierTaxNumber)){Message="A kiválasztott szállító adószáma nem egyezik a számlával.";return;}
        IsBusy=true;
        try
        {
            var result=await ModuleAsync("receiving","nav-import",new{number=invoice.Number,supplierTaxNumber=invoice.SupplierTaxNumber,supplierId=SelectedNavSupplier.Id,batchIndex=invoice.BatchIndex});if(_company()!=company)return;
            Fill(result);_mode="editor";NotifyMode();IsDirty=false;_ = LoadAsync();Message="NAV-számla betöltve. Ellenőrizd a tételeket és a párosítást, majd válassz: bevételezés, költség vagy függő. A már betöltött számlát nem duplázzuk.";
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){Message=ex.Message;}
        finally{IsBusy=false;}
    }
    private static string Get(JsonElement row,string name)=>row.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??"":"";
}