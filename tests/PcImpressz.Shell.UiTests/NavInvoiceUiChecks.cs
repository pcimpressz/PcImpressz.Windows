using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;
using PcImpressz.Shell.Windows;
namespace PcImpressz.Shell.UiTests;
internal static class NavInvoiceUiChecks
{
    public static async Task VerifyAsync(string output)
    {
        var company=Guid.NewGuid();var product=Guid.NewGuid();var supplier=Guid.NewGuid();var receipt=Guid.NewGuid();var lineId=Guid.NewGuid();var previews=0;var writes=0;var imports=0;var receiptSaves=0;JsonElement savedData=default;JsonElement? preparation=null;var preparationVersion=0L;var preparationWrites=0;var partnerCreates=0;var productVersion=7L;
        var holdProductSave=false;var delayed=new TaskCompletionSource<JsonElement>();JsonElement delayedResult=default;
        Task<JsonElement> Send(string operation,object input,CancellationToken ct)
        {
            if(operation=="ui.workspace.read")return Task.FromResult(JsonSerializer.SerializeToElement(new UiWorkspaceLayout(0,[],[])));
            var request=(ModuleCommandRequest)input;
            if(request.Command=="products-save"&&holdProductSave){holdProductSave=false;delayedResult=JsonSerializer.SerializeToElement(SaveProduct());return delayed.Task;}
            if(request.Command=="preparation-read")return Task.FromResult(preparation is null?JsonSerializer.SerializeToElement<object?>(null):JsonSerializer.SerializeToElement(new{version=preparationVersion,data=preparation.Value}));
            object result=request.Command switch
            {
                "list"=>request.ModuleId=="partners"?(object)new{total=partnerCreates>0?1:0,rows=partnerCreates>0?new object[]{new{id=supplier,name="Új szállító",taxId="11111111-2-42"}}:Array.Empty<object>()}:new{total=0,rows=Array.Empty<object>(),counts=new{uj=0,bevetelezve=0,koltseg=0,fuggo=0}},
                "nav-list"=>new{currentPage=request.Input.GetProperty("page").GetInt32(),availablePage=2,rows=new[]{new{number="NAV-1",supplierName="Új szállító",supplierTaxNumber="11111111",issuedOn="2026-10-21",currency="HUF",operation="CREATE",net=200m,received=false}}},
                "products-schema"=>new EntityField[]{new("name","Név"),new("sku","Cikkszám"),new("unitCode","Egység"),new("barcode","Vonalkód"),new("manufacturerPartNumber","Gyártói cikkszám"),new("description","Leírás","multiline"),new("isActive","Aktív","boolean",DefaultChecked:true),new("saleVatRate","Eladási áfa","decimal",DefaultText:"27"),new("saleNetUnitPrice","Eladási nettó","decimal")},
                "products-get"=>GetProduct(),
                "nav-preview"=>Preview(),
                "products-save"=>SaveProduct(),
                "nav-import"=>Import(),
                "save"=>request.ModuleId=="partners"?SavePartner():SaveReceipt(),
                "preparation-save"=>SavePreparation(),
                _=>Unexpected()
            };
            return Task.FromResult(JsonSerializer.SerializeToElement(result,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            object Unexpected(){writes++;throw new InvalidOperationException("Váratlan írás vagy parancs: "+request.Command);}
            object SaveProduct(){savedData=request.Input.GetProperty("data").Clone();if(request.Input.GetProperty("version").GetInt64()!=productVersion)throw new InvalidOperationException("Hiányzó verzióellenőrzés.");return new{id=product,version=++productVersion,sku="M-1",barcode="",unitCode="DB"};}
            object GetProduct()=>new{id=product,version=productVersion,name="Törzsben tárolt név",sku="M-1",unitCode="DB",barcode="",manufacturerPartNumber="MPN-123",description="Megőrzendő leírás",isActive=true,saleNetUnitPrice=savedData.ValueKind==JsonValueKind.Object?savedData.GetProperty("saleNetUnitPrice").GetDecimal():1000m,saleVatRate=27m};
            object SavePartner(){partnerCreates++;return new{id=supplier,version=1};}
            object SavePreparation(){if(request.Input.GetProperty("version").GetInt64()!=preparationVersion)throw new InvalidOperationException("Előkészítési verzióütközés.");preparation=request.Input.GetProperty("data").Clone();preparationWrites++;return new{version=++preparationVersion,data=preparation.Value};}
            object Import(){imports++;return Detail(1);}
            object SaveReceipt(){receiptSaves++;if(request.Input.GetProperty("id").GetGuid()!=receipt||request.Input.GetProperty("version").GetInt64()!=1||request.Input.GetProperty("draft").GetProperty("supplierId").GetGuid()!=supplier)throw new InvalidOperationException("A NAV-előnézet mentése nem a megfelelő számlát módosítja.");return Detail(2);}
            object Detail(long version)=>new{id=receipt,version,source="nav",stockPosted=false,status="uj",number="NAV-1",issuedOn="2026-10-21",dueOn="",warehouse="Bolt",net=200m,gross=254m,supplierId=supplier,supplierName="Új szállító",supplierTaxId="11111111",lines=new[]{new{id=lineId,productId=(Guid?)product,name="Szállítói megnevezés",sku="M-1",barcode="",unitCode="DB",quantity=2m,netUnitPrice=100m,vatRate=27m,match="megvan",matchLabel="Párosítva"}}};
            object Preview(){previews++;return new{document=new{issuedOn="2026-10-21",dueOn=""},receipt=(object?)null,lines=new[]{new{id=Guid.NewGuid(),productId=(Guid?)product,name="Szállítói megnevezés",sku="M-1",barcode="",unitCode="DB",quantity=2m,netUnitPrice=100m,vatRate=27m,match="megvan"}}};}
        }
        var connection=new HostConnection(output,null,Send);connection.SetSession(company,new("test","admin","admin",DateTimeOffset.UtcNow.AddMinutes(30)));
        var definition=new EntityPageDescriptor("receiving","receiving","Számlák","","",[],[new("number","Bizonylat")],new("list","save","reverse","export")){ViewKind="receiving"};
        var page=new ReceivingPageViewModel(definition,connection,()=>company,CancellationToken.None,new ListLayoutStore(Path.Combine(output,"nav-ui-check")));
        page.NavFrom=new DateTime(2026,10,1);page.NavTo=new DateTime(2026,10,21);await page.FetchNavAsync(1);
        await page.FetchNavAsync(2);if(!page.NavPageLabel.StartsWith("2 / 2"))throw new InvalidOperationException("Hibás lapozás.");
        page.IsNav=true;page.SelectedNavInvoice=page.NavInvoices.Single();
        if(previews!=1||writes!=0||!page.IsEditor||page.SelectedSupplier is null||partnerCreates!=1||page.Lines.Count!=1||page.IsDirty||!page.IsNav)throw new InvalidOperationException("Ismeretlen szállítóval nem működik az automatikus, írás nélküli előnézet: "+page.Message);
        var line=page.Lines.Single();if(line.RetailPrice!="1270"||line.ProductFields.Single(f=>f.Definition.Id=="manufacturerPartNumber").Text!="MPN-123")throw new InvalidOperationException("Hiányzó kisker ár vagy termékadat.");
        line.SelectedMatch=new(product,"Törzsben tárolt név","M-1","","DB");if(line.Name!="Szállítói megnevezés")throw new InvalidOperationException("A párosítás átírta a NAV-tétel megnevezését.");
        line.Markup="30";line.StockItem=false;if(line.RetailPrice!="165"||!line.ProductDirty||!page.IsDirty)throw new InvalidOperationException("Hibás haszonkulcsos árképzés vagy módosításjelzés.");
        await page.FlushPreparationAsync();if(preparationWrites!=1||page.IsDirty||imports!=0||preparation!.Value.GetProperty("lines")[0].GetProperty("stockItem").GetBoolean())throw new InvalidOperationException("Az előkészítés nem mentődött készletmozgás nélkül.");
        if(savedData.ValueKind!=JsonValueKind.Object||savedData.GetProperty("description").GetString()!="Megőrzendő leírás"||savedData.GetProperty("name").GetString()!="Törzsben tárolt név"||savedData.GetProperty("manufacturerPartNumber").GetString()!="MPN-123"||savedData.GetProperty("saleNetUnitPrice").GetDecimal()!=129.9213m||line.ProductDirty)throw new InvalidOperationException("Az ármentés elvesztette a terméktörzs adatait: "+page.Message);
        line.RetailPrice="180";holdProductSave=true;var pendingSave=page.FlushPreparationAsync();if(pendingSave.IsCompleted)throw new InvalidOperationException("A késleltetett mentési próba nem várakozott.");
        line.RetailPrice="200";delayed.SetResult(delayedResult);await pendingSave;
        if(line.RetailPrice!="200"||!line.ProductDirty||!page.IsDirty)throw new InvalidOperationException("A lassú mentési válasz elvesztette az újabb árat.");
        await page.FlushPreparationAsync();if(savedData.GetProperty("saleNetUnitPrice").GetDecimal()!=157.4803m)throw new InvalidOperationException("Az újabb ár nem mentődött.");
        line.RetailPrice="165";await page.FlushPreparationAsync();
        await page.DownloadNavAsync(true);line=page.Lines.Single();if(line.StockItem||line.RetailPrice!="165"||page.IsDirty||partnerCreates!=1)throw new InvalidOperationException("Az előkészítés újranyitáskor elveszett: "+page.PreparationStatus);
        if(page.CanEditInvoiceFacts||page.CanAddInvoiceLine)throw new InvalidOperationException("A NAV-számla eredeti sorai szerkeszthetők.");
        var raster=NiimbotLabelPrinter.Raster("2012345678901","Teszt címke");if(raster.Length!=320||raster.Any(r=>r.Length!=12)||!raster.SelectMany(r=>r).Any(b=>b!=0))throw new InvalidOperationException("Hibás címkekép.");
        if(!NiimbotLabelPrinter.Packet(0x23,1).SequenceEqual(new byte[]{0x55,0x55,0x23,1,1,0x23,0xaa,0xaa}))throw new InvalidOperationException("Hibás Bluetooth-csomag.");
        ThemeManager.Apply(BuiltInSkins.All.Single(s=>s.Id=="paper"),new AppearancePreferences{SkinId="paper"});
        page.NavInvoices.Add(new("NAV-RECEIVED","Már bevételezett szállító","22222222","2026-10-21","HUF","CREATE","1000",null,Guid.NewGuid(),true));
        var view=new ReceivingPageView{DataContext=page,Width=1200};var surface=new Border{Child=view,Background=(Brush)Application.Current.Resources["BackgroundBrush"]};
        surface.Measure(new Size(1200,1900));surface.Arrange(new Rect(0,0,1200,1900));surface.UpdateLayout();
        var grids=Descendants(surface).OfType<DataGrid>().ToArray();var items=grids.Single(g=>ReferenceEquals(g.ItemsSource,page.Lines));
        if(items.Columns.Count<9||items.HeadersVisibility!=DataGridHeadersVisibility.Column)throw new InvalidOperationException("Hiányos tételtáblázat.");
        items.SelectedIndex=0;surface.UpdateLayout();var itemRow=(DataGridRow)items.ItemContainerGenerator.ContainerFromItem(line);items.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent,Source=itemRow});if(itemRow.DetailsVisibility!=Visibility.Visible)throw new InvalidOperationException("A tétel nem nyílt meg.");items.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent,Source=itemRow});if(itemRow.DetailsVisibility!=Visibility.Collapsed)throw new InvalidOperationException("A második kattintás nem csukta be a tételt.");itemRow.DetailsVisibility=Visibility.Visible;surface.UpdateLayout();
        var nav=grids.Single(g=>ReferenceEquals(g.ItemsSource,page.NavInvoices));var received=(DataGridRow)nav.ItemContainerGenerator.ContainerFromItem(page.NavInvoices.Last());var pending=(DataGridRow)nav.ItemContainerGenerator.ContainerFromItem(page.NavInvoices.First());
        if(((SolidColorBrush)received.Background).Color!=Color.FromRgb(217,242,220)||((SolidColorBrush)pending.Background).Color!=Colors.White)throw new InvalidOperationException("Hibás NAV-állapotszínezés.");
        var bitmap=new RenderTargetBitmap(1200,1900,96,96,PixelFormats.Pbgra32);bitmap.Render(surface);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,"nav-preview-retail.png")))encoder.Save(file);
        view.DataContext=null;surface.Child=null;
        page.SelectedSupplier=page.Suppliers.Single();
        await page.SaveAsync();if(imports!=1||receiptSaves!=1||page.IsDirty||page.Lines.Count!=1)throw new InvalidOperationException("A szállító kiválasztása után nem menthető a NAV-előnézet: "+page.Message);
        page.ApplyPdfPreview("teszt.pdf","PDF tesztszöveg");page.CompanyChanged();if(page.HasPdfPreview||page.IsNav||page.Lines.Count!=0)throw new InvalidOperationException("Cégváltás után megmaradt az előnézet.");
        Console.WriteLine("ui-ok automatic supplier, durable preparation restore, bidirectional pricing, NAV fact locks, row toggle and Bluetooth packet/raster without printing");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var descendant in Descendants(child))yield return descendant;}
    }
}
