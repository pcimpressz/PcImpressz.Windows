using System.Text.Json;
using PcImpressz.Contracts;

namespace PcImpressz.Shell.Windows;
public sealed partial class ShellViewModel
{
    private bool _billingBusy;
    internal async Task<JsonElement> BillingSettingsAsync(Guid company,object input,bool save)
    {
        if(_billingBusy||SelectedCompany?.Id!=company||!IsCompanyAdmin)throw new InvalidOperationException("A számlázási beállításokat a kiválasztott cég adminja kezeli.");
        _billingBusy=true;Changed(nameof(WorkspaceIsIdle));
        try
        {
            return await _connection.SendAsync("module.execute",new ModuleCommandRequest(company,"receiving",save?"invoicing-settings-save":"invoicing-settings",JsonSerializer.SerializeToElement(input)),_shutdown.Token);
        }
        finally{_billingBusy=false;Changed(nameof(WorkspaceIsIdle));}
    }
}
