using System.IO;
using System.Text.Json;
using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;
using PcImpressz.Shell.Windows;
namespace PcImpressz.Shell.UiTests;
internal static class NavInvoiceUiChecks
{
    public static async Task VerifyAsync(string output)
    {
        var company=Guid.NewGuid();var supplier=Guid.NewGuid();var imported=0;
        Task<JsonElement> Send(string operation,object input,CancellationToken ct)
        {
            var request=(ModuleCommandRequest)input;
            if(request.Command=="nav-outgoing-list")return Task.FromResult(JsonSerializer.SerializeToElement(new{currentPage=1,availablePage=1,rows=new[]{new{number="OUT-1",customerName="Vevő",customerTaxNumber="22222222",issuedOn="2026-10-21",currency="HUF",operation="CREATE",net=200m}}}));
            if(request.Command=="nav-list")return Task.FromResult(JsonSerializer.SerializeToElement(new{currentPage=request.Input.GetProperty("page").GetInt32(),availablePage=2,rows=new[]{new{number="NAV-1",supplierName="Teszt",supplierTaxNumber="11111111",issuedOn="2026-10-21",currency="HUF",operation="CREATE",net=200m}}}));
            if(request.Command=="nav-import")
            {
                imported++;return Task.FromResult(JsonSerializer.SerializeToElement(new{id=Guid.NewGuid(),version=1,status="uj",number="NAV-1",issuedOn="2026-10-21",dueOn="",warehouse="Bolt",net=200m,gross=254m,supplierId=supplier,supplierName="Teszt",supplierTaxId="11111111",lines=new[]{new{id=Guid.NewGuid(),productId=(Guid?)null,name="USB egér",sku="M-1",barcode="",unitCode="DB",quantity=2m,netUnitPrice=100m,vatRate=27m,match="uj",matchLabel="Nincs a törzsben"}}}));
            }
            throw new InvalidOperationException("Váratlan parancs: "+request.Command);
        }
        var connection=new HostConnection(output,null,Send);connection.SetSession(company,new("test","admin","admin",DateTimeOffset.UtcNow.AddMinutes(30)));
        var definition=new EntityPageDescriptor("receiving","receiving","Bevételezés","","",[],[new("number","Bizonylat")],new("list","save","reverse","export")){ViewKind="receiving"};
        var page=new ReceivingPageViewModel(definition,connection,()=>company,CancellationToken.None,new ListLayoutStore(Path.Combine(output,"nav-ui-check")));
        page.Suppliers.Add(new(supplier,"Teszt","11111111-2-42"));page.NavFrom=new DateTime(2026,10,1);page.NavTo=new DateTime(2026,10,21);await page.FetchNavAsync(1);if(page.NavInvoices.Count!=1)throw new InvalidOperationException("A NAV-lista nem töltődött be.");
        await page.FetchNavAsync(2);if(!page.NavPageLabel.StartsWith("2 / 2"))throw new InvalidOperationException("A NAV-lapozás hibás.");
        page.SelectedNavInvoice=page.NavInvoices.Single();if(page.SelectedNavSupplier?.Id!=supplier)throw new InvalidOperationException("A NAV-szállító adószám alapján nem párosult.");
        page.IsNav=true;
        await page.DownloadNavAsync();if(imported!=1||!page.IsEditor||!page.IsEditable||page.Lines.Count!=1||page.IsDirty)throw new InvalidOperationException("A NAV-letöltés nem nyitotta meg az új számlát párosításhoz.");
        if(!page.IsNav)throw new InvalidOperationException("A NAV-panel eltűnt a számla betöltése után.");
        var original=page.SelectedSupplier;
        page.SelectedNavInvoice=new("OTHER","Másik szállító","33333333","2026-10-21","HUF","CREATE","100",null);
        if(page.SelectedSupplier!=original||page.IsDirty)throw new InvalidOperationException("A NAV-kijelölés átírta a szerkesztett számlát.");
        page.OutgoingCommand.Execute(null);if(!page.IsOutgoing)throw new InvalidOperationException("Nem váltott kimenő nézetre.");
        await page.FetchOutgoingAsync(1);if(page.OutgoingInvoices.Single().CustomerName!="Vevő")throw new InvalidOperationException("Nem töltődött be a kimenő lista.");
        page.IncomingCommand.Execute(null);if(!page.IsEditor||!page.IsIncoming||!page.IsNav)throw new InvalidOperationException("A nézetváltás elvesztette a bejövő munkát.");
        page.ApplyPdfPreview("teszt.pdf","PDF tesztszöveg");if(!page.HasPdfPreview||!page.IsDirty||page.PdfText!="PDF tesztszöveg")throw new InvalidOperationException("Hiányzik a PDF ellenőrzési előnézet.");
        page.CompanyChanged();if(page.HasPdfPreview||page.OutgoingInvoices.Count>0||page.IsNav)throw new InvalidOperationException("Cégváltás után megmaradt másik cég számlája.");
        Console.WriteLine("ui-ok NAV dates, pagination, supplier matching and imported invoice editor");
    }
}