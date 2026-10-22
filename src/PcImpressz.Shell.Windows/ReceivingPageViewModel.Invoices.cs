using PcImpressz.Contracts;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Win32;

namespace PcImpressz.Shell.Windows;

public sealed record OutgoingInvoiceRow(string Number,string CustomerName,string CustomerTaxNumber,string IssuedOn,string DeliveryOn,string DueOn,string PaymentMethod,string Currency,string ProviderStatus,string NavStatus,string FinancialStatus,string Net,string Vat,string Gross,string PublicUrl);

internal sealed partial class ReceivingPageViewModel
{
    private bool _outgoing;
    private DateTime? _outFrom=DateTime.Today.AddDays(-30),_outTo=DateTime.Today;
    private string _outMessage="A saját adatbázisban tárolt, ténylegesen kiállított számlák. A NAV csak egyeztetési forrás.";
    private int _outPage,_outPages;
    public string OutgoingPageLabel=>OutgoingInvoices.Count==0?"":$"{_outPage+1} / {Math.Max(_outPages,1)}. oldal";
    public ICommand NextOutgoingCommand=>new RelayCommand(()=>{if(_outPage+1<_outPages)_=FetchOutgoingAsync(_outPage+1);});
    public ICommand PreviousOutgoingCommand=>new RelayCommand(()=>{if(_outPage>0)_=FetchOutgoingAsync(_outPage-1);});
    private string _outSearch="",_outPayment="all",_outNavStatus="all";
    private string _pdfText="",_pdfName="",_draftSource="manual";
    public bool IsOutgoing=>_outgoing;
    public bool IsIncoming=>!_outgoing;
    public ICommand IncomingCommand=>new RelayCommand(()=>SwitchInvoices(false));
    public ICommand OutgoingCommand=>new RelayCommand(()=>SwitchInvoices(true));
    private void SwitchInvoices(bool outgoing)
    {
        if(IsBusy||outgoing==_outgoing||!CanLeave())return;
        if(IsDirty){IsDirty=false;_mode="home";ClearPdfPreview();NotifyMode();}
        _outgoing=outgoing;Changed(nameof(IsOutgoing));Changed(nameof(IsIncoming));
        if(outgoing)_=FetchOutgoingAsync();
    }
    public DateTime? OutgoingFrom{get=>_outFrom;set{if(IsBusy)return;_outFrom=value;Changed();}}
    public DateTime? OutgoingTo{get=>_outTo;set{if(IsBusy)return;_outTo=value;Changed();}}
    public string OutgoingSearch{get=>_outSearch;set{_outSearch=value??"";Changed();}}
    public string OutgoingPayment{get=>_outPayment;set{_outPayment=value??"all";Changed();}}
    public string OutgoingNavStatus{get=>_outNavStatus;set{_outNavStatus=value??"all";Changed();}}
    public EntityFilterOption[] OutgoingPaymentOptions=>[new("all","Minden fizetés"),new("BANK_TRANSFER","Átutalás"),new("CASH","Készpénz"),new("CARD","Bankkártya"),new("VOUCHER","Utalvány"),new("OTHER","Egyéb")];
    public EntityFilterOption[] OutgoingNavOptions=>[new("all","Minden NAV-állapot"),new("matched","Egyeztetve"),new("pending","Egyeztetésre vár"),new("missing","NAV-nál hiányzik"),new("mismatch","Eltérés")];
    public ObservableCollection<OutgoingInvoiceRow> OutgoingInvoices{get;}=[];
    public string OutgoingMessage{get=>_outMessage;private set{_outMessage=value;Changed();}}
    public string OutgoingSummary{get;private set;}="Nincs betöltött számla.";
    public ICommand FetchOutgoingCommand=>new RelayCommand(()=>_=FetchOutgoingAsync());
    public ICommand ReconcileOutgoingCommand=>new RelayCommand(()=>_=ReconcileOutgoingAsync());
    public ICommand ClearOutgoingFiltersCommand=>new RelayCommand(()=>{OutgoingSearch="";OutgoingPayment="all";OutgoingNavStatus="all";_=FetchOutgoingAsync();});
    private void ClearOutgoing(){OutgoingInvoices.Clear();OutgoingSummary="Nincs betöltött számla.";Changed(nameof(OutgoingSummary));OutgoingMessage="A saját kimenő számlák még nincsenek betöltve.";}
    private void ResetInvoices(){_outSearch="";_outPayment=_outNavStatus="all";Changed(nameof(OutgoingSearch));Changed(nameof(OutgoingPayment));Changed(nameof(OutgoingNavStatus));_outgoing=false;_navExpanded=false;ClearOutgoing();ClearPdfPreview();Changed(nameof(IsOutgoing));Changed(nameof(IsIncoming));}

