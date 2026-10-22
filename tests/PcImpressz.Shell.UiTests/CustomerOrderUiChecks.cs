using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PcImpressz.Contracts;
using PcImpressz.Shell.Windows;
namespace PcImpressz.Shell.UiTests;
internal static class CustomerOrderUiChecks
{
    private static EntityPageDescriptor Definition=>new("orders","sales","Vevői rendelések","","",[new("number","Rendelés")],[new("number","Rendelés")],new("orders-list","orders-save","orders-cancel","orders-list")){ViewKind="orders"};
    private static CustomerOrder Sample(Guid customer,Guid product)=>new(Guid.NewGuid(),2,"VR-2026-000001",customer,"Teszt Vevő","Bolt","2026-12-01","Részátadásra előkészített rendelés","partial",[new(Guid.NewGuid(),product,"Vezeték nélküli egér","MOUSE-1","DB",3,100,27,1,2)],300,81,381,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,"seller",DateTimeOffset.UtcNow.AddDays(7),[new(Guid.NewGuid(),"EL-2026-000001",DateTimeOffset.UtcNow,[])]);
    public static async Task VerifyAsync(string output)
    {
        Guid? company=Guid.NewGuid();var customer=Guid.NewGuid();var product=Guid.NewGuid();var writes=new List<OrderSave>();CustomerOrder? saved=null;var lose=true;
        Task<JsonElement> Send(string operation,object payload,CancellationToken ct)
        {
            var request=(ModuleCommandRequest)payload;
            if(request.Command=="orders-save")
            {
                var input=request.Input.Deserialize<OrderSave>(CompanyDocuments.Json)!;writes.Add(input);
                saved=new(input.Id,1,"VR-TEST-1",input.CustomerId,"Teszt Vevő",input.Warehouse,input.DueDate,input.Notes,"open",input.Lines.Select(l=>new CustomerOrderLine(l.Id,l.ProductId,"Egér","M-1","DB",l.Quantity,l.NetUnitPrice,l.VatRate)).ToArray(),100,27,127,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,"seller");
                if(lose){lose=false;throw new IOException("Simulated response loss");}return Task.FromResult(JsonSerializer.SerializeToElement(saved,CompanyDocuments.Json));
            }
            if(request.Command=="orders-list")return Task.FromResult(JsonSerializer.SerializeToElement(new{rows=saved is null?Array.Empty<CustomerOrder>():new[]{saved},total=saved is null?0:1},CompanyDocuments.Json));
            return Task.FromResult(JsonSerializer.SerializeToElement(new{products=new[]{new OrderProductOption(product,"Egér","M-1","DB",100,27,10,3,7)},customers=new[]{new OrderCustomerOption(customer,"Teszt Vevő")}},CompanyDocuments.Json));
        }
        var connection=new HostConnection(output,null,Send);connection.SetSession(company,new("test","seller","sales",DateTimeOffset.UtcNow.AddMinutes(30)));
        var page=new OrdersPageViewModel(Definition,connection,()=>company,CancellationToken.None);await page.ReferencesAsync();page.SelectedCustomer=page.Customers.Single();page.SelectedProduct=page.Products.Single();page.AddLineCommand.Execute(null);page.Notes="Mentésre váró rendelés";page.SaveCommand.Execute(null);
        if(!page.HasUnresolvedOperation||page.CanEdit||page.CanAct)throw new InvalidOperationException("A bizonytalan rendelésművelet nem zárolta a szerkesztőt.");
        await page.RunPendingAsync();
        if(page.HasUnresolvedOperation||page.IsDirty||writes.Count!=2||writes[0].RequestId!=writes[1].RequestId||writes[0].Id!=writes[1].Id||page.Rows.Count!=1)throw new InvalidOperationException("A rendelés újrapróbálása új azonosítót vagy hibás állapotot kapott.");
        connection.SetSession(company,new("view","viewer","user",DateTimeOffset.UtcNow.AddMinutes(30)));page.PermissionsChanged();page.SaveCommand.Execute(null);page.ReserveCommand.Execute(null);
        if(page.CanEdit||page.CanAct||writes.Count!=2)throw new InvalidOperationException("A megtekintő módosíthat rendelést.");
        company=null;page.CompanyChanged();if(page.Notes.Length>0||page.Lines.Count>0||page.Rows.Count>0||page.SelectedCustomer is not null)throw new InvalidOperationException("Cégváltáskor rendelési adatok maradtak a képernyőn.");
        Console.WriteLine("ui-ok orders lost-response replay, roles and company cleanup");
    }
    public static void Render(string output,string skin)
    {
        PcImpressz.Shell.Windows.ThemeManager.Apply(PcImpressz.Shell.Customization.BuiltInSkins.All.Single(s=>s.Id==skin),new PcImpressz.Shell.Customization.AppearancePreferences{SkinId=skin});
        var company=Guid.NewGuid();var sample=Sample(Guid.NewGuid(),Guid.NewGuid());
        Task<JsonElement> Send(string operation,object payload,CancellationToken ct)=>Task.FromResult(((ModuleCommandRequest)payload).Command=="orders-list"?JsonSerializer.SerializeToElement(new{rows=new[]{sample},total=1},CompanyDocuments.Json):JsonSerializer.SerializeToElement(sample,CompanyDocuments.Json));
        var connection=new HostConnection(output,null,Send);connection.SetSession(company,new("test","seller","sales",DateTimeOffset.UtcNow.AddMinutes(30)));
        var page=new OrdersPageViewModel(Definition,connection,()=>company,CancellationToken.None);page.LoadAsync().GetAwaiter().GetResult();page.SelectedOrder=page.Rows.Single();
        var view=new OrdersPageView{DataContext=page};var surface=new Border{Background=(Brush)Application.Current.Resources["BackgroundBrush"],Child=view,Width=1100,Height=1350};
        surface.Measure(new Size(1100,1350));surface.Arrange(new Rect(0,0,1100,1350));surface.UpdateLayout();
        if(page.CanEdit||!page.CanDeliver||!page.DeliveryHistory.Contains("EL-2026-000001"))throw new InvalidOperationException("A részátadási nézet állapota hibás.");
        var image=new RenderTargetBitmap(1100,1350,96,96,PixelFormats.Pbgra32);image.Render(surface);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(output,"orders-"+skin+".png"));encoder.Save(file);Console.WriteLine("ui-ok customer orders "+skin);
    }
}
