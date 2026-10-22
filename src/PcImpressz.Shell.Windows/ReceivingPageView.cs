using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PcImpressz.Shell.Windows;

public sealed class ReceivingPageView : UserControl
{
    private readonly DataGrid _lines = new();
    private DataGrid _receipts = null!;
    private ReceivingPageViewModel? _page;
    public ReceivingPageView()
    {
        var root = new StackPanel();
        root.Children.Add(Text("Számlák",26,true));
        var tabs=new WrapPanel{Margin=new(0,0,0,16)};
        tabs.Children.Add(Tab("Bejövő számlák","IncomingCommand","IsIncoming"));
        tabs.Children.Add(Tab("Kimenő számlák","OutgoingCommand","IsOutgoing"));
        root.Children.Add(tabs);
        var incoming=new StackPanel();
        incoming.SetBinding(VisibilityProperty,new Binding("IsIncoming"){Converter=new BooleanToVisibilityConverter()});
                var incomingWorkspace=new ArrangeableWorkspace("incoming");incomingWorkspace.AddPanel("list","Bejövő számlák listája",(FrameworkElement)Home());incomingWorkspace.AddPanel("nav","NAV-lekérés és számlabevételezés",(FrameworkElement)Intake());incoming.Children.Add(incomingWorkspace);root.Children.Add(incoming);
        var outgoingWorkspace=new ArrangeableWorkspace("outgoing");outgoingWorkspace.SetBinding(VisibilityProperty,new Binding("IsOutgoing"){Converter=new BooleanToVisibilityConverter()});outgoingWorkspace.AddPanel("list","Kimenő számlák",(FrameworkElement)Outgoing());root.Children.Add(outgoingWorkspace);
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DataContextChanged += (_, _) => Hook();
    }
    private UIElement Home()
    {
        var panel = new StackPanel();

        panel.Children.Add(Text("Bejövő számlák", 22, true));
        panel.Children.Add(Muted("Válaszd ki, honnan jön a számla. Ami NAV-ról vagy importból érkezik, új állapotba kerül: bevételezed, költségként hagyod, vagy függőben marad."));
        var modes = new WrapPanel { Margin = new(0, 0, 0, 12) };
        modes.Children.Add(Primary("Kézi bevételezés", "ManualCommand"));
        modes.Children.Add(Button("NAV-számlák lekérése", "NavCommand"));
        modes.Children.Add(Button("PDF alapján rögzítés", "PdfCommand"));
        modes.Children.Add(Button("Excel / CSV / XML import", "ImportCommand"));
        panel.Children.Add(modes);

        var home=panel;var listing=new StackPanel();listing.SetBinding(VisibilityProperty,new Binding("IsReceiptListVisible"){Converter=new BooleanToVisibilityConverter()});home.Children.Add(listing);panel=listing;
        panel.Children.Add(Bound(Muted(""), "CountsLabel"));
        var tools = new WrapPanel();
        var search = Input(); search.Width = 240; search.SetBinding(TextBox.TextProperty, new Binding("Search") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        tools.Children.Add(search);
        tools.Children.Add(ListGrid.MatchCount());
        var filter = new ComboBox { Width = 220, Margin = new(0, 0, 10, 10), DisplayMemberPath = "Label", SelectedValuePath = "Value" };
        filter.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Filters"));
        filter.SetBinding(ComboBox.SelectedValueProperty, new Binding("Filter"));
        tools.Children.Add(filter);
        tools.Children.Add(Button("Keresés", "SearchCommand"));
        tools.Children.Add(Button("Megnyitás", "OpenCommand"));




        tools.Children.Add(Button("CSV", "ExportCommand"));
        tools.Children.Add(ListGrid.ColumnButton(() => DataContext as IColumnList));
        panel.Children.Add(tools);
        panel.Children.Add(Bound(Muted(""), "Message"));
        _receipts = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column, MinHeight = 220, MaxHeight = 320, Margin = new(0, 8, 0, 0) };
        _receipts.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Rows"));
        _receipts.SetBinding(DataGrid.SelectedItemProperty, new Binding("SelectedRow"));
        _receipts.MouseDoubleClick += (_, _) => { if (DataContext is ReceivingPageViewModel page) page.OpenCommand.Execute(null); };
        ListGrid.Prepare(_receipts, () => DataContext as IColumnList);
        panel.Children.Add(_receipts);
        return home;
    }
    private UIElement Intake()
    {
        var panel = new StackPanel();

        var nav = panel;
        var import = new StackPanel();
        import.SetBinding(VisibilityProperty, new Binding("IsImport") { Converter = new BooleanToVisibilityConverter() });
        var both = new StackPanel();
        var navExpander=new Expander{Header="NAV-lekérés – bejövő számlák",Content=nav,Margin=new(0,0,0,16)};
        navExpander.SetBinding(Expander.IsExpandedProperty,new Binding("IsNav"){Mode=BindingMode.TwoWay});
        navExpander.SetBinding(IsEnabledProperty,"IsNotBusy");
        both.Children.Add(navExpander);
        both.Children.Add(import);both.Children.Add(Editor());
        FillIntake(nav, true);
        FillIntake(import, false);
        return both;
    }
    private void FillIntake(StackPanel panel, bool nav)
    {
        if(!nav)panel.SetBinding(VisibilityProperty, new Binding("IsImport") { Converter = new BooleanToVisibilityConverter() });
        if(!nav)panel.Children.Add(Bound(Text("", 22, true), "IntakeTitle"));
        panel.Children.Add(Bound(Muted(""), "IntakeHelp"));
        if(nav)
        {
            var dates=new WrapPanel{Margin=new(0,0,0,12)};dates.Children.Add(Text("Kezdő nap",14,false));var from=new DatePicker{Width=160,Margin=new(10,0,18,0)};from.SetBinding(DatePicker.SelectedDateProperty,new Binding("NavFrom"){Mode=BindingMode.TwoWay});from.SetBinding(IsEnabledProperty,"IsNotBusy");dates.Children.Add(from);dates.Children.Add(Text("Záró nap",14,false));var to=new DatePicker{Width=160,Margin=new(10,0,18,0)};to.SetBinding(DatePicker.SelectedDateProperty,new Binding("NavTo"){Mode=BindingMode.TwoWay});to.SetBinding(IsEnabledProperty,"IsNotBusy");dates.Children.Add(to);dates.Children.Add(Primary("Bejövő számlák lekérése","FetchNavCommand"));panel.Children.Add(dates);
            var invoices=new DataGrid{AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,MinHeight=100,MaxHeight=250,Margin=new(0,0,0,12)};invoices.HeadersVisibility=DataGridHeadersVisibility.Column;invoices.RowStyle=NavRowStyle();invoices.SetBinding(ItemsControl.ItemsSourceProperty,"NavInvoices");invoices.SetBinding(DataGrid.SelectedItemProperty,new Binding("SelectedNavInvoice"){Mode=BindingMode.TwoWay});invoices.SetBinding(IsEnabledProperty,"IsNotBusy");foreach(var (title,path) in new[]{("Állapot","State"),("Kelt","IssuedOn"),("Számlaszám","Number"),("Szállító","SupplierName"),("Adószám","SupplierTaxNumber"),("Nettó","Net"),("Pénznem","Currency"),("Művelet","Operation")})invoices.Columns.Add(new DataGridTextColumn{Header=title,Binding=new Binding(path),MinWidth=path=="SupplierName"?220:path=="Number"?150:path=="Currency"?80:110,Width=path=="SupplierName"?new DataGridLength(1,DataGridLengthUnitType.Star):new DataGridLength(path=="Number"?160:path=="Currency"?80:120)});panel.Children.Add(invoices);
            var pages=new WrapPanel();pages.Children.Add(Button("Előző oldal","PreviousNavCommand"));pages.Children.Add(Button("Következő oldal","NextNavCommand"));pages.Children.Add(Bound(Muted(""),"NavPageLabel"));panel.Children.Add(pages);
        }
        if(!nav){panel.Children.Add(Text("Szállító", 14, true));panel.Children.Add(Suppliers());}
        if(!nav)panel.Children.Add(Button("Új szállító a partnertörzsbe", "NewSupplierCommand"));
        if(nav)
        {
            panel.Children.Add(Button("Előnézet frissítése","DownloadNavCommand"));
            panel.Children.Add(new Expander{Header="Korábban letöltött NAV XML betöltése",Content=Button("NAV XML megnyitása","PickFileCommand"),Margin=new(0,12,0,12)});
            panel.Children.Add(Bound(Muted(""),"Message"));return;
        }
        panel.Children.Add(Labeled("Számlaszám (XML-ből kitölthető)", "Number"));
        panel.Children.Add(Labeled("Kelt (éééé-hh-nn, üresen a fájl vagy a mai nap)", "IssuedOn"));
        panel.Children.Add(Primary(nav ? "NAV-számla XML megnyitása" : "Fájl kiválasztása és beolvasás", "PickFileCommand"));
        panel.Children.Add(Button("Import bezárása", "BackCommand"));
        panel.Children.Add(Bound(Muted(""), "Message"));
    }
    private UIElement Editor()
    {
        var panel = new StackPanel();
        panel.SetBinding(VisibilityProperty, new Binding("IsEditor") { Converter = new BooleanToVisibilityConverter() });
        panel.Children.Add(new Separator{Margin=new(0,20,0,16)});
        panel.Children.Add(Bound(Text("", 22, true), "EditorTitle"));
        var pdf=new StackPanel();pdf.SetBinding(VisibilityProperty,new Binding("HasPdfPreview"){Converter=new BooleanToVisibilityConverter()});
        pdf.Children.Add(Bound(Text("",14,true),"PdfName"));
        pdf.Children.Add(Muted("PDF-ből kiolvasott szöveg – a számlaadatokat és tételeket ellenőrizve töltsd ki. Az eredeti PDF-et őrizd meg külön."));
        var preview=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,MinHeight=140,MaxHeight=280,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new(0,0,0,16)};
        preview.SetBinding(TextBox.TextProperty,new Binding("PdfText"){Mode=BindingMode.OneWay});pdf.Children.Add(preview);panel.Children.Add(pdf);
        panel.Children.Add(Muted("A szállítási díj és a hasonló sor szolgáltatás: termék nélkül is bevételezhető. Készletes sornál válassz terméket, vagy vegyél fel újat."));
        panel.Children.Add(Text("Szállító", 14, true));
        panel.Children.Add(Suppliers());
        panel.Children.Add(Bound(Muted(""),"NavSupplierHint"));
        var supplierButton = Button("Új szállító", "NewSupplierCommand");
        supplierButton.SetBinding(VisibilityProperty, new Binding("IsEditable") { Converter = new BooleanToVisibilityConverter() });
        panel.Children.Add(supplierButton);
        var mainFields=new System.Windows.Controls.Primitives.UniformGrid{Columns=2};mainFields.Children.Add(Labeled("Számlaszám","Number"));mainFields.Children.Add(Labeled("Kelt","IssuedOn"));panel.Children.Add(mainFields);
        var extraFields=new System.Windows.Controls.Primitives.UniformGrid{Columns=2};extraFields.Children.Add(Labeled("Fizetési határidő","DueOn"));extraFields.Children.Add(Labeled("Raktár","Warehouse"));
        var extra=new StackPanel();extra.Children.Add(extraFields);extra.Children.Add(Labeled("Megjegyzés","Notes"));panel.Children.Add(new Expander{Header="További számlaadatok és megjegyzés",Content=extra,Margin=new(0,0,0,10)});
        panel.Children.Add(Text("Tételek", 16, true));
        _lines.AutoGenerateColumns=false;_lines.CanUserAddRows=false;_lines.CanUserDeleteRows=false;_lines.HeadersVisibility=DataGridHeadersVisibility.Column;
        _lines.MinHeight=180;_lines.MaxHeight=460;_lines.RowDetailsVisibilityMode=DataGridRowDetailsVisibilityMode.VisibleWhenSelected;
        _lines.SetBinding(ItemsControl.ItemsSourceProperty,"Lines");_lines.SetBinding(DataGrid.IsReadOnlyProperty,"IsLocked");
        foreach(var (title,path,width) in new[]{("Megnevezés","Name",230d),("Mennyiség","QuantityText",95d),("Egység","UnitCode",75d),("Nettó beszerzés","PriceText",120d),("Áfa %","VatText",70d),("Bruttó kisker (Ft)","RetailPrice",140d),("Haszonkulcs %","Markup",125d),("Eladási áfa %","SaleVat",115d),("Párosítás","MatchLabel",150d)})
            _lines.Columns.Add(new DataGridTextColumn{Header=title,Width=width,IsReadOnly=path=="MatchLabel",Binding=new Binding(path){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged}});
        _lines.RowDetailsTemplate=new DataTemplate{VisualTree=new FrameworkElementFactory(typeof(LineDetails))};
        panel.Children.Add(Muted("Kattints a tételre a termékpárosításhoz és a további adatokhoz. A Termék és ár mentése azonnal ment a terméktörzsbe; a számla Mentés gombja a módosított termékadatokat is elmenti."));
        panel.Children.Add(_lines);
        var add = Button("Új tételsor", "AddLineCommand");
        add.SetBinding(VisibilityProperty, new Binding("IsEditable") { Converter = new BooleanToVisibilityConverter() });
        panel.Children.Add(add);
        panel.Children.Add(Bound(Text("", 14, true), "NetLabel"));
        panel.Children.Add(Bound(Text("", 14, true), "GrossLabel"));
        var save = Primary("Mentés", "SaveCommand");
        save.SetBinding(VisibilityProperty, new Binding("IsEditable") { Converter = new BooleanToVisibilityConverter() });
        var actions = new WrapPanel { Margin = new(0, 8, 0, 0) };
        actions.Children.Add(save);
        actions.Children.Add(Classify("Bevételezés", "ReceiveCommand"));
        actions.Children.Add(Button("Bevételezés visszafordítása", "ReverseReceiptCommand"));
        actions.Children.Add(Classify("Költség", "ExpenseCommand"));
        actions.Children.Add(Classify("Függőben", "PendingCommand"));
        actions.Children.Add(Button("Szerkesztő bezárása", "BackCommand"));
        panel.Children.Add(actions);
        panel.Children.Add(Bound(Muted(""), "Message"));
        return panel;
    }
    private UIElement Outgoing()
    {
        var panel=new StackPanel();panel.SetBinding(VisibilityProperty,new Binding("IsOutgoing"){Converter=new BooleanToVisibilityConverter()});
        panel.Children.Add(Text("Kimenő számlák",22,true));
        panel.Children.Add(Muted("Elsődleges forrás: a PC-Impressz saját adatbázisa. A Bizonylat API állítja ki a számlát; a NAV csak külön egyeztetés."));
        var dates=new WrapPanel();dates.Children.Add(Text("Időszak",14,false));
        foreach(var path in new[]{"OutgoingFrom","OutgoingTo"}){var date=new DatePicker{Width=150,Margin=new(10,0,10,12)};date.SetBinding(DatePicker.SelectedDateProperty,new Binding(path){Mode=BindingMode.TwoWay});date.SetBinding(IsEnabledProperty,"IsNotBusy");dates.Children.Add(date);}panel.Children.Add(dates);
        var filters=new WrapPanel();filters.SetBinding(IsEnabledProperty,"IsNotBusy");var search=Input();search.Width=230;search.SetBinding(TextBox.TextProperty,new Binding("OutgoingSearch"){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});filters.Children.Add(search);
        var pay=new ComboBox{Width=160,Margin=new(0,0,10,10),DisplayMemberPath="Label",SelectedValuePath="Value"};pay.SetBinding(ItemsControl.ItemsSourceProperty,"OutgoingPaymentOptions");pay.SetBinding(ComboBox.SelectedValueProperty,"OutgoingPayment");filters.Children.Add(pay);
        var nav=new ComboBox{Width=180,Margin=new(0,0,10,10),DisplayMemberPath="Label",SelectedValuePath="Value"};nav.SetBinding(ItemsControl.ItemsSourceProperty,"OutgoingNavOptions");nav.SetBinding(ComboBox.SelectedValueProperty,"OutgoingNavStatus");filters.Children.Add(nav);
        filters.Children.Add(Primary("Keresés / frissítés","FetchOutgoingCommand"));filters.Children.Add(Button("Szűrők törlése","ClearOutgoingFiltersCommand"));filters.Children.Add(Button("NAV egyeztetés","ReconcileOutgoingCommand"));panel.Children.Add(filters);
        panel.Children.Add(Bound(Text("",14,true),"OutgoingSummary"));panel.Children.Add(Bound(Muted(""),"OutgoingMessage"));
        var grid=new DataGrid{AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,MinHeight=320,MaxHeight=650,HeadersVisibility=DataGridHeadersVisibility.Column};grid.SetBinding(ItemsControl.ItemsSourceProperty,"OutgoingInvoices");
        foreach(var c in new[]{("Kelt","IssuedOn",105d),("Számlaszám","Number",150d),("Vevő","CustomerName",210d),("Adószám","CustomerTaxNumber",120d),("Fizetés","PaymentMethod",110d),("Határidő","DueOn",105d),("Nettó","Net",110d),("ÁFA","Vat",100d),("Bruttó","Gross",110d),("Pénznem","Currency",80d),("Pénzügy","FinancialStatus",120d),("NAV","NavStatus",130d)})grid.Columns.Add(new DataGridTextColumn{Header=c.Item1,Binding=new Binding(c.Item2),Width=c.Item3});
        panel.Children.Add(grid);var paging=new WrapPanel{Margin=new(0,12,0,0)};paging.Children.Add(Button("Előző oldal","PreviousOutgoingCommand"));paging.Children.Add(Button("Következő oldal","NextOutgoingCommand"));paging.Children.Add(Bound(Muted(""),"OutgoingPageLabel"));panel.Children.Add(paging);return panel;
    }
    private void Hook()
    {
        if (_page is not null)
        {
            _page.Lines.CollectionChanged -= LinesChanged;
            _page.PropertyChanged -= PageChanged;
        }
        _page = DataContext as ReceivingPageViewModel;
        if (_page is not null)
        {
            _page.Lines.CollectionChanged += LinesChanged;
            _page.PropertyChanged += PageChanged;
            ListGrid.Show(_receipts, _page);
        }
        Rebuild();
    }
    private void PageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ReceivingPageViewModel.LayoutRevision) && _page is not null)
            ListGrid.Show(_receipts, _page);
    }
    private void LinesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();
    private void Rebuild()
    {
    }
    public sealed class LineDetails:ContentControl
    {
        public LineDetails(){DataContextChanged+=(_,_)=>{if(DataContext is LineEditor line){Content=new LineCard(line);line.LoadProductCommand.Execute(null);}};}
    }
    private static Style NavRowStyle()
    {
        var style=new Style(typeof(DataGridRow));
        style.Setters.Add(new Setter(BackgroundProperty,new DynamicResourceExtension("NavPendingBrush")));
        style.Setters.Add(new Setter(ForegroundProperty,new DynamicResourceExtension("NavPendingTextBrush")));
        var done=new DataTrigger{Binding=new Binding("Received"),Value=true};
        done.Setters.Add(new Setter(BackgroundProperty,new DynamicResourceExtension("NavReceivedBrush")));
        done.Setters.Add(new Setter(ForegroundProperty,new DynamicResourceExtension("NavReceivedTextBrush")));style.Triggers.Add(done);
        return style;
    }
    private static Button Classify(string content, string command)
    {
        var button = Button(content, command);
        button.SetBinding(VisibilityProperty, new Binding("CanClassify") { Converter = new BooleanToVisibilityConverter() });
        return button;
    }
    private static ComboBox Suppliers(string selection="SelectedSupplier")
    {
        var combo = new ComboBox { MinHeight = 36, Margin = new(0, 0, 0, 10), DisplayMemberPath = "Label" };
        combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Suppliers"));
        combo.SetBinding(ComboBox.SelectedItemProperty, new Binding(selection));
        combo.SetBinding(IsEnabledProperty, new Binding(selection=="SelectedNavSupplier"?"IsNotBusy":"CanPickSupplier"));
        return combo;
    }
    private static UIElement Labeled(string label, string path)
    {
        var panel = new StackPanel { Margin = new(0, 0, 0, 8) };
        panel.Children.Add(Muted(label));
        var box = Input();
        box.SetBinding(TextBox.TextProperty, new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        box.SetBinding(TextBox.IsReadOnlyProperty, new Binding("IsLocked"));
        panel.Children.Add(box);
        return panel;
    }
    private static TextBlock Text(string value, double size, bool bold)
    {
        var block = new TextBlock { Text = value, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = new(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        return block;
    }
    private static TextBlock Muted(string value)
    {
        var block = Text(value, 13, false);
        block.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        block.Margin = new(0, 0, 0, 12);
        return block;
    }
    private static TextBlock Bound(TextBlock block, string path) { block.SetBinding(TextBlock.TextProperty, new Binding(path)); return block; }
    private static TextBox Input()
    {
        var box = new TextBox { MinHeight = 36, Margin = new(0, 0, 10, 10), Padding = new(8, 4, 8, 4) };
        box.SetResourceReference(Control.BackgroundProperty, "ElevatedBrush");
        box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        box.SetResourceReference(Control.BorderBrushProperty, "StrokeBrush");
        return box;
    }
    private static Button Button(string content, string command)
    {
        var button = new Button { Content = content, HorizontalAlignment=HorizontalAlignment.Left, Margin = new(0, 0, 10, 10) };
        button.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty, new Binding(command));
        button.SetBinding(IsEnabledProperty, new Binding("IsNotBusy"));
        return button;
    }
    private static Button Tab(string title,string command,string selected)
    {
        var button=Button(title,command);
        button.SetBinding(StyleProperty,new Binding(selected){Converter=new InvoiceTabStyle()});
        return button;
    }
    private sealed class InvoiceTabStyle:IValueConverter
    {
        public object Convert(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>Application.Current.FindResource(value is true ? "PrimaryButton" : typeof(Button));
        public object ConvertBack(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>throw new NotSupportedException();
    }
    private static Button Primary(string content, string command)
    {
        var button = Button(content, command);
        button.SetResourceReference(StyleProperty, "PrimaryButton");
        return button;
    }


    public sealed class ProductFieldControl:ContentControl
    {
        public ProductFieldControl(){DataContextChanged+=(_,_)=>Build();}
        private void Build()
        {
            if(DataContext is not EntityFieldViewModel field)return;
            if(field.Definition.Id is "saleNetUnitPrice" or "saleVatRate"){Visibility=Visibility.Collapsed;return;}
            var panel=new StackPanel{Margin=new(0,0,0,8)};panel.Children.Add(Text(field.Label,13,false));
            if(field.Definition.Kind=="boolean"){var check=new CheckBox();check.SetBinding(CheckBox.IsCheckedProperty,new Binding("Checked"){Mode=BindingMode.TwoWay});panel.Children.Add(check);}
            else{var input=Input();input.AcceptsReturn=field.Definition.Kind=="multiline";input.TextWrapping=TextWrapping.Wrap;input.MinHeight=input.AcceptsReturn?65:34;input.SetBinding(TextBox.TextProperty,new Binding("Text"){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});panel.Children.Add(input);}
            panel.SetBinding(IsEnabledProperty,new Binding("DataContext.IsEditable"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(ReceivingPageView),1)});Content=panel;
        }
    }
    private sealed class LineCard : Border
    {
        public LineCard(LineEditor line)
        {
            DataContext = line;
            Margin = new(0, 0, 0, 12); Padding = new(12); BorderThickness = new(1); CornerRadius = new(10);
            SetResourceReference(BorderBrushProperty, "StrokeBrush"); SetResourceReference(BackgroundProperty, "SurfaceBrush");
            var panel = new StackPanel();
            panel.Children.Add(Field("Termék neve", "Name"));
            var service = new CheckBox { Content = "Szolgáltatás (nem készletes tétel)", Margin = new(0, 0, 0, 10) };
            service.SetBinding(CheckBox.IsCheckedProperty, new Binding("IsService"));
            service.SetBinding(IsEnabledProperty, new Binding("DataContext.IsEditable") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ReceivingPageView), 1) });
            panel.Children.Add(service);
            var codes = new Grid();
            codes.ColumnDefinitions.Add(new()); codes.ColumnDefinitions.Add(new()); codes.ColumnDefinitions.Add(new());
            codes.Children.Add(Field("Cikkszám", "Sku"));
            var barcode = Field("Vonalkód", "Barcode"); Grid.SetColumn(barcode, 1); codes.Children.Add(barcode);
            var unit = Field("Egység", "UnitCode"); Grid.SetColumn(unit, 2); codes.Children.Add(unit);
            panel.Children.Add(codes);
            var search = new WrapPanel();
            var box = new TextBox { Width = 220, MinHeight = 34, Margin = new(0, 0, 8, 8) };
            box.SetBinding(TextBox.TextProperty, new Binding("SearchText") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            search.Children.Add(box);
            search.Children.Add(LineButton("Keresés a terméktörzsben", "SearchCommand"));
            search.Children.Add(LineButton("Termék és ár mentése", "SaveProductCommand"));
            search.Children.Add(LineButton("Ár számítása haszonkulccsal", "CalculateRetailCommand"));
            search.Children.Add(LineButton("Sor törlése", "RemoveCommand"));
            panel.Children.Add(search);
            var matches = new ComboBox { MinHeight = 34, Margin = new(0, 0, 0, 8), DisplayMemberPath = "Label" };
            matches.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Matches"));
            matches.SetBinding(ComboBox.SelectedItemProperty, new Binding("SelectedMatch"));
            panel.Children.Add(matches);
            var amounts = new Grid();
            amounts.ColumnDefinitions.Add(new()); amounts.ColumnDefinitions.Add(new()); amounts.ColumnDefinitions.Add(new());
            amounts.Children.Add(Field("Mennyiség", "QuantityText"));
            var price = Field("Nettó egységár", "PriceText"); Grid.SetColumn(price, 1); amounts.Children.Add(price);
            var vat = Field("Áfa %", "VatText"); Grid.SetColumn(vat, 2); amounts.Children.Add(vat);
            panel.Children.Add(amounts);
            var hint = new TextBlock { Margin = new(0, 4, 0, 0) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            hint.SetBinding(TextBlock.TextProperty, new Binding("SuggestedSale"));
            var retailFields=new System.Windows.Controls.Primitives.UniformGrid{Columns=3};retailFields.Children.Add(Field("Bruttó kisker ár (Ft)","RetailPrice"));retailFields.Children.Add(Field("Haszonkulcs %","Markup"));retailFields.Children.Add(Field("Eladási áfa %","SaleVat"));panel.Children.Add(retailFields);
            panel.Children.Add(hint);
            var fields=new ItemsControl();fields.SetBinding(ItemsControl.ItemsSourceProperty,"ProductFields");fields.ItemTemplate=new DataTemplate{VisualTree=new FrameworkElementFactory(typeof(ProductFieldControl))};
            panel.Children.Add(new Expander{Header="További termékadatok kitöltése",Content=fields,Margin=new(0,10,0,10)});
            var match = new TextBlock { FontWeight = FontWeights.SemiBold };
            match.SetBinding(TextBlock.TextProperty, new Binding("MatchLabel"));
            panel.Children.Add(match);
            Child = panel;
        }
        private static UIElement Field(string label, string path)
        {
            var panel = new StackPanel { Margin = new(0, 0, 8, 8) };
            var caption = new TextBlock { Text = label, Margin = new(0, 0, 0, 4) };
            caption.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            panel.Children.Add(caption);
            var box = new TextBox { MinHeight = 34 };
            box.SetBinding(TextBox.TextProperty, new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            box.SetBinding(TextBox.IsReadOnlyProperty, new Binding("DataContext.IsLocked")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ReceivingPageView), 1)
            });
            panel.Children.Add(box);
            return panel;
        }
        private static Button LineButton(string content, string command)
        {
            var button = new Button { Content = content, HorizontalAlignment=HorizontalAlignment.Left, Margin = new(0, 0, 8, 8) };
            button.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty, new Binding(command));
            return button;
        }
    }
}
