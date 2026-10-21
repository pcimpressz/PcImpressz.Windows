using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
namespace PcImpressz.Shell.Windows;
internal sealed class SalesCheckoutView:UserControl
{
    private OperationsPageViewModel? _page;private TextBox? _search;
    public SalesCheckoutView(){DataContextChanged+=(_,_)=>Build();Loaded+=(_,_)=>{if(_page is {} p){p.SearchFocusRequested-=FocusSearch;p.SearchFocusRequested+=FocusSearch;}};Unloaded+=(_,_)=>{if(_page is {} p)p.SearchFocusRequested-=FocusSearch;};}
    private void FocusSearch()=>Dispatcher.BeginInvoke(()=>_search?.Focus());
    private void Build()
    {
        if(_page is {} old)old.SearchFocusRequested-=FocusSearch;if(DataContext is not OperationsPageViewModel page)return;_page=page;page.SearchFocusRequested+=FocusSearch;
        var root=new StackPanel();
        var editor=new StackPanel();editor.SetBinding(IsEnabledProperty,"CanEditSale");
        var heading=new TextBlock{Text="Termék neve, cikkszám vagy vonalkód",FontWeight=FontWeights.SemiBold,FontSize=17,Margin=new(0,0,0,8)};editor.Children.Add(heading);
        var searchRow=new Grid{Margin=new(0,0,0,10)};searchRow.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});searchRow.ColumnDefinitions.Add(new(){Width=new(175)});
        _search=Input("ProductSearch");_search.MinHeight=46;_search.FontSize=18;_search.ToolTip="Gépelj névrészletet, vagy olvasd be a vonalkódot. A pontos vonalkód rögtön a kosárba kerül.";searchRow.Children.Add(_search);
        var warehouse=Input("Warehouse");warehouse.ToolTip="Raktár";warehouse.Margin=new(12,0,0,0);Grid.SetColumn(warehouse,1);searchRow.Children.Add(warehouse);editor.Children.Add(searchRow);
        var matches=new ListBox{MaxHeight=210,Margin=new(0,0,0,12),HorizontalContentAlignment=HorizontalAlignment.Stretch};matches.SetBinding(ItemsControl.ItemsSourceProperty,"QuickMatches");matches.SetBinding(VisibilityProperty,new Binding("HasQuickMatches"){Converter=new BooleanToVisibilityConverter()});
        matches.SetResourceReference(Control.BackgroundProperty,"SurfaceBrush");matches.SetResourceReference(Control.ForegroundProperty,"TextBrush");matches.SetResourceReference(Control.BorderBrushProperty,"StrokeBrush");
        var template=new DataTemplate();var row=new FrameworkElementFactory(typeof(Grid));
        var panel=new FrameworkElementFactory(typeof(DockPanel));panel.SetValue(MarginProperty,new Thickness(10,7,10,7));
        var price=new FrameworkElementFactory(typeof(TextBlock));price.SetBinding(TextBlock.TextProperty,new Binding("PriceLabel"));price.SetValue(DockPanel.DockProperty,Dock.Right);price.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);price.SetValue(MarginProperty,new Thickness(20,0,0,0));panel.AppendChild(price);
        var stock=new FrameworkElementFactory(typeof(TextBlock));stock.SetBinding(TextBlock.TextProperty,new Binding("StockLabel"));stock.SetValue(DockPanel.DockProperty,Dock.Right);stock.SetValue(MarginProperty,new Thickness(20,0,0,0));panel.AppendChild(stock);
        var name=new FrameworkElementFactory(typeof(TextBlock));name.SetBinding(TextBlock.TextProperty,new Binding("Label"));name.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);panel.AppendChild(name);row.AppendChild(panel);template.VisualTree=row;matches.ItemTemplate=template;
        matches.PreviewMouseLeftButtonUp+=(_,e)=>{var item=ItemsControl.ContainerFromElement(matches,e.OriginalSource as DependencyObject) as ListBoxItem;if(item?.DataContext is OperationProduct product&&page.AddQuickProduct(product)){_search.Focus();e.Handled=true;}};
        matches.KeyDown+=(_,e)=>{if(e.Key==Key.Enter&&matches.SelectedItem is OperationProduct product){page.AddQuickProduct(product);_search.Focus();e.Handled=true;}};
        _search.PreviewKeyDown+=async(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;await page.EnterQuickProductAsync();}else if(e.Key==Key.Down&&matches.Items.Count>0){matches.SelectedIndex=0;matches.Focus();e.Handled=true;}};
        editor.Children.Add(matches);
        var basket=new DataGrid{AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,IsReadOnly=false,MinHeight=230,MaxHeight=470,HeadersVisibility=DataGridHeadersVisibility.Column,SelectionMode=DataGridSelectionMode.Single,Margin=new(0,0,0,12)};basket.SetBinding(ItemsControl.ItemsSourceProperty,"Basket");
        basket.SetResourceReference(Control.BackgroundProperty,"SurfaceBrush");basket.SetResourceReference(Control.ForegroundProperty,"TextBrush");
        basket.Columns.Add(new DataGridTextColumn{Header="Termék",Binding=new Binding("Name"),IsReadOnly=true,Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=170});
        basket.Columns.Add(EditColumn("Mennyiség","QuantityText",110));basket.Columns.Add(new DataGridTextColumn{Header="Egység",Binding=new Binding("UnitCode"),IsReadOnly=true,Width=75});
        var priceDisplay=new DataTemplate();var priceText=new FrameworkElementFactory(typeof(TextBlock));priceText.SetBinding(TextBlock.TextProperty,new Binding("GrossPriceDisplay"));priceDisplay.VisualTree=priceText;
        var priceEditing=new DataTemplate();var priceBox=new FrameworkElementFactory(typeof(TextBox));priceBox.SetValue(FrameworkElement.StyleProperty,EditingStyle());priceBox.SetBinding(TextBox.TextProperty,new Binding("GrossPriceText"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged,ValidatesOnDataErrors=true});priceEditing.VisualTree=priceBox;
        basket.Columns.Add(new DataGridTemplateColumn{Header="Bruttó egységár (Ft)",CellTemplate=priceDisplay,CellEditingTemplate=priceEditing,Width=170});
        basket.Columns.Add(new DataGridComboBoxColumn{Header="Áfa %",ItemsSource=new decimal[]{0,5,18,27},SelectedItemBinding=new Binding("VatRate"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged,ValidatesOnDataErrors=true},Width=85});
        basket.Columns.Add(new DataGridTextColumn{Header="Bruttó (Ft)",Binding=new Binding("GrossDisplay"),IsReadOnly=true,Width=135});
        var removeTemplate=new DataTemplate();var remove=new FrameworkElementFactory(typeof(Button));remove.SetValue(ContentControl.ContentProperty,"Törlés");remove.SetValue(Control.PaddingProperty,new Thickness(8,4,8,4));remove.SetBinding(Button.CommandProperty,new Binding("RemoveCommand"));removeTemplate.VisualTree=remove;
        basket.Columns.Add(new DataGridTemplateColumn{Header="",CellTemplate=removeTemplate,Width=85});        basket.SizeChanged+=(_,_)=>{var width=Math.Max(210,basket.ActualWidth-665);if(Math.Abs(basket.Columns[0].ActualWidth-width)>1)basket.Columns[0].Width=new DataGridLength(width);};
        basket.PreviewMouseLeftButtonDown+=(_,e)=>
        {
            DependencyObject? node=e.OriginalSource as DependencyObject;while(node is not null&&node is not DataGridCell)node=VisualTreeHelper.GetParent(node);
            if(node is DataGridCell cell&&!cell.IsReadOnly&&cell.Column is DataGridTextColumn or DataGridTemplateColumn&&!cell.IsEditing){cell.Focus();basket.CurrentCell=new DataGridCellInfo(cell.DataContext,cell.Column);basket.BeginEdit();}
        };
        editor.Children.Add(basket);
        var hint=new TextBlock{Text="Az ár és a mennyiség közvetlenül a kosárban szerkeszthető. A Törlés csak az adott sort távolítja el.",Margin=new(0,0,0,10)};hint.SetResourceReference(StyleProperty,"Muted");editor.Children.Add(hint);root.Children.Add(editor);
        var actions=new DockPanel{Margin=new(0,8,0,14)};
        var total=new TextBlock{FontSize=27,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center};total.SetBinding(TextBlock.TextProperty,"BasketTotal");actions.Children.Add(total);
        var finish=new Button{HorizontalAlignment=HorizontalAlignment.Right,Padding=new(24,14,24,14)};finish.SetResourceReference(StyleProperty,"PrimaryButton");finish.SetBinding(ContentControl.ContentProperty,"CheckoutLabel");finish.SetBinding(IsEnabledProperty,"CanBeginCheckout");
        finish.Click+=async(_,_)=>{if(!basket.CommitEdit(DataGridEditingUnit.Cell,true)||!basket.CommitEdit(DataGridEditingUnit.Row,true)){page.ShowCheckoutError("Javítsd a kosár hibás adatait.");return;}await page.OpenCheckoutAsync();};actions.Children.Add(finish);root.Children.Add(actions);
        var clear=new Button{Content="Kosár ürítése",HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,0,0,10)};clear.SetBinding(Button.CommandProperty,"ResetCommand");clear.SetBinding(IsEnabledProperty,"CanEditSale");root.Children.Add(clear);
        var message=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,16)};message.SetBinding(TextBlock.TextProperty,"Message");message.SetResourceReference(TextBlock.ForegroundProperty,"AccentBrush");root.Children.Add(message);
        var history=new Expander{Header="Korábbi eladások",IsExpanded=false};var historyPanel=new StackPanel{Margin=new(0,12,0,0)};
        var search=Input("RecordSearch");search.ToolTip="Bizonylatszám vagy vevő";historyPanel.Children.Add(search);historyPanel.Children.Add(Action("Keresés / frissítés","RefreshCommand"));
        var records=new DataGrid{AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,MaxHeight=300,MinHeight=100,Margin=new(0,8,0,8)};records.SetBinding(ItemsControl.ItemsSourceProperty,"Rows");records.SetBinding(DataGrid.SelectedItemProperty,new Binding("SelectedRow"){Mode=BindingMode.TwoWay});
        foreach(var (label,path) in new[]{("Bizonylat","number"),("Vevő","customerName"),("Bruttó","gross"),("Fizetendő","payable"),("Állapot","statusLabel")})records.Columns.Add(new DataGridTextColumn{Header=label,Binding=new Binding("["+path+"]"),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        historyPanel.Children.Add(records);var buttons=new WrapPanel();buttons.Children.Add(Action("Tételek","DetailsCommand"));buttons.Children.Add(Action("Visszafordítás","ReverseCommand"));buttons.Children.Add(Action("CSV export","ExportCommand"));historyPanel.Children.Add(buttons);var details=new TextBlock{TextWrapping=TextWrapping.Wrap};details.SetBinding(TextBlock.TextProperty,"Details");historyPanel.Children.Add(details);history.Content=historyPanel;root.Children.Add(history);Content=root;
    }
    private static DataGridTextColumn EditColumn(string heading,string path,int width)=>new(){Header=heading,Width=width,Binding=new Binding(path){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged,ValidatesOnDataErrors=true},EditingElementStyle=EditingStyle()};
    private static Style EditingStyle(){var style=new Style(typeof(TextBox));style.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension("ElevatedBrush")));style.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension("TextBrush")));style.Setters.Add(new Setter(TextBox.CaretBrushProperty,new DynamicResourceExtension("TextBrush")));return style;}
    private static TextBox Input(string path){var box=new TextBox{MinHeight=36,Padding=new(10)};box.SetResourceReference(Control.BackgroundProperty,"ElevatedBrush");box.SetResourceReference(Control.ForegroundProperty,"TextBrush");box.SetResourceReference(Control.BorderBrushProperty,"StrokeBrush");box.SetBinding(TextBox.TextProperty,new Binding(path){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});return box;}
    private static Button Action(string text,string command){var b=new Button{Content=text,Margin=new(0,6,10,6)};b.SetBinding(Button.CommandProperty,command);b.SetBinding(IsEnabledProperty,command=="ReverseCommand"?"CanWrite":command=="ExportCommand"?"CanExport":"IsNotBusy");return b;}
}
