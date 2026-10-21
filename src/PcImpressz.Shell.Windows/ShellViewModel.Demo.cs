using System.Text.Json;
using System.Windows.Input;
using PcImpressz.Contracts;
namespace PcImpressz.Shell.Windows;
public sealed partial class ShellViewModel
{
    private string _demoMessage="A mintaadatok kitaláltak; meglévő adataid megmaradnak. Feltöltés előtt automatikus cégmentés készül.";
    public string DemoMessage {get=>_demoMessage;private set{_demoMessage=value;Changed();}}
    public bool IsDemoCompany=>SelectedCompany is {IsDemo:true};
    public bool CanSeedDemo=>IsCompanyAdmin&&IsDemoCompany&&WorkspaceIsIdle;
    public ICommand SeedDemoCommand {get;private set;}=null!;
    private void ResetDemoMessage()=>DemoMessage="A mintaadatok kitaláltak; meglévő adataid megmaradnak. Feltöltés előtt automatikus cégmentés készül.";
    private void InitializeDemo()=>SeedDemoCommand=new RelayCommand(()=>_=SeedDemoAsync());
    internal async Task SeedDemoAsync()
    {
        if(!CanSeedDemo||SelectedCompany is not {} company){DemoMessage="A feltöltéshez a demócég adminjaként jelentkezz be, és várd meg a folyamatban lévő műveleteket.";return;}
        if(_entityPages.Values.Any(p=>p.IsDirty)){DemoMessage="Előbb mentsd vagy vesd el a nyitott adatlapok módosításait.";return;}
        _maintenanceBusy=true;Changed(nameof(WorkspaceIsIdle));Changed(nameof(CanSeedDemo));
        DemoMessage="Mintaadatok feltöltése és előzetes mentés készítése…";
        try
        {
            var response=await _connection.SendAsync("companies.seed-demo",new CompanyDemoSeedRequest(company.Id),_shutdown.Token);
            var report=response.GetProperty("report").Deserialize<DemoSeedReport>(new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new InvalidOperationException("Érvénytelen feltöltési válasz.");
            var backup=response.GetProperty("backupFile").GetString();
            DemoMessage=report.AlreadySeeded?"A mintacsomag már be van töltve. Nem keletkezett duplikált adat.":$"Mintaadatok betöltve: {report.Products} termék, {report.Partners} partner (5 vevő, 5 szállító), {report.Receipts} bevételezés, {report.Sales} eladás. Előzetes mentés: {backup}";
            foreach(var page in _entityPages.Values)page.CompanyChanged();
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){DemoMessage="A feltöltés nem fejeződött be: "+ex.Message;}
        finally{_maintenanceBusy=false;Changed(nameof(WorkspaceIsIdle));Changed(nameof(CanSeedDemo));}
    }
}
