using System.IO;
using System.Text.Json;
using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;
using PcImpressz.Shell.Windows;

namespace PcImpressz.Shell.UiTests;
internal static class InvoiceAccountingUiChecks
{
    public static async Task VerifyAsync(string output)
    {
        var company=Guid.NewGuid();var supplier=Guid.NewGuid();var product=Guid.NewGuid();var receipt=Guid.NewGuid();var navCalls=0;JsonElement lastQuery=default;
        object Invoice(string number,string name,decimal net,decimal gross,string operation="CREATE",Guid? id=null)=>new{number,supplierName=name,supplierTaxNumber="22222222",issuedOn="2026-10-22",currency="HUF",operation,net,gross,received=id is not null,receiptId=id};
        Task<JsonElement> Send(string operation,object input,CancellationToken ct)
        {
            if(operation=="ui.workspace.read")return Task.FromResult(JsonSerializer.SerializeToElement(new UiWorkspaceLayout(0,[],[])));
            var request=(ModuleCommandRequest)input;var data=request.Input;
            object result=request.Command switch
            {
                "nav-list"=>Nav(),
                "list" when request.ModuleId=="partners"=>new{total=1,rows=new[]{new{id=supplier,name="SMA Rendszerház",taxId="22222222"}}},
                "list"=>Local(),
                "get"=>new{id=receipt,version=1,source="nav",status="bevetelezve",stockPosted=true,number="POSTED",issuedOn="2026-10-22",dueOn="",warehouse="Bolt",net=110m,gross=139.7m,supplierId=supplier,supplierName="SMA Rendszerház",supplierTaxId="22222222",lines=new object[]{Line("Áru",1,100,true,product),Line("Szállítási költség",1,10,false,null)}},
                "nav-preview"=>new{document=new{issuedOn="2026-10-22",dueOn=""},receipt=(object?)null,lines=new[]{Line("Jóváírt termék",-1,100,true,product)}},
                "products-schema"=>new EntityField[]{new("name","Név"),new("sku","Cikkszám"),new("barcode","Vonalkód"),new("unitCode","Egység"),new("saleVatRate","Áfa","decimal",DefaultText:"27")},
                "products-get"=>new{id=product,version=1,name="Áru",sku="P1",barcode="",unitCode="DB",saleVatRate=27m,saleNetUnitPrice=200m},
                "products-suggest"=>new{rows=Array.Empty<object>()},
                "categories-list"=>new{total=0,rows=Array.Empty<object>()},
                "preparation-read"=>(object?)null!,
                _=>throw new InvalidOperationException("Váratlan parancs: "+request.Command)
            };
            return Task.FromResult(JsonSerializer.SerializeToElement(result,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            object Line(string name,decimal quantity,decimal price,bool stockItem,Guid? productId)=>new{id=Guid.NewGuid(),productId,name,sku=productId is null?"":"P1",barcode="",unitCode="DB",quantity,netUnitPrice=price,vatRate=27m,stockItem,match=productId is null?"fuggo":"megvan",sourceNetAmount=quantity*price,sourceVatAmount=quantity*price*.27m};
            object Nav(){navCalls++;var page=data.GetProperty("page").GetInt32();return new{currentPage=page,availablePage=2,rows=page==1?new[]{Invoice("ORINK-1","Orink",1000,1270),Invoice("CREDIT","SMA Rendszerház",-100,-127,"MODIFY")}:new[]{Invoice("SMA-BIG","SMA Rendszerház",100000,127000),Invoice("POSTED","SMA Rendszerház",110,139.7m,"CREATE",receipt)}};}
            object Local(){lastQuery=data.Clone();return new{total=0,rows=Array.Empty<object>(),counts=new{uj=0,bevetelezve=0,koltseg=0,fuggo=0}};}
        }
        var connection=new HostConnection(output,null,Send);connection.SetSession(company,new("test","admin","admin",DateTimeOffset.UtcNow.AddMinutes(30)));
        var definition=new EntityPageDescriptor("receiving","receiving","Számlák","","",[],[new("number","Számla"),new("supplierName","Szállító"),new("gross","Bruttó")],new("list","save","reverse","export")){ViewKind="receiving"};
        var page=new ReceivingPageViewModel(definition,connection,()=>company,CancellationToken.None,new ListLayoutStore(Path.Combine(output,"invoice-accounting")));
        InvoiceListPreferences.LiveFiltering=false;page.IsNav=true;await page.FetchNavAsync(1);var calls=navCalls;
        page.InvoiceSupplierText="SMA";page.InvoiceGrossFrom="100000";if(page.NavInvoices.Count!=2||navCalls!=calls)throw new InvalidOperationException("A kikapcsolt élő szűrés már gépelésre lekérdez.");
        page.ApplyInvoiceFiltersCommand.Execute(null);if(navCalls!=calls+2||page.NavInvoices.Count!=1||page.NavInvoices.Single().Number!="SMA-BIG"||!page.NavPageLabel.Contains("Összes NAV-oldal"))throw new InvalidOperationException("A szűrés kihagyta a következő NAV-oldal találatát: "+page.Message);
        page.ClearInvoiceFiltersCommand.Execute(null);if(page.NavInvoices.Count!=4)throw new InvalidOperationException("A szűrés törlése nem állította vissza a teljes NAV-listát.");
        InvoiceListPreferences.LiveFiltering=true;page.SetInvoiceColumn("number","CREDIT",true);await Task.Delay(400);
        if(page.NavInvoices.Count!=1||page.NavInvoices[0].Operation!="MODIFY")throw new InvalidOperationException("Az élő oszlopszűrés nem frissült.");
        page.SelectedNavInvoice=page.NavInvoices[0];if(!page.IsNavEditor||page.Lines.Single().QuantityText!="-1"||page.Lines.Single().StockEffect!="Készletet csökkenti")throw new InvalidOperationException("A negatív NAV-tétel nem tölthető be: "+page.Message);
        page.ClearInvoiceFiltersCommand.Execute(null);page.SelectedNavInvoice=page.NavInvoices.Single(r=>r.Number=="POSTED");
        if(!page.ExcludedInvoiceLines.Contains("Szállítási költség")||page.Lines.Single(l=>!l.StockItem).StockEffect!="Nem lett raktárra véve")throw new InvalidOperationException("A lezárt számlában nem látható a készletre nem vett szállítás.");
        page.ReceivedInvoicesCommand.Execute(null);InvoiceListPreferences.LiveFiltering=false;page.InvoiceSupplierText="SMA";page.InvoiceGrossFrom="-200";page.InvoiceGrossTo="200";page.InvoiceDateFrom=new(2026,10,1);page.InvoiceDateTo=new(2026,10,22);page.ApplyInvoiceFiltersCommand.Execute(null);
        if(lastQuery.GetProperty("supplier").GetString()!="SMA"||lastQuery.GetProperty("minGross").GetDecimal()!=-200m||lastQuery.GetProperty("dateTo").GetString()!="2026-10-22")throw new InvalidOperationException("A helyi lista nem kapta meg az összetett szűrőt.");
        InvoiceListPreferences.LiveFiltering=true;
        Console.WriteLine("ui-ok freight stock exclusion caption, negative MODIFY preview, debounced and manual invoice filters, all NAV pages and local query ranges");
    }
}
