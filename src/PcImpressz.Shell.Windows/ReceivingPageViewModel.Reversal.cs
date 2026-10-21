using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PcImpressz.Shell.Customization;

namespace PcImpressz.Shell.Windows;
internal sealed partial class ReceivingPageViewModel
{
    public ICommand ReverseReceiptCommand {get;private set;}=null!;
    private void InitializeReversal()=>ReverseReceiptCommand=new RelayCommand(()=>_=ReverseReceiptAsync());
    private async Task ReverseReceiptAsync()
    {
        if(IsBusy)return;
        if(IsDirty){Message="Előbb mentsd vagy vesd el a szerkesztett számlaadatokat.";return;}
        var id=IsEditor?_editId:SelectedRow?.Id;var version=IsEditor?_version:SelectedRow?.Version;
        if(id is null||version is null){Message="Válassz egy készletre könyvelt bevételezést.";return;}
        var window=new Window{Title="Bevételezés visszafordítása",Width=560,Height=320,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=Application.Current.MainWindow};
        window.SetResourceReference(Control.BackgroundProperty,"SurfaceBrush");window.SetResourceReference(Control.ForegroundProperty,"TextBrush");
        var panel=new StackPanel{Margin=new(24)};panel.Children.Add(new TextBlock{Text="A készlet ellenmozgással csökken, az eredeti számla megmarad. Már eladott készlet esetén előbb az eladást kell rendezni. A visszafordítás nem beszállítói jóváíró számla.",TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,14)});
        panel.Children.Add(new TextBlock{Text="Visszafordítás oka (3–200 karakter)",Margin=new(0,0,0,8)});var reason=new TextBox{MaxLength=200,MinHeight=36,Margin=new(0,0,0,14)};reason.SetResourceReference(Control.BackgroundProperty,"ElevatedBrush");reason.SetResourceReference(Control.ForegroundProperty,"TextBrush");panel.Children.Add(reason);
        var submit=new Button{Content="Visszafordítás",HorizontalAlignment=HorizontalAlignment.Left};submit.Click+=(_,_)=>{if(reason.Text.Trim().Length>=3)window.DialogResult=true;};panel.Children.Add(submit);window.Content=panel;
        if(window.ShowDialog()!=true)return;
        IsBusy=true;var succeeded=false;
        try{var saved=await ModuleAsync("receiving","reverse",new{id,version,reason=reason.Text});if(IsEditor)Fill(saved);succeeded=true;Message="A bevételezés visszafordítva, a készlet ellenmozgása rögzítve.";}
        catch(Exception ex){Message=ex.Message;}
        finally{IsBusy=false;}
        if(succeeded&&IsHome){await LoadAsync();Message="A bevételezés visszafordítva; az eredeti és az ellenmozgás megmaradt.";}
    }
}
