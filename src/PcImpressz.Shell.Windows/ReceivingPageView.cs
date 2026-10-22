using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PcImpressz.Shell.Windows;

public sealed class ReceivingPageView : UserControl
{
    private readonly List<DataGrid> _lineGrids=[];
    private DataGrid _receipts = null!;
    private ReceivingPageViewModel? _page;
    public ReceivingPageView()
    {
        var root = new StackPanel();

        var tabs=new WrapPanel{Margin=new(0,0,0,16)};
        tabs.Children.Add(Tab("Bejövő számlák","IncomingCommand","IsIncoming"));
        tabs.Children.Add(Tab("Kimenő számlák","OutgoingCommand","IsOutgoing"));
        root.Children.Add(tabs);
        var incoming=new StackPanel();
        incoming.SetBinding(VisibilityProperty,new Binding("IsIncoming"){Converter=new BooleanToVisibilityConverter()});
                var incomingWorkspace=new ArrangeableWorkspace("incoming"){UsePageScrolling=true};incomingWorkspace.AddPanel("list","Bejövő számlák listája",(FrameworkElement)Home());incomingWorkspace.AddPanel("nav","NAV-lekérés és számlabevételezés",(FrameworkElement)Intake());incoming.Children.Add(incomingWorkspace);root.Children.Add(incoming);
        var outgoingWorkspace=new ArrangeableWorkspace("outgoing"){UsePageScrolling=true};outgoingWorkspace.SetBinding(VisibilityProperty,new Binding("IsOutgoing"){Converter=new BooleanToVisibilityConverter()});outgoingWorkspace.AddPanel("list","Kimenő számlák",(FrameworkElement)Outgoing());root.Children.Add(outgoingWorkspace);
        Content = root;
        DataContextChanged += (_, _) => Hook();
    }
    private UIElement Home()
    {
        var panel = new StackPanel();



        var modes = new WrapPanel { Margin = new(0, 0, 0, 12) };
        modes.Children.Add(Primary("Új kézi bevételezés", "ManualCommand"));modes.Children.Add(Button("Bevételezett számlák", "ReceivedInvoicesCommand"));modes.Children.Add(Button("Kézi piszkozatok", "DraftsCommand"));
        modes.Children.Add(Button("NAV-számlák lekérése", "NavCommand"));
        modes.Children.Add(Button("PDF alapján rögzítés", "PdfCommand"));
        modes.Children.Add(Button("Excel / CSV / XML import", "ImportCommand"));
        panel.Children.Add(modes);

        panel.Children.Add(DraftList());
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
            HeadersVisibility = DataGridHeadersVisibility.Column, MinHeight = 80, Margin = new(0, 8, 0, 0) };
        _receipts.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Rows"));
        _receipts.SetBinding(DataGrid.SelectedItemProperty, new Binding("SelectedRow"));
        _receipts.MouseDoubleClick += (_, _) => { if (DataContext is ReceivingPageViewModel page) page.OpenCommand.Execute(null); };
        ListGrid.Prepare(_receipts, () => DataContext as IColumnList);
        panel.Children.Add(_receipts);var paging=new WrapPanel();paging.Children.Add(Button("Előző oldal","PreviousReceiptPageCommand"));paging.Children.Add(Button("Következő oldal","NextReceiptPageCommand"));paging.Children.Add(PageSizePicker());paging.Children.Add(Bound(Muted(""),"ReceiptPageLabel"));panel.Children.Add(paging);
        return home;
    }
    private UIElement DraftList()
    {
        var panel=new StackPanel();panel.SetBinding(VisibilityProperty,new Binding("IsDraftList"){Converter=new BooleanToVisibilityConverter()});panel.Children.Add(Text("Kézi számlák – piszkozatok",18,true));
        var grid=new DataGrid{AutoGenerateColumns=false,CanUserAddRows=false,IsReadOnly=true,HeadersVisibility=DataGridHeadersVisibility.Column,MinHeight=70};grid.SetBinding(ItemsControl.ItemsSourceProperty,"InvoiceDrafts");
        foreach(var (caption,path) in new[]{("Számlaszám","Number"),("Szállító","Supplier"),("Utolsó mentés","UpdatedAt")})grid.Columns.Add(new DataGridTextColumn{Header=caption,Binding=new Binding(path),Width=220});
        var factory=new FrameworkElementFactory(typeof(DraftActions));factory.SetBinding(TagProperty,new Binding{RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(ReceivingPageView),1)});grid.Columns.Add(new DataGridTemplateColumn{Header="Műveletek",Width=230,CellTemplate=new DataTemplate{VisualTree=factory}});panel.Children.Add(grid);panel.Children.Add(Bound(Muted(""),"Message"));return panel;
    }
    public sealed class DraftActions:WrapPanel
    {
        public DraftActions()
        {
            foreach(var caption in new[]{"Folytatás","Törlés"}){var button=new Button{Content=caption,Padding=new(8,3,8,3),Margin=new(0,0,6,0)};button.Click+=async(_,_)=>{if(Tag is ReceivingPageView view&&view._page is {} page&&DataContext is InvoiceDraftRow row){if(caption=="Folytatás")await page.ResumeDraftAsync(row);else await page.DeleteDraftAsync(row);}};Children.Add(button);}
        }
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
        both.Children.Add(import);both.Children.Add(Editor(true));both.Children.Add(Editor(false));
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
            var dates=new WrapPanel{Margin=new(0,0,0,12)};dates.Children.Add(Text("Kezdő nap",14,false));var from=CompactDatePicker();from.SetBinding(DatePicker.SelectedDateProperty,new Binding("NavFrom"){Mode=BindingMode.TwoWay});from.SetBinding(IsEnabledProperty,"IsNotBusy");dates.Children.Add(from);dates.Children.Add(Text("Záró nap",14,false));var to=CompactDatePicker();to.SetBinding(DatePicker.SelectedDateProperty,new Binding("NavTo"){Mode=BindingMode.TwoWay});to.SetBinding(IsEnabledProperty,"IsNotBusy");dates.Children.Add(to);dates.Children.Add(Primary("Bejövő számlák lekérése","FetchNavCommand"));dates.Children.Add(Button("NAV XML megnyitása","PickFileCommand"));dates.Children.Add(PageSizePicker());panel.Children.Add(dates);
            var invoices=new DataGrid{AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,MinHeight=65,Margin=new(0,0,0,12)};invoices.HeadersVisibility=DataGridHeadersVisibility.Column;invoices.RowStyle=NavRowStyle();invoices.SetBinding(ItemsControl.ItemsSourceProperty,"NavInvoices");invoices.SetBinding(DataGrid.SelectedItemProperty,new Binding("SelectedNavInvoice"){Mode=BindingMode.TwoWay});invoices.SetBinding(IsEnabledProperty,"IsNotBusy");foreach(var (title,path) in new[]{("Állapot","State"),("Kelt","IssuedOn"),("Számlaszám","Number"),("Szállító","SupplierName"),("Adószám","SupplierTaxNumber"),("Nettó","Net"),("Pénznem","Currency")})invoices.Columns.Add(new DataGridTextColumn{Header=title,Binding=new Binding(path),MinWidth=path=="SupplierName"?180:60,Width=path=="SupplierName"?new DataGridLength(1,DataGridLengthUnitType.Star):new DataGridLength(path switch{"State"=>110,"Number"=>150,"Net"=>90,"Currency"=>70,_=>100})});invoices.Columns.Add(NavActions());panel.Children.Add(invoices);
            var pages=new WrapPanel();pages.Children.Add(Button("Előző oldal","PreviousNavCommand"));pages.Children.Add(Button("Következő oldal","NextNavCommand"));pages.Children.Add(Bound(Muted(""),"NavPageLabel"));panel.Children.Add(pages);
        }
        if(!nav){panel.Children.Add(Text("Szállító", 14, true));panel.Children.Add(Suppliers());}
        if(!nav)panel.Children.Add(Button("Új szállító a partnertörzsbe", "NewSupplierCommand"));
        if(nav)
        {


            panel.Children.Add(Bound(Muted(""),"Message"));return;
        }
        panel.Children.Add(Labeled("Számlaszám (XML-ből kitölthető)", "Number"));
        panel.Children.Add(Labeled("Kelt (éééé-hh-nn, üresen a fájl vagy a mai nap)", "IssuedOn"));
        panel.Children.Add(Primary(nav ? "NAV-számla XML megnyitása" : "Fájl kiválasztása és beolvasás", "PickFileCommand"));
        panel.Children.Add(Button("Import bezárása", "BackCommand"));
        panel.Children.Add(Bound(Muted(""), "Message"));
    }
    private UIElement Editor(bool navEditor)
    {
        var panel = new StackPanel();var lines=new DataGrid();_lineGrids.Add(lines);lines.SetBinding(VisibilityProperty,new Binding(navEditor?"IsNavEditor":"IsManualEditor"){Converter=new BooleanToVisibilityConverter()});
        panel.SetBinding(VisibilityProperty, new Binding(navEditor?"IsNavEditor":"IsManualEditor") { Converter = new BooleanToVisibilityConverter() });
        panel.Children.Add(Text(navEditor?"NAV-számla – eredeti adatok":"Kézi bevételezés",18,true));
        var heading=new DockPanel{Margin=new(0,12,0,8)};var close=Button("×","BackCommand");close.Foreground=System.Windows.Media.Brushes.Firebrick;close.FontSize=20;close.Padding=new(9,0,9,0);close.ToolTip="Szerkesztő bezárása – az előkészítés automatikusan mentődik";DockPanel.SetDock(close,Dock.Right);heading.Children.Add(close);heading.Children.Add(Bound(Text("",18,true),"EditorTitle"));panel.Children.Add(heading);
        panel.Children.Add(Bound(Muted(""),"PreparationStatus"));
        panel.Children.Add(Classify("Bevételezés", "ReceiveCommand"));
        var pdf=new StackPanel();pdf.SetBinding(VisibilityProperty,new Binding("HasPdfPreview"){Converter=new BooleanToVisibilityConverter()});
        pdf.Children.Add(Bound(Text("",14,true),"PdfName"));
        pdf.Children.Add(Muted("PDF-ből kiolvasott szöveg – a számlaadatokat és tételeket ellenőrizve töltsd ki. Az eredeti PDF-et őrizd meg külön."));
        var preview=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,MinHeight=140,MaxHeight=280,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new(0,0,0,16)};
        preview.SetBinding(TextBox.TextProperty,new Binding("PdfText"){Mode=BindingMode.OneWay});pdf.Children.Add(preview);panel.Children.Add(pdf);

        panel.Children.Add(Text("Szállító", 14, true));
        var supplierPicker=Suppliers();supplierPicker.SetBinding(VisibilityProperty,new Binding("ShowSupplierPicker"){Converter=new BooleanToVisibilityConverter()});panel.Children.Add(supplierPicker);
        var navIdentity=Bound(Text("",14,true),"NavHeader");navIdentity.SetBinding(VisibilityProperty,new Binding("IsNavReceipt"){Converter=new BooleanToVisibilityConverter()});panel.Children.Add(navIdentity);
        var automatic=new CheckBox{Content="Szállító automatikus azonosítása / felvétele",Margin=new(0,4,0,8)};automatic.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,new Binding("AutoNavSupplier"){Mode=BindingMode.TwoWay});automatic.SetBinding(VisibilityProperty,new Binding("IsNavReceipt"){Converter=new BooleanToVisibilityConverter()});automatic.SetBinding(IsEnabledProperty,"IsNotBusy");panel.Children.Add(automatic);
        panel.Children.Add(Bound(Muted(""),"SupplierStatus"));
        var mainFields=new System.Windows.Controls.Primitives.UniformGrid{Columns=2};mainFields.Children.Add(Labeled("Számlaszám","Number",true));mainFields.Children.Add(Labeled("Kelt","IssuedOn",true));mainFields.SetBinding(VisibilityProperty,new Binding("CanEditInvoiceFacts"){Converter=new BooleanToVisibilityConverter()});panel.Children.Add(mainFields);
        var extraFields=new System.Windows.Controls.Primitives.UniformGrid{Columns=2};extraFields.Children.Add(Labeled("Fizetési határidő","DueOn",true));extraFields.Children.Add(Labeled("Raktár","Warehouse"));
        var extra=new StackPanel();extra.Children.Add(extraFields);extra.Children.Add(Labeled("Megjegyzés","Notes"));panel.Children.Add(new Expander{Header="További számlaadatok és megjegyzés",Content=extra,Margin=new(0,0,0,10)});
        panel.Children.Add(Text("Tételek", 16, true));
        lines.AutoGenerateColumns=false;lines.CanUserAddRows=false;lines.CanUserDeleteRows=false;lines.HeadersVisibility=DataGridHeadersVisibility.Column;
        lines.MinHeight=60;lines.RowDetailsVisibilityMode=DataGridRowDetailsVisibilityMode.Collapsed;ScrollViewer.SetVerticalScrollBarVisibility(lines,ScrollBarVisibility.Disabled);lines.PreviewMouseLeftButtonDown+=ToggleLineDetails;
        lines.SetBinding(ItemsControl.ItemsSourceProperty,"Lines");lines.SetBinding(DataGrid.IsReadOnlyProperty,"IsLocked");
        lines.Columns.Add(new DataGridCheckBoxColumn{Header="Készletre",Binding=new Binding("StockItem"){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=75});
        foreach(var (title,path,width) in new[]{("Megnevezés","Name",230d),("Kategória","Category",120d),("Mennyiség","QuantityText",95d),("Egység","UnitCode",75d),("Nettó beszerzés","PriceText",120d),("Áfa %","VatText",70d),("Bruttó kisker (Ft)","RetailPrice",140d),("Haszonkulcs %","Markup",125d),("Eladási áfa %","SaleVat",115d),("Fedezet","Margin",85d),("Párosítás","MatchLabel",150d)})
        {
            var column=new DataGridTextColumn{Header=title,Width=width,IsReadOnly=path is "MatchLabel" or "Margin" or "Category" or "Name",Binding=new Binding(path){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged}};
            if(path is "Name" or "QuantityText" or "UnitCode" or "PriceText" or "VatText")column.IsReadOnly=true;
            lines.Columns.Add(column);
        }
        lines.RowDetailsTemplate=new DataTemplate{VisualTree=new FrameworkElementFactory(typeof(LineDetails))};
        panel.Children.Add(Muted("Készletre: csak a jelölt tételeket vesszük készletre. Az ár és a haszonkulcs egymást számolja. Kattintás: részletek megnyitása / bezárása."));
        panel.Children.Add(lines);
        var add = Button("Új tételsor", "AddLineCommand");
        add.SetBinding(VisibilityProperty, new Binding("CanAddInvoiceLine") { Converter = new BooleanToVisibilityConverter() });
        if(!navEditor)panel.Children.Add(add);
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

        panel.Children.Add(actions);
        panel.Children.Add(Bound(Muted(""), "Message"));
        return panel;
    }
    private DataGridTemplateColumn NavActions()
    {
        var factory=new FrameworkElementFactory(typeof(NavRowActions));factory.SetBinding(TagProperty,new Binding{RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(ReceivingPageView),1)});
        return new DataGridTemplateColumn{Header="Számlaműveletek",CellTemplate=new DataTemplate{VisualTree=factory},Width=260};
    }
    public sealed class NavRowActions:WrapPanel
    {
        public NavRowActions()
        {
            var manual=new Button{Content="Kézire",ToolTip="Áthelyezés kézi bevételezésre",Margin=new(0,0,6,0),Padding=new(6,2,6,2)};
            manual.Click+=async(_,e)=>{e.Handled=true;if(Tag is ReceivingPageView view&&view._page is {} page&&DataContext is NavInvoiceRow row)await page.NavActionAsync(row,"manual");};Children.Add(manual);
            var read=new Button{Content="Adatok beolvasása ▾",Padding=new(6,2,6,2)};var menu=new ContextMenu();
            foreach(var (title,action) in new[]{("XML-segédlet","xml"),("Szállítói API","api"),("PDF-segédlet","pdf")}){var item=new MenuItem{Header=title};item.Click+=async(_,e)=>{e.Handled=true;if(Tag is ReceivingPageView view&&view._page is {} page&&DataContext is NavInvoiceRow row)await page.NavActionAsync(row,action);};menu.Items.Add(item);}
            read.Click+=(_,e)=>{e.Handled=true;menu.PlacementTarget=read;menu.IsOpen=true;};Children.Add(read);
        }
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
        var grid=new DataGrid{AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,MinHeight=100,HeadersVisibility=DataGridHeadersVisibility.Column};grid.SetBinding(ItemsControl.ItemsSourceProperty,"OutgoingInvoices");
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
            foreach(var column in _lineGrids.SelectMany(g=>g.Columns).OfType<DataGridTextColumn>())if((column.Binding as Binding)?.Path.Path is "QuantityText" or "UnitCode" or "PriceText" or "VatText")BindingOperations.SetBinding(column,DataGridColumn.IsReadOnlyProperty,new Binding("CanEditInvoiceFacts"){Source=_page,Converter=new Negate()});
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
        var button = command=="ReceiveCommand"?Primary(content,command):Button(content, command);
        button.SetBinding(VisibilityProperty, new Binding("CanClassify") { Converter = new BooleanToVisibilityConverter() });
        return button;
    }
    private static ComboBox Suppliers(string selection="SelectedSupplier")
    {
        var combo = new ComboBox { Style=(Style)Application.Current.FindResource("EditableInvoiceCombo"),IsEditable=true,IsTextSearchEnabled=false,StaysOpenOnEdit=true,MinHeight = 34, Margin = new(0, 0, 0, 10), DisplayMemberPath = "Label" };
        combo.SetResourceReference(Control.BackgroundProperty,"ElevatedBrush");combo.SetResourceReference(Control.ForegroundProperty,"TextBrush");TextSearch.SetTextPath(combo,"Label");
        combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Suppliers"));
        combo.SetBinding(ComboBox.SelectedItemProperty, new Binding(selection));
        combo.SetBinding(IsEnabledProperty, new Binding(selection=="SelectedNavSupplier"?"IsNotBusy":"CanPickSupplier"));
        combo.AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler((_,e)=>
        {
            if(e.OriginalSource is TextBox box&&box.IsKeyboardFocusWithin&&combo.ItemsSource is not null)
            {
                var query=box.Text;var view=CollectionViewSource.GetDefaultView(combo.ItemsSource);view.Filter=item=>item is SupplierOption supplier&&supplier.Label.Contains(query,StringComparison.CurrentCultureIgnoreCase);combo.IsDropDownOpen=true;
            }
        }));
        combo.DropDownClosed+=(_,_)=>{if(combo.ItemsSource is not null)CollectionViewSource.GetDefaultView(combo.ItemsSource).Filter=null;};
        return combo;
    }
    private static UIElement Labeled(string label, string path,bool fact=false)
    {
        var panel = new StackPanel { Margin = new(0, 0, 0, 8) };
        panel.Children.Add(Muted(label));
        var box = Input();
        box.SetBinding(TextBox.TextProperty, new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        box.SetBinding(TextBox.IsReadOnlyProperty, fact?new Binding("CanEditInvoiceFacts"){Converter=new Negate()}:new Binding("IsLocked"));
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


    private sealed class Negate:IValueConverter
    {
        public object Convert(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>value is not true;
        public object ConvertBack(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>throw new NotSupportedException();
    }
    private static DatePicker CompactDatePicker()=>new(){Style=(Style)Application.Current.FindResource("CompactInvoiceDatePicker"),Width=145,Height=34,MinHeight=0,Padding=new(0),VerticalAlignment=VerticalAlignment.Top,Margin=new(8,0,14,0)};
    private static UIElement PageSizePicker()
    {
        var panel=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(8,0,0,0)};panel.Children.Add(new TextBlock{Text="Sor / oldal",VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,6,10)});
        var size=new ComboBox{Width=85,MinHeight=34,Padding=new(6),Margin=new(0,0,0,10)};size.SetBinding(ItemsControl.ItemsSourceProperty,"InvoicePageSizes");size.SetBinding(ComboBox.SelectedItemProperty,new Binding("InvoicePageSize"){Mode=BindingMode.TwoWay});panel.Children.Add(size);return panel;
    }
    private void ToggleLineDetails(object sender,System.Windows.Input.MouseButtonEventArgs e)
    {
        var node=e.OriginalSource as DependencyObject;
        while(node is not null&&node is not DataGridRow)
        {
            if(node is TextBox or System.Windows.Controls.Primitives.ButtonBase or ComboBox)return;
            node=node is System.Windows.Media.Visual?System.Windows.Media.VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node);
        }
        if(node is DataGridRow row&&row.Item is LineEditor line){line.DetailsOpen=!line.DetailsOpen;row.DetailsVisibility=line.DetailsOpen?Visibility.Visible:Visibility.Collapsed;}
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

            panel.Children.Add(NameField());
            var codes = new Grid();
            codes.ColumnDefinitions.Add(new()); codes.ColumnDefinitions.Add(new()); codes.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            codes.Children.Add(Field("Cikkszám", "Sku"));
            var barcode = Field("Vonalkód", "Barcode"); Grid.SetColumn(barcode, 1); codes.Children.Add(barcode);
            var barcodeButtons=new WrapPanel{VerticalAlignment=VerticalAlignment.Bottom,Margin=new(0,0,0,8)};barcodeButtons.Children.Add(BarcodeButton("⚙","Vonalkód generálása","GenerateBarcodeCommand"));barcodeButtons.Children.Add(BarcodeButton("▥","Címke nyomtatása","PrintBarcodeCommand"));Grid.SetColumn(barcodeButtons,2);codes.Children.Add(barcodeButtons);
            panel.Children.Add(codes);
            panel.Children.Add(CategoryPicker(line));
            var search = new WrapPanel();
            var browse=new Button{Content="Terméktörzs tallózása…",Margin=new(0,0,8,8)};browse.Click+=(_,_)=>line.BrowseProductCommand.Execute(null);search.Children.Add(browse);

            var newProduct=LineButton("Új termék", "NewProductCommand");newProduct.SetBinding(VisibilityProperty,new Binding("HasNoProduct"){Converter=new BooleanToVisibilityConverter()});search.Children.Add(newProduct);

            var remove=LineButton("Tétel törlése","RemoveCommand");remove.SetBinding(VisibilityProperty,new Binding("DataContext.CanEditInvoiceFacts"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(ReceivingPageView),1),Converter=new BooleanToVisibilityConverter()});search.Children.Add(remove);
            panel.Children.Add(search);
            var matches = new ComboBox { MinHeight = 34, Margin = new(0, 0, 0, 8), DisplayMemberPath = "Label" };
            matches.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Matches"));
            matches.SetBinding(ComboBox.SelectedItemProperty, new Binding("SelectedMatch"));
            matches.Visibility=line.Matches.Count==0?Visibility.Collapsed:Visibility.Visible;line.Matches.CollectionChanged+=(_,_)=>matches.Visibility=line.Matches.Count==0?Visibility.Collapsed:Visibility.Visible;panel.Children.Add(matches);
            var fields=new ItemsControl();fields.SetBinding(ItemsControl.ItemsSourceProperty,"ProductFields");fields.ItemTemplate=new DataTemplate{VisualTree=new FrameworkElementFactory(typeof(ProductFieldControl))};
            panel.Children.Add(new Expander{Header="További termékadatok kitöltése",Content=fields,Margin=new(0,10,0,10)});
            var match = new TextBlock { FontWeight = FontWeights.SemiBold };
            match.SetBinding(TextBlock.TextProperty, new Binding("MatchLabel"));
            panel.Children.Add(match);
            Child = panel;
        }
        private static UIElement NameField()
        {
            var panel=new StackPanel{Margin=new(0,0,0,8)};panel.Children.Add(new TextBlock{Text="Megnevezés",Margin=new(0,0,0,4)});
            var input=Input();input.Height=34;input.MinHeight=34;input.Margin=new(0);input.SetBinding(TextBox.TextProperty,new Binding("Name"){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});input.SetBinding(TextBox.IsReadOnlyProperty,new Binding("DataContext.CanEditInvoiceFacts"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(ReceivingPageView),1),Converter=new Negate()});panel.Children.Add(input);return panel;
        }
        private static Button BarcodeButton(string symbol,string tooltip,string command)
        {
            var button=LineButton(symbol,command);button.ToolTip=tooltip;button.Width=34;button.Height=34;button.MinHeight=0;button.Padding=new(3);button.Margin=new(0,0,6,0);return button;
        }
        private static UIElement CategoryPicker(LineEditor line)
        {
            var panel=new StackPanel{Margin=new(0,0,8,8)};panel.Children.Add(new TextBlock{Text="Kategória – keresés név vagy kód alapján",Margin=new(0,0,0,4)});
            var combo=new ComboBox{Style=(Style)Application.Current.FindResource("EditableInvoiceCombo"),IsEditable=true,IsTextSearchEnabled=false,StaysOpenOnEdit=true,MinHeight=34,MaxWidth=480,HorizontalAlignment=HorizontalAlignment.Left,MinWidth=280};TextSearch.SetTextPath(combo,"Code");
            combo.DisplayMemberPath="Label";
            combo.SetBinding(ItemsControl.ItemsSourceProperty,new Binding("DataContext.CategoryOptions"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(ReceivingPageView),1)});
            combo.SetBinding(ComboBox.TextProperty,new Binding("Category"){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
            combo.SelectionChanged+=(_,_)=>{if(combo.SelectedItem is InvoiceCategoryOption selected)line.Category=selected.Code;};
            combo.SetBinding(IsEnabledProperty,new Binding("DataContext.IsEditable"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(ReceivingPageView),1)});
            ListCollectionView? choices=null;combo.GotKeyboardFocus+=(_,_)=>{if(choices is null&&combo.ItemsSource is System.Collections.IList list){choices=new ListCollectionView(list);combo.ItemsSource=choices;}};
            combo.AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler((_,_)=>{if(!combo.IsKeyboardFocusWithin||combo.SelectedItem is not null)return;var query=combo.Text; if(choices is not null)choices.Filter=item=>item is InvoiceCategoryOption option&&(option.Code.Contains(query,StringComparison.CurrentCultureIgnoreCase)||option.Name.Contains(query,StringComparison.CurrentCultureIgnoreCase));combo.IsDropDownOpen=true;}));panel.Children.Add(combo);return panel;
        }
        private static UIElement Field(string label, string path)
        {
            var panel = new StackPanel { Margin = new(0, 0, 8, 8) };
            var caption = new TextBlock { Text = label, Margin = new(0, 0, 0, 4) };
            caption.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            panel.Children.Add(caption);
            var box = Input();box.Height=34;box.MinHeight=34;box.Margin=new(0);
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
