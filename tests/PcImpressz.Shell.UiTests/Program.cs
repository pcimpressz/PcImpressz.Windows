using System.Diagnostics;
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
internal static class Program
{
    private static void VerifyBusyEditor()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pcimpressz-ui-audit-" + Guid.NewGuid().ToString("N"));
        var definition = new EntityPageDescriptor("partners", "partners", "Partnerek", "", "",
            [new("name", "Név")], [new("name", "Név")], new("list", "save", "archive", "export"));
        var connection=new HostConnection(directory,null);connection.SetSession(null,new UiSession("ui-test","admin","admin",DateTimeOffset.UtcNow.AddMinutes(30)));
        var vm = new EntityPageViewModel(definition, connection, () => null,
            CancellationToken.None, new ListLayoutStore(directory));
        var view = new EntityPageView { DataContext = vm };
        view.Measure(new Size(1000, double.PositiveInfinity));
        view.Arrange(new Rect(0, 0, 1000, 1200)); view.UpdateLayout();
        vm.Fields[0].Text = "Mentésre váró név";
        var busy = typeof(EntityPageViewModel).GetProperty(nameof(EntityPageViewModel.IsBusy))!;
        busy.SetValue(vm, true);
        var editorInputs = Descendants(view).OfType<TextBox>().Where(input => input.DataContext is EntityFieldViewModel).ToArray();
        if (editorInputs.Length == 0 || editorInputs.Any(input => input.IsEnabled))
            throw new InvalidOperationException("Mentés közben szerkeszthető adatlap.");
        if (Descendants(view).OfType<DataGrid>().Any(grid => grid.IsEnabled))
            throw new InvalidOperationException("Mentés közben váltható kijelölés.");
        vm.NewCommand.Execute(null);
        vm.SelectedRow = new EntityRow(JsonSerializer.SerializeToElement(new { id = Guid.NewGuid(), version = 1, name = "Másik rekord" }));
        if (vm.Fields[0].Text != "Mentésre váró név" || !vm.IsDirty || vm.SelectedRow is not null)
            throw new InvalidOperationException("Folyamatban lévő művelet felülírta az adatlapot.");
        busy.SetValue(vm, false);
        if (editorInputs.Any(input => !input.IsEnabled)) throw new InvalidOperationException("Az adatlap zárolva maradt.");
        Console.WriteLine("ui-ok busy editor and selection guards");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var descendant in Descendants(child))yield return descendant;}
    }
    [STAThread]
    public static int Main()
    {
        try
        {
            var app=new App();app.InitializeComponent();
            var output=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/ui-checks"));Directory.CreateDirectory(output);
            var bindingErrors=new StringWriter();PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(bindingErrors));PresentationTraceSources.DataBindingSource.Switch.Level=SourceLevels.Error;
            CheckoutUiChecks.VerifyAsync(output).GetAwaiter().GetResult();
            PartnerEditorChecks.VerifyAsync(output).GetAwaiter().GetResult();
            NavInvoiceUiChecks.VerifyAsync(output).GetAwaiter().GetResult();
            VerifyBusyEditor();
            UiProductChecks.VerifyAppliedQuery();
            foreach(var skin in BuiltInSkins.All)
            {
                ThemeManager.Apply(skin,new AppearancePreferences{SkinId=skin.Id});
                UiProductChecks.Render(output,skin.Id);
                AdminUiChecks.Render(output,skin.Id);
                WorkflowUiChecks.Render(output,skin.Id);
                RoleUiChecks.Render(output,skin.Id);
                DemoUiChecks.Render(output,skin.Id);
                CheckoutUiChecks.Render(output,skin.Id);
                PartnerEditorChecks.Render(output,skin.Id);
                foreach(var kind in new[]{"inventory","sales"})
                {
                    var definition=new EntityPageDescriptor(kind,kind,kind,"","",[new("name","Név")],
                        kind=="inventory"?[new("productName","Termék"),new("warehouse","Raktár"),new("quantity","Készlet"),new("unitCode","Egység")]
                        :[new("number","Bizonylat"),new("customerName","Vevő"),new("gross","Bruttó"),new("statusLabel","Állapot")],new("list","save","reverse","export")){ViewKind=kind};
                    var vm=new OperationsPageViewModel(definition,new HostConnection(output,null),()=>null,CancellationToken.None,new ListLayoutStore(Path.Combine(output,skin.Id+kind)));
                    var product=new OperationProduct(Guid.NewGuid(),"Vezeték nélküli egér","MOUSE-1","DB");vm.Products.Add(product);vm.SelectedProduct=product;
                    vm.Customers.Add(new(null,"Alkalmi vevő"));vm.SelectedCustomer=vm.Customers[0];
                    if(kind=="sales")vm.Basket.Add(new(product.Id,product.Name,"DB",2,1250,27));
                    vm.Rows.Add(new EntityRow(JsonSerializer.SerializeToElement(kind=="inventory"?(object)new{productName=product.Name,warehouse="Bolt",quantity=12.5m,unitCode="DB"}:new{number="EL-2026-000001",customerName="Alkalmi vevő",gross=3175m,statusLabel="Végleges"})));
                    var view=new ContentControl{Content=vm,Width=1000};
                    view.Measure(new Size(1000,double.PositiveInfinity));var height=Math.Min(2500,Math.Max(900,view.DesiredSize.Height));
                    view.Arrange(new Rect(0,0,1000,height));view.UpdateLayout();
                    if(view.ActualWidth<900||view.ActualHeight<500)throw new InvalidOperationException("A nézet nem rendeződött el.");
                    var surface=new Border{Background=(Brush)app.Resources["BackgroundBrush"],Child=view,Width=1000,Height=height};surface.Measure(new Size(1000,height));surface.Arrange(new Rect(0,0,1000,height));surface.UpdateLayout();
                    foreach(var grid in Descendants(surface).OfType<DataGrid>())
                        if(grid.Columns.Any(c=>c.Visibility==Visibility.Visible&&c.ActualWidth<60))throw new InvalidOperationException("Összeesett táblázatoszlop.");
                    var bitmap=new RenderTargetBitmap(1000,(int)Math.Ceiling(height),96,96,PixelFormats.Pbgra32);bitmap.Render(surface);
                    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,kind+"-"+skin.Id+".png"));encoder.Save(file);
                    Console.WriteLine("ui-ok "+kind+" "+skin.Id);
                }
            }
            PresentationTraceSources.DataBindingSource.Flush();
            if(bindingErrors.ToString().Length>0)throw new InvalidOperationException(bindingErrors.ToString());
            Console.WriteLine("66/66 WPF renders passed; no binding errors.");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
