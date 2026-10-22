using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;

namespace PcImpressz.Shell.Windows;

public sealed record InvoiceDraftRow(Guid Id,long Version,string Number,string Supplier,string UpdatedAt);
internal sealed partial class ReceivingPageViewModel
{
    private Guid? _manualDraftId;
    private long _manualDraftVersion;
    private static readonly JsonSerializerOptions DraftJson=new(JsonSerializerDefaults.Web);
    public bool IsNavEditor=>IsEditor&&IsNavReceipt;
    public bool IsManualEditor=>IsEditor&&!IsNavReceipt;
    public bool IsDraftList=>_mode=="drafts";
    public ObservableCollection<InvoiceDraftRow> InvoiceDrafts {get;}=[];
    public ICommand DraftsCommand=>new RelayCommand(()=>_=ShowDraftsAsync());
    private void NotifyEditorKind()
    {
        foreach(var name in new[]{nameof(IsNavReceipt),nameof(IsNavEditor),nameof(IsManualEditor),nameof(ShowSupplierPicker),nameof(NavHeader),nameof(CanEditInvoiceFacts),nameof(CanAddInvoiceLine),nameof(CanClassify)})Changed(name);
    }
    private void ResetManualDraft(){_manualDraftId=null;_manualDraftVersion=0;}
    private object CaptureManualDraft()=>new
    {
        editId=_editId,receiptVersion=_version,source=_receiptSource,draftSource=_draftSource,convertToManual=_convertToManual,
        navPreview=_navPreview,origin=_preparationIdentity,number=Number,supplierId=SelectedSupplier?.Id,supplierName=SelectedSupplier?.Name??"",issuedOn=IssuedOn,dueOn=DueOn,notes=Notes,warehouse=Warehouse,pdfName=_pdfName,pdfText=_pdfText,
        lines=Lines.Select(l=>new{l.Id,l.ProductId,l.ProductVersion,l.ProductDirty,l.Name,l.Sku,l.Barcode,l.UnitCode,l.QuantityText,l.PriceText,l.VatText,l.StockItem,l.IsService,l.RetailPrice,l.Markup,l.SaleVat,fields=l.ProductFields.ToDictionary(f=>f.Definition.Id,f=>new{f.Text,f.Checked})}).ToArray()
    };
    private async Task<bool> FlushManualDraftAsync()
    {
        if(_preparationLoading||!IsDirty||!IsEditable||_company() is not Guid company)return true;
        _manualDraftId??=Guid.NewGuid();var id=_manualDraftId.Value;
        await _preparationGate.WaitAsync(_shutdown);
        try
        {
            if(_company()!=company||_manualDraftId!=id||!IsManualEditor||!IsDirty)return true;
            var revision=_preparationRevision;var data=JsonSerializer.SerializeToElement(CaptureManualDraft(),DraftJson);
            var saved=await _connection.SendAsync("module.execute",new PcImpressz.Contracts.ModuleCommandRequest(company,"receiving","draft-save",JsonSerializer.SerializeToElement(new{id,version=_manualDraftVersion,data},DraftJson)),_shutdown);
            if(_company()!=company||_manualDraftId!=id)return true;
            _manualDraftVersion=saved.GetProperty("version").GetInt64();
            if(revision==_preparationRevision){IsDirty=false;PreparationStatus="Kézi piszkozat automatikusan mentve · "+DateTime.Now.ToString("HH:mm:ss");}
            return true;
        }
        catch(Exception ex){PreparationStatus="A kézi piszkozat mentése nem sikerült: "+ex.Message;return false;}
        finally{_preparationGate.Release();}
    }
    private async Task RetireManualDraftAsync(Guid company)
    {
        if(_manualDraftId is not Guid id)return;
        _preparationDelay?.Cancel();await _preparationGate.WaitAsync(_shutdown);
        try
        {
            if(_company()!=company||_manualDraftId!=id)return;
            await ModuleAsync("receiving","draft-delete",new{id,version=_manualDraftVersion});ResetManualDraft();
        }
        catch(Exception ex){PreparationStatus="A számla mentve, de a korábbi piszkozat törlése nem sikerült: "+ex.Message;}
        finally{_preparationGate.Release();}
    }
    private async Task StartManualAsync(){if(IsBusy||!await FlushPreparationAsync()||!CanLeave())return;_navExpanded=false;BeginManual();await LoadCategoriesAsync();}
    private async Task StartImportAsync(){if(IsBusy||!await FlushPreparationAsync()||!CanLeave())return;_navExpanded=false;BeginIntake("import");}
    private async Task ShowNavAsync()
    {
        if(IsBusy||!await FlushPreparationAsync()||!CanLeave())return;
        ClearPreparation();ResetManualDraft();_navPreview=null;_supplier=null;SelectedNavSupplier=null;Lines.Clear();_mode="home";_navExpanded=true;_navSelected=null;Changed(nameof(SelectedNavInvoice));NotifyMode();
    }
    internal async Task MoveNavToManualAsync()
    {
        if(IsBusy||!IsNavEditor||!IsEditable||!await FlushPreparationAsync())return;
        if(_company() is Guid company&&(_preparationIdentity??_navPreview) is {} origin)
        {
            IsBusy=true;InvoiceDraftRow? existing=null;
            try
            {
                var rows=await ModuleAsync("receiving","draft-list",new{});if(_company()!=company)return;
                foreach(var row in rows.EnumerateArray())if(row.GetProperty("data").TryGetProperty("origin",out var identity)&&identity.ValueKind==JsonValueKind.Object&&Get(identity,"number")==origin.Number&&Tax(Get(identity,"supplierTaxNumber"))==Tax(origin.SupplierTaxNumber)){existing=new(row.GetProperty("id").GetGuid(),row.GetProperty("version").GetInt64(),origin.Number,origin.SupplierName,Get(row,"updatedAt"));break;}
            }
            catch(Exception ex){Message="A meglévő kézi piszkozat ellenőrzése nem sikerült: "+ex.Message;return;}
            finally{IsBusy=false;}
            if(existing is not null){await ResumeDraftAsync(existing);return;}
        }
        ResetManualDraft();ConvertToManual();_navExpanded=false;NotifyMode();MarkDirty();
        if(await FlushManualDraftAsync())Message="A NAV-számla összes adata átkerült a külön kézi bevételezésbe. Piszkozatként mentve; a NAV-nézet eredeti adatai változatlanok.";
        await LoadCategoriesAsync();
    }
    private async Task OpenLinkedManualAsync(Guid id)
    {
        if(IsBusy||!await FlushPreparationAsync()||_company() is not Guid company)return;IsBusy=true;
        try
        {
            var drafts=await ModuleAsync("receiving","draft-list",new{});if(_company()!=company)return;
            foreach(var saved in drafts.EnumerateArray())if(saved.GetProperty("data").TryGetProperty("editId",out var edit)&&edit.ValueKind==JsonValueKind.String&&edit.GetGuid()==id)
            {
                var data=saved.GetProperty("data");IsBusy=false;
                await ResumeDraftAsync(new(saved.GetProperty("id").GetGuid(),saved.GetProperty("version").GetInt64(),Get(data,"number"),Get(data,"supplierName"),Get(saved,"updatedAt")));return;
            }
            var detail=await ModuleAsync("receiving","get",new{id});
            if(_company()!=company)return;
            if(Get(detail,"status") is not ("uj" or "fuggo")){Message="A kézi bevételezést már lezárták. A bevételezett számlák listájában megtekinthető.";return;}
            await LoadSuppliersAsync();if(_company()!=company)return;ClearPreparation();Fill(detail);_editorKind="manual";_navExpanded=false;_mode="editor";NotifyMode();
            await LoadLineProductsAsync(company);
            MarkDirty();
        }
        catch(Exception ex){Message=ex.Message;return;}
        finally{IsBusy=false;}
        await FlushManualDraftAsync();
    }
    private async Task ShowDraftsAsync()
    {
        if(IsBusy||!await FlushPreparationAsync()||!CanLeave())return;
        _mode="drafts";_navExpanded=false;NotifyMode();await LoadDraftsAsync();
    }
    internal async Task LoadDraftsAsync()
    {
        if(IsBusy||_company() is not Guid company)return;IsBusy=true;
        try
        {
            var rows=await ModuleAsync("receiving","draft-list",new{});if(_company()!=company)return;
            InvoiceDrafts.Clear();foreach(var row in rows.EnumerateArray()){var data=row.GetProperty("data");InvoiceDrafts.Add(new(row.GetProperty("id").GetGuid(),row.GetProperty("version").GetInt64(),Get(data,"number") is {Length:>0} number?number:"Számlaszám nélkül",Get(data,"supplierName"),Get(row,"updatedAt")));}
            Message=InvoiceDrafts.Count==0?"Nincs félbehagyott kézi számla.":"A piszkozatok készletmozgás nélkül folytathatók vagy törölhetők.";
        }
        catch(Exception ex){Message=ex.Message;}finally{IsBusy=false;}
    }
    internal async Task DeleteDraftAsync(InvoiceDraftRow row)
    {
        if(IsBusy)return;IsBusy=true;
        try{await ModuleAsync("receiving","draft-delete",new{id=row.Id,version=row.Version});InvoiceDrafts.Remove(row);Message="Piszkozat törölve. A készlet nem változott.";}catch(Exception ex){Message=ex.Message;}finally{IsBusy=false;}
    }
    internal async Task ResumeDraftAsync(InvoiceDraftRow row)
    {
        if(IsBusy||!await FlushPreparationAsync()||!CanLeave()||_company() is not Guid company)return;IsBusy=true;
        try
        {
            var saved=await ModuleAsync("receiving","draft-read",new{id=row.Id});if(_company()!=company)return;
            var data=saved.GetProperty("data");
            if(data.TryGetProperty("editId",out var edit)&&edit.ValueKind==JsonValueKind.String)
            {
                var current=await ModuleAsync("receiving","get",new{id=edit.GetGuid()});if(_company()!=company)return;
                if(Get(current,"status") is not ("uj" or "fuggo")||current.GetProperty("version").GetInt64()!=data.GetProperty("receiptVersion").GetInt64())throw new ArgumentException("A kapcsolódó számlát közben módosították vagy lezárták. A piszkozatot megőriztük; a bizonylatot a számlalistában ellenőrizheted.");
            }
            await LoadSuppliersAsync();if(_company()!=company)return;
            ClearPreparation();ResetManualDraft();_preparationLoading=true;_populating=true;_editorKind="manual";_mode="editor";_status="uj";
            _manualDraftId=row.Id;_manualDraftVersion=saved.GetProperty("version").GetInt64();
            _editId=data.TryGetProperty("editId",out edit)&&edit.ValueKind==JsonValueKind.String?edit.GetGuid():null;_version=data.GetProperty("receiptVersion").GetInt64();
            _receiptSource=Get(data,"source");_draftSource=Get(data,"draftSource");_convertToManual=data.TryGetProperty("convertToManual",out var converted)&&converted.GetBoolean();
            _navPreview=data.TryGetProperty("navPreview",out var preview)&&preview.ValueKind==JsonValueKind.Object?preview.Deserialize<NavInvoiceRow>(DraftJson):null;
            _preparationIdentity=data.TryGetProperty("origin",out var origin)&&origin.ValueKind==JsonValueKind.Object?origin.Deserialize<NavInvoiceRow>(DraftJson):null;
            _number=Get(data,"number");_issued=Get(data,"issuedOn");_due=Get(data,"dueOn");_notes=Get(data,"notes");_warehouse=Get(data,"warehouse");_pdfName=Get(data,"pdfName");_pdfText=Get(data,"pdfText");
            _supplier=data.TryGetProperty("supplierId",out var supplier)&&supplier.ValueKind==JsonValueKind.String?Suppliers.FirstOrDefault(s=>s.Id==supplier.GetGuid()):null;
            Lines.Clear();foreach(var item in data.GetProperty("lines").EnumerateArray())
            {
                var line=new LineEditor(this);line.FillQuiet(()=>{line.Id=item.GetProperty("id").GetGuid();line.ProductId=item.TryGetProperty("productId",out var id)&&id.ValueKind==JsonValueKind.String?id.GetGuid():null;line.Name=Get(item,"name");line.Sku=Get(item,"sku");line.Barcode=Get(item,"barcode");line.UnitCode=Get(item,"unitCode");line.QuantityText=Get(item,"quantityText");line.PriceText=Get(item,"priceText");line.VatText=Get(item,"vatText");line.StockItem=item.GetProperty("stockItem").GetBoolean();line.IsService=item.GetProperty("isService").GetBoolean();});Lines.Add(line);
                await LoadLineProductAsync(line);if(_company()!=company)return;
                line.ApplyPrices(Get(item,"retailPrice"),Get(item,"markup"),Get(item,"saleVat"));
                foreach(var field in line.ProductFields)if(item.GetProperty("fields").TryGetProperty(field.Definition.Id,out var value)){field.Text=Get(value,"text");field.Checked=value.GetProperty("checked").GetBoolean();}
                line.ProductDirty=item.GetProperty("productDirty").GetBoolean();if(line.ProductDirty)line.ProductVersion=item.GetProperty("productVersion").GetInt64();
            }
            foreach(var name in new[]{nameof(Number),nameof(IssuedOn),nameof(DueOn),nameof(Notes),nameof(Warehouse),nameof(SelectedSupplier),nameof(PdfText),nameof(PdfName),nameof(HasPdfPreview)})Changed(name);
            IsDirty=false;NotifyMode();UpdateTotals();PreparationStatus="Mentett kézi piszkozat folytatása.";Message="A piszkozat még nem bevételezés. A készletet a Bevételezés gomb módosítja.";
            await LoadCategoriesAsync();
        }
        catch(Exception ex){Message=ex.Message;}finally{_preparationLoading=false;_populating=false;IsBusy=false;}
    }
}
