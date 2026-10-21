using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PcImpressz.Contracts;

namespace PcImpressz.Shell.Windows;
public sealed partial class ShellViewModel
{
    private bool _maintenanceBusy;
    private string _backupMessage="A mentés céges adatokat tartalmaz. A NAV-titkok és a licencek nem kerülnek bele; visszaállításkor a kapcsolatot újra be kell állítani.";
    public string BackupMessage {get=>_backupMessage;private set{_backupMessage=value;Changed();}}
    public ICommand BackupCompanyCommand {get;private set;}=null!;
    public ICommand RestoreCompanyCommand {get;private set;}=null!;
    private void InitializeBackups()
    {
        BackupCompanyCommand=new RelayCommand(()=>_=BackupCompanyAsync());
        RestoreCompanyCommand=new RelayCommand(()=>_=RestoreCompanyAsync());
    }
    private bool CanMaintain()
    {
        if(!WorkspaceIsIdle) {BackupMessage="Várd meg a folyamatban lévő műveletet.";return false;}
        if(_entityPages.Values.Any(p=>p.IsDirty)) {BackupMessage="Előbb mentsd vagy vesd el a nyitott adatlapok módosításait.";return false;}
        return true;
    }
    private async Task BackupCompanyAsync()
    {
        if(!CanMaintain()||SelectedCompany is not { } company)return;
        var file=new SaveFileDialog{Filter="PC-Impressz cégmentés (*.pcibak)|*.pcibak",FileName="cegmentes-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".pcibak"};
        if(file.ShowDialog()!=true)return;
        _maintenanceBusy=true;Changed(nameof(WorkspaceIsIdle));
        try
        {
            await _connection.SendAsync("companies.backup",new CompanyBackupRequest(company.Id,file.FileName),_shutdown.Token);
            BackupMessage="Ellenőrzött cégmentés elkészült: "+file.FileName;
        }
        catch(Exception ex){BackupMessage=ex.Message;}
        finally{_maintenanceBusy=false;Changed(nameof(WorkspaceIsIdle));}
    }
    private async Task RestoreCompanyAsync()
    {
        if(!CanMaintain())return;
        var file=new OpenFileDialog{Filter="PC-Impressz cégmentés (*.pcibak)|*.pcibak",CheckFileExists=true};if(file.ShowDialog()!=true)return;
        var name=AskRestoredCompanyName();if(name is null)return;
        _maintenanceBusy=true;Changed(nameof(WorkspaceIsIdle));
        CompanyInfo? restored=null;
        try
        {
            var response=await _connection.SendAsync("companies.restore",new CompanyRestoreRequest(file.FileName,name,false),_shutdown.Token);
            restored=response.Deserialize<CompanyInfo>()??throw new InvalidOperationException("Érvénytelen visszaállítási válasz.");
            Companies.Add(restored);BackupMessage="A mentés új cégként visszaállítva: "+restored.Name+". Az eredeti cég megmaradt. A NAV-kapcsolatot állítsd be újra.";
        }
        catch(Exception ex){BackupMessage=ex.Message;}
        finally{_maintenanceBusy=false;Changed(nameof(WorkspaceIsIdle));}
        if(restored is not null)SelectedCompany=restored;
    }
    private static string? AskRestoredCompanyName()
    {
        var window=new Window{Title="Cég visszaállítása",Width=460,Height=250,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=Application.Current.MainWindow};
        window.SetResourceReference(Window.BackgroundProperty,"SurfaceBrush");window.SetResourceReference(Window.ForegroundProperty,"TextBrush");
        var panel=new StackPanel{Margin=new(24)};panel.Children.Add(new TextBlock{Text="Adj új, egyedi cégnevet. A meglévő cégeket nem írjuk felül.",TextWrapping=TextWrapping.Wrap});
        var name=new TextBox{Text="Visszaállított cég",Margin=new(0,16,0,16),MinHeight=34};name.SetResourceReference(Control.BackgroundProperty,"ElevatedBrush");name.SetResourceReference(Control.ForegroundProperty,"TextBrush");panel.Children.Add(name);
        var ok=new Button{Content="Visszaállítás új cégként",IsDefault=true};ok.Click+=(_,_)=>{if(name.Text.Trim().Length>=2)window.DialogResult=true;};panel.Children.Add(ok);window.Content=panel;
        return window.ShowDialog()==true?name.Text.Trim():null;
    }
}
