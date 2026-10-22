using System.IO;
using System.Text.Json;
using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;
using PcImpressz.Shell.Windows;

namespace PcImpressz.Shell.UiTests;
internal static class InvoiceDraftUiChecks
{
    public static async Task VerifyAsync(string output)
    {
        var company=Guid.NewGuid();var orink=Guid.NewGuid();var sma=Guid.NewGuid();var receipt=Guid.NewGuid();var created=false;var stockWrites=0;
        var drafts=new Dictionary<Guid,JsonElement>();var preparations=new Dictionary<string,JsonElement>();
        Task<JsonElement> Send(string operation,object input,CancellationToken ct)
        {
            if(operation=="ui.workspace.read")return Task.FromResult(JsonSerializer.SerializeToElement(new UiWorkspaceLayout(0,[],[])));
            var request=(ModuleCommandRequest)input;var data=request.Input;
            object result=request.Command switch
            {
                "list" when request.ModuleId=="partners"=>new{total=created?2:1,rows=created?new object[]{Partner(orink,"Orink","11111111"),Partner(sma,"SMA Rendszerház","22222222")}:new object[]{Partner(orink,"Orink","11111111")}},
                "list"=>new{total=0,rows=Array.Empty<object>(),counts=new{uj=0,bevetelezve=0,koltseg=0,fuggo=0}},
                "save" when request.ModuleId=="partners"=>Create(),
                "nav-list"=>new{currentPage=1,availablePage=1,rows=new[]{Invoice("ORINK","Orink","11111111"),Invoice("SMA","SMA Rendszerház","22222222")}},
                "nav-preview"=>Preview(data.GetProperty("number").GetString()!),
                "get"=>new{id=receipt,version=1,source="manual",status="uj",number="MENTETT-KÉZI-SMA",issuedOn="2026-10-22",dueOn="2026-11-01",warehouse="Bolt",notes="kézi megjegyzés",net=300m,gross=381m,supplierId=sma,supplierName="SMA Rendszerház",supplierTaxId="22222222",lines=new[]{new{id=Guid.NewGuid(),productId=(Guid?)null,name="Mentett kézi sor",sku="",barcode="",unitCode="DB",quantity=3m,netUnitPrice=100m,vatRate=27m,match="fuggo"}}},
                "products-schema"=>new EntityField[]{new("name","Név"),new("sku","Cikkszám"),new("barcode","Vonalkód"),new("unitCode","Egység"),new("categoryCode","Kategória"),new("saleVatRate","Áfa","decimal",DefaultText:"27")},
                "products-suggest"=>new{rows=Array.Empty<object>()},
                "categories-list"=>new{total=2,rows=new[]{new{code="TONER",name="Tonerek",isActive=true},new{code="PAPER",name="Papír",isActive=true}}},
                "preparation-read"=>preparations.TryGetValue(data.GetProperty("number").GetString()!,out var prep)?(object)prep:null!,
                "preparation-save"=>SavePreparation(),
                "draft-save"=>SaveDraft(),
                "draft-list"=>drafts.Values.ToArray(),
                "draft-read"=>drafts[data.GetProperty("id").GetGuid()],
                "draft-delete"=>DeleteDraft(),
                _=>Unexpected()
            };
            return Task.FromResult(JsonSerializer.SerializeToElement(result,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            object Partner(Guid id,string name,string taxId)=>new{id,name,taxId,version=1};
            object Create(){if(data.GetProperty("data").GetProperty("taxId").GetString()!="22222222")throw new InvalidOperationException("Az előző számla adószámával próbál új szállítót létrehozni.");created=true;return Partner(sma,"SMA Rendszerház","22222222");}
            object Invoice(string number,string supplierName,string supplierTaxNumber)=>new{number,supplierName,supplierTaxNumber,issuedOn="2026-10-22",currency="HUF",operation="CREATE",net=100m,received=false};
            object Preview(string number)=>new{document=new{issuedOn="2026-10-22",dueOn="2026-11-01"},receipt=(object?)null,lines=new[]{new{id=Guid.NewGuid(),productId=(Guid?)null,name=number+" termék",sku="",barcode="",unitCode="DB",quantity=2m,netUnitPrice=100m,vatRate=27m,match="fuggo"}}};
            object SavePreparation(){var key=data.GetProperty("number").GetString()!;var saved=JsonSerializer.SerializeToElement(new{version=data.GetProperty("version").GetInt64()+1,data=data.GetProperty("data")});preparations[key]=saved;return saved;}
            object SaveDraft(){var id=data.GetProperty("id").GetGuid();var version=drafts.TryGetValue(id,out var old)?old.GetProperty("version").GetInt64():0;if(version!=data.GetProperty("version").GetInt64())throw new InvalidOperationException("Hibás piszkozatverzió.");var saved=JsonSerializer.SerializeToElement(new{id,version=version+1,data=data.GetProperty("data"),updatedAt=DateTimeOffset.UtcNow});drafts[id]=saved;return saved;}
            object DeleteDraft(){drafts.Remove(data.GetProperty("id").GetGuid());return new{deleted=true};}
            object Unexpected(){stockWrites++;throw new InvalidOperationException("Piszkozatkezelés közben váratlan számla-/készletírás: "+request.Command);}
        }
        var connection=new HostConnection(output,null,Send);connection.SetSession(company,new("test","admin","admin",DateTimeOffset.UtcNow.AddMinutes(30)));
        var definition=new EntityPageDescriptor("receiving","receiving","Számlák","","",[],[new("number","Számla")],new("list","save","reverse","export")){ViewKind="receiving"};
        ReceivingPageViewModel Page()=>new(definition,connection,()=>company,CancellationToken.None,new ListLayoutStore(Path.Combine(output,"draft-workflow")));
        var page=Page();await page.FetchNavAsync(1);page.IsNav=true;page.SelectedNavInvoice=page.NavInvoices[0];
        if(page.SelectedSupplier?.Id!=orink||!page.IsNavEditor||page.CanAddInvoiceLine)throw new InvalidOperationException("Hibás első NAV-nézet.");
        page.SelectedNavInvoice=page.NavInvoices[1];
        if(!created||page.SelectedSupplier?.Id!=sma||!page.IsNavEditor||page.IsManualEditor||page.CanAddInvoiceLine)throw new InvalidOperationException("Az Orink szállító megmaradt az SMA-számlán: "+page.Message);
        var count=page.Lines.Count;page.AddLineCommand.Execute(null);if(page.Lines.Count!=count)throw new InvalidOperationException("NAV-számlához új tételsor adható.");
        await page.MoveNavToManualAsync();if(!page.IsManualEditor||page.IsNavEditor||!page.CanAddInvoiceLine||page.Number!="SMA"||page.SelectedSupplier?.Id!=sma||page.Lines.Single().Name!="SMA termék"||drafts.Count!=1)throw new InvalidOperationException("A kézi átemelés nem őrizte meg a számla minden adatát: "+page.PreparationStatus);
        page.Number="KÉZI-SMA";page.Lines[0].Name="Szerkesztett megnevezés";page.AddLineCommand.Execute(null);page.Lines[1].Name="Még hiányos sor";
        page.NavCommand.Execute(null);if(page.IsDirty||!page.IsNav||page.IsEditor||drafts.Count!=1)throw new InvalidOperationException("A NAV-ra váltás nem mentette el a hiányos kézi piszkozatot: "+page.PreparationStatus);
        page.SelectedNavInvoice=page.NavInvoices[1];if(page.Number!="SMA"||page.Lines.Count!=1||page.Lines[0].Name!="SMA termék"||!page.IsNavEditor||page.CanAddInvoiceLine)throw new InvalidOperationException("A kézi másolat átírta az eredeti NAV-nézetet.");
        await page.MoveNavToManualAsync();if(page.Number!="KÉZI-SMA"||drafts.Count!=1||page.Lines.Count!=2)throw new InvalidOperationException("Az ismételt kézi átemelés új, üres másolatot készített a meglévő piszkozat helyett.");page.NavCommand.Execute(null);
        var reopened=Page();await reopened.LoadDraftsAsync();if(reopened.InvoiceDrafts.Count!=1)throw new InvalidOperationException("Új munkamenetben hiányzik a kézi piszkozat.");await reopened.ResumeDraftAsync(reopened.InvoiceDrafts[0]);
        if(!reopened.IsManualEditor||reopened.Number!="KÉZI-SMA"||reopened.SelectedSupplier?.Id!=sma||reopened.Lines.Count!=2||reopened.Lines[1].Name!="Még hiányos sor"||reopened.IsDirty)throw new InvalidOperationException("A hiányos kézi piszkozat nem folytatható: "+reopened.Message);
        VerifyManualLayout(reopened,output);
        reopened.DraftsCommand.Execute(null);await reopened.DeleteDraftAsync(reopened.InvoiceDrafts.Single());if(drafts.Count!=0||stockWrites!=0)throw new InvalidOperationException("A piszkozat törlése készletet vagy számlát módosított.");
        var linked=page.NavInvoices[1] with{ReceiptId=receipt};page.NavInvoices[1]=linked;page.SelectedNavInvoice=linked;
        if(!page.IsNavEditor||page.IsEditable||page.CanAddInvoiceLine||page.Number!="SMA"||page.Lines.Single().Name!="SMA termék")throw new InvalidOperationException("A már kézire mentett számla adatai belekeveredtek az eredeti NAV-nézetbe.");
        await page.NavActionAsync(linked,"manual");if(!page.IsManualEditor||page.Number!="MENTETT-KÉZI-SMA"||page.Lines.Single().Name!="Mentett kézi sor"||page.Notes!="kézi megjegyzés")throw new InvalidOperationException("A korábban áthelyezett kézi bevételezés nem folytatható: "+page.Message);
        Console.WriteLine("ui-ok Orink to SMA supplier isolation, separate NAV/manual editors, immutable NAV lines, durable partial draft resume/delete without stock writes");
    }
    private static IEnumerable<System.Windows.DependencyObject> Children(System.Windows.DependencyObject parent)
    {
        for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);i++){var child=System.Windows.Media.VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var descendant in Children(child))yield return descendant;}
    }
    private static void VerifyManualLayout(ReceivingPageViewModel page,string output)
    {
        ThemeManager.Apply(BuiltInSkins.All.Single(s=>s.Id=="paper"),new AppearancePreferences{SkinId="paper"});
        var view=new ReceivingPageView{DataContext=page,Width=1100};view.Measure(new(1100,1800));view.Arrange(new(0,0,1100,1800));view.UpdateLayout();
        var grid=Children(view).OfType<System.Windows.Controls.DataGrid>().Single(g=>ReferenceEquals(g.ItemsSource,page.Lines)&&g.Visibility==System.Windows.Visibility.Visible);
        if(!grid.Columns.OfType<System.Windows.Controls.DataGridTextColumn>().Single(c=>(c.Binding as System.Windows.Data.Binding)?.Path.Path=="Name").IsReadOnly)throw new InvalidOperationException("A megnevezés még mindig a táblázatban szerkeszthető.");
        var row=(System.Windows.Controls.DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);row.DetailsVisibility=System.Windows.Visibility.Visible;view.UpdateLayout();
        var details=Children(row).ToArray();var name=details.OfType<System.Windows.Controls.TextBox>().Single(b=>b.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.ParentBinding.Path.Path=="Name");
        if(name.IsReadOnly||name.Text!="Szerkesztett megnevezés")throw new InvalidOperationException("A külön kézi névmező nem szerkeszthető.");
        var barcode=details.OfType<System.Windows.Controls.TextBox>().Single(b=>b.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.ParentBinding.Path.Path=="Barcode");
        var generate=details.OfType<System.Windows.Controls.Button>().Single(b=>Equals(b.ToolTip,"Vonalkód generálása"));
        if(generate.ActualHeight>35||Math.Abs(generate.TransformToAncestor(view).Transform(new(0,0)).Y-barcode.TransformToAncestor(view).Transform(new(0,0)).Y)>2)throw new InvalidOperationException("A vonalkódgomb túl nagy vagy nincs egy vonalban a mezővel.");
        var categories=details.OfType<System.Windows.Controls.ComboBox>().Single(b=>b.GetBindingExpression(System.Windows.Controls.ComboBox.TextProperty)?.ParentBinding.Path.Path=="Category");if(categories.Items.Count!=2)throw new InvalidOperationException("Hiányzik a kategóriatörzs ajánlása.");
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(1100,1800,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,"manual-invoice-draft.png")))encoder.Save(file);view.DataContext=null;
    }
}
