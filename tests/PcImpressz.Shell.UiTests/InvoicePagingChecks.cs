using System.IO;
using System.Text.Json;
using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;
using PcImpressz.Shell.Windows;
namespace PcImpressz.Shell.UiTests;
internal static class InvoicePagingChecks
{
    public static async Task VerifyAsync(string output)
    {
        var company=Guid.NewGuid();var requests=0;
        Task<JsonElement> Send(string operation,object input,CancellationToken ct)
        {
            var request=(ModuleCommandRequest)input;object result;
            if(request.Command=="nav-list")
            {
                requests++;var page=request.Input.GetProperty("page").GetInt32();result=new{currentPage=page,availablePage=2,rows=Enumerable.Range(page==1?1:101,page==1?51:3).Select(i=>new{number="N"+i,supplierName="Teszt",supplierTaxNumber="11111111",issuedOn="2026-10-22",currency="HUF",operation="CREATE",net=100}).ToArray()};
            }
            else result=new{rows=Array.Empty<object>(),total=0,counts=new{uj=0,bevetelezve=0,koltseg=0,fuggo=0}};
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
        var connection=new HostConnection(output,null,Send);connection.SetSession(company,new("test","test","admin",DateTimeOffset.UtcNow.AddMinutes(30)));
        var definition=new EntityPageDescriptor("receiving","receiving","Számlák","","",[],[new("number","Számlaszám")],new("list","save","archive","export")){ViewKind="receiving"};
        var vm=new ReceivingPageViewModel(definition,connection,()=>company,CancellationToken.None,new ListLayoutStore(Path.Combine(output,"invoice-page-check")));
        vm.InvoicePageSize=10;await vm.FetchNavAsync(1);if(vm.NavInvoices.Count!=10||vm.NavInvoices[0].Number!="N1")throw new InvalidOperationException("A listaoldal mérete nem 10.");
        for(var i=0;i<5;i++)vm.NextNavCommand.Execute(null);
        if(vm.NavInvoices.Count!=1||vm.NavInvoices[0].Number!="N51"||requests!=1)throw new InvalidOperationException("A helyi lapozás fölösleges NAV-kérést indított vagy tételeket hagyott ki.");
        vm.NextNavCommand.Execute(null);if(vm.NavInvoices.Count!=3||vm.NavInvoices[0].Number!="N101"||requests!=2)throw new InvalidOperationException("A következő NAV-oldal hibás.");
        vm.PreviousNavCommand.Execute(null);if(vm.NavInvoices.Single().Number!="N51")throw new InvalidOperationException("A visszalapozás nem az előző listaoldalra tért vissza.");
        InvoiceListPreferences.PageSize=20;
        Console.WriteLine("ui-ok configurable invoice page size, local slicing and NAV page transitions without omitted rows");
    }
}
