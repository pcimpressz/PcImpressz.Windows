using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
namespace PcImpressz.Shell.Windows;
public sealed class SupplierConnectionCard:UserControl
{
    private readonly TextBox _customer=new(){MinHeight=34,MaxWidth=400,HorizontalAlignment=HorizontalAlignment.Left},_proxy=new(){MinHeight=34,MaxWidth=600,HorizontalAlignment=HorizontalAlignment.Left};
    private readonly PasswordBox _auth=new(){MinHeight=34,MaxWidth=400,HorizontalAlignment=HorizontalAlignment.Left};
    private readonly CheckBox _enabled=new(){Content="CompMarket-kapcsolat bekapcsolása (árajánlat és számla)",Margin=new(0,8,0,8)};
    private readonly TextBlock _message=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,0)};
    private Guid? _company;private ShellViewModel? _shell;
    public SupplierConnectionCard()
    {
        var panel=new StackPanel{Margin=new(16),MaxWidth=750,HorizontalAlignment=HorizontalAlignment.Left};
        panel.Children.Add(new TextBlock{Text="CompMarket · csak olvasási számlaegyeztetés",FontSize=18,FontWeight=FontWeights.SemiBold});
        panel.Children.Add(new TextBlock{Text="Az árajánlat CompMarket-keresése és a számlaazonosítók ezt a kapcsolatot használják. A hitelesítő kód a Windows-fiókodhoz és a céghez kötve, titkosítva tárolódik. A CompMarket a hívó IP-t nézi, ezért a kérés csak a 217.113.49.52 VPS-proxyn mehet ki. Üres proxy esetén közvetlen kapcsolat nem indul. A jelszavas cím mentés után csak gépként és portként látszik; változatlan címnél a jelszó megmarad.",TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,8)});
        foreach(var (caption,control) in new(string,Control)[]{("Ügyfélazonosító (pid)",_customer),("Hitelesítő kód (üresen a mentett marad)",_auth),("VPS-proxy (http://felhasználó:jelszó@217.113.49.52:port)",_proxy)}){panel.Children.Add(new TextBlock{Text=caption,Margin=new(0,8,0,4)});control.SetResourceReference(BackgroundProperty,"ElevatedBrush");control.SetResourceReference(ForegroundProperty,"TextBrush");panel.Children.Add(control);}
        panel.Children.Add(_enabled);var buttons=new WrapPanel();var load=new Button{Content="Beállítások betöltése",Margin=new(0,0,8,0)};load.Click+=async(_,_)=>await LoadAsync();buttons.Children.Add(load);var save=new Button{Content="CompMarket-kapcsolat mentése"};save.Click+=async(_,_)=>await SaveAsync();buttons.Children.Add(save);panel.Children.Add(buttons);panel.Children.Add(_message);Content=panel;
        DataContextChanged+=(_,_)=>{if(_shell is not null)_shell.PropertyChanged-=ShellChanged;_shell=DataContext as ShellViewModel;if(_shell is not null)_shell.PropertyChanged+=ShellChanged;Clear();};Unloaded+=(_,_)=>_auth.Clear();
    }
    private void ShellChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e){if(e.PropertyName is "SelectedCompany" or "IsCompanyAdmin")Clear();}
    private void Clear(){_company=null;_customer.Clear();_proxy.Clear();_auth.Clear();_enabled.IsChecked=false;_message.Text="A kiválasztott céghez töltsd be a beállításokat.";}
    private async Task LoadAsync()
    {
        if(_shell?.SelectedCompany?.Id is not Guid company||!_shell.IsCompanyAdmin)return;IsEnabled=false;
        try{var status=await LoadStatus(company);if(_shell.SelectedCompany?.Id!=company)return;_customer.Text=status.GetProperty("customerId").GetString()??"";_proxy.Text=status.GetProperty("proxyUrl").GetString()??"";_enabled.IsChecked=status.GetProperty("enabled").GetBoolean();_auth.Clear();_company=company;_message.Text=status.GetProperty("configured").GetBoolean()?"A hitelesítő kód mentve van; nem jelenítjük meg. A kérés a 217.113.49.52 címen megy ki.":"Még nincs mentett CompMarket-kapcsolat.";}catch(Exception ex){_message.Text=ex.Message;}finally{IsEnabled=true;}
    }
    private async Task SaveAsync()
    {
        if(_shell?.SelectedCompany?.Id is not Guid company||!_shell.IsCompanyAdmin||_company!=company){_message.Text="Előbb töltsd be a kiválasztott cég beállításait.";return;}IsEnabled=false;
        try{var payload=new{customerId=_customer.Text,authCode=_auth.Password,proxyUrl=_proxy.Text,enabled=_enabled.IsChecked==true};var saved=0;string? blocked=null;foreach(var (module,command) in new[]{("quotes","compmarket-save"),("receiving","supplier-save")}){try{await _shell.SupplierSettingsAsync(company,module,command,payload);saved++;}catch(Exception ex) when(ex is not OutOfMemoryException){if(ex.Message.Contains("ki van kapcsolva",StringComparison.Ordinal)||ex.Message.Contains("nem érhető el",StringComparison.Ordinal))continue;blocked=ex.Message;}}if(_shell.SelectedCompany?.Id!=company)return;if(saved==0)throw new InvalidOperationException(blocked??"A CompMarket-kapcsolat nem menthető.");_auth.Clear();_message.Text="CompMarket-kapcsolat mentve. A kérések a 217.113.49.52 VPS-proxyn mennek ki. Közvetlen kapcsolat nem indul, és most sem történt API-próba.";}catch(Exception ex){_message.Text=ex.Message;}finally{IsEnabled=true;}
    }
    private async Task<JsonElement> LoadStatus(Guid company)
    {
        JsonElement quotes;
        try{quotes=await _shell!.SupplierSettingsAsync(company,"quotes","compmarket-status",new{});}
        catch(Exception ex) when(ex is not OutOfMemoryException){return await _shell!.SupplierSettingsAsync(company,"receiving","supplier-status",new{});}
        if(quotes.TryGetProperty("configured",out var ready)&&ready.ValueKind==JsonValueKind.True)return quotes;
        try{var invoices=await _shell!.SupplierSettingsAsync(company,"receiving","supplier-status",new{});if(invoices.TryGetProperty("configured",out var invoiceReady)&&invoiceReady.ValueKind==JsonValueKind.True)return invoices;}
        catch(Exception ex) when(ex is not OutOfMemoryException){}
        return quotes;
    }
}
