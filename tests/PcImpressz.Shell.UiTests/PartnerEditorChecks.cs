using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;
using PcImpressz.Shell.Windows;
namespace PcImpressz.Shell.UiTests;
internal static class PartnerEditorChecks
{
    private static EntityPageDescriptor Definition=>new("partners","partners","Partnertörzs","","",[new("name","Cégnév",Required:true),new("taxId","Adószám"),new("country","Ország"),new("postalCode","Irányítószám"),new("city","Település"),new("address","Székhely"),new("email","E-mail"),new("industry","Iparág"),new("creditLimit","Hitelkeret","decimal"),new("notes","Megjegyzés","multiline")],[new("name","Partner"),new("taxId","Adószám")],new("list","save","archive","export"));
    public static async Task VerifyAsync(string output)
    {
        var company=Guid.NewGuid();var lookups=0;
        Task<JsonElement> Send(string operation,object input,CancellationToken ct)
        {
            if(operation!="partners.taxpayer")throw new InvalidOperationException("Váratlan írás: "+operation);lookups++;
            return Task.FromResult(JsonSerializer.SerializeToElement(new BillingCustomer(Guid.Empty,"NAV Teszt Kft.","12345678-2-42","HU","1111","Budapest","Teszt utca 1."),new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
        var connection=new HostConnection(output,null,Send);connection.SetSession(company,new("test","admin","admin",DateTimeOffset.UtcNow.AddMinutes(30)));
        var page=new EntityPageViewModel(Definition,connection,()=>company,CancellationToken.None,new ListLayoutStore(Path.Combine(output,"partner-check")));
        page.Fields.Single(f=>f.Definition.Id=="taxId").Text="12345678";page.Fields.Single(f=>f.Definition.Id=="industry").Text="Megőrzendő bővített adat";
        await page.LookupPartnerNavAsync();if(lookups!=1||!page.IsDirty||page.Fields[0].Text!="NAV Teszt Kft."||page.Fields.Single(f=>f.Definition.Id=="industry").Text!="Megőrzendő bővített adat")throw new InvalidOperationException("Hibás NAV-adatlapkitöltés.");
        page.ConfirmDiscardOverride=()=>false;page.NewCommand.Execute(null);if(!page.IsDirty||page.Fields[0].Text!="NAV Teszt Kft.")throw new InvalidOperationException("A Nem válasz elvesztette az adatlapot.");
        page.SelectedRow=new EntityRow(JsonSerializer.SerializeToElement(new{id=Guid.NewGuid(),version=1,name="Másik partner"}));if(page.SelectedRow is not null||page.Fields[0].Text!="NAV Teszt Kft.")throw new InvalidOperationException("A Nem válasz partnerkiválasztáskor elvesztette az adatlapot.");
        var shell=new ShellViewModel(new AppearanceStore(Path.Combine(output,"partner-close")),output,null);
        var pages=(Dictionary<string,IWorkspacePage>)typeof(ShellViewModel).GetField("_entityPages",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(shell)!;pages.Add("partners.partners",page);
        if(shell.CanClose(()=>false)||shell.CanClose(()=>false)||!page.IsDirty)throw new InvalidOperationException("A Nem válasz engedte a kilépést.");shell.Stop();
        page.ConfirmDiscardOverride=()=>true;page.DiscardChangesCommand.Execute(null);if(page.IsDirty||page.Fields[0].Text.Length!=0)throw new InvalidOperationException("Az explicit elvetés nem állította vissza az adatlapot.");
        Console.WriteLine("ui-ok partner NAV fills draft only, preserves extended fields and No cancels discard and exit");
    }
    public static void Render(string output,string skin)
    {
        var page=new EntityPageViewModel(Definition,new HostConnection(output,null),()=>null,CancellationToken.None,new ListLayoutStore(Path.Combine(output,"partner-layout-"+skin)));
        var view=new EntityPageView{DataContext=page};var surface=new Border{Child=view,Padding=new(20),Background=(Brush)Application.Current.Resources["BackgroundBrush"]};
        Arrange();var grid=Descendants(view).OfType<DataGrid>().Single();var input=Descendants(view).OfType<TextBox>().First(t=>t.DataContext is EntityFieldViewModel f&&f.Definition.Id=="name");
        if(input.TransformToAncestor(view).Transform(new Point()).Y<=grid.TransformToAncestor(view).Transform(new Point(0,grid.ActualHeight)).Y||grid.ActualWidth<900)throw new InvalidOperationException("A szerkesztő nem a teljes szélességű lista alatt van.");
        var advanced=Descendants(view).OfType<Expander>().Single(e=>e.Header?.ToString()=="Bővített adatok kitöltése");if(advanced.IsExpanded)throw new InvalidOperationException("A bővített mezők alapból nyitva vannak.");Snapshot("partner-basic-");advanced.IsExpanded=true;Arrange();Snapshot("partner-extended-");
        if(!Descendants(view).OfType<TextBox>().Any(t=>t.DataContext is EntityFieldViewModel f&&f.Definition.Id=="industry"&&t.ActualHeight>0))throw new InvalidOperationException("A bővített adat nem nyitható meg.");
        Console.WriteLine("ui-ok full-width partner list, editor below and collapsible advanced fields "+skin);
        void Arrange(){surface.Measure(new Size(1100,1900));surface.Arrange(new Rect(0,0,1100,1900));surface.UpdateLayout();}
        void Snapshot(string name){var bitmap=new RenderTargetBitmap(1100,1900,96,96,PixelFormats.Pbgra32);bitmap.Render(surface);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name+skin+".png"));encoder.Save(file);}
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root){for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
}