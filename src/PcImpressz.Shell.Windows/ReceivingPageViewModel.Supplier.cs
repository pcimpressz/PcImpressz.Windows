using System.Text.Json;
using System.Windows.Input;
namespace PcImpressz.Shell.Windows;
internal sealed partial class ReceivingPageViewModel
{
    public ICommand EnrichSupplierCommand=>new RelayCommand(()=>_=EnrichSupplierManuallyAsync());
    private async Task EnrichSupplierManuallyAsync()
    {
        if(IsBusy||_company() is not Guid company||(_preparationIdentity??SelectedNavInvoice) is not {} invoice)return;
        IsBusy=true;try{await EnrichSupplierAsync(invoice,company);MarkDirty();}catch(Exception ex){Message=ex.Message;}finally{IsBusy=false;}
    }
    private async Task EnrichSupplierAsync(NavInvoiceRow invoice,Guid company)
    {
        if(Tax(invoice.SupplierTaxNumber)!="23126806")return;
        var result=await ModuleAsync("receiving","supplier-enrich",new{number=invoice.Number,supplierTaxNumber=invoice.SupplierTaxNumber});
        if(_company()!=company)return;if(!result.GetProperty("configured").GetBoolean()){Message="A CompMarket API még nincs beállítva. A Beállításokban add meg a kapcsolat adatait.";return;}
        var rows=result.GetProperty("rows").EnumerateArray().ToArray();var matched=0;
        static string Fold(string s)=>new(s.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        foreach(var line in Lines)
        {
            var candidates=rows.Where(r=>Fold(Get(r,"name"))==Fold(line.Name)||(line.Barcode.Length>0&&Get(r,"barcode")==line.Barcode)||(line.Sku.Length>0&&Get(r,"sku")==line.Sku)).ToArray();
            if(candidates.Length!=1)continue;var row=candidates[0];matched++;
            if(string.IsNullOrWhiteSpace(line.Barcode))line.Barcode=Get(row,"barcode");
            if(line.ProductId is null&&string.IsNullOrWhiteSpace(line.Sku))line.Sku=Get(row,"sku");
            if(line.ProductId is null)await SuggestLineProductsAsync(line,true);
            await LoadLineProductAsync(line);
            foreach(var (id,value) in new[]{("barcode",line.Barcode),("manufacturerPartNumber",Get(row,"manufacturerPartNumber"))})
            {var field=line.ProductFields.FirstOrDefault(f=>f.Definition.Id==id);if(field is not null&&string.IsNullOrWhiteSpace(field.Text)&&value.Length>0)field.Text=value;}
            var category=Get(row,"category");if(category.Length>0){var field=line.ProductFields.FirstOrDefault(f=>f.Definition.Id=="parameters");if(field is not null&&!field.Text.Contains("Szállítói kategória=",StringComparison.Ordinal))field.Text+=(field.Text.Length>0?"\n":"")+"Szállítói kategória="+category;}
        }
        Message=$"CompMarket: {matched} tétel azonosítóit sikerült egyértelműen egyeztetni. A számla mennyiségei és beszerzési árai változatlanok.";
    }
    private async Task SuggestLineProductsAsync(LineEditor line,bool selectExact=true)
    {
        var result=await ModuleAsync("catalog","products-suggest",new{name=line.Name,sku=line.Sku,barcode=line.Barcode});
        line.Matches.Clear();var rows=result.GetProperty("rows").EnumerateArray().ToArray();
        foreach(var row in rows)line.Matches.Add(new(row.GetProperty("id").GetGuid(),Get(row,"name"),Get(row,"sku"),Get(row,"barcode"),Get(row,"unitCode")));
        var exact=rows.Where(r=>r.GetProperty("exact").GetBoolean()).ToArray();
        if(selectExact&&exact.Length==1&&(_convertToManual||line.UnitCode.Equals(Get(exact[0],"unitCode"),StringComparison.OrdinalIgnoreCase)))
        {
            var row=exact[0];line.FillQuiet(()=>{line.ProductId=row.GetProperty("id").GetGuid();line.Sku=Get(row,"sku");line.Barcode=Get(row,"barcode");line.UnitCode=Get(row,"unitCode");});line.ShowMatch("Automatikus egyezés a terméktörzsben");
        }
        else line.ShowMatch(line.Matches.Count>0?"Hasonló termékek: válassz a javaslatokból, vagy vegyél fel újat.":"Nincs egyezés. Új termékként felveszed?");
    }
}
