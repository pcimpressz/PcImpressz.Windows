using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Win32;

namespace PcImpressz.Shell.Windows;

public sealed record OutgoingInvoiceRow(string Number, string CustomerName, string CustomerTaxNumber,
    string IssuedOn, string Currency, string Operation, string Net);

internal sealed partial class ReceivingPageViewModel
{
    private bool _outgoing;
    private DateTime? _outFrom=DateTime.Today.AddDays(-30), _outTo=DateTime.Today, _outAppliedFrom, _outAppliedTo;
    private int _outPage, _outPages;
    private string _outMessage="A saját céged által kiállított, NAV-nál nyilvántartott számlák. A helyi eladási bizonylatok az Eladás menüben maradnak.";
    private string _pdfText="", _pdfName="", _draftSource="manual";
    public bool IsOutgoing => _outgoing;
    public bool IsIncoming => !_outgoing;
    public ICommand IncomingCommand => new RelayCommand(()=>SwitchInvoices(false));
    public ICommand OutgoingCommand => new RelayCommand(()=>SwitchInvoices(true));
    private void SwitchInvoices(bool outgoing)
    {
        if(IsBusy||outgoing==_outgoing||!CanLeave())return;
        if(IsDirty){IsDirty=false;_mode="home";ClearPdfPreview();NotifyMode();}
        _outgoing=outgoing;Changed(nameof(IsOutgoing));Changed(nameof(IsIncoming));
    }
    public DateTime? OutgoingFrom {get=>_outFrom;set{if(IsBusy)return;_outFrom=value;ClearOutgoing();Changed();}}
    public DateTime? OutgoingTo {get=>_outTo;set{if(IsBusy)return;_outTo=value;ClearOutgoing();Changed();}}
    public ObservableCollection<OutgoingInvoiceRow> OutgoingInvoices {get;}=[];
    public string OutgoingMessage {get=>_outMessage;private set{_outMessage=value;Changed();}}
    public string OutgoingPageLabel => _outPage==0?"Még nem történt lekérés.":$"{_outPage} / {Math.Max(_outPages,1)}. oldal · {OutgoingInvoices.Count} számla";
    public ICommand FetchOutgoingCommand=>new RelayCommand(()=>_=FetchOutgoingAsync(1));
    public ICommand NextOutgoingCommand=>new RelayCommand(()=>{if(_outPage>0&&_outPage<_outPages)_=FetchOutgoingAsync(_outPage+1);});
    public ICommand PreviousOutgoingCommand=>new RelayCommand(()=>{if(_outPage>1)_=FetchOutgoingAsync(_outPage-1);});
    private void ClearOutgoing(){OutgoingInvoices.Clear();_outPage=_outPages=0;_outAppliedFrom=_outAppliedTo=null;Changed(nameof(OutgoingPageLabel));OutgoingMessage="Válassz legfeljebb 35 napot, majd kérd le a saját kiállított számláidat.";}
    private void ResetInvoices(){_outgoing=false;_navExpanded=false;ClearOutgoing();ClearPdfPreview();Changed(nameof(IsOutgoing));Changed(nameof(IsIncoming));}
    internal async Task FetchOutgoingAsync(int page)
    {
        if(IsBusy||_company() is not Guid company)return;
        IsBusy=true;
        try
        {
            var from=page==1?OutgoingFrom:_outAppliedFrom;var to=page==1?OutgoingTo:_outAppliedTo;
            if(from is null||to is null||from>to||(to.Value-from.Value).TotalDays>34)throw new ArgumentException("Legfeljebb 35 napos kezdő és záró dátum kell.");
            var result=await ModuleAsync("receiving","nav-outgoing-list",new{from=from.Value.ToString("yyyy-MM-dd"),to=to.Value.ToString("yyyy-MM-dd"),page});
            if(_company()!=company)return;
            OutgoingInvoices.Clear();
            foreach(var row in result.GetProperty("rows").EnumerateArray())
                OutgoingInvoices.Add(new(Get(row,"number"),string.IsNullOrWhiteSpace(Get(row,"customerName"))?"Nem közölt vevő / magánszemély":Get(row,"customerName"),Get(row,"customerTaxNumber"),Get(row,"issuedOn"),Get(row,"currency"),Get(row,"operation"),row.TryGetProperty("net",out var net)&&net.ValueKind==JsonValueKind.Number?MoneyDisplay.Format(net.GetDecimal()):"—"));
            _outAppliedFrom=from;_outAppliedTo=to;_outPage=result.GetProperty("currentPage").GetInt32();_outPages=result.GetProperty("availablePage").GetInt32();Changed(nameof(OutgoingPageLabel));
            OutgoingMessage=OutgoingInvoices.Count==0?"Ebben az időszakban nincs NAV-nál nyilvántartott kimenő számla.":"Saját kiállított számlák a NAV-ból. Ez a lista nem hoz létre készletmozgást és nem állít ki új számlát.";
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){OutgoingMessage=ex.Message;}
        finally{IsBusy=false;}
    }
    public bool HasPdfPreview => _pdfText.Length>0;
    public string PdfText => _pdfText;
    public string PdfName => _pdfName;
    public ICommand PdfCommand=>new RelayCommand(()=>_=PickPdfAsync());
    private void ClearPdfPreview(){_pdfText=_pdfName="";_draftSource="manual";Changed(nameof(HasPdfPreview));Changed(nameof(PdfText));Changed(nameof(PdfName));}
    private async Task PickPdfAsync()
    {
        if(IsBusy||!CanLeave()||_company() is not Guid company)return;
        var dialog=new OpenFileDialog{Title="PDF alapján bejövő számla rögzítése",Filter="PDF (*.pdf)|*.pdf",CheckFileExists=true};
        if(dialog.ShowDialog()!=true)return;
        IsBusy=true;
        try
        {
            if(new FileInfo(dialog.FileName).Length>1_500_000)throw new IOException("Legfeljebb 1,5 MB-os PDF tölthető be.");
            var bytes=await File.ReadAllBytesAsync(dialog.FileName,_shutdown);
            var result=await ModuleAsync("receiving","pdf-preview",new{contentBase64=Convert.ToBase64String(bytes)});
            if(_company()!=company)return;
            // Only replace the current form after successful extraction; no business write occurs here.
            IsBusy=false;IsDirty=false;BeginManual();ApplyPdfPreview(Path.GetFileName(dialog.FileName),result.GetProperty("text").GetString()??"");
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){Message=ex.Message;}
        finally{IsBusy=false;}
    }
    internal void ApplyPdfPreview(string name,string text)
    {
        _pdfName=name;_pdfText=text;_draftSource="pdf";Notes="PDF alapján: "+name;
        Changed(nameof(HasPdfPreview));Changed(nameof(PdfName));Changed(nameof(PdfText));
        Message="A PDF szövege beolvasva. Töltsd ki és ellenőrizd a számla adatait és tételeit; automatikus sorfelismerés még nincs. Az eredeti PDF-et őrizd meg külön.";
        MarkDirty();
    }
}
