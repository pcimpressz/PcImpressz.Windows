using System.Text.Json;
using System.Windows.Input;

namespace PcImpressz.Shell.Windows;
internal sealed partial class ReceivingPageViewModel
{
    private NavInvoiceRow? _preparationIdentity;
    private long _preparationVersion,_preparationRevision;
    private bool _preparationLoading,_convertToManual;
    private string _receiptSource="manual",_preparationStatus="";
    private CancellationTokenSource? _preparationDelay;
    private readonly SemaphoreSlim _preparationGate=new(1,1);
    public string PreparationStatus {get=>_preparationStatus;private set{_preparationStatus=value;Changed();}}
    public bool IsNavReceipt=>!_convertToManual&&(_navPreview is not null||_receiptSource=="nav");
    public bool CanEditInvoiceFacts=>IsEditable&&!IsNavReceipt;
    public bool CanAddInvoiceLine=>CanEditInvoiceFacts;
    public ICommand ConvertToManualCommand=>new RelayCommand(()=>{ConvertToManual();MarkDirty();});
    private void ConvertToManual()
    {
        if(!IsEditable)return;
        _convertToManual=true;Changed(nameof(ShowSupplierPicker));Changed(nameof(NavHeader));Changed(nameof(CanClassify));Changed(nameof(IsNavReceipt));Changed(nameof(CanEditInvoiceFacts));Changed(nameof(CanAddInvoiceLine));PreparationStatus="Kézi bevételezés: az eredeti NAV-adatoktól eltérő szerkesztés.";
    }
    private void SchedulePreparation()
    {
        if(_preparationLoading||_preparationIdentity is null||!IsEditor||!IsEditable)return;
        _preparationRevision++;PreparationStatus="Előkészítés mentése…";
        _preparationDelay?.Cancel();_preparationDelay?.Dispose();_preparationDelay=new();_ = SavePreparationAfterDelay(_preparationDelay.Token);
    }
    private async Task SavePreparationAfterDelay(CancellationToken token)
    {
        try{await Task.Delay(650,token);await FlushPreparationAsync();}catch(OperationCanceledException){}
    }
    internal async Task<bool> FlushPreparationAsync()
    {
        if(_preparationIdentity is not {} identity||_preparationLoading||!IsDirty||_company() is not Guid company)return true;
        await _preparationGate.WaitAsync(_shutdown);
        try
        {
            if(_preparationIdentity!=identity||_company()!=company||!IsDirty)return true;
            var revision=_preparationRevision;
            var data=new{manual=_convertToManual,number=Number,supplierId=SelectedSupplier?.Id,issuedOn=IssuedOn,dueOn=DueOn,warehouse=Warehouse,notes=Notes,pdfName=_pdfName,pdfText=_pdfText,
                lines=Lines.Select(l=>new{l.ProductId,l.Name,l.Sku,l.Barcode,l.UnitCode,l.QuantityText,l.PriceText,l.VatText,l.StockItem,l.IsService,l.RetailPrice,l.Markup,l.SaleVat,fields=l.ProductFields.ToDictionary(f=>f.Definition.Id,f=>new{f.Text,f.Checked})}).ToArray()};
            var result=await _connection.SendAsync("module.execute",new PcImpressz.Contracts.ModuleCommandRequest(company,"receiving","preparation-save",JsonSerializer.SerializeToElement(new{number=identity.Number,supplierTaxNumber=identity.SupplierTaxNumber,version=_preparationVersion,data},new JsonSerializerOptions(JsonSerializerDefaults.Web))),_shutdown);
            if(_preparationIdentity!=identity||_company()!=company)return true;
            _preparationVersion=result.GetProperty("version").GetInt64();
            if(revision==_preparationRevision){IsDirty=false;PreparationStatus="Előkészítés automatikusan mentve · "+DateTime.Now.ToString("HH:mm:ss");}
            // The durable preparation survives even if a catalog validation fails.
            if(!IsBusy)
                foreach(var line in Lines.Where(l=>l.ProductId is not null&&l.ProductDirty).ToArray())
                {
                    await SaveLineProductAsync(line,false,true);
                    if(_company()!=company||_preparationIdentity!=identity)return true;
                }
            if(revision==_preparationRevision)IsDirty=false;
            return true;
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){PreparationStatus="Nem sikerült az automatikus mentés: "+ex.Message;return false;}
        finally{_preparationGate.Release();}
    }
    private async Task RestorePreparationAsync(NavInvoiceRow identity,Guid company)
    {
        _preparationLoading=true;_preparationIdentity=identity;_preparationVersion=0;_preparationRevision=0;
        try
        {
            var result=await ModuleAsync("receiving","preparation-read",new{number=identity.Number,supplierTaxNumber=identity.SupplierTaxNumber});
            if(_company()!=company||_preparationIdentity!=identity)return;
            if(result.ValueKind==JsonValueKind.Null){PreparationStatus="A módosításokat automatikusan mentjük; készletre vétel később is elvégezhető.";return;}
            _preparationVersion=result.GetProperty("version").GetInt64();
            var data=result.GetProperty("data");var rows=data.GetProperty("lines").EnumerateArray().ToArray();
            var manual=data.TryGetProperty("manual",out var converted)&&converted.GetBoolean();
            if(!manual&&(rows.Length!=Lines.Count||rows.Where((r,i)=>Get(r,"name")!=Lines[i].Name||Get(r,"quantityText")!=Lines[i].QuantityText||Get(r,"priceText")!=Lines[i].PriceText).Any()))throw new ArgumentException("Az előkészítés eredeti tételsorai eltérnek a NAV-számlától.");
            _populating=true;
            if(manual){ConvertToManual();_number=Get(data,"number");_issued=Get(data,"issuedOn");if(data.TryGetProperty("supplierId",out var supplier)&&supplier.ValueKind==JsonValueKind.String)_supplier=Suppliers.FirstOrDefault(s=>s.Id==supplier.GetGuid());}
            _pdfName=Get(data,"pdfName");_pdfText=Get(data,"pdfText");Changed(nameof(PdfName));Changed(nameof(PdfText));Changed(nameof(HasPdfPreview));_due=Get(data,"dueOn");_notes=Get(data,"notes");_warehouse=Get(data,"warehouse");
            if(manual){Lines.Clear();foreach(var row in rows){var line=new LineEditor(this);line.FillQuiet(()=>{line.Name=Get(row,"name");line.QuantityText=Get(row,"quantityText");line.PriceText=Get(row,"priceText");line.VatText=Get(row,"vatText");});Lines.Add(line);}}
            for(var i=0;i<rows.Length;i++)
            {
                var row=rows[i];var line=Lines[i];
                line.FillQuiet(()=>{line.ProductId=row.TryGetProperty("productId",out var id)&&id.ValueKind==JsonValueKind.String?id.GetGuid():null;line.Sku=Get(row,"sku");line.Barcode=Get(row,"barcode");line.UnitCode=Get(row,"unitCode");line.StockItem=!row.TryGetProperty("stockItem",out var stock)||stock.GetBoolean();line.IsService=row.TryGetProperty("isService",out var service)&&service.GetBoolean();});
                line.ProductLoaded=false;await LoadLineProductAsync(line);
                line.ApplyPrices(Get(row,"retailPrice"),Get(row,"markup"),Get(row,"saleVat"));
                if(row.TryGetProperty("fields",out var fields))foreach(var field in line.ProductFields)if(fields.TryGetProperty(field.Definition.Id,out var saved)){field.Text=Get(saved,"text");field.Checked=saved.GetProperty("checked").GetBoolean();}
                line.ProductDirty=false;
            }
            Changed(nameof(Number));Changed(nameof(SelectedSupplier));Changed(nameof(IssuedOn));Changed(nameof(DueOn));Changed(nameof(Notes));Changed(nameof(Warehouse));
            PreparationStatus="Korábbi, mentett előkészítés visszatöltve.";
        }
        finally{_populating=false;_preparationLoading=false;IsDirty=false;UpdateTotals();}
    }
    private void ClearPreparation()
    {
        _preparationDelay?.Cancel();_preparationIdentity=null;_preparationVersion=0;_convertToManual=false;PreparationStatus="";
        Changed(nameof(ShowSupplierPicker));Changed(nameof(NavHeader));Changed(nameof(CanClassify));Changed(nameof(IsNavReceipt));Changed(nameof(CanEditInvoiceFacts));Changed(nameof(CanAddInvoiceLine));
    }
    private async Task SelectNavAsync(NavInvoiceRow value)
    {
        if(!await FlushPreparationAsync()||IsBusy)return;
        if(!CanLeave())return;
        _navSelected=value;Changed(nameof(SelectedNavInvoice));await DownloadNavAsync(true);
    }
    private async Task ClosePreparedEditorAsync()
    {
        if(!await FlushPreparationAsync())return;Back();
    }
}
