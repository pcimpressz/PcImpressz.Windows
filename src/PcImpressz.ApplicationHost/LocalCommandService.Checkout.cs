using System.Text.Json;
using PcImpressz.Contracts;
using PcImpressz.Core.Commands;
namespace PcImpressz.ApplicationHost;
internal sealed partial class LocalCommandService
{
    private async Task<LocalResponse> LookupPartnerTaxpayer(LocalRequest request,string sid,CancellationToken ct)
    {
        var input=request.Payload.Deserialize<TaxpayerLookup>()??throw new ArgumentException("Hiányzó adószám.");
        var session=administration.Authenticate(input.CompanyId,request.SessionToken,sid);
        if(!CompanyRoles.Allows(session.Role,"partners.write"))throw new CommandDeniedException("A partner lekérdezéséhez partnerszerkesztési jog kell.");
        CustomerWriteLicense();
        foreach(var required in commands.RequiredEnabledModules("receiving","taxpayer").Prepend("receiving"))if(!modulesEnabled.IsEnabled(required))throw new CommandDeniedException("A szükséges modul ki van kapcsolva: "+required);
        var caller=new CallerIdentity(sid+"/"+session.UserName,new HashSet<string>(StringComparer.Ordinal){"receiving.read"});
        var result=await commands.ExecuteAsync(new("receiving","taxpayer",JsonSerializer.SerializeToElement(input)),caller,ct);
        var current=administration.Authenticate(input.CompanyId,request.SessionToken,sid);
        if(!CompanyRoles.Allows(current.Role,"partners.write"))throw new CommandDeniedException("A partner szerkesztési joga megváltozott.");
        return new(true,result);
    }
    private void CustomerWriteLicense()
    {
        if(!modulesEnabled.IsEnabled("partners"))throw new CommandDeniedException("A partnermodul ki van kapcsolva.");
        var decision=licenses.Evaluate("partners","partners.write");if(!decision.Allowed)throw new CommandDeniedException(decision.Reason,decision.Status);
    }
    private async Task<LocalResponse> LookupCheckoutCustomer(LocalRequest request,string sid,CancellationToken ct)
    {
        var input=request.Payload.Deserialize<TaxpayerLookup>()??throw new ArgumentException("Hiányzó adószám.");
        var session=administration.Authenticate(input.CompanyId,request.SessionToken,sid);
        if(!CompanyRoles.Allows(session.Role,"sales.write"))throw new CommandDeniedException("A vevőlekérdezéshez eladói vagy adminjog szükséges.");
        CustomerWriteLicense();
        foreach(var (module,command) in new[]{("sales","customer-save"),("receiving","taxpayer")})
            foreach(var required in commands.RequiredEnabledModules(module,command).Prepend(module))if(!modulesEnabled.IsEnabled(required))throw new CommandDeniedException("A szükséges modul ki van kapcsolva: "+required);
        var saleLicense=licenses.Evaluate("sales","sales.write");if(!saleLicense.Allowed)throw new CommandDeniedException(saleLicense.Reason,saleLicense.Status);
        var caller=new CallerIdentity(sid+"/"+session.UserName,new HashSet<string>(StringComparer.Ordinal){"receiving.read","sales.write"});
        var result=await commands.ExecuteAsync(new("receiving","taxpayer",JsonSerializer.SerializeToElement(new{companyId=input.CompanyId,taxNumber=input.TaxNumber})),caller,ct);
        administration.Authenticate(input.CompanyId,request.SessionToken,sid);
        var saved=await commands.ExecuteAsync(new("sales","customer-save",result),caller,ct);
        return new(true,saved);
    }
}
