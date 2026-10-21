using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using PcImpressz.Contracts;

namespace PcImpressz.Shell.Windows;
internal static class AccountDialogs
{
    private sealed record Role(string Id,string Name);
    private static readonly Role[] Roles=[new("admin","Adminisztrátor"),new("warehouse","Raktáros"),new("sales","Eladó"),new("user","Megtekintő")];
    private static Window Create(string title,int height=680)
    {
        var w=new Window{Title=title,Width=640,Height=height,MinWidth=500,MinHeight=400,Owner=Application.Current.MainWindow,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        w.SetResourceReference(Window.BackgroundProperty,"SurfaceBrush");w.SetResourceReference(Window.ForegroundProperty,"TextBrush");return w;
    }
    private static void Input(Panel panel,string label,Control control)
    {
        panel.Children.Add(new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap});control.Margin=new(0,5,0,12);control.MinHeight=32;
        control.SetResourceReference(Control.BackgroundProperty,"ElevatedBrush");control.SetResourceReference(Control.ForegroundProperty,"TextBrush");panel.Children.Add(control);
    }
    public static async Task UsersAsync(ShellViewModel vm)
    {
        var company=vm.SelectedCompany!.Id;var token=vm.CurrentSession.Token;var actor=vm.CurrentSession.UserName;
        var users=(await vm.AccountRequest("ui.users.list",new{CompanyId=company})).Deserialize<CompanyUser[]>()!;
        var window=Create("Céges felhasználók és jogosultságok");var panel=new StackPanel{Margin=new(24)};
        panel.Children.Add(new TextBlock{Text=vm.SelectedCompany.Name+" · Admin: "+vm.CurrentSession.UserName,FontSize=20,Margin=new(0,0,0,14)});
        var list=new ComboBox{ItemsSource=users,DisplayMemberPath="UserName"};Input(panel,"Meglévő fiók (új fiókhoz: Új felhasználó)",list);
        var fresh=new Button{Content="Új felhasználó",HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,0,0,12)};panel.Children.Add(fresh);
        var name=new TextBox{MaxLength=40};Input(panel,"Felhasználónév",name);
        var role=new ComboBox{ItemsSource=Roles,DisplayMemberPath="Name",SelectedIndex=2};Input(panel,"Szerepkör",role);
        var disabled=new CheckBox{Content="Fiók letiltva",Margin=new(0,0,0,12)};panel.Children.Add(disabled);
        var password=new PasswordBox{MaxLength=200};Input(panel,"Új jelszó (meglévő fióknál üresen hagyva megmarad)",password);
        var confirm=new PasswordBox{MaxLength=200};Input(panel,"Új jelszó ismét",confirm);
        var message=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)};panel.Children.Add(message);
        var save=new Button{Content="Fiók mentése",IsDefault=true};panel.Children.Add(save);
        var busy=false;window.Closing+=(_,e)=>{if(busy)e.Cancel=true;};
        list.SelectionChanged+=(_,_)=>{if(list.SelectedItem is not CompanyUser user)return;name.Text=user.UserName;name.IsReadOnly=true;role.SelectedItem=Roles.Single(r=>r.Id==user.Role);disabled.IsChecked=user.Disabled;password.Clear();confirm.Clear();};
        fresh.Click+=(_,_)=>{list.SelectedItem=null;name.Text="";name.IsReadOnly=false;disabled.IsChecked=false;password.Clear();confirm.Clear();};
        save.Click+=async(_,_)=>
        {
            busy=true;panel.IsEnabled=false;
            try
            {
                if(password.Password!=confirm.Password)throw new ArgumentException("A két jelszó nem egyezik.");
                var selectedRole=(role.SelectedItem as Role)?.Id??throw new ArgumentException("Válassz szerepkört.");
                if(list.SelectedItem is CompanyUser)
                    await vm.AccountRequest("ui.users.update",new UiUpdateUser(company,token,name.Text,selectedRole,disabled.IsChecked==true,password.Password.Length==0?null:password.Password));
                else
                {
                    if(disabled.IsChecked==true)throw new ArgumentException("Új fiókot aktívként hozz létre, majd szükség esetén tiltsd le.");
                    await vm.AccountRequest("ui.users.create",new UiCreateUser(company,token,name.Text,password.Password,selectedRole));
                }
                if(name.Text.Trim().Equals(actor,StringComparison.OrdinalIgnoreCase))
                {message.Text="Saját fiók módosítva. Új bejelentkezés szükséges.";busy=false;window.Close();return;}
                list.ItemsSource=(await vm.AccountRequest("ui.users.list",new{CompanyId=company})).Deserialize<CompanyUser[]>();
                name.Text="";name.IsReadOnly=false;message.Text="Fiók mentve. A módosított felhasználó újra be kell jelentkezzen.";
            }
            catch(Exception ex) when(ex is not OutOfMemoryException){message.Text=ex.Message;}
            finally{busy=false;password.Clear();confirm.Clear();panel.IsEnabled=true;}
        };
        window.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};window.ShowDialog();
    }
    public static void Password(ShellViewModel vm)
    {
        if(!vm.IsSignedIn||vm.SelectedCompany is null)return;
        var window=Create("Saját jelszó módosítása",500);var panel=new StackPanel{Margin=new(24)};
        var current=new PasswordBox{MaxLength=200};Input(panel,"Jelenlegi jelszó",current);
        var password=new PasswordBox{MaxLength=200};Input(panel,"Új jelszó (12–200 karakter)",password);
        var confirm=new PasswordBox{MaxLength=200};Input(panel,"Új jelszó ismét",confirm);
        var message=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)};panel.Children.Add(message);
        var save=new Button{Content="Jelszó módosítása",IsDefault=true};panel.Children.Add(save);
        var busy=false;window.Closing+=(_,e)=>{if(busy)e.Cancel=true;};
        save.Click+=async(_,_)=>{busy=true;panel.IsEnabled=false;try{if(password.Password!=confirm.Password)throw new ArgumentException("A két jelszó nem egyezik.");await vm.PasswordChangedAsync(new(vm.SelectedCompany.Id,vm.CurrentSession.Token,current.Password,password.Password));busy=false;window.Close();}catch(Exception ex) when(ex is not OutOfMemoryException){message.Text=ex.Message;}finally{busy=false;current.Clear();password.Clear();confirm.Clear();panel.IsEnabled=true;}};
        window.Content=panel;window.ShowDialog();
    }
}
