using System.Text.Json;
using PcImpressz.Contracts;
namespace PcImpressz.Shell.Windows;
public sealed partial class ShellViewModel
{
    internal Task<JsonElement> SupplierSettingsAsync(Guid company,string module,string command,object data)=>_connection.SendAsync("module.execute",new ModuleCommandRequest(company,module,command,JsonSerializer.SerializeToElement(data)),_shutdown.Token);
    internal async Task<bool> FlushInvoicePreparationsAsync()
    {
        foreach(var page in _entityPages.Values.OfType<ReceivingPageViewModel>())if(!await page.FlushPreparationAsync())return false;return true;
    }
}
