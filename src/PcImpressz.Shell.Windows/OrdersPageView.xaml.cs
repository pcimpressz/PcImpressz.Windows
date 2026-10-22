using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
namespace PcImpressz.Shell.Windows;
public partial class OrdersPageView:UserControl
{
    private OrdersPageViewModel? _page;
    public OrdersPageView(){InitializeComponent();DataContextChanged+=(_,_)=>BindPage();Unloaded+=(_,_)=>{if(_page is not null)_page.PropertyChanged-=Changed;};Loaded+=(_,_)=>BindPage();}
    private void BindPage(){if(_page is not null)_page.PropertyChanged-=Changed;_page=DataContext as OrdersPageViewModel;if(_page is not null)_page.PropertyChanged+=Changed;Sync();}
    private void Changed(object? sender,PropertyChangedEventArgs args)=>Sync();
    private void Sync(){if(_page is null)return;foreach(var i in new[]{1,2,3})LinesGrid.Columns[i].IsReadOnly=!_page.CanEdit;LinesGrid.Columns[7].IsReadOnly=!_page.CanDeliver;}
}
