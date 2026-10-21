using System.Text.Json;
using System.Windows.Input;
using PcImpressz.Contracts;
namespace PcImpressz.Shell.Windows;
public sealed partial class EntityPageViewModel
{
    public bool IsPartnerMaster=>Definition.ModuleId=="partners"&&Definition.Id=="partners";
    public bool CanLookupPartnerNav=>IsPartnerMaster&&_editId is null&&CanWrite;
    public ICommand LookupPartnerNavCommand=>new RelayCommand(()=>_=LookupPartnerNavAsync());
    public ICommand DiscardChangesCommand=>new RelayCommand(()=>{if(!IsBusy&&CanDiscard())Populate(_selected);});
    internal Func<bool>? ConfirmDiscardOverride{get;set;}
    internal async Task LookupPartnerNavAsync()
    {
        if(!CanLookupPartnerNav||_company() is not Guid company)return;
        var tax=Fields.FirstOrDefault(f=>f.Definition.Id=="taxId")?.Text??"";IsBusy=true;
        try
        {
            var result=await _connection.SendAsync("partners.taxpayer",new TaxpayerLookup(company,tax),_shutdown);
            if(_company()!=company||_editId is not null)return;
            foreach(var field in Fields.Where(f=>new[]{"name","taxId","country","postalCode","city","address"}.Contains(f.Definition.Id)))
                if(result.TryGetProperty(field.Definition.Id,out var value))field.Text=field.Definition.Id is "name" or "city" or "address"?PartnerNameFormatting.Format(value.GetString()??""):value.GetString()??"";
            Message="NAV-adatok betöltve az adatlapra. Ellenőrizd, majd a Mentés gombbal rögzítsd a partnert.";
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){Message=ex.Message;}
        finally{IsBusy=false;}
    }
}