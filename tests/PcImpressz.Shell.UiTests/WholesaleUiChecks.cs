using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;
using PcImpressz.Shell.Windows;
namespace PcImpressz.Shell.UiTests;
internal static class WholesaleUiChecks
{
    private static IEnumerable<DependencyObject> Tree(DependencyObject node)
    {yield return node;for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Tree(VisualTreeHelper.GetChild(node,i)))yield return child;}
    public static async Task VerifyAsync(string output)
    {
        var directory=Path.Combine(output,"wholesale-ui");var vm=new ShellViewModel(new AppearanceStore(directory),directory,null);
        var company=new CompanyInfo(Guid.NewGuid(),"Nagyker tesztcég",false,DateTimeOffset.UtcNow);var saved=false;var tested=false;
        JsonElement Rows()=>JsonSerializer.SerializeToElement(new[]{new{id="compmarket",name="CompMarket",description="XML készletlista · VPS-proxy",used=false,enabled=true,note="Saját nagyker-megjegyzés",prepared=false,
            health=tested?(object)new{attemptedAt=DateTimeOffset.UtcNow,succeededAt=DateTimeOffset.UtcNow,success=true,products=79546,message="Sikeres XML-lekérés."}:null,fields=new[]{new{key="compmarket_pid",label="Ügyfélazonosító (pid)",secret=false,value="test-pid",hasValue=true},new{key="compmarket_authcode",label="Hitelesítő kód",secret=true,value="",hasValue=saved},new{key="nagyker_proxy",label="VPS-proxy",secret=true,value="",hasValue=true}}}},CompanyDocuments.Json);
        Task<JsonElement> Send(string operation,object payload,CancellationToken ct)
        {
            var request=(ModuleCommandRequest)payload;
            if(request.CompanyId!=company.Id)throw new Exception("Hibás cég.");
            if(request.Command=="wholesale-save"){saved=true;if(request.Input.GetProperty("values").GetProperty("compmarket_authcode").GetString()!="test-private-code")throw new Exception("Elveszett a kód.");}
            if(request.Command is "compmarket-test" or "compmarket-job-start")tested=true;
            return Task.FromResult(request.Command=="supplier-status"?JsonSerializer.SerializeToElement(new{proxyUrl="http://217.113.49.52:8888"}):Rows());
        }
        var connection=new HostConnection(directory,null,Send);connection.SetSession(company.Id,new("test","admin","admin",DateTimeOffset.UtcNow.AddMinutes(30)));
        typeof(ShellViewModel).GetField("_connection",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(vm,connection);
        typeof(ShellViewModel).GetField("_selectedCompany",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(vm,company);
        var previous=Application.Current.MainWindow;
        var content=new ContentControl{Content=new SettingsPage()};var window=new Window{DataContext=vm,Content=new ScrollViewer{Content=content},Width=1050,Height=900,Left=-20000,Top=-20000,ShowInTaskbar=false,ShowActivated=false};
        try
        {
            window.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            var tabs=Tree(content).OfType<TabControl>().Single();
            if(tabs.Items.Count!=2||((TabItem)tabs.Items[1]).Header?.ToString()!="Külső szolgáltatók")throw new Exception("Hiányzó szolgáltatók fül.");
            tabs.SelectedIndex=1;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            var card=Tree(content).OfType<ExternalProvidersCard>().Single();
            var expander=Tree(card).OfType<Expander>().Single();expander.IsExpanded=true;window.UpdateLayout();
            var passwords=Tree(card).OfType<PasswordBox>().ToArray();if(passwords.Length!=2)throw new Exception("Hiányzó titkos mező.");
            passwords[0].Password="test-private-code";
            var test=Tree(card).OfType<Button>().Single(b=>b.Content?.ToString()=="Mentés és XML lekérése");test.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            if(!saved||!tested||!card.IsEnabled)throw new Exception("Az automatikus betöltés utáni mentés vagy teszt nem futott le.");
            if(Tree(card).OfType<PasswordBox>().Any(b=>b.Password.Length>0))throw new Exception("Mentés után bent maradt a titkos kód.");
            Tree(card).OfType<Expander>().Single().IsExpanded=true;window.UpdateLayout();
            var bitmap=new RenderTargetBitmap(1050,900,96,96,PixelFormats.Pbgra32);bitmap.Render((Visual)window.Content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,"external-providers.png")))encoder.Save(file);
            Console.WriteLine("ui-ok external providers auto-load, save, test and secret clearing");
        }
        finally{window.Close();Application.Current.MainWindow=previous;vm.Stop();}
    }
}