    internal async Task FetchOutgoingAsync(int page=0)
    {
        if(IsBusy||_company() is not Guid company)return;IsBusy=true;
        try
        {
            if(OutgoingFrom is null||OutgoingTo is null||OutgoingFrom>OutgoingTo)throw new ArgumentException("Érvényes kezdő és záró dátum kell.");
            var result=await ModuleAsync("receiving","outgoing-local-list",new{search=OutgoingSearch,paymentMethod=OutgoingPayment,navStatus=OutgoingNavStatus,providerStatus="all",from=OutgoingFrom.Value.ToString("yyyy-MM-dd"),to=OutgoingTo.Value.ToString("yyyy-MM-dd"),page,pageSize=100});
            if(_company()!=company)return;OutgoingInvoices.Clear();
            foreach(var row in result.GetProperty("rows").EnumerateArray())
                OutgoingInvoices.Add(new(Get(row,"number"),Get(row,"customerName"),Get(row,"customerTaxNumber"),Get(row,"issuedOn"),Get(row,"deliveryOn"),Get(row,"dueOn"),PaymentLabel(Get(row,"paymentMethod")),Get(row,"currency"),Get(row,"providerStatus"),NavLabel(Get(row,"navStatus")),FinanceLabel(Get(row,"financialStatus")),Money(row,"net"),Money(row,"vat"),Money(row,"gross"),Get(row,"publicUrl")));
            _outPage=page;_outPages=(result.GetProperty("total").GetInt32()+99)/100;Changed(nameof(OutgoingPageLabel));
            var s=result.GetProperty("summary");OutgoingSummary=s.GetProperty("count").GetInt32()+" számla · "+string.Join(" | ",s.GetProperty("currencies").EnumerateArray().Select(g=>Get(g,"currency")+": nettó "+Money(g,"net")+" · áfa "+Money(g,"vat")+" · bruttó "+Money(g,"gross")));Changed(nameof(OutgoingSummary));
            OutgoingMessage=OutgoingInvoices.Count==0?"A saját nyilvántartásban nincs ilyen kimenő számla.":"A lista a PC-Impressz saját adatbázisából származik. A NAV oszlop csak az utolsó egyeztetés állapotát mutatja.";
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){OutgoingMessage=ex.Message;}
        finally{IsBusy=false;}
    }
    private async Task ReconcileOutgoingAsync()
    {
        if(IsBusy||_company() is not Guid company)return;IsBusy=true;var summary="";var completed=false;
        try
        {
            if(OutgoingFrom is null||OutgoingTo is null||OutgoingFrom>OutgoingTo||(OutgoingTo.Value-OutgoingFrom.Value).TotalDays>34)throw new ArgumentException("NAV-egyeztetéshez legfeljebb 35 napos időszak választható.");
            var result=await ModuleAsync("receiving","nav-outgoing-reconcile",new{from=OutgoingFrom.Value.ToString("yyyy-MM-dd"),to=OutgoingTo.Value.ToString("yyyy-MM-dd"),page=1});
            if(_company()!=company)return;
            completed=true;summary=$"NAV-egyeztetés: {result.GetProperty("matched").GetInt32()} egyező · {result.GetProperty("missingAtNav").GetInt32()} NAV-nál hiányzó · {result.GetProperty("navOnly").GetInt32()} csak NAV-ban · {result.GetProperty("mismatched").GetInt32()} eltérő.";
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){summary=ex.Message;}
        finally{IsBusy=false;}if(_company()!=company)return;if(completed)await FetchOutgoingAsync();if(summary.Length>0)OutgoingMessage=summary;
    }
    private static string Money(JsonElement row,string key)=>row.TryGetProperty(key,out var n)&&n.ValueKind==JsonValueKind.Number?MoneyDisplay.Format(n.GetDecimal()):"—";
    private static string PaymentLabel(string value)=>value switch{"BANK_TRANSFER"=>"Átutalás","CASH"=>"Készpénz","CARD"=>"Bankkártya","VOUCHER"=>"Utalvány","OTHER"=>"Egyéb",_=>value};
    private static string NavLabel(string value)=>value switch{"matched"=>"✓ Egyeztetve","pending"=>"Függő","missing"=>"⚠ NAV-nál hiányzik","mismatch"=>"⚠ Eltérés",_=>value};
    private static string FinanceLabel(string value)=>value switch{"paid"=>"Fizetve","open"=>"Nyitott","partial"=>"Részben fizetve","overdue"=>"Lejárt","unknown"=>"Nincs pénzügyi adat",_=>value};

    public bool HasPdfPreview=>_pdfText.Length>0;
    public string PdfText=>_pdfText;
    public string PdfName=>_pdfName;
    public ICommand PdfCommand=>new RelayCommand(()=>_=PickPdfAsync());
    private void ClearPdfPreview(){_pdfText=_pdfName="";_draftSource="manual";Changed(nameof(HasPdfPreview));Changed(nameof(PdfText));Changed(nameof(PdfName));}
    private async Task PickPdfAsync()
    {
        if(IsBusy||!CanLeave()||_company() is not Guid company)return;
        var dialog=new OpenFileDialog{Title="PDF alapján bejövő számla rögzítése",Filter="PDF (*.pdf)|*.pdf",CheckFileExists=true};
        if(dialog.ShowDialog()!=true)return;IsBusy=true;
        try
        {
            if(new FileInfo(dialog.FileName).Length>1_500_000)throw new IOException("Legfeljebb 1,5 MB-os PDF tölthető be.");
            var bytes=await File.ReadAllBytesAsync(dialog.FileName,_shutdown);var result=await ModuleAsync("receiving","pdf-preview",new{contentBase64=Convert.ToBase64String(bytes)});if(_company()!=company)return;
            IsBusy=false;IsDirty=false;BeginManual();ApplyPdfPreview(Path.GetFileName(dialog.FileName),result.GetProperty("text").GetString()??"");
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){Message=ex.Message;}
        finally{IsBusy=false;}
    }
    internal void ApplyPdfPreview(string name,string text)
    {
        _pdfName=name;_pdfText=text;_draftSource="pdf";Notes="PDF alapján: "+name;Changed(nameof(HasPdfPreview));Changed(nameof(PdfName));Changed(nameof(PdfText));
        Message="A PDF szövege beolvasva. Töltsd ki és ellenőrizd a számla adatait és tételeit; automatikus sorfelismerés még nincs. Az eredeti PDF-et őrizd meg külön.";MarkDirty();
    }
}
