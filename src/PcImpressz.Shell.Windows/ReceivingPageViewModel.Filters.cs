using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using PcImpressz.Shell.Customization;

namespace PcImpressz.Shell.Windows;

public sealed partial class ShellViewModel
{
    public bool InvoiceLiveFilter {get=>_preferences.InvoiceLiveFilter;set{_preferences=_preferences with{InvoiceLiveFilter=value};InvoiceListPreferences.LiveFiltering=value;ApplyAndSave();Changed();}}
}

internal sealed record InvoiceFilterSnapshot(string Supplier,string? From,string? To,decimal? MinGross,decimal? MaxGross,Dictionary<string,string> Columns)
{
    public bool Active=>Supplier.Length>0||From is not null||To is not null||MinGross is not null||MaxGross is not null||Columns.Count>0;
}

internal sealed partial class ReceivingPageViewModel
{
    private InvoiceFilterSnapshot _invoiceFilters=new("",null,null,null,null,[]);
    private readonly Dictionary<string,string> _invoiceColumns=[];
    private TypedSearch? _invoiceFilterTyping;
    private string _invoiceSupplier="",_invoiceGrossFrom="",_invoiceGrossTo="";
    private DateTime? _invoiceFrom,_invoiceTo;
    private bool _invoiceFilterOpen,_navAllPagesLoaded;
    public bool InvoiceFilterOpen {get=>_invoiceFilterOpen;set{_invoiceFilterOpen=value;Changed();}}
    public string InvoiceSupplierText {get=>_invoiceSupplier;set{_invoiceSupplier=value;Changed();ScheduleInvoiceFilters();}}
    public string InvoiceGrossFrom {get=>_invoiceGrossFrom;set{_invoiceGrossFrom=value;Changed();ScheduleInvoiceFilters();}}
    public string InvoiceGrossTo {get=>_invoiceGrossTo;set{_invoiceGrossTo=value;Changed();ScheduleInvoiceFilters();}}
    public DateTime? InvoiceDateFrom {get=>_invoiceFrom;set{_invoiceFrom=value;Changed();ScheduleInvoiceFilters();}}
    public DateTime? InvoiceDateTo {get=>_invoiceTo;set{_invoiceTo=value;Changed();ScheduleInvoiceFilters();}}
    public ICommand ToggleInvoiceFiltersCommand=>new RelayCommand(()=>InvoiceFilterOpen=!InvoiceFilterOpen);
    public ICommand ApplyInvoiceFiltersCommand=>new RelayCommand(ApplyInvoiceFilters);
    public ICommand ClearInvoiceFiltersCommand=>new RelayCommand(ClearInvoiceFilters);
    public string InvoiceActiveFilters=>!_invoiceFilters.Active?"":string.Join(" · ",new[]{_invoiceFilters.Supplier.Length>0?"Szállító: "+_invoiceFilters.Supplier:"",_invoiceFilters.From is not null||_invoiceFilters.To is not null?$"Kelt: {_invoiceFilters.From??"…"} – {_invoiceFilters.To??"…"}":"",_invoiceFilters.MinGross is not null||_invoiceFilters.MaxGross is not null?$"Bruttó: {_invoiceFilters.MinGross?.ToString("N0")??"…"} – {_invoiceFilters.MaxGross?.ToString("N0")??"…"} Ft":""}.Where(s=>s.Length>0).Concat(_invoiceFilters.Columns.Select(c=>InvoiceColumnTitle(c.Key)+": "+c.Value)));
    internal static string InvoiceColumnTitle(string id)=>id switch{"number"=>"Számlaszám","supplierName"=>"Szállító","supplierTaxId"=>"Adószám","issuedOn"=>"Kelt","sourceLabel"=>"Forrás","statusLabel"=>"Állapot","net"=>"Nettó","gross"=>"Bruttó",_=>id};
    internal string InvoiceColumnText(string id)=>_invoiceColumns.GetValueOrDefault(id)??"";
    internal void SetInvoiceColumn(string id,string text,bool typed=false)
    {
        if(text.Length>100||text.Any(char.IsControl)){Message="Az oszlopszűrő legfeljebb 100 karakter lehet.";return;}
        if(text.Trim().Length==0)_invoiceColumns.Remove(id);else _invoiceColumns[id]=text.Trim();
        if(!typed)ApplyInvoiceFilters();else ScheduleInvoiceFilters();
    }
    private void ScheduleInvoiceFilters(){if(_populating||!InvoiceListPreferences.LiveFiltering)return;(_invoiceFilterTyping??=new(ApplyInvoiceFilters,_shutdown)).Schedule();}
    private void ApplyInvoiceFilters()
    {
        _invoiceFilterTyping?.CancelPending();
        decimal? Amount(string value){if(string.IsNullOrWhiteSpace(value))return null;if(!TryMoney(value,out var money)||money is < -1_000_000_000_000_000m or > 1_000_000_000_000_000m)throw new ArgumentException("Érvényes összeghatárt adj meg.");return money;}
        try
        {
            var min=Amount(InvoiceGrossFrom);var max=Amount(InvoiceGrossTo);
            if(min>max||InvoiceDateFrom>InvoiceDateTo)throw new ArgumentException("A szűrés alsó határa nem lehet nagyobb a felsőnél.");
            if(InvoiceSupplierText.Length>200)throw new ArgumentException("A szállítószűrő legfeljebb 200 karakter lehet.");
            _invoiceFilters=new(InvoiceSupplierText.Trim(),InvoiceDateFrom?.ToString("yyyy-MM-dd"),InvoiceDateTo?.ToString("yyyy-MM-dd"),min,max,new(_invoiceColumns));
            Changed(nameof(InvoiceActiveFilters));_receiptPage=0;_navLocalPage=0;_typed.CancelPending();
            if(IsNav&&_navPage>0&&_invoiceFilters.Active&&!_navAllPagesLoaded)_=ReloadNavFilterAsync(_invoiceFilters);
            else{ShowNavSlice();if(!IsEditor)_=LoadAsync();}
        }
        catch(Exception ex){Message=ex.Message;}
    }
    private async Task ReloadNavFilterAsync(InvoiceFilterSnapshot snapshot)
    {
        try
        {
            while(IsBusy){await Task.Delay(40,_shutdown);if(_invoiceFilters!=snapshot)return;}
            if(IsNav&&_invoiceFilters==snapshot)await FetchNavAsync(1);
        }
        catch(OperationCanceledException){}
    }
    private void ClearInvoiceFilters()
    {
        _invoiceFilterTyping?.CancelPending();_invoiceColumns.Clear();_invoiceSupplier=_invoiceGrossFrom=_invoiceGrossTo="";_invoiceFrom=_invoiceTo=null;
        foreach(var name in new[]{nameof(InvoiceSupplierText),nameof(InvoiceGrossFrom),nameof(InvoiceGrossTo),nameof(InvoiceDateFrom),nameof(InvoiceDateTo)})Changed(name);
        ApplyInvoiceFilters();
    }
    private NavInvoiceRow[] FilteredNavRows()=>_navAll.Where(MatchesNavFilters).ToArray();
    private static NavInvoiceRow[] ReadNavRows(JsonElement result)=>result.GetProperty("rows").EnumerateArray().Select(row=>
    {
        decimal? Amount(string id)=>row.TryGetProperty(id,out var value)&&value.ValueKind==JsonValueKind.Number?value.GetDecimal():null;
        var net=Amount("net");var vat=Amount("vat");var gross=Amount("gross")??(net is not null&&vat is not null?net+vat:null);
        return new NavInvoiceRow(Get(row,"number"),Get(row,"supplierName"),Get(row,"supplierTaxNumber"),Get(row,"issuedOn"),Get(row,"currency"),Get(row,"operation"),net is not null?MoneyDisplay.Format(net.Value):"—",row.TryGetProperty("batchIndex",out var batch)&&batch.ValueKind==JsonValueKind.Number?batch.GetInt32():null,row.TryGetProperty("receiptId",out var receipt)&&receipt.ValueKind==JsonValueKind.String?receipt.GetGuid():null,row.TryGetProperty("received",out var received)&&received.ValueKind==JsonValueKind.True,net,gross);
    }).ToArray();
    private bool MatchesNavFilters(NavInvoiceRow row)
    {
        static bool Contains(string value,string text)=>CultureInfo.GetCultureInfo("hu-HU").CompareInfo.IndexOf(value,text,CompareOptions.IgnoreCase|CompareOptions.IgnoreNonSpace)>=0;
        var f=_invoiceFilters;var gross=row.GrossAmount;var net=row.NetAmount??(TryMoney(row.Net,out var amount)?amount:(decimal?)null);
        if(f.Supplier.Length>0&&!Contains(row.SupplierName+" "+row.SupplierTaxNumber,f.Supplier)||f.From is not null&&string.CompareOrdinal(row.IssuedOn,f.From)<0||f.To is not null&&string.CompareOrdinal(row.IssuedOn,f.To)>0||f.MinGross is not null&&(gross is null||gross<f.MinGross)||f.MaxGross is not null&&(gross is null||gross>f.MaxGross))return false;
        foreach(var (id,text) in f.Columns)
        {
            var value=id switch{"number"=>row.Number,"supplierName"=>row.SupplierName,"supplierTaxId"=>row.SupplierTaxNumber,"issuedOn"=>row.IssuedOn,"sourceLabel"=>"NAV","statusLabel"=>row.State,"net"=>net?.ToString(CultureInfo.InvariantCulture)??"","gross"=>gross?.ToString(CultureInfo.InvariantCulture)??"",_=>""};
            if(!Contains(value,id is "net" or "gross"?text.Replace(" ","").Replace(',','.'):text))return false;
        }
        return true;
    }
}
