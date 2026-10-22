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
internal static class WorkflowUiChecks
{
    public static void Render(string output,string skin)
    {
        var definition=new EntityPageDescriptor("receiving","receiving","Bevételezés","","",[new("number","Számlaszám")],
            [new("number","Számlaszám"),new("supplierName","Szállító"),new("net","Nettó"),new("statusLabel","Állapot")],new("list","save","archive","export")){ViewKind="receiving"};
        var page=new ReceivingPageViewModel(definition,new HostConnection(output,null),()=>null,CancellationToken.None,new ListLayoutStore(Path.Combine(output,"receiving-layout")));
        var view=new ReceivingPageView{DataContext=page,Width=1100};
        Snapshot(view,output,"receiving-home-"+skin,1100,1000);
        var labels=Descendants(view).OfType<Button>().Select(b=>b.Content?.ToString()??"").ToArray();
        if(!labels.Contains("NAV-számlák lekérése")||!labels.Contains("PDF alapján rögzítés")||!labels.Contains("Kimenő számlák"))throw new InvalidOperationException("Nem támogatott importot ígér a felület.");
        page.IsNav=true;typeof(ReceivingPageViewModel).GetMethod("NotifyMode",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(page,null);page.NavInvoices.Add(new("NAV-MINTA-001","Minta nagyker","11111111","2026-10-21","HUF","CREATE","20 000",null));Snapshot(view,output,"nav-invoices-"+skin,1100,1100);if(Descendants(view).OfType<DatePicker>().Count()<2)throw new InvalidOperationException("Hiányzik a NAV-időszakválasztó.");
        page.OutgoingCommand.Execute(null);page.OutgoingInvoices.Add(new("SAJAT-001","Minta vevő","22222222","2026-10-21","2026-10-21","2026-11-01","Átutalás","HUF","issued","Egyeztetve","Nyitott","10 000","2 700","12 700",""));Snapshot(view,output,"outgoing-invoices-"+skin,1100,900);page.IncomingCommand.Execute(null);page.IsNav=false;
        Set(page,"_mode","editor");Set(page,"_editId",Guid.NewGuid());Set(page,"_version",2L);Set(page,"_status","bevetelezve");
        page.Number="BE-2026-001";page.IssuedOn="2026-10-21";
        page.Suppliers.Add(new(Guid.NewGuid(),"Teszt nagyker","12345678-2-42"));page.SelectedSupplier=page.Suppliers[0];
        var line=new LineEditor(page);line.FillQuiet(()=>{line.Name="Vezeték nélküli egér";line.Sku="MOUSE-1";line.UnitCode="DB";line.QuantityText="10";line.PriceText="12345,67";line.VatText="27";});page.Lines.Add(line);
        typeof(ReceivingPageViewModel).GetMethod("NotifyMode",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(page,null);
        Snapshot(view,output,"receiving-editor-"+skin,1100,1800);
        page.ApplyPdfPreview("Minta-szamla.pdf","Számlaszám: PDF-001\nUSB egér, 2 db, nettó 10 000 Ft, áfa 27%\nA tételeket ellenőrizve kell kitölteni.");Snapshot(view,output,"pdf-invoice-"+skin,1100,1800);
        if(page.IsEditable)throw new InvalidOperationException("Lezárt bevételezés szerkeszthető.");
        var billing=new InvoicingSettingsCard{Width=1000};Snapshot(billing,output,"invoicing-settings-"+skin,1100,1150);
        if(Descendants(billing).OfType<PasswordBox>().Count()!=1||!Descendants(billing).OfType<DataGrid>().Any(g=>g.Columns.Any(c=>c.Header?.ToString()=="Tervezett kezdősorszám")))throw new InvalidOperationException("Hiányzó titkos számlázókulcs vagy számlatömb-beállítás.");
        var saleDef=definition with{Id="sales",ModuleId="sales",ViewKind="sales"};
        var sale=new OperationsPageViewModel(saleDef,new HostConnection(output,null),()=>null,CancellationToken.None,new ListLayoutStore(Path.Combine(output,"basket-check")));
        var product=new OperationProduct(Guid.NewGuid(),"Egér","MOUSE-1","DB",5);sale.Products.Add(product);sale.SelectedProduct=product;
        Set(sale,"_stockWarehouse","Bolt");sale.QuantityText="2";sale.PriceText="100";sale.VatText="5";sale.AddLineCommand.Execute(null);
        if(sale.Basket.Count!=1||sale.BasketTotal!="210 Ft")throw new InvalidOperationException("Hibás kosárösszeg.");
        sale.SelectedBasketLine=sale.Basket[0];sale.EditLineCommand.Execute(null);sale.QuantityText="3";sale.PriceText="200";sale.UpdateLineCommand.Execute(null);
        if(sale.Basket.Count!=1||sale.Basket[0].Quantity!=3||sale.Basket[0].NetUnitPrice!=200)throw new InvalidOperationException("Nem frissült a kosártétel.");
        sale.QuantityText="3";sale.AddLineCommand.Execute(null);if(sale.Basket.Count!=1)throw new InvalidOperationException("Az összesített kosár túllépte a jelzett készletet.");
        Console.WriteLine("ui-ok receipt intake, locked document and editable stock-checked basket "+skin);
    }
    private static void Set(object target,string field,object value)=>target.GetType().GetField(field,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(target,value);
    private static void Snapshot(FrameworkElement view,string output,string name,int width,int height)
    {
        var surface=new Border{Background=(Brush)Application.Current.Resources["BackgroundBrush"],Child=view};
        surface.Measure(new Size(width,height));surface.Arrange(new Rect(0,0,width,height));surface.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(surface);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,name+".png")))encoder.Save(file);
        surface.Child=null;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var descendant in Descendants(child))yield return descendant;}
    }
}
