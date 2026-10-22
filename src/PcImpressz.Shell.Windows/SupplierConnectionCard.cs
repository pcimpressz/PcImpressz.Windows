using System.Windows;
using System.Windows.Controls;
namespace PcImpressz.Shell.Windows;
public sealed class SupplierConnectionCard:UserControl
{
    private readonly TextBox _customer=new(){MinHeight=34,MaxWidth=400,HorizontalAlignment=HorizontalAlignment.Left},_proxy=new(){MinHeight=34,MaxWidth=600,HorizontalAlignment=HorizontalAlignment.Left};
    private readonly PasswordBox _auth=new(){MinHeight=34,MaxWidth=400,HorizontalAlignment=HorizontalAlignment.Left};
    private readonly CheckBox _enabled=new(){Content="CompMarket számlaadatok kiegészítése",Margin=new(0,8,0,8)};
    private readonly TextBlock _message=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,0)};
    private Guid? _company;private ShellViewModel? _shell;
    public SupplierConnectionCard()
    {
        var panel=new StackPanel{Margin=new(16),MaxWidth=750,HorizontalAlignment=HorizontalAlignment.Left};
        panel.Children.Add(new TextBlock{Text="CompMarket · csak olvasási számlaegyeztetés",FontSize=18,FontWeight=FontWeights.SemiBold});
        panel.Children.Add(new TextBlock{Text="A számla tételeinek azonosítóit egészíti ki. Mennyiséget, beszerzési árat és készletet nem módosít. A hitelesítő kód a Windows-fiókodhoz és a céghez kötve, titkosítva tárolódik. A fix kimenő IP-t biztosító proxy kötelező.",TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,8)});
        foreach(var (caption,control) in new(string,Control)[]{("Ügyfélazonosító (pid)",_customer),("Hitelesítő kód (üresen a mentett marad)",_auth),("Proxy címe (http://gép:port)",_proxy)}){panel.Children.Add(new TextBlock{Text=caption,Margin=new(0,8,0,4)});control.SetResourceReference(BackgroundProperty,"ElevatedBrush");control.SetResourceReference(ForegroundProperty,"TextBrush");panel.Children.Add(control);}
        panel.Children.Add(_enabled);var buttons=new WrapPanel();var load=new Button{Content="Beállítások betöltése",Margin=new(0,0,8,0)};load.Click+=async(_,_)=>await LoadAsync();buttons.Children.Add(load);var save=new Button{Content="CompMarket-kapcsolat mentése"};save.Click+=async(_,_)=>await SaveAsync();buttons.Children.Add(save);panel.Children.Add(buttons);panel.Children.Add(_message);Content=panel;
        DataContextChanged+=(_,_)=>{if(_shell is not null)_shell.PropertyChanged-=ShellChanged;_shell=DataContext as ShellViewModel;if(_shell is not null)_shell.PropertyChanged+=ShellChanged;Clear();};Unloaded+=(_,_)=>_auth.Clear();
    }
    private void ShellChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e){if(e.PropertyName is "SelectedCompany" or "IsCompanyAdmin")Clear();}
    private void Clear(){_company=null;_customer.Clear();_proxy.Clear();_auth.Clear();_enabled.IsChecked=false;_message.Text="A kiválasztott céghez töltsd be a beállításokat.";}
    private async Task LoadAsync()
    {
        if(_shell?.SelectedCompany?.Id is not Guid company||!_shell.IsCompanyAdmin)return;IsEnabled=false;
        try{var status=await _shell.SupplierSettingsAsync(company,"supplier-status",new{});if(_shell.SelectedCompany?.Id!=company)return;_customer.Text=status.GetProperty("customerId").GetString()??"";_proxy.Text=status.GetProperty("proxyUrl").GetString()??"";_enabled.IsChecked=status.GetProperty("enabled").GetBoolean();_auth.Clear();_company=company;_message.Text=status.GetProperty("configured").GetBoolean()?"A hitelesítő kód mentve van; nem jelenítjük meg.":"Még nincs mentett CompMarket-kapcsolat.";}catch(Exception ex){_message.Text=ex.Message;}finally{IsEnabled=true;}
    }
    private async Task SaveAsync()
    {
        if(_shell?.SelectedCompany?.Id is not Guid company||!_shell.IsCompanyAdmin||_company!=company){_message.Text="Előbb töltsd be a kiválasztott cég beállításait.";return;}IsEnabled=false;
        try{await _shell.SupplierSettingsAsync(company,"supplier-save",new{customerId=_customer.Text,authCode=_auth.Password,proxyUrl=_proxy.Text,enabled=_enabled.IsChecked==true});if(_shell.SelectedCompany?.Id!=company)return;_auth.Clear();_message.Text="CompMarket-kapcsolat mentve. Külső API-próba nem történt.";}catch(Exception ex){_message.Text=ex.Message;}finally{IsEnabled=true;}
    }
}
