using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PcImpressz.Shell.Windows;
internal sealed class OperationsPageView:UserControl
{
    private OperationsPageViewModel? _observed;
    private System.ComponentModel.PropertyChangedEventHandler? _layoutHandler;
    public OperationsPageView()
    {
        DataContextChanged+=(_,_)=>Build();
        Loaded+=(_,_)=>{if(_observed is not null && _layoutHandler is not null){_observed.PropertyChanged-=_layoutHandler;_observed.PropertyChanged+=_layoutHandler;}};
        Unloaded+=(_,_)=>{if(_observed is not null && _layoutHandler is not null)_observed.PropertyChanged-=_layoutHandler;};
    }
    private void Build()
    {
        if(_observed is not null && _layoutHandler is not null)_observed.PropertyChanged-=_layoutHandler;
        if(DataContext is not OperationsPageViewModel page)return;
        _observed=page;
        var root=new StackPanel();
        var actions=new WrapPanel();actions.Children.Add(Button("Frissítés","RefreshCommand"));actions.Children.Add(Button("CSV export","ExportCommand"));actions.Children.Add(ListGrid.ColumnButton(()=>DataContext as IColumnList));root.Children.Add(actions);
        var message=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,16)};message.SetResourceReference(TextBlock.ForegroundProperty,"AccentBrush");message.SetBinding(TextBlock.TextProperty,new Binding("Message"));root.Children.Add(message);
        root.Children.Add(Label(page.IsInventory?"Készletváltozás":"Új eladás"));
        if(page.IsInventory){var scope=new TextBlock{FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,10)};scope.SetBinding(TextBlock.TextProperty,new Binding("StockScope"));root.Children.Add(scope);}
        if(page.IsInventory)root.Children.Add(new TextBlock{Text="A régi, csak besorolt számlákból nem képzünk visszamenőleg készletet. A tényleges induló mennyiséget egyszer, nyitókészletként rögzítsd.",TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)});
        var editor=new StackPanel();editor.SetBinding(IsEnabledProperty,new Binding("CanWrite"));
        editor.Children.Add(Field("Termékkeresés (név, cikkszám, gyártói cikkszám, vonalkód)","ProductSearch"));
        editor.Children.Add(Button("Választók frissítése","RefreshCommand"));
        editor.Children.Add(Combo("Termék","Products","SelectedProduct","Label"));
        editor.Children.Add(Field("Raktár","Warehouse"));
        if(page.IsSales){var stock=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,10)};stock.SetBinding(TextBlock.TextProperty,new Binding("SelectedStockInfo"));editor.Children.Add(stock);}
        editor.Children.Add(Field(page.IsInventory?"Mennyiség (+ bevét / − kiadás)":"Mennyiség","QuantityText"));
        if(page.IsInventory)editor.Children.Add(Field("Indoklás (nyitókészlet vagy korrekció)","Reason"));
        else
        {
            editor.Children.Add(Field("Nettó egységár (Ft)","PriceText"));editor.Children.Add(Field("Áfa %","VatText"));
            editor.Children.Add(Field("Vevőkeresés (név vagy adószám)","CustomerSearch"));
            editor.Children.Add(Button("Vevők keresése","RefreshCommand"));
            editor.Children.Add(Combo("Vevő","Customers","SelectedCustomer","Name"));
            var lineButtons=new WrapPanel();lineButtons.Children.Add(Button("Tétel hozzáadása","AddLineCommand"));lineButtons.Children.Add(Button("Kijelölt tétel szerkesztése","EditLineCommand"));lineButtons.Children.Add(Button("Kijelölt tétel cseréje","UpdateLineCommand"));lineButtons.Children.Add(Button("Kijelölt tétel törlése","RemoveLineCommand"));editor.Children.Add(lineButtons);
            var basket=Grid("Basket","SelectedBasketLine");
            foreach(var (title,path) in new[]{("Termék","Name"),("Egység","UnitCode"),("Mennyiség","Quantity"),("Nettó egységár","NetUnitPrice"),("Áfa %","VatRate"),("Bruttó","Gross")})basket.Columns.Add(new DataGridTextColumn{Header=title,Binding=new Binding(path){StringFormat=path is "NetUnitPrice" or "Gross" ? "N2" : null},Width=new DataGridLength(path=="Name"?300:path=="NetUnitPrice"?150:110)});
            editor.Children.Add(basket);
            var total=Label("");total.SetBinding(TextBlock.TextProperty,new Binding("BasketTotal"));editor.Children.Add(total);
            editor.Children.Add(new TextBlock{Text="A tételek a hozzáadáskor megadott árral kerülnek a kosárba. Véglegesítés után szerkesztés helyett visszafordítás használható.",TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,8)});
        }
        var save=new WrapPanel();var primary=Button(page.SaveLabel,"SaveCommand");primary.SetResourceReference(StyleProperty,"PrimaryButton");save.Children.Add(primary);save.Children.Add(Button("Új művelet / szerkesztés elvetése","ResetCommand"));editor.Children.Add(save);
        var card=new Border{Child=editor,Padding=new(22),Margin=new(0,0,0,18),CornerRadius=new(12)};card.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");root.Children.Add(card);
        root.Children.Add(Label(page.IsInventory?"Készletegyenleg":"Eladási bizonylatok (legutóbbi 200)"));
        root.Children.Add(Field(page.IsInventory?"Készletlista keresése (termék vagy raktár)":"Bizonylatkeresés (szám vagy vevő)","RecordSearch"));
        root.Children.Add(Button("Lista keresése","RefreshCommand"));
        var notice=new TextBlock{TextWrapping=TextWrapping.Wrap};notice.SetBinding(TextBlock.TextProperty,new Binding("ListNotice"));root.Children.Add(notice);
        var rows=Grid("Rows","SelectedRow");
        ListGrid.Prepare(rows,()=>page);ListGrid.Show(rows,page);
        _layoutHandler=(_,e)=>{if(e.PropertyName=="LayoutRevision")ListGrid.Show(rows,page);};
        page.PropertyChanged+=_layoutHandler;
        root.Children.Add(rows);
        if(page.IsInventory)
        {
            root.Children.Add(Label("Mozgásnapló (legutóbbi 200)"));var movements=Grid("Movements","SelectedMovement");
            foreach(var (title,path) in new[]{("Idő","at"),("Termék","productName"),("Raktár","warehouse"),("Mennyiség","quantity"),("Indoklás","reason")})movements.Columns.Add(new DataGridTextColumn{Header=title,Binding=new Binding("["+path+"]"),Width=new DataGridLength(path=="productName"?260:path=="reason"?300:path=="at"?160:110)});
            root.Children.Add(movements);
        }
        if(page.IsSales)
        {
            root.Children.Add(Button("Kijelölt eladás tételei","DetailsCommand"));
            var details=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=300,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new(0,0,0,12)};
            details.SetResourceReference(Control.BackgroundProperty,"ElevatedBrush");details.SetResourceReference(Control.ForegroundProperty,"TextBrush");details.SetBinding(TextBox.TextProperty,new Binding("Details"){Mode=BindingMode.OneWay});root.Children.Add(details);
        }
        root.Children.Add(Button(page.IsInventory?"Kijelölt kézi mozgás visszafordítása":"Kijelölt eladás visszafordítása","ReverseCommand"));
        Content=root;
    }
    private static DataGrid Grid(string source,string selected)
    {
        var grid=new DataGrid{AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,MinHeight=110,MaxHeight=340,Margin=new(0,8,0,14),SelectionMode=DataGridSelectionMode.Single};
        grid.SetBinding(ItemsControl.ItemsSourceProperty,new Binding(source));grid.SetBinding(DataGrid.SelectedItemProperty,new Binding(selected){Mode=BindingMode.TwoWay});return grid;
    }
    private static TextBlock Label(string text)=>new(){Text=text,FontWeight=FontWeights.SemiBold,FontSize=16,Margin=new(0,8,0,10)};
    private static UIElement Combo(string label,string source,string selected,string display)
    {
        var panel=new StackPanel{Margin=new(0,0,0,12)};panel.Children.Add(new TextBlock{Text=label,Margin=new(0,0,0,4)});
        var combo=new ComboBox{DisplayMemberPath=display,MinHeight=36};combo.SetBinding(ItemsControl.ItemsSourceProperty,new Binding(source));combo.SetBinding(ComboBox.SelectedItemProperty,new Binding(selected){Mode=BindingMode.TwoWay});panel.Children.Add(combo);return panel;
    }
    private static UIElement Field(string label,string path)
    {
        var panel=new StackPanel{Margin=new(0,0,0,12)};panel.Children.Add(new TextBlock{Text=label,Margin=new(0,0,0,4)});
        var box=new TextBox{MinHeight=34,Padding=new(8,4,8,4)};box.SetResourceReference(Control.BackgroundProperty,"ElevatedBrush");box.SetResourceReference(Control.ForegroundProperty,"TextBrush");box.SetResourceReference(Control.BorderBrushProperty,"StrokeBrush");box.SetBinding(TextBox.TextProperty,new Binding(path){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});panel.Children.Add(box);return panel;
    }
    private static Button Button(string text,string command)
    {var button=new Button{Content=text,Margin=new(0,0,10,10)};button.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,new Binding(command));button.SetBinding(IsEnabledProperty,new Binding(command is "SaveCommand" or "ReverseCommand"?"CanWrite":command=="ExportCommand"?"CanExport":"IsNotBusy"));return button;}
}
