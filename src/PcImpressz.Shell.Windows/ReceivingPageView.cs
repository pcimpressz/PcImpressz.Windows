using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PcImpressz.Shell.Windows;

public sealed class ReceivingPageView : UserControl
{
    private readonly StackPanel _lines = new();
    private DataGrid _receipts = null!;
    private ReceivingPageViewModel? _page;
    public ReceivingPageView()
    {
        var root = new StackPanel();
        root.Children.Add(Home());
        root.Children.Add(Intake());
        root.Children.Add(Editor());
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DataContextChanged += (_, _) => Hook();
    }
    private UIElement Home()
    {
        var panel = new StackPanel();
        panel.SetBinding(VisibilityProperty, new Binding("IsHome") { Converter = new BooleanToVisibilityConverter() });
        panel.Children.Add(Text("Bevételezés", 22, true));
        panel.Children.Add(Muted("Válaszd ki, honnan jön a számla. A NAV-lekérés az Online Számla bejövő számláit hozza. Ami bekerül, új állapotba kerül: bevételezed, költségként hagyod, vagy függőben marad."));
        var modes = new WrapPanel { Margin = new(0, 0, 0, 12) };
        modes.Children.Add(Primary("Kézi számla", "ManualCommand"));
        modes.Children.Add(Button("NAV XML betöltése", "NavCommand"));
        modes.Children.Add(Button("Excel / CSV / XML import", "ImportCommand"));
        panel.Children.Add(modes);
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
        tools.Children.Add(Button("Bevételezés", "ReceiveCommand"));
        tools.Children.Add(Button("Költség", "ExpenseCommand"));
        tools.Children.Add(Button("Függő", "PendingCommand"));
        tools.Children.Add(Button("Visszafordítás", "ReverseReceiptCommand"));
        tools.Children.Add(Button("CSV", "ExportCommand"));
        tools.Children.Add(ListGrid.ColumnButton(() => DataContext as IColumnList));
        panel.Children.Add(tools);
        panel.Children.Add(Bound(Muted(""), "Message"));
        _receipts = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column, MinHeight = 280, MaxHeight = 460, Margin = new(0, 8, 0, 0) };
        _receipts.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Rows"));
        _receipts.SetBinding(DataGrid.SelectedItemProperty, new Binding("SelectedRow"));
        _receipts.MouseDoubleClick += (_, _) => { if (DataContext is ReceivingPageViewModel page) page.OpenCommand.Execute(null); };
        ListGrid.Prepare(_receipts, () => DataContext as IColumnList);
        panel.Children.Add(_receipts);
        return panel;
    }
    private UIElement Intake()
    {
        var panel = new StackPanel();
        panel.SetBinding(VisibilityProperty, new Binding("IsNav") { Converter = OrHome() });
        var nav = panel;
        var import = new StackPanel();
        import.SetBinding(VisibilityProperty, new Binding("IsImport") { Converter = new BooleanToVisibilityConverter() });
        var both = new StackPanel();
        both.Children.Add(nav);
        both.Children.Add(import);
        FillIntake(nav, true);
        FillIntake(import, false);
        return both;
    }
    private void FillIntake(StackPanel panel, bool nav)
    {
        panel.SetBinding(VisibilityProperty, new Binding(nav ? "IsNav" : "IsImport") { Converter = new BooleanToVisibilityConverter() });
        panel.Children.Add(Bound(Text("", 22, true), "IntakeTitle"));
        panel.Children.Add(Bound(Muted(""), "IntakeHelp"));
        panel.Children.Add(Text("Szállító", 14, true));
        panel.Children.Add(Suppliers());
        panel.Children.Add(Button("Új szállító a partnertörzsbe", "NewSupplierCommand"));
        if (nav)
        {
            panel.Children.Add(Plain("Kezdő nap (éééé-hh-nn)", "PullFrom"));
            panel.Children.Add(Plain("Záró nap (éééé-hh-nn)", "PullTo"));
            panel.Children.Add(Primary("Számlák lekérése a NAV-tól", "PullNavCommand"));
        }
        panel.Children.Add(Labeled("Számlaszám (XML-ből kitölthető)", "Number"));
        panel.Children.Add(Labeled("Kelt (éééé-hh-nn, üresen a fájl vagy a mai nap)", "IssuedOn"));
        panel.Children.Add(Primary(nav ? "NAV-számla XML megnyitása" : "Fájl kiválasztása és beolvasás", "PickFileCommand"));
        panel.Children.Add(Button("Vissza a listához", "BackCommand"));
        panel.Children.Add(Bound(Muted(""), "Message"));
    }
    private UIElement Editor()
    {
        var panel = new StackPanel();
        panel.SetBinding(VisibilityProperty, new Binding("IsEditor") { Converter = new BooleanToVisibilityConverter() });
        panel.Children.Add(Bound(Text("", 22, true), "EditorTitle"));
        panel.Children.Add(Muted("A szállítási díj és a hasonló sor szolgáltatás: termék nélkül is bevételezhető. Készletes sornál válassz terméket, vagy vegyél fel újat."));
        panel.Children.Add(Text("Szállító", 14, true));
        panel.Children.Add(Suppliers());
        var supplierButton = Button("Új szállító", "NewSupplierCommand");
        supplierButton.SetBinding(VisibilityProperty, new Binding("IsEditable") { Converter = new BooleanToVisibilityConverter() });
        panel.Children.Add(supplierButton);
        panel.Children.Add(Labeled("Számlaszám", "Number"));
        panel.Children.Add(Labeled("Kelt", "IssuedOn"));
        panel.Children.Add(Labeled("Fizetési határidő", "DueOn"));
        panel.Children.Add(Labeled("Raktár", "Warehouse"));
        panel.Children.Add(Labeled("Megjegyzés", "Notes"));
        panel.Children.Add(Text("Tételek", 16, true));
        panel.Children.Add(_lines);
        var add = Button("Új tételsor", "AddLineCommand");
        add.SetBinding(VisibilityProperty, new Binding("IsEditable") { Converter = new BooleanToVisibilityConverter() });
        panel.Children.Add(add);
        var supplement = Button("Hiányzó adatok XML-ből vagy PDF-ből", "SupplementCommand");
        supplement.SetBinding(VisibilityProperty, new Binding("IsEditable") { Converter = new BooleanToVisibilityConverter() });
        panel.Children.Add(supplement);
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
        actions.Children.Add(Button("Vissza", "BackCommand"));
        panel.Children.Add(actions);
        panel.Children.Add(Bound(Muted(""), "Message"));
        return panel;
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
        _lines.Children.Clear();
        if (_page is null) return;
        foreach (var line in _page.Lines) _lines.Children.Add(new LineCard(line));
    }
    private static Button Classify(string content, string command)
    {
        var button = Button(content, command);
        button.SetBinding(VisibilityProperty, new Binding("CanClassify") { Converter = new BooleanToVisibilityConverter() });
        return button;
    }
    private static ComboBox Suppliers()
    {
        var combo = new ComboBox { MinHeight = 36, Margin = new(0, 0, 0, 10), DisplayMemberPath = "Label" };
        combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Suppliers"));
        combo.SetBinding(ComboBox.SelectedItemProperty, new Binding("SelectedSupplier"));
        combo.SetBinding(IsEnabledProperty, new Binding("CanPickSupplier"));
        return combo;
    }
    private static UIElement Plain(string label, string path)
    {
        var panel = new StackPanel { Margin = new(0, 0, 0, 8) };
        panel.Children.Add(Muted(label));
        var box = Input();
        box.SetBinding(TextBox.TextProperty, new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        panel.Children.Add(box);
        return panel;
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
        var button = new Button { Content = content, Margin = new(0, 0, 10, 10) };
        button.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty, new Binding(command));
        button.SetBinding(IsEnabledProperty, new Binding("IsNotBusy"));
        return button;
    }
    private static Button Primary(string content, string command)
    {
        var button = Button(content, command);
        button.SetResourceReference(StyleProperty, "PrimaryButton");
        return button;
    }
    private static IValueConverter OrHome() => new BooleanToVisibilityConverter();

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
            search.Children.Add(LineButton("Új termék", "NewProductCommand"));
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
            panel.Children.Add(hint);
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
            var button = new Button { Content = content, Margin = new(0, 0, 8, 8) };
            button.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty, new Binding(command));
            return button;
        }
    }
}
