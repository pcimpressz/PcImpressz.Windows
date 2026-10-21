using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;
using PcImpressz.Shell.Windows;
namespace PcImpressz.Shell.UiTests;
internal static class CheckoutUiChecks
{
    private static EntityPageDescriptor Definition=>new("sales","sales","Eladás","","",[new("name","Név")],[new("number","Bizonylat")],new("list","save","reverse","export")){ViewKind="sales"};
    public static async Task VerifyAsync(string output)
    {
        var moneyStore=new AppearanceStore(Path.Combine(output,"money-preferences"));moneyStore.Save(new AppearancePreferences{MoneyDecimals=3});if(moneyStore.Load().MoneyDecimals!=3)throw new InvalidOperationException("A tizedesbeállítás nem maradt meg.");
        MoneyDisplay.Decimals=0;var precise=new SaleBasketLine(Guid.NewGuid(),"Pontos ár","DB",1,100.1234m,27);var exact=precise.NetUnitPrice;
        if(precise.GrossPriceDisplay.Contains(','))throw new InvalidOperationException("Az alap megjelenítés nem 0 tizedes.");MoneyDisplay.Decimals=2;if(!precise.GrossPriceDisplay.Contains(',')||precise.NetUnitPrice!=exact)throw new InvalidOperationException("A formázás megváltoztatta az árat.");MoneyDisplay.Decimals=0;
        var company=Guid.NewGuid();var product=Guid.NewGuid();var saved=new List<JsonElement>();var fail=true;
        async Task<JsonElement> Send(string operation,object payload,CancellationToken ct)
        {
            var request=(ModuleCommandRequest)payload;
            if(request.Command=="save")
            {
                saved.Add(request.Input.Clone());if(fail){fail=false;throw new IOException("Simulated lost response");}
                return JsonSerializer.SerializeToElement(new{number="EL-TEST-001",paymentMethod="card",invoiceRequested=false});
            }
            if(request.Command=="get")throw new HostCommandException("Az eladás nem található.","not-found");
            if(request.Command=="list")return JsonSerializer.SerializeToElement(new{rows=Array.Empty<object>()});
            var query=request.Input.TryGetProperty("search",out var s)?s.GetString():"";
            if(query=="old")await Task.Delay(350,CancellationToken.None);else await Task.Delay(10,ct);
            var name=query=="old"?"Régi, késő válasz":"Egér";
            return JsonSerializer.SerializeToElement(new{products=new[]{new{id=product,name,sku="M-1",barcode="BC-1",manufacturerPartNumber="MPN-1",unitCode="DB",availableQuantity=5m,netUnitPrice=100m,vatRate=27m,priceSource="Terméktörzs"}},customers=Array.Empty<object>()});
        }
        var connection=new HostConnection(output,null,Send);connection.SetSession(company,new("test","seller","sales",DateTimeOffset.UtcNow.AddMinutes(30)));
        var page=new OperationsPageViewModel(Definition,connection,()=>company,CancellationToken.None,new ListLayoutStore(Path.Combine(output,"quick-test")));
        page.ProductSearch="old";await Task.Delay(260);page.ProductSearch="eger";await Task.Delay(700);
        if(page.QuickMatches.Count!=1||page.QuickMatches[0].Name!="Egér")throw new InvalidOperationException("A késői válasz felülírta az aktuális keresést.");
        if(!page.AddQuickProduct(page.QuickMatches[0])||page.Basket.Count!=1)throw new InvalidOperationException("A kattintás nem helyezte kosárba.");
        page.ProductSearch="BC-1";await Task.Delay(400);await page.EnterQuickProductAsync();
        if(page.Basket.Count!=1||page.Basket[0].Quantity!=2)throw new InvalidOperationException("A vonalkód és Enter duplázott vagy nem került a kosárba.");
        var line=page.Basket[0];line.QuantityText="3";line.GrossPriceText="1270";if(line.NetUnitPrice!=1000||line.Gross!=3810)throw new InvalidOperationException("A kosár közvetlen szerkesztése hibás összeget számol.");
        line.QuantityText="-1";try{page.ValidateCheckout();throw new InvalidOperationException("Hibás mennyiséggel véglegesíthető.");}catch(ArgumentException){}line.QuantityText="3";
        if(await page.CommitCheckoutAsync("card",false,null)||!page.HasUnresolvedOperation||page.CanEditSale)throw new InvalidOperationException("Bizonytalan küldés után szerkeszthető maradt a kosár.");
        line.RemoveCommand.Execute(null);if(page.Basket.Count!=1)throw new InvalidOperationException("Bizonytalan küldés után törölhető a kosár.");
        if(!await page.CommitCheckoutAsync("cash",false,null)||page.Basket.Count!=0||page.HasUnresolvedOperation)throw new InvalidOperationException("Az azonos művelet újrapróbálása nem zárult le.");
        if(saved.Count!=2||saved[0].GetRawText()!=saved[1].GetRawText()||saved[1].GetProperty("paymentMethod").GetString()!="card")throw new InvalidOperationException("Az újrapróbálás megváltoztatta a fizetést vagy a kosarat.");
        page.AddQuickProduct(new(product,"Egér","M-1","DB",5,NetUnitPrice:100));page.Basket[0].RemoveCommand.Execute(null);if(page.Basket.Count!=0)throw new InvalidOperationException("A sor saját törlésgombja nem működik.");
        JsonElement? recoveredPayload=null;var recoveryPosts=0;
        async Task<JsonElement> ReadCommitted(string operation,object payload,CancellationToken ct)
        {
            var request=(ModuleCommandRequest)payload;
            if(request.Command=="save"){recoveryPosts++;recoveredPayload=request.Input.Clone();throw new IOException("Response lost after commit");}
            if(request.Command=="get")
            {
                var record=System.Text.Json.Nodes.JsonNode.Parse(recoveredPayload!.Value.GetRawText())!.AsObject();record["id"]=record["operationId"]!.DeepClone();record["number"]="EL-ALREADY-SAVED";record["status"]="vegleges";return JsonSerializer.SerializeToElement(record);
            }
            return await Send(operation,payload,ct);
        }
        var recoveredConnection=new HostConnection(output,null,ReadCommitted);recoveredConnection.SetSession(company,new("test","seller","sales",DateTimeOffset.UtcNow.AddMinutes(30)));
        var recoveredPage=new OperationsPageViewModel(Definition,recoveredConnection,()=>company,CancellationToken.None,new ListLayoutStore(Path.Combine(output,"recovered-test")));
        recoveredPage.AddQuickProduct(new(product,"Egér","M-1","DB",5,NetUnitPrice:100));
        if(await recoveredPage.CommitCheckoutAsync("card",false,null))throw new InvalidOperationException("A szimulált válaszvesztés nem maradt bizonytalan.");
        if(!await recoveredPage.CommitCheckoutAsync("cash",false,null)||recoveryPosts!=1||recoveredPage.HasUnresolvedOperation)throw new InvalidOperationException("A már mentett eladást az olvasás nem oldotta fel újraküldés nélkül.");

        var queued=new OperationsPageViewModel(Definition,connection,()=>company,CancellationToken.None,new ListLayoutStore(Path.Combine(output,"queued-test")));
        queued.ProductSearch="BC-1";var firstScan=queued.EnterQuickProductAsync();queued.ProductSearch="BC-1";var secondScan=queued.EnterQuickProductAsync();await Task.WhenAll(firstScan,secondScan);
        if(queued.Basket.Sum(line=>line.Quantity)!=2||!queued.CanBeginCheckout)throw new InvalidOperationException("Az egymás utáni gyors beolvasások elvesztek vagy duplázódtak.");
        Console.WriteLine("ui-ok live search race, barcode single addition, inline edits, row deletion and immutable retry");
    }
    public static void Render(string output,string skin)
    {
        var connection=new HostConnection(output,null);connection.SetSession(Guid.NewGuid(),new("test","seller","sales",DateTimeOffset.UtcNow.AddMinutes(30)));
        var page=new OperationsPageViewModel(Definition,connection,()=>null,CancellationToken.None,new ListLayoutStore(Path.Combine(output,"pos-"+skin)));
        page.Basket.Add(new(Guid.NewGuid(),"USB optikai egér","DB",2,1990,27));page.Basket.Add(new(Guid.NewGuid(),"UTP hálózati kábel","M",1.5m,190,27));
        page.QuickMatches.Add(new(Guid.NewGuid(),"USB optikai egér","DEMO-001","DB",12,"DEMO-BC-001","DEMO-MPN-001",1990));
        var view=new OperationsPageView{DataContext=page};Snapshot(view,output,"quick-sales-"+skin,1100,1050);
        var texts=Descendants(view).OfType<Button>().Select(b=>b.Content?.ToString()??"").ToArray();if(texts.Any(t=>t=="Választók frissítése"||t=="Tétel hozzáadása"||t=="Kijelölt tétel cseréje"))throw new InvalidOperationException("Régi többlépéses eladási gomb maradt a nézetben.");
        var basket=Descendants(view).OfType<DataGrid>().First();if(basket.IsReadOnly||basket.Columns.Count(c=>!c.IsReadOnly)<3)throw new InvalidOperationException("A kosár nem szerkeszthető közvetlenül.");
        var dialog=CheckoutDialog.Build(page);var root=(FrameworkElement)dialog.Content;dialog.Content=null;root.Measure(new Size(800,730));root.Arrange(new Rect(0,0,800,730));root.UpdateLayout();
        Descendants(root).OfType<CheckBox>().Single().IsChecked=true;Snapshot(root,output,"quick-checkout-"+skin,800,730);dialog.Close();
        Console.WriteLine("ui-ok quick POS cart and cash/card invoice-customer checkout "+skin);
    }
    private static void Snapshot(FrameworkElement root,string output,string name,int width,int height)
    {
        var surface=new Border{Child=root,Background=(Brush)Application.Current.Resources["BackgroundBrush"],Padding=new(18)};surface.Measure(new Size(width,height));surface.Arrange(new Rect(0,0,width,height));surface.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(surface);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,name+".png")))encoder.Save(file);surface.Child=null;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root){for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var next in Descendants(child))yield return next;}}
}
