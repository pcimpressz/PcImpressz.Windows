using System.Collections.ObjectModel;
namespace PcImpressz.Shell.Windows;
public sealed partial class EntityPageViewModel
{
    private int _overviewGeneration;
    public string PartnerOverviewTitle=>_selected is null?(IsProductMaster?"Új termék áttekintése":"Új partner áttekintése"):_selected["name"]?.ToString()??"Partner";
    public string OverviewEyebrow=>IsProductMaster?"TERMÉK · KÉSZLET ÉS FORGALOM":"PARTNER ÁTTEKINTÉS";
    public string PartnerOverviewSubtitle=>_selected is null?(IsProductMaster?"Töltsd ki az alapadatokat. Mentés után itt látszanak a termék tulajdonságai, készlete és forgalma.":"Az alapadatokat bal oldalon töltheted ki. A mentett partner összesítője itt jelenik meg."):(IsProductMaster?"Mentett termékadatok · készlet és forgalmi összesítő":"Mentett partneradatok · a szerkesztés nem írja át az előzményeket");
    public string PartnerOverviewContact=>_selected is null?"":IsProductMaster?ProductDetails():string.Join("\n",(IsProductMaster?new[]{"sku","manufacturerPartNumber","barcode","unitCode","parameters"}:new[]{"taxId","postalCode","city","address","email","phone"}).Select(key=>_selected[key]?.ToString()).Where(value=>!string.IsNullOrWhiteSpace(value)));
    private string ProductDetails()
    {
        var lines=new[]{("sku","Saját cikkszám"),("manufacturerPartNumber","Gyártói cikkszám"),("barcode","Vonalkód"),("unitCode","Egység"),("saleNetUnitPrice","Nettó eladási ár"),("saleVatRate","Eladási áfa %"),("parameters","Tulajdonságok")}.Where(item=>!string.IsNullOrWhiteSpace(_selected?[item.Item1]?.ToString())).Select(item=>item.Item2+": "+_selected![item.Item1]).ToList();
        if(decimal.TryParse((_selected?["saleNetUnitPrice"]?.ToString()??"").Replace(" ","").Replace(",","."),System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var net)&&decimal.TryParse((_selected?["saleVatRate"]?.ToString()??"").Replace(",","."),System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var vat))
            lines.Insert(lines.FindIndex(line=>line.StartsWith("Eladási áfa",StringComparison.Ordinal)) is var at&&at>=0?at+1:lines.Count,"Bruttó eladási ár: "+decimal.Round(net*(1+vat/100m),2,MidpointRounding.AwayFromZero).ToString("N2",System.Globalization.CultureInfo.GetCultureInfo("hu-HU"))+" Ft");
        return string.Join("\n",lines);
    }
    public ObservableCollection<DossierCardRow> PartnerOverviewCards{get;}=[];
    private string _overviewNotice="";
    public string PartnerOverviewNotice{get=>_overviewNotice;private set{_overviewNotice=value;Changed();}}
    private void ClearPartnerOverviewCards(){_overviewGeneration++;PartnerOverviewCards.Clear();PartnerOverviewNotice="";}
    private void RefreshPartnerOverview()
    {
        ClearPartnerOverviewCards();Changed(nameof(OverviewEyebrow));Changed(nameof(PartnerOverviewTitle));Changed(nameof(PartnerOverviewSubtitle));Changed(nameof(PartnerOverviewContact));
        var productOverview=IsProductMaster&&_connection.Allows("catalog.read");
        var partnerOverview=IsPartnerMaster&&HasDossier&&_connection.Session?.Role=="admin";
        if((productOverview||partnerOverview)&&_editId is Guid id&&_company() is Guid company)_=LoadPartnerOverviewAsync(company,id,_overviewGeneration);
    }
    private async Task LoadPartnerOverviewAsync(Guid company,Guid id,int generation)
    {
        PartnerOverviewNotice="Összesítő betöltése…";
        try
        {
            var result=await ExecuteAsync(IsProductMaster?"products-overview":Definition.DossierCommand!,new{id});
            if(generation!=_overviewGeneration||_company()!=company||_editId!=id||(!IsProductMaster&&_connection.Session?.Role!="admin"))return;
            foreach(var card in result.GetProperty("cards").EnumerateArray().Take(12))PartnerOverviewCards.Add(new(Text(card,"label"),Text(card,"value")));
            PartnerOverviewNotice=Text(result,"notice");
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){if(generation==_overviewGeneration)PartnerOverviewNotice="Az összesítő nem tölthető be: "+ex.Message;}
    }
}