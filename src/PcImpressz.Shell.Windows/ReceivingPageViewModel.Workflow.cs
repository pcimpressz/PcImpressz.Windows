using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace PcImpressz.Shell.Windows;

internal sealed partial class ReceivingPageViewModel
{
    private bool _autoNavSupplier=true;
    private string _supplierStatus="";
    public string SupplierStatus {get=>_supplierStatus;private set{_supplierStatus=value;Changed();}}
    public bool AutoNavSupplier {get=>_autoNavSupplier;set{_autoNavSupplier=value;Changed();Changed(nameof(ShowSupplierPicker));if(value&&!IsBusy&&IsNavReceipt&&_company() is Guid company&&(_preparationIdentity??_navPreview) is {} invoice)_=ResolveSupplierAsync(invoice,company);}}
    private async Task ResolveSupplierAsync(NavInvoiceRow invoice,Guid company)
    {
        IsBusy=true;try{await EnsureNavSupplierAsync(invoice,company);}catch(Exception ex){SupplierStatus="A szállítófelvétel nem sikerült: "+ex.Message;}finally{IsBusy=false;}
    }
    public bool ShowSupplierPicker=>!IsNavReceipt||!AutoNavSupplier;
    public string NavHeader=>(_preparationIdentity??_navPreview) is {} row?$"{row.SupplierName} · {row.SupplierTaxNumber} · {Number} · Kelt: {IssuedOn}":$"{SelectedSupplier?.Name} · {Number} · Kelt: {IssuedOn}";
    public ICommand ReceivedInvoicesCommand=>new RelayCommand(()=>_=ShowReceivedAsync());
    private async Task ShowReceivedAsync()
    {
        if(IsBusy||!await FlushPreparationAsync()||!CanLeave())return;
        _navExpanded=false;_mode="home";_filter="bevetelezve";_receiptPage=0;
        Changed(nameof(IsNav));Changed(nameof(Filter));NotifyMode();await LoadAsync();
        Message="Bevételezett számlák: dupla kattintással megnyithatók a tételek és a bizonylat adatai.";
    }
    internal async Task NavActionAsync(NavInvoiceRow row,string action)
    {
        if(IsBusy)return;
        await SelectNavAsync(row);
        if(IsBusy||SelectedNavInvoice!=row||!IsEditor||Number!=row.Number)return;
        if(action=="manual"&&_linkedManualReceiptId is Guid linked){await OpenLinkedManualAsync(linked);return;}
        if(!IsEditable){Message="A lezárt számla nem módosítható. A bevételezett számlák listájában megtekinthető.";return;}
        if(action=="manual"){await MoveNavToManualAsync();return;}
        if(action=="api")
        {
            if(Tax(row.SupplierTaxNumber)!="23126806"){Message="Ehhez a szállítóhoz még nincs bekötött API. XML vagy PDF segédletet választhatsz.";return;}
            await EnrichSupplierManuallyAsync();return;
        }
        await PickInvoiceAssistantAsync(action);
    }
    private async Task PickInvoiceAssistantAsync(string kind)
    {
        if(_company() is not Guid company)return;
        var dialog=new OpenFileDialog{Title="Segédadatok a kiválasztott számlához",Filter=kind=="pdf"?"PDF (*.pdf)|*.pdf":"XML (*.xml)|*.xml",CheckFileExists=true};
        if(dialog.ShowDialog()!=true)return;
        IsBusy=true;
        try
        {
            if(new FileInfo(dialog.FileName).Length>1_500_000)throw new IOException("Legfeljebb 1,5 MB tölthető be.");
            if(kind=="pdf")
            {
                var bytes=await File.ReadAllBytesAsync(dialog.FileName,_shutdown);
                var result=await ModuleAsync("receiving","pdf-preview",new{contentBase64=Convert.ToBase64String(bytes)});
                if(_company()!=company)return;
                _pdfName=Path.GetFileName(dialog.FileName);_pdfText=Get(result,"text");Changed(nameof(PdfName));Changed(nameof(PdfText));Changed(nameof(HasPdfPreview));
                Message="PDF-segédlet csatolva az előkészítéshez. A szövege olvasható; automatikus PDF-tételpárosítás még nincs.";
            }
            else
            {
                using var reader=XmlReader.Create(dialog.FileName,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1_500_000});
                var doc=XDocument.Load(reader);await ApplyAssistantXmlAsync(doc);
            }
            MarkDirty();
        }
        catch(Exception ex){Message=ex.Message;}
        finally{IsBusy=false;}
    }
    internal async Task ApplyAssistantXmlAsync(XDocument doc)
    {
        static string Value(XElement row,params string[] names)=>row.Descendants().FirstOrDefault(e=>names.Contains(e.Name.LocalName,StringComparer.OrdinalIgnoreCase))?.Value.Trim()??"";
        var numbers=doc.Descendants().Where(e=>e.Name.LocalName is "invoiceNumber" or "szamlaszam").Select(e=>e.Value.Trim()).Distinct().ToArray();
        if(numbers.Length!=1||numbers[0]!=(_preparationIdentity??_navPreview)?.Number)throw new ArgumentException("Az XML-ben egyértelműen szerepelnie kell a kiválasztott számla számlaszámának (invoiceNumber / szamlaszam).");
        var taxes=doc.Descendants().Where(e=>e.Name.LocalName is "supplierTaxNumber" or "supplierTaxId").Select(e=>Tax(e.Value)).Distinct().ToArray();
        if(taxes.Length!=1||taxes[0]!=Tax((_preparationIdentity??_navPreview)!.SupplierTaxNumber))throw new ArgumentException("Az XML szállítói adószáma eltér a NAV-számláétól.");
        var rows=doc.Descendants().Where(e=>e.Name.LocalName is "line" or "item" or "tetel").ToArray();var matched=0;
        foreach(var line in Lines)
        {
            var matches=rows.Where(r=>Value(r,"lineDescription","name","megnevezes").Equals(line.Name,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(matches.Length!=1||Lines.Count(l=>l.Name.Equals(line.Name,StringComparison.OrdinalIgnoreCase))!=1)continue;
            var row=matches[0];var barcode=Value(row,"barcode","ean","vonalkod");var sku=Value(row,"sku","cikkszam");
            if(line.ProductId is null){if(line.Barcode.Length==0)line.Barcode=barcode;if(line.Sku.Length==0)line.Sku=sku;await SuggestLineProductsAsync(line);await LoadLineProductAsync(line);}
            if(line.ProductId is null)foreach(var (id,value) in new[]{("manufacturerPartNumber",Value(row,"manufacturerPartNumber","mpn")),("categoryCode",Value(row,"categoryCode"))})
            {var field=line.ProductFields.FirstOrDefault(f=>f.Definition.Id==id);if(field is not null&&field.Text.Length==0&&value.Length>0)field.Text=value;}
            matched++;
        }
        Message=$"XML-segédlet: {matched} egyértelmű tétel egyeztetve. A NAV fejlécét, mennyiségeit és beszerzési árait nem módosítottuk.";
    }
    internal async Task BrowseProductAsync(LineEditor line)
    {
        if(IsBusy||!IsEditable||_company() is not Guid company)return;
        await SearchProductAsync(line);
        if(_company()!=company||!Lines.Contains(line))return;
        var panel=new DockPanel{Margin=new Thickness(16)};
        var query=new TextBox{Text=line.Name,Margin=new Thickness(0,0,0,10)};DockPanel.SetDock(query,Dock.Top);panel.Children.Add(query);
        var select=new Button{Content="Kiválasztás",Margin=new Thickness(0,10,0,0)};DockPanel.SetDock(select,Dock.Bottom);panel.Children.Add(select);
        var options=new System.Collections.ObjectModel.ObservableCollection<ProductOption>(line.Matches);
        var list=new ListBox{ItemsSource=options,DisplayMemberPath="Label"};panel.Children.Add(list);
        var window=new Window{Title="Terméktörzs – keresés név, cikkszám vagy vonalkód alapján",Width=780,Height=500,Content=panel,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=Application.Current.MainWindow};
        CancellationTokenSource? pending=null;var closed=false;var generation=0;
        query.TextChanged+=async(_,_)=>
        {
            pending?.Cancel();pending?.Dispose();pending=new();var token=pending.Token;var request=++generation;var text=query.Text;
            try
            {
                await Task.Delay(250,token);if(closed||_company()!=company)return;
                var result=await ModuleAsync("catalog","products-list",new{search=text,filter="all",page=0,pageSize=50});
                if(closed||generation!=request||_company()!=company)return;
                options.Clear();foreach(var row in result.GetProperty("rows").EnumerateArray())options.Add(new(row.GetProperty("id").GetGuid(),Get(row,"name"),Get(row,"sku"),Get(row,"barcode"),Get(row,"unitCode")));
            }
            catch(OperationCanceledException){}catch(Exception ex){if(!closed&&generation==request)window.Title="Keresési hiba: "+ex.Message;}
        };
        void Choose(){if(_company()==company&&Lines.Contains(line)&&list.SelectedItem is ProductOption item){line.SelectedMatch=item;window.Close();}}
        select.Click+=(_,_)=>Choose();list.MouseDoubleClick+=(_,_)=>Choose();window.Closed+=(_,_)=>{closed=true;pending?.Cancel();pending?.Dispose();};window.ShowDialog();
    }
}
