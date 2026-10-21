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
internal static class DemoUiChecks
{
    public static void Render(string output,string skin)
    {
        var directory=Path.Combine(output,"demo-ui-"+skin);var vm=new ShellViewModel(new AppearanceStore(directory),directory,null);
        ThemeManager.Apply(BuiltInSkins.All.Single(s=>s.Id==skin),new AppearancePreferences{SkinId=skin});
        var company=new CompanyInfo(Guid.NewGuid(),"Demó próbacég",true,DateTimeOffset.UtcNow);
        var companyField=typeof(ShellViewModel).GetField("_selectedCompany",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var connection=(HostConnection)typeof(ShellViewModel).GetField("_connection",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(vm)!;
        var changed=typeof(ShellViewModel).GetMethod("AuthChanged",BindingFlags.Instance|BindingFlags.NonPublic)!;
        companyField.SetValue(vm,company);connection.SetSession(company.Id,new("test","admin","admin",DateTimeOffset.UtcNow.AddMinutes(30)));changed.Invoke(vm,null);
        var card=new DemoDataCard{DataContext=vm};var surface=new Border{Child=card,Padding=new(24),Background=(Brush)Application.Current.Resources["SurfaceBrush"]};
        surface.Measure(new Size(1000,360));surface.Arrange(new Rect(0,0,1000,360));surface.UpdateLayout();
        var button=card.Children.OfType<Button>().Single();if(!button.IsEnabled||!vm.CanSeedDemo)throw new InvalidOperationException("Demóadmin nem használhatja a feltöltést.");
        connection.SetSession(company.Id,new("test","worker","warehouse",DateTimeOffset.UtcNow.AddMinutes(30)));changed.Invoke(vm,null);
        if(button.IsEnabled||vm.CanSeedDemo)throw new InvalidOperationException("A raktáros demóadatot tölthet fel.");
        connection.SetSession(company.Id,new("test","admin","admin",DateTimeOffset.UtcNow.AddMinutes(30)));companyField.SetValue(vm,company with{IsDemo=false});changed.Invoke(vm,null);
        if(card.Visibility!=Visibility.Collapsed||vm.CanSeedDemo)throw new InvalidOperationException("Saját cégben elérhető a demófeltöltés.");
        vm.SeedDemoAsync().GetAwaiter().GetResult();if(!vm.DemoMessage.Contains("demócég adminjaként",StringComparison.Ordinal))throw new InvalidOperationException("A felületi őr nem utasította el az éles céget.");
        companyField.SetValue(vm,company);changed.Invoke(vm,null);typeof(ShellViewModel).GetMethod("ResetDemoMessage",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(vm,null);surface.UpdateLayout();
        var bitmap=new RenderTargetBitmap(1000,360,96,96,PixelFormats.Pbgra32);bitmap.Render(surface);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,"demo-seed-settings-"+skin+".png")))encoder.Save(file);
        vm.Stop();Console.WriteLine("ui-ok demo settings seed button, admin guard and real-company isolation "+skin);
    }
}
