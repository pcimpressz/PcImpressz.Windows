using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;
using PcImpressz.Shell.Windows;
namespace PcImpressz.Shell.UiTests;
internal static class OrderSettingsRegressionChecks
{
    private static IEnumerable<DependencyObject> Tree(DependencyObject node)
    {yield return node;for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Tree(VisualTreeHelper.GetChild(node,i)))yield return child;}
    public static async Task VerifyAsync(string output)
    {
        var workspace=new OperationWorkspace(Path.Combine(output,"settings-switch-051"),()=>true);
        var content=new ContentControl();var previousMain=Application.Current.MainWindow;
        var window=new Window{Content=content,DataContext=new SettingsHost(workspace),Width=1000,Height=900,Left=-20000,Top=-20000,Opacity=0,ShowInTaskbar=false,ShowActivated=false};
        try
        {
            window.Show();OperationSettingsView? first=null;
            foreach(var groupId in new[]{"wholesale","mail","printer","wholesale","printer","mail"})
            {
                content.Content=new OperationSettingsPage(groupId);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
                var view=Tree(content).OfType<OperationSettingsView>().Single();first??=view;
                var title=OperationCatalog.Find(groupId)!.Sections[0].Title;
                if(!Tree(view).OfType<TextBlock>().Any(t=>t.Text==title))throw new InvalidOperationException("Beakadt beállításváltás: "+groupId);
                if(view.DataContext is not OperationSettingsPage p||p.GroupId!=groupId||view.Workspace!=workspace)throw new InvalidOperationException("A beállításoldal elvesztette a csoportját.");
            }
            content.Content=null;
            var company=Guid.NewGuid();var filters=new List<string>();var saves=new List<OrderSave>();
            Task<JsonElement> Send(string operation,object payload,CancellationToken ct)
            {
                var r=(ModuleCommandRequest)payload;
                if(r.Command=="orders-list"){filters.Add(r.Input.GetProperty("status").GetString()!);return Task.FromResult(JsonSerializer.SerializeToElement(new{rows=Array.Empty<CustomerOrder>(),total=0}));}
                if(r.Command=="orders-save")
                {
                    var request=r.Input.Deserialize<OrderSave>(CompanyDocuments.Json)!;saves.Add(request);
                    var order=new CustomerOrder(request.Id,1,"VR-051",Guid.Empty,request.Customer!.Name,"Bolt","","","open",[],0,0,0,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,"seller",Customer:request.Customer);
                    return Task.FromResult(JsonSerializer.SerializeToElement(order,CompanyDocuments.Json));
                }
                return Task.FromResult(JsonSerializer.SerializeToElement(new{products=Array.Empty<object>(),customers=Array.Empty<object>()}));
            }
            var connection=new HostConnection(output,null,Send);connection.SetSession(company,new("test","seller","sales",DateTimeOffset.UtcNow.AddMinutes(30)));
            var definition=new EntityPageDescriptor("orders","sales","Vevői rendelések","","",[new("number","Rendelés")],[new("number","Rendelés")],new("orders-list","orders-save","orders-cancel","orders-list")){ViewKind="orders"};
            var vm=new OrdersPageViewModel(definition,connection,()=>company,CancellationToken.None);var orderView=new OrdersPageView{DataContext=vm};content.Content=orderView;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            var combo=Tree(orderView).OfType<ComboBox>().Single(c=>ReferenceEquals(c.ItemsSource,vm.StatusFilters));combo.IsDropDownOpen=true;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var popup=(Popup)combo.Template.FindName("PART_Popup",combo);var labels=Tree(popup.Child).OfType<TextBlock>().Select(t=>t.Text).ToArray();
            foreach(var text in new[]{"Nyitott rendelések","Megrendelve","Beérkezett","Átadva"})if(!labels.Contains(text))throw new InvalidOperationException("Hiányzó lenyíló címke: "+text);
            combo.SelectedValue="ordered";await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);if(filters.LastOrDefault()!="ordered")throw new InvalidOperationException("Az állapotszűrő nem tölti újra a rendeléslistát.");combo.IsDropDownOpen=false;
            vm.NewCustomer=true;vm.CustomerName="Csak rendelési vevő";if(!vm.SaveCustomerToPartners)throw new InvalidOperationException("Az alapértelmezés nem menti vevőként.");vm.SaveCustomerToPartners=false;
            vm.SaveCommand.Execute(null);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if(saves.Count!=1||saves[0].CustomerId!=Guid.Empty||saves[0].SaveCustomerToPartners||saves[0].Customer?.Name!="Csak rendelési vevő")throw new InvalidOperationException("A rendelés-only vevő kérése hibás.");
            var preferences=new AppearanceStore(Path.Combine(output,"order-customer-default-051"));preferences.Save(new AppearancePreferences{SaveOrderCustomers=false});if(preferences.Load().SaveOrderCustomers)throw new InvalidOperationException("A vevőmentés alapbeállítása nem marad meg.");
            var configured=new OrdersPageViewModel(definition,connection,()=>company,CancellationToken.None,()=>preferences.Load().SaveOrderCustomers);if(configured.SaveCustomerToPartners)throw new InvalidOperationException("A rendelés nem veszi át a vevőmentés alapbeállítását.");
        }
        finally{window.Close();Application.Current.MainWindow=previousMain;}
        Console.WriteLine("ui-ok direct wholesale/mail/printer switching, visible order filter popup, reload and optional customer save");
    }
    private sealed record SettingsHost(OperationWorkspace Operations);
}
