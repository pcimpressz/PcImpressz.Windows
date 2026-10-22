using System.Collections.ObjectModel;

namespace PcImpressz.Shell.Windows;
public sealed record InvoiceCategoryOption(string Code,string Name){public string Label=>Code+" · "+Name;}
internal sealed partial class ReceivingPageViewModel
{
    private Guid? _categoriesCompany;
    public ObservableCollection<InvoiceCategoryOption> CategoryOptions {get;}=[];
    internal async Task LoadCategoriesAsync()
    {
        if(_company() is not Guid company||_categoriesCompany==company)return;
        try
        {
            var options=new List<InvoiceCategoryOption>();var page=0;var total=0;
            do
            {
                var result=await ModuleAsync("catalog","categories-list",new{search="",filter="all",page,pageSize=200});if(_company()!=company)return;
                total=result.GetProperty("total").GetInt32();foreach(var row in result.GetProperty("rows").EnumerateArray())if(!row.TryGetProperty("isActive",out var active)||active.ValueKind!=System.Text.Json.JsonValueKind.False)options.Add(new(Get(row,"code"),Get(row,"name")));page++;
            }while(page*200<total&&page<50);
            CategoryOptions.Clear();foreach(var option in options.OrderBy(o=>o.Name,StringComparer.CurrentCultureIgnoreCase))CategoryOptions.Add(option);_categoriesCompany=company;
        }
        catch(Exception ex){Message="A kategóriajavaslatok nem tölthetők be: "+ex.Message;}
    }
}
