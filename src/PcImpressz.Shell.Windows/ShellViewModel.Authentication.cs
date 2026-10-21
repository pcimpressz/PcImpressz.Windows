using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PcImpressz.Contracts;

namespace PcImpressz.Shell.Windows;
public sealed partial class ShellViewModel
{
    private bool _authBusy,_authConfigured;
    private string? _lockedUser;private Guid? _lockedCompany;
    private string _loginName="",_authMessage="Válassz céget, majd jelentkezz be.";
    public string LoginName {get=>_loginName;set{_loginName=value;Changed();}}
    public string AuthMessage {get=>_authMessage;private set{_authMessage=value;Changed();}}
    public bool AuthBusy {get=>_authBusy;private set{_authBusy=value;Changed();Changed(nameof(AuthIdle));Changed(nameof(WorkspaceIsIdle));}}
    public bool AuthIdle=>!AuthBusy;
    public bool NeedsFirstAdmin=>!_authConfigured;
    public string LoginHeading=>NeedsFirstAdmin?"Első céges admin létrehozása":"Céges bejelentkezés";
    public string LoginCompany=>SelectedCompany?.Name??"Először hozz létre egy céget.";
    public bool IsSignedIn=>_connection.Session is not null && _connection.SessionCompany==SelectedCompany?.Id;
    public bool CanCreateCompany=>IsCompanyAdmin||Companies.Count==0;
    public bool IsCompanyAdmin=>_connection.Session?.Role=="admin";
    public string SignedInLabel=>_connection.Session is {} s?s.UserName+" · "+CompanyRoles.Label(s.Role):"Nincs bejelentkezve";
    public ICommand LogoutCommand {get;private set;}=null!;
    public ICommand UsersCommand {get;private set;}=null!;
    public ICommand PasswordCommand {get;private set;}=null!;
    private void InitializeAuthentication()
    {
        LogoutCommand=new RelayCommand(()=>_=LogoutAsync());
        UsersCommand=new RelayCommand(()=>_=OpenUsersAsync());
        PasswordCommand=new RelayCommand(()=>AccountDialogs.Password(this));
    }
    private void AuthChanged()
    {
        foreach(var page in _entityPages.Values)page.PermissionsChanged();
        SyncNavigation();
        foreach(var name in new[]{nameof(CanCreateCompany),nameof(IsSignedIn),nameof(IsCompanyAdmin),nameof(SignedInLabel),nameof(NeedsFirstAdmin),nameof(LoginHeading),nameof(LoginCompany),nameof(CurrentPage)})Changed(name);
    }
    private async Task PrepareAuthenticationAsync()
    {
        if(SelectedCompany is not {} company)return;
        try
        {
            var configured=(await _connection.SendAsync("ui.auth.status",new{CompanyId=company.Id},_shutdown.Token)).Deserialize<UiAuthStatus>()!.Configured;
            if(SelectedCompany?.Id!=company.Id)return;
            _authConfigured=configured;
            var checkedSession=_connection.Session;
            if(checkedSession is not null)
            {
                try{await _connection.SendAsync("ui.auth.session",new{CompanyId=company.Id},_shutdown.Token);}
                catch{if(SelectedCompany?.Id!=company.Id||_connection.Session?.Token!=checkedSession.Token)return;_lockedUser=checkedSession.UserName;_lockedCompany=company.Id;ClearAuthentication(false);AuthMessage="A munkamenet lejárt vagy visszavonták. Jelentkezz be újra.";}
            }
            AuthChanged();
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){AuthMessage=ex.Message;}
    }
    private async Task RevokePreviousSessionAsync(Guid company,UiSession session)
    {
        try{await _connection.SendAsync("ui.auth.logout",new{CompanyId=company,session.Token},_shutdown.Token);}
        catch(Exception ex) when(ex is not OutOfMemoryException){AuthMessage="Az előző kapcsolat lezárása nem sikerült: "+ex.Message;}
    }
    private void ClearAuthentication(bool clearPages=true)
    {
        _connection.SetSession(null,null);
        if(clearPages){_lockedUser=null;_lockedCompany=null;foreach(var page in _entityPages.Values)page.CompanyChanged();}
        AuthChanged();
    }
    public async Task LoginAsync(string password,string confirm)
    {
        if(AuthBusy||SelectedCompany is not {} company)return;
        if(_status?.State!="Ready"||_status.CompanyAuthenticationVersion<1){AuthMessage="Várd meg a friss háttérszolgáltatás elindulását. Korábbi külön indított mag esetén azt is zárd be és indítsd újra.";return;}
        AuthBusy=true;
        try
        {
            if(NeedsFirstAdmin&&password!=confirm)throw new ArgumentException("A két jelszó nem egyezik.");
            var session=(await _connection.SendAsync(NeedsFirstAdmin?"ui.auth.bootstrap":"ui.auth.login",new UiLogin(company.Id,LoginName,password),_shutdown.Token)).Deserialize<UiSession>()!;
            _connection.SetSession(company.Id,session);_authConfigured=true;AuthMessage="Bejelentkezve.";AuthChanged();
            if(_lockedCompany!=company.Id||_lockedUser!=session.UserName)foreach(var page in _entityPages.Values)page.CompanyChanged();
            _lockedCompany=null;_lockedUser=null;
            await LoadNavAsync();
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){AuthMessage=ex.Message;await PrepareAuthenticationAsync();}
        finally{AuthBusy=false;}
    }
    private async Task LogoutAsync()
    {
        if(!WorkspaceIsIdle)return;
        if(_entityPages.Values.Any(p=>p.IsDirty)&&MessageBox.Show("Elveted a nem mentett változtatásokat és kijelentkezel?","Kijelentkezés",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        try{if(_connection.Session is {} session&&SelectedCompany is {} company)await _connection.SendAsync("ui.auth.logout",new{CompanyId=company.Id,session.Token},_shutdown.Token);}
        catch(Exception ex) when(ex is not OutOfMemoryException){AuthMessage=ex.Message;}
        finally{ClearAuthentication();}
    }
    internal async Task<JsonElement> AccountRequest(string operation,object payload)=>await _connection.SendAsync(operation,payload,_shutdown.Token);
    internal UiSession CurrentSession=>_connection.Session??throw new InvalidOperationException("Jelentkezz be.");
    internal async Task PasswordChangedAsync(UiChangePassword input)
    {
        await AccountRequest("ui.auth.change-password",input);ClearAuthentication();AuthMessage="Jelszó módosítva. Jelentkezz be az új jelszóval.";
    }
    private async Task OpenUsersAsync()
    {
        if(!IsCompanyAdmin||SelectedCompany is null)return;
        try{await AccountDialogs.UsersAsync(this);await PrepareAuthenticationAsync();}
        catch(Exception ex) when(ex is not OutOfMemoryException){MessageBox.Show(ex.Message,"Felhasználók");}
    }
}
public sealed class LoginPanel:StackPanel
{
    public LoginPanel()
    {
        MaxWidth=540;HorizontalAlignment=HorizontalAlignment.Left;Margin=new(0,12,0,24);
        var heading=new TextBlock{FontSize=25,Margin=new(0,0,0,12)};heading.SetBinding(TextBlock.TextProperty,"LoginHeading");Children.Add(heading);
        var company=new TextBlock{FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,18)};company.SetBinding(TextBlock.TextProperty,"LoginCompany");Children.Add(company);
        Children.Add(new TextBlock{Text="Felhasználónév"});var name=new TextBox{MaxLength=40,MinWidth=360,Margin=new(0,6,0,12)};name.SetBinding(TextBox.TextProperty,new System.Windows.Data.Binding("LoginName"){UpdateSourceTrigger=System.Windows.Data.UpdateSourceTrigger.PropertyChanged});Children.Add(name);
        Children.Add(new TextBlock{Text="Jelszó"});var password=new PasswordBox{MaxLength=200,Margin=new(0,6,0,12)};Children.Add(password);
        var confirmPanel=new StackPanel();confirmPanel.SetBinding(VisibilityProperty,new System.Windows.Data.Binding("NeedsFirstAdmin"){Converter=new BooleanToVisibilityConverter()});confirmPanel.Children.Add(new TextBlock{Text="Jelszó ismét (12–200 karakter)"});var confirm=new PasswordBox{MaxLength=200,Margin=new(0,6,0,12)};confirmPanel.Children.Add(confirm);Children.Add(confirmPanel);
        var message=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,14)};message.SetBinding(TextBlock.TextProperty,"AuthMessage");Children.Add(message);
        var submit=new Button{IsDefault=true};submit.SetBinding(ContentControl.ContentProperty,"LoginHeading");Children.Add(submit);
        foreach(var control in new Control[]{name,password,confirm}){control.SetResourceReference(Control.BackgroundProperty,"ElevatedBrush");control.SetResourceReference(Control.ForegroundProperty,"TextBrush");control.SetResourceReference(Control.BorderBrushProperty,"StrokeBrush");control.Padding=new(10);control.MinHeight=38;}
        SetBinding(IsEnabledProperty,"AuthIdle");
        submit.Click+=async(_,_)=>{try{if(DataContext is ShellViewModel vm)await vm.LoginAsync(password.Password,confirm.Password);}finally{password.Clear();confirm.Clear();}};
    }
}
