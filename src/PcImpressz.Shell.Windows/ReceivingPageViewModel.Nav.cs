using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
namespace PcImpressz.Shell.Windows;
public sealed record NavInvoiceRow(string Number,string SupplierName,string SupplierTaxNumber,string IssuedOn,string Currency,string Operation,string Net,int? BatchIndex,Guid? ReceiptId=null,bool Received=false){public string State=>Received?"Bevételezve":"Nincs bevételezve";}
internal sealed partial class ReceivingPageViewModel
{
    private NavInvoiceRow? _navPreview; private SupplierOption? _navSupplier;
    public string NavSupplierHint=>_navPreview is {} invoice?invoice.SupplierName+" · "+invoice.SupplierTaxNumber+(SelectedSupplier is null?" · Még nincs a szállítótörzsben.":""):"";
    public SupplierOption? SelectedNavSupplier{get=>_navSupplier;set{_navSupplier=value;Changed();}}
    public ICommand NewNavSupplierCommand=>new RelayCommand(()=>_=CreateSupplierAsync(true));
    private bool _navExpanded;
    private DateTime? _navFrom=DateTime.Today.AddDays(-30),_navTo=DateTime.Today;
    private DateTime? _navAppliedFrom,_navAppliedTo;private int _navPage,_navPages;private NavInvoiceRow? _navSelected;
    public DateTime? NavFrom{get=>_navFrom;set{if(IsBusy)return;_navFrom=value;ResetNavList();Changed();}}
    public DateTime? NavTo{get=>_navTo;set{if(IsBusy)return;_navTo=value;ResetNavList();Changed();}}
    public ObservableCollection<NavInvoiceRow> NavInvoices{get;}=[];
    public string NavPageLabel=>_navPage==0?"Válassz időszakot és kérd le a bejövő számlákat.":_navPage+" / "+Math.Max(_navPages,1)+". oldal · "+NavInvoices.Count+" számla";
    public NavInvoiceRow? SelectedNavInvoice
    {
        get=>_navSelected;
        set
        {
            if(_navSelected==value||IsBusy)return;
            if(value is not null&&!CanLeave()){Changed();return;}

            _navSelected=value;Changed();
            if(value is not null){SelectedNavSupplier=Suppliers.FirstOrDefault(s=>Tax(s.TaxId)==Tax(value.SupplierTaxNumber));_=DownloadNavAsync(true);}
        }
    }
    public ICommand FetchNavCommand=>new RelayCommand(()=>_=FetchNavAsync(1));
    public ICommand NextNavCommand=>new RelayCommand(()=>{if(_navPage>0&&_navPage<_navPages)_=FetchNavAsync(_navPage+1);});
    public ICommand PreviousNavCommand=>new RelayCommand(()=>{if(_navPage>1)_=FetchNavAsync(_navPage-1);});
    public ICommand DownloadNavCommand=>new RelayCommand(()=>_=DownloadNavAsync());
    private static string Tax(string value)=>new string(value.Where(char.IsAsciiDigit).Take(8).ToArray());
    private void ResetNavList(){NavInvoices.Clear();_navPage=_navPages=0;_navAppliedFrom=_navAppliedTo=null;_navSelected=null;Changed(nameof(SelectedNavInvoice));SelectedNavSupplier=null;Changed(nameof(NavPageLabel));}
    internal async Task FetchNavAsync(int page)
    {
        if(IsBusy||_company() is not Guid company)return;IsBusy=true;
        try
        {
            var from=page==1?NavFrom:_navAppliedFrom;var to=page==1?NavTo:_navAppliedTo;
            if(from is null||to is null||from>to||(to.Value-from.Value).TotalDays>34)throw new ArgumentException("Legfeljebb 35 napos kezdő és záró dátum kell.");
            var result=await ModuleAsync("receiving","nav-list",new{from=from.Value.ToString("yyyy-MM-dd"),to=to.Value.ToString("yyyy-MM-dd"),page});if(_company()!=company)return;
            NavInvoices.Clear();_navSelected=null;Changed(nameof(SelectedNavInvoice));foreach(var row in result.GetProperty("rows").EnumerateArray())NavInvoices.Add(new(Get(row,"number"),Get(row,"supplierName"),Get(row,"supplierTaxNumber"),Get(row,"issuedOn"),Get(row,"currency"),Get(row,"operation"),row.TryGetProperty("net",out var net)&&net.ValueKind==JsonValueKind.Number?MoneyDisplay.Format(net.GetDecimal()):"—",row.TryGetProperty("batchIndex",out var batch)&&batch.ValueKind==JsonValueKind.Number?batch.GetInt32():null,row.TryGetProperty("receiptId",out var receipt)&&receipt.ValueKind==JsonValueKind.String?receipt.GetGuid():null,row.TryGetProperty("received",out var received)&&received.ValueKind==JsonValueKind.True));
            _navAppliedFrom=from;_navAppliedTo=to;_navPage=result.GetProperty("currentPage").GetInt32();_navPages=result.GetProperty("availablePage").GetInt32();Changed(nameof(NavPageLabel));Message=NavInvoices.Count==0?"Ebben az időszakban nincs bejövő számla.":"Kattints egy számlára: alatta automatikusan megjelennek a tételei. A készlet még nem változik.";
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){Message=ex.Message;}
        finally{IsBusy=false;}
    }
    internal async Task DownloadNavAsync(bool discardChecked=false)
    {
        if(IsBusy||_company() is not Guid company)return;
        if(!discardChecked&&!CanLeave())return;
        if(SelectedNavInvoice is not {} invoice){Message="Válassz NAV-számlát a listából.";return;}
        if(invoice.Operation!="CREATE"){Message="Módosító/sztornó számla külön ellenőrzést igényel, nem normál bevételezés.";return;}
        IsBusy=true;
        try
        {
            await LoadSuppliersAsync();if(_company()!=company)return;
            JsonElement detail;
            if(invoice.ReceiptId is Guid id)detail=await ModuleAsync("receiving","get",new{id});
            else
            {
                var preview=await ModuleAsync("receiving","nav-preview",new{number=invoice.Number,supplierTaxNumber=invoice.SupplierTaxNumber,batchIndex=invoice.BatchIndex});if(_company()!=company)return;
                if(preview.GetProperty("receipt").ValueKind==JsonValueKind.Object)detail=preview.GetProperty("receipt");
                else
                {
                    var document=preview.GetProperty("document");
                    detail=JsonSerializer.SerializeToElement(new{id=Guid.Empty,version=0,status="uj",number=invoice.Number,issuedOn=Get(document,"issuedOn"),dueOn=Get(document,"dueOn"),warehouse="Bolt",net=0,gross=0,supplierId=SelectedNavSupplier?.Id??Guid.Empty,supplierName=invoice.SupplierName,supplierTaxId=invoice.SupplierTaxNumber,lines=preview.GetProperty("lines")});
                }
            }
            if(_company()!=company)return;
            Fill(detail);if(_editId==Guid.Empty){_editId=null;_navPreview=invoice;Changed(nameof(CanClassify));}
            Changed(nameof(NavSupplierHint));_mode="editor";NotifyMode();UpdateTotals();IsDirty=false;
            await LoadLineProductsAsync(company);
            Message=SelectedSupplier is null?"A számla előnézete kész. A szállítót innen felveheted; mentésig nincs új számla és készletmozgás.":"Ellenőrizd a tételeket, az eladási árakat és a termékpárosítást. A készletet csak a Bevételezés gomb módosítja.";
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){Message=ex.Message;}
        finally{IsBusy=false;}
    }
    private static string Get(JsonElement row,string name)=>row.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??"":"";
}
