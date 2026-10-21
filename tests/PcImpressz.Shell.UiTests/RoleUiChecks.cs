using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;
using PcImpressz.Shell.Windows;
namespace PcImpressz.Shell.UiTests;
internal static class RoleUiChecks
{
    public static void Render(string output,string skin)
    {
        var directory=Path.Combine(output,"roles-"+skin);
        var vm=new ShellViewModel(new AppearanceStore(directory),directory,null);
        ThemeManager.Apply(BuiltInSkins.All.Single(s=>s.Id==skin),new AppearancePreferences{SkinId=skin});
        typeof(ShellViewModel).GetField("_selectedCompany",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(vm,new CompanyInfo(Guid.NewGuid(),"Másolati próbacég",true,DateTimeOffset.UtcNow));
        vm.LoginName="raktaros";
        var login=(LoginPanel)vm.CurrentPage;
        var surface=Surface(login,820,660);
        if(Descendants(login).OfType<PasswordBox>().Count()!=2)throw new InvalidOperationException("Hiányzó rejtett jelszó és megerősítés.");
        Save(surface,output,"company-login-"+skin,820,660);
        surface.Child=null;
        var window=new MainWindow(vm);var shell=(FrameworkElement)window.Content;window.Content=null;shell.DataContext=vm;
        var fullShell=Surface(shell,1280,900);
        if(Descendants(shell).OfType<PasswordBox>().Count()!=2)throw new InvalidOperationException("A teljes alkalmazásban nem jelenik meg a belépőpanel.");
        Save(fullShell,output,"company-login-shell-"+skin,1280,900);window.Close();
        var connection=new HostConnection(directory,null);connection.SetSession(Guid.NewGuid(),new("ui-test","seller","sales",DateTimeOffset.UtcNow.AddMinutes(30)));
        var definition=new EntityPageDescriptor("products","catalog","Termékadatlap","","",[new("name","Termék",Required:true),new("sku","Saját cikkszám")],[new("name","Termék")],new("list","save","archive","export"));
        var page=new EntityPageViewModel(definition,connection,()=>null,CancellationToken.None,new ListLayoutStore(directory));
        page.Fields[0].Text="Eladóként megtekinthető termék";
        if(page.CanWrite||page.CanAdmin||page.CanExport)throw new InvalidOperationException("Eladó törzsadatszerkesztési jogot kapott.");
        var view=new EntityPageView{DataContext=page};var readOnly=Surface(view,1000,1000);
        if(Descendants(view).OfType<TextBox>().Where(t=>t.DataContext is EntityFieldViewModel).Any(t=>t.IsEnabled))throw new InvalidOperationException("Az eladónak írható termékmező jelent meg.");
        connection.SetSession(Guid.NewGuid(),new("ui-test","warehouse","warehouse",DateTimeOffset.UtcNow.AddMinutes(30)));page.PermissionsChanged();
        if(!page.CanWrite||page.CanAdmin)throw new InvalidOperationException("Hibás raktáros szerkesztési jog.");
        connection.SetSession(null,new("ui-test","seller","sales",DateTimeOffset.UtcNow.AddMinutes(30)));page.PermissionsChanged();
        Save(readOnly,output,"seller-readonly-products-"+skin,1000,1000);vm.Stop();
        Console.WriteLine("ui-ok company login, hidden passwords and role-aware product editor "+skin);
    }
    private static Border Surface(FrameworkElement view,int width,int height)
    {
        var surface=new Border{Child=view,Padding=new(24),Background=(Brush)Application.Current.Resources["BackgroundBrush"]};surface.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,"TextBrush");surface.Measure(new Size(width,height));surface.Arrange(new Rect(0,0,width,height));surface.UpdateLayout();return surface;
    }
    private static void Save(FrameworkElement surface,string output,string name,int width,int height)
    {
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(surface);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name+".png"));encoder.Save(file);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
}
