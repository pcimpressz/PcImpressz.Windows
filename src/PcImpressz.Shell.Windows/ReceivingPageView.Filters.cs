using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace PcImpressz.Shell.Windows;
public sealed partial class ReceivingPageView
{
    private FrameworkElement InvoiceFilterPanel()
    {
        var root=new StackPanel{Margin=new(0,0,0,10)};
        root.Children.Add(Bound(Muted(""),"InvoiceActiveFilters"));
        var fields=new WrapPanel();fields.SetBinding(VisibilityProperty,new Binding("InvoiceFilterOpen"){Converter=new BooleanToVisibilityConverter()});
        void TextFilter(string caption,string path,double width)
        {
            var panel=new StackPanel{Width=width,Margin=new(0,0,10,0)};panel.Children.Add(Muted(caption));var input=Input();input.Margin=new(0,0,0,10);input.SetBinding(TextBox.TextProperty,new Binding(path){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});panel.Children.Add(input);fields.Children.Add(panel);
        }
        TextFilter("Szállító neve / adószáma","InvoiceSupplierText",240);
        foreach(var (caption,path) in new[]{("Kelt – ettől","InvoiceDateFrom"),("Kelt – eddig","InvoiceDateTo")})
        {
            var panel=new StackPanel{Margin=new(0,0,10,0)};panel.Children.Add(Muted(caption));var date=CompactDatePicker();date.Margin=new(0,0,0,10);date.SetBinding(DatePicker.SelectedDateProperty,new Binding(path){Mode=BindingMode.TwoWay});panel.Children.Add(date);fields.Children.Add(panel);
        }
        TextFilter("Bruttó összeg – tól","InvoiceGrossFrom",140);TextFilter("Bruttó összeg – ig","InvoiceGrossTo",140);
        fields.Children.Add(Primary("Szűrés alkalmazása","ApplyInvoiceFiltersCommand"));fields.Children.Add(Button("Szűrők törlése","ClearInvoiceFiltersCommand"));root.Children.Add(fields);return root;
    }
    private void OpenInvoiceColumnFilter(string id,FrameworkElement anchor)
    {
        if(_page is not {} page)return;
        var panel=new StackPanel{Margin=new(12),Width=280};panel.Children.Add(Text(ReceivingPageViewModel.InvoiceColumnTitle(id)+" szűrése",15,true));
        var input=Input();input.Text=page.InvoiceColumnText(id);input.Margin=new(0,0,0,10);panel.Children.Add(input);
        var popup=new Popup{PlacementTarget=anchor,Placement=PlacementMode.Bottom,StaysOpen=false,AllowsTransparency=true};var frame=new Border{BorderThickness=new(1),CornerRadius=new(8),Child=panel};frame.SetResourceReference(BackgroundProperty,"SurfaceBrush");frame.SetResourceReference(Border.BorderBrushProperty,"StrokeBrush");popup.Child=frame;
        input.TextChanged+=(_,_)=>page.SetInvoiceColumn(id,input.Text,true);
        var actions=new WrapPanel();var apply=new Button{Content="Alkalmazás",Margin=new(0,0,8,0)};apply.Click+=(_,_)=>{page.SetInvoiceColumn(id,input.Text);popup.IsOpen=false;};var clear=new Button{Content="Törlés"};clear.Click+=(_,_)=>{page.SetInvoiceColumn(id,"");popup.IsOpen=false;};actions.Children.Add(apply);actions.Children.Add(clear);panel.Children.Add(actions);
        input.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){page.SetInvoiceColumn(id,input.Text);popup.IsOpen=false;e.Handled=true;}else if(e.Key==Key.Escape)popup.IsOpen=false;};
        popup.IsOpen=true;input.Focus();input.SelectAll();
    }
    private object NavFilterHeader(string title,string path)
    {
        var id=path switch{"State"=>"statusLabel","Number"=>"number","IssuedOn"=>"issuedOn","SupplierName"=>"supplierName","SupplierTaxNumber"=>"supplierTaxId","Net"=>"net","Gross"=>"gross",_=>""};
        if(id.Length==0)return title;
        var button=new Button{Content=title,ToolTip="Szűrés: "+title,Height=24,MinHeight=0,Margin=new(0),Padding=new(0),Background=System.Windows.Media.Brushes.Transparent,BorderThickness=new(0),HorizontalContentAlignment=HorizontalAlignment.Left};button.Click+=(_,_)=>OpenInvoiceColumnFilter(id,button);return button;
    }
}
