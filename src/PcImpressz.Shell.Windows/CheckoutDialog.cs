using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace PcImpressz.Shell.Windows;
internal static class CheckoutDialog
{
    public static void Show(OperationsPageViewModel page)=>Build(page).ShowDialog();
    internal static Window Build(OperationsPageViewModel page)
    {
        var window=new Window{Title="Eladás véglegesítése",Width=820,Height=760,MinWidth=650,MinHeight=580,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=Application.Current.MainWindow is {IsVisible:true} owner?owner:null};
        window.SetResourceReference(Window.BackgroundProperty,"SurfaceBrush");window.SetResourceReference(Window.ForegroundProperty,"TextBrush");
        var dock=new DockPanel{Margin=new(24)};var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);dock.Children.Add(bottom);
        var message=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,10,0,12)};message.SetResourceReference(TextBlock.ForegroundProperty,"AccentBrush");bottom.Children.Add(message);
        var finish=new Button{Content="Készpénzes eladás rögzítése",Padding=new(20,12,20,12),IsDefault=true};finish.SetResourceReference(FrameworkElement.StyleProperty,"PrimaryButton");bottom.Children.Add(finish);
        var body=new StackPanel();dock.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        body.Children.Add(new TextBlock{Text=page.BasketTotal,FontSize=30,FontWeight=FontWeights.Bold,Margin=new(0,0,0,18)});
        var payments=new WrapPanel{Margin=new(0,0,0,18)};var cash=new RadioButton{Content="Készpénz",IsChecked=true,GroupName="payment",Margin=new(0,0,30,0),FontSize=18};var card=new RadioButton{Content="Kártya",GroupName="payment",FontSize=18};payments.Children.Add(cash);payments.Children.Add(card);body.Children.Add(payments);
        cash.Checked+=(_,_)=>finish.Content="Készpénzes eladás rögzítése";card.Checked+=(_,_)=>finish.Content="Kártyás eladás rögzítése";
        var invoice=new CheckBox{Content="Számlát kér a vevő",FontSize=17,Margin=new(0,0,0,12)};body.Children.Add(invoice);
        var buyer=new StackPanel{Visibility=Visibility.Collapsed};body.Children.Add(buyer);
        buyer.Children.Add(new TextBlock{Text="Vevő keresése névrészlettel vagy adószámmal",FontWeight=FontWeights.SemiBold,Margin=new(0,6,0,6)});
        var search=Text();buyer.Children.Add(search);
        var customers=new ObservableCollection<OperationCustomer>();var list=new ListBox{ItemsSource=customers,DisplayMemberPath="Label",MaxHeight=150,Margin=new(0,6,0,10)};list.SetResourceReference(Control.BackgroundProperty,"SurfaceBrush");list.SetResourceReference(Control.ForegroundProperty,"TextBrush");list.SetResourceReference(Control.BorderBrushProperty,"StrokeBrush");buyer.Children.Add(list);
        var selectedText=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12),FontWeight=FontWeights.SemiBold};buyer.Children.Add(selectedText);
        var taxRow=new Grid{Margin=new(0,0,0,10)};taxRow.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});taxRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var tax=Text();tax.ToolTip="8 számjegyű törzsszám vagy teljes magyar adószám";taxRow.Children.Add(tax);var nav=new Button{Content="NAV-lekérdezés és vevő kiválasztása",Margin=new(10,0,0,0)};Grid.SetColumn(nav,1);taxRow.Children.Add(nav);buyer.Children.Add(new TextBlock{Text="Új céges vevő: adószám alapján",Margin=new(0,6,0,6)});buyer.Children.Add(taxRow);
        var manualButton=new Button{Content="Új vevő / hiányzó cím kézi megadása",HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,0,0,10)};buyer.Children.Add(manualButton);
        var manual=new StackPanel{Visibility=Visibility.Collapsed};buyer.Children.Add(manual);
        var name=Text();var country=Text();country.Text="HU";country.MaxLength=2;var postal=Text();var city=Text();var address=Text();
        Field(manual,"Vevő neve",name);Field(manual,"Ország",country);Field(manual,"Irányítószám",postal);Field(manual,"Település",city);Field(manual,"Cím",address);
        var saveCustomer=new Button{Content="Vevő mentése és kiválasztása",HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,6,0,12)};manual.Children.Add(saveCustomer);
        buyer.Children.Add(new TextBlock{Text="A vevőadatok és a számlaigény mentésre kerülnek. Adóügyi számla kiállítása még nincs bekötve.",TextWrapping=TextWrapping.Wrap,Margin=new(0,10,0,8)});
        OperationCustomer? selected=null;CancellationTokenSource? finder=null;var generation=0;var busy=false;var choosing=false;
        void Choose(OperationCustomer customer)
        {
            selected=customer;selectedText.Text="Kiválasztva: "+customer.Label;choosing=true;search.Text=customer.Name;tax.Text=customer.TaxId;choosing=false;list.Visibility=Visibility.Collapsed;manual.Visibility=Visibility.Collapsed;message.Text="Vevő kiválasztva.";
        }
        async Task SearchAsync(string query,int current,CancellationToken ct)
        {
            try{await Task.Delay(220,ct);var rows=await page.FindCheckoutCustomersAsync(query,ct);if(current!=generation||ct.IsCancellationRequested)return;customers.Clear();foreach(var row in rows)customers.Add(row);list.Visibility=Visibility.Visible;if(rows.Length==0)message.Text="Nincs ilyen vevő. Adószámmal lekérdezheted, vagy felveheted kézzel.";}
            catch(OperationCanceledException) when(ct.IsCancellationRequested){}
            catch(Exception ex) when(ex is not OutOfMemoryException){if(current==generation)message.Text=ex.Message;}
        }
        void Find()
        {
            if(choosing||busy||invoice.IsChecked!=true)return;finder?.Cancel();finder?.Dispose();finder=new();var current=++generation;if(selected is {} previous&&tax.Text==previous.TaxId)tax.Text="";selected=null;selectedText.Text="";_=SearchAsync(search.Text.Trim(),current,finder.Token);
        }
        search.TextChanged+=(_,_)=>Find();list.SelectionChanged+=async(_,_)=>
        {
            if(list.SelectedItem is not OperationCustomer customer||busy)return;
            if(customer.IsCustomer){Choose(customer);return;}
            busy=true;body.IsEnabled=false;finish.IsEnabled=false;finder?.Cancel();generation++;
            try{Choose(await page.SaveCheckoutCustomerAsync(customer.Name,customer.TaxId,customer.Country,customer.PostalCode,customer.City,customer.Address));}
            catch(Exception ex) when(ex is not OutOfMemoryException){Choose(customer);manual.Visibility=Visibility.Visible;name.Text=customer.Name;country.Text=customer.Country;postal.Text=customer.PostalCode;city.Text=customer.City;address.Text=customer.Address;message.Text=ex.Message;}
            finally{busy=false;body.IsEnabled=true;finish.IsEnabled=true;}
        };
        invoice.Checked+=(_,_)=>{buyer.Visibility=Visibility.Visible;search.Focus();Find();};invoice.Unchecked+=(_,_)=>{buyer.Visibility=Visibility.Collapsed;finder?.Cancel();generation++;};
        manualButton.Click+=(_,_)=>{manual.Visibility=Visibility.Visible;name.Text=selected?.Name??search.Text;country.Text=selected?.Country??"HU";postal.Text=selected?.PostalCode??"";city.Text=selected?.City??"";address.Text=selected?.Address??"";name.Focus();};
        async Task SaveCustomer(bool fromNav)
        {
            if(busy)return;busy=true;body.IsEnabled=false;finish.IsEnabled=false;finder?.Cancel();generation++;
            try{var customer=fromNav?await page.LookupCheckoutCustomerAsync(tax.Text):await page.SaveCheckoutCustomerAsync(name.Text,tax.Text,country.Text,postal.Text,city.Text,address.Text);Choose(customer);message.Text=fromNav?"NAV-adatok lekérdezve, a vevő a partnertörzsben és kiválasztva.":"Vevő a partnertörzsbe mentve és kiválasztva.";}
            catch(Exception ex) when(ex is not OutOfMemoryException){message.Text=ex.Message;}
            finally{busy=false;body.IsEnabled=true;finish.IsEnabled=true;}
        }
        nav.Click+=async(_,_)=>await SaveCustomer(true);saveCustomer.Click+=async(_,_)=>await SaveCustomer(false);
        finish.Click+=async(_,_)=>
        {
            if(busy)return;if(invoice.IsChecked==true&&(selected?.Id is null||!selected.IsCustomer)){message.Text="Válassz vevőt, vagy mentsd az új vevő adatait.";search.Focus();return;}
            finder?.Cancel();generation++;busy=true;body.IsEnabled=false;finish.IsEnabled=false;
            try{if(await page.CommitCheckoutAsync(card.IsChecked==true?"card":"cash",invoice.IsChecked==true,selected)){busy=false;window.DialogResult=true;}else{message.Text=page.Message;if(!page.CanEditSale){busy=false;window.Close();}}}
            finally{busy=false;body.IsEnabled=true;finish.IsEnabled=true;}
        };
        window.Closing+=(_,e)=>{if(busy)e.Cancel=true;};window.Closed+=(_,_)=>{finder?.Cancel();finder?.Dispose();generation++;};window.Content=dock;return window;
    }
    private static TextBox Text(){var box=new TextBox{MinHeight=36,Padding=new(8),MaxLength=200};box.SetResourceReference(Control.BackgroundProperty,"ElevatedBrush");box.SetResourceReference(Control.ForegroundProperty,"TextBrush");box.SetResourceReference(Control.BorderBrushProperty,"StrokeBrush");return box;}
    private static void Field(Panel panel,string label,Control box){panel.Children.Add(new TextBlock{Text=label,Margin=new(0,5,0,4)});panel.Children.Add(box);}
}
