using System.Collections.ObjectModel;
namespace PcImpressz.Shell.Windows;
public sealed partial class EntityPageViewModel
{
    private int _overviewGeneration;
    public string PartnerOverviewTitle=>_selected is null?(IsProductMaster?"Új termék áttekintése":"Új partner áttekintése"):_selected["name"]?.ToString()??"Partner";
    public string OverviewEyebrow=>IsProductMaster?"TERMÉK · KÉSZLET ÉS FORGALOM":"PARTNER ÁTTEKINTÉS";
    public string PartnerOverviewSubtitle=>_selected is null?(IsProductMaster?"Töltsd ki az alapadatokat. Mentés után itt látszanak a termék tulajdonságai, készlete és forgalma.":"Az alapadatokat bal oldalon töltheted ki. A mentett partner összesítője itt jelenik meg."):(IsProductMaster?"Mentett termékadatok · készlet és forgalmi összesítő":"Mentett partneradatok · a szerkesztés nem írja át az előzményeket");
    public string PartnerOverviewContact=>_selected is null?"":IsProductMaster?ProductDetails():string.Join("\n",(IsProductMaster?new[]{"sku","manufacturerPartNumber","barcode","unitCode","parameters"}:new[]{"taxId","postalCode","city","address","email","phone"}).Select(key=>_selected[key]?.ToString()).Where(value=>!string.IsNullOrWhiteSpace(value)));
    private string ProductDetails()=>string.Join("\n",new[]{("sku","Saját cikkszám"),("manufacturerPartNumber","Gyártói cikkszám"),("barcode","Vonalkód"),("unitCode","Egység"),("parameters","Tulajdonságok")}.Where(item=>!string.IsNullOrWhiteSpace(_selected?[item.Item1]?.ToString())).Select(item=>item.Item2+": "+_selected![item.Item1]));
    public ObservableCollection<DossierCardRow> PartnerOverviewCards{get;}=[];
    private string _overviewNotice="";
    public string PartnerOverviewNotice{get=>_overviewNotice;private set{_overviewNotice=value;Changed();}}
    private void ClearPartnerOverviewCards(){_overviewGeneration++;PartnerOverviewCards.Clear();PartnerOverviewNotice="";}
    private void RefreshPartnerOverview()
    {
        ClearPartnerOverviewCards();Changed(nameof(OverviewEyebrow));Changed(nameof(PartnerOverviewTitle));Changed(nameof(PartnerOverviewSubtitle));Changed(nameof(PartnerOverviewContact));
        if((IsProductMaster||IsPartnerMaster&&HasDossier)&&_connection.Session?.Role=="admin"&&_editId is Guid id&&_company() is Guid company)_=LoadPartnerOverviewAsync(company,id,_overviewGeneration);
    }
    private async Task LoadPartnerOverviewAsync(Guid company,Guid id,int generation)
    {
        PartnerOverviewNotice="Összesítő betöltése…";
        try
        {
            var result=await ExecuteAsync(IsProductMaster?"products-overview":Definition.DossierCommand!,new{id});
            if(generation!=_overviewGeneration||_company()!=company||_editId!=id||_connection.Session?.Role!="admin")return;
            foreach(var card in result.GetProperty("cards").EnumerateArray().Take(4))PartnerOverviewCards.Add(new(Text(card,"label"),Text(card,"value")));
            PartnerOverviewNotice=Text(result,"notice");
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){if(generation==_overviewGeneration)PartnerOverviewNotice="Az összesítő nem tölthető be: "+ex.Message;}
    }
}