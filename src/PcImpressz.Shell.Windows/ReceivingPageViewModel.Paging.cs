using System.Windows.Input;
using PcImpressz.Shell.Customization;
namespace PcImpressz.Shell.Windows;
internal sealed partial class ReceivingPageViewModel
{
    private readonly List<NavInvoiceRow> _navAll=[];
    private int _navLocalPage,_receiptPage;
    public int[] InvoicePageSizes=>[10,20,50,100];
    public int InvoicePageSize{get=>InvoiceListPreferences.PageSize;set{if(!InvoicePageSizes.Contains(value)||IsBusy)return;InvoiceListPreferences.PageSize=value;_navLocalPage=0;ShowNavSlice();Changed();_receiptPage=0;_=LoadAsync();}}
    private void ShowNavSlice()
    {
        NavInvoices.Clear();foreach(var row in FilteredNavRows().Skip(_navLocalPage*InvoicePageSize).Take(InvoicePageSize))NavInvoices.Add(row);Changed(nameof(NavPageLabel));
    }
    private void MoveNavPage(int direction)
    {
        if(IsBusy)return;
        if(direction>0&&(_navLocalPage+1)*InvoicePageSize<FilteredNavRows().Length){_navLocalPage++;ShowNavSlice();}
        else if(direction<0&&_navLocalPage>0){_navLocalPage--;ShowNavSlice();}
        else if(direction>0&&_navPage<_navPages)_=FetchNavAsync(_navPage+1);
        else if(direction<0&&_navPage>1)_=FetchNavAsync(_navPage-1,true);
    }
    public ICommand NextReceiptPageCommand=>new RelayCommand(()=>{if(!IsBusy&&(_receiptPage+1)*InvoicePageSize<_matches){_receiptPage++;_=LoadAsync();}});
    public ICommand PreviousReceiptPageCommand=>new RelayCommand(()=>{if(!IsBusy&&_receiptPage>0){_receiptPage--;_=LoadAsync();}});
    public string ReceiptPageLabel=>$"{_receiptPage+1}. oldal · {Rows.Count} / {_matches} számla";
}
