using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace PcImpressz.Shell.Windows;

// The module supplies field and column metadata; the Shell contains no partner assembly reference.
public sealed class EntityPageView : UserControl
{
    public EntityPageView()
    {
        var root = new StackPanel();
        var toolbar = new WrapPanel { Margin = new(0,0,0,18) };
        var search = Input(); search.Width = 230; search.Margin = new(0,0,10,10);
        search.SetBinding(TextBox.TextProperty, new Binding("Search") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        toolbar.Children.Add(search);
        toolbar.Children.Add(ListGrid.MatchCount());
        var filter = new ComboBox { Width = 180, Margin = new(0,0,10,10), DisplayMemberPath = "Label", SelectedValuePath = "Value" };
        filter.SetBinding(ComboBox.SelectedValueProperty, new Binding("Filter") { Mode = BindingMode.TwoWay });
        toolbar.Children.Add(filter);
        toolbar.Children.Add(ActionButton("Keresés", "SearchCommand"));
        var create = ActionButton("Új", "NewCommand"); toolbar.Children.Add(create);
        toolbar.Children.Add(ActionButton("CSV export", "ExportCommand"));
        toolbar.Children.Add(ListGrid.ColumnButton(() => DataContext as IColumnList));
        root.Children.Add(toolbar);
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0,0,0,16) };
        message.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); message.SetBinding(TextBlock.TextProperty, new Binding("Message"));
        root.Children.Add(message);
        var applied = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0,0,0,10) };
        applied.SetBinding(TextBlock.TextProperty, new Binding("AppliedSearchLabel")); root.Children.Add(applied);
        var body = new Grid(); body.ColumnDefinitions.Add(new() { Width = new(3, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new() { Width = new(2, GridUnitType.Star) });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto }); body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var listPanel = new StackPanel();
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
            CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow, HeadersVisibility = DataGridHeadersVisibility.Column,
            MinHeight = 360, MaxHeight = 480, GridLinesVisibility = DataGridGridLinesVisibility.None,
            BorderThickness = new(0), FontSize = 13, RowHeight = 44, ColumnHeaderHeight = 40 };
        grid.SetResourceReference(Control.FontSizeProperty, "BodySize");
        grid.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush"); grid.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        grid.SetResourceReference(DataGrid.RowBackgroundProperty, "SurfaceBrush");
        grid.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty, "ElevatedBrush");
        grid.SetBinding(IsEnabledProperty, new Binding("IsNotBusy"));
        grid.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Rows"));
        grid.SetBinding(DataGrid.SelectedItemProperty, new Binding("SelectedRow") { Mode = BindingMode.TwoWay });
        var headerStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader),
            Application.Current?.TryFindResource(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)) as Style);
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("ElevatedBrush")));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextBrush")));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12,8,8,8)));
        headerStyle.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
        grid.ColumnHeaderStyle = headerStyle;
        ListGrid.Prepare(grid, () => DataContext as IColumnList);
        ScrollViewer.SetHorizontalScrollBarVisibility(grid, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(grid, ScrollBarVisibility.Auto);
        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10,4,10,4)));
        var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("AccentSoftBrush")));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextBrush")));
        cellStyle.Triggers.Add(selected); grid.CellStyle = cellStyle;
        listPanel.Children.Add(grid);
        var pager = new DockPanel { Margin = new(0,14,0,0) };
        var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        count.SetBinding(TextBlock.TextProperty, new Binding("CountLabel")); DockPanel.SetDock(count, Dock.Left); pager.Children.Add(count);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ActionButton("← Előző", "PreviousCommand")); buttons.Children.Add(ActionButton("Következő →", "NextCommand"));
        pager.Children.Add(buttons); listPanel.Children.Add(pager); body.Children.Add(listPanel);
        var editorBorder = new Border { Padding = new(24), CornerRadius = new(14), BorderThickness = new(1), Margin = new(20,0,0,0) };
        editorBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); editorBorder.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");
        var editor = new StackPanel();
        var title = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new(0,0,0,20) };
        title.SetBinding(TextBlock.TextProperty, new Binding("EditorTitle")); editor.Children.Add(title);
        var fields = new Grid(); fields.ColumnDefinitions.Add(new()); fields.ColumnDefinitions.Add(new());
        fields.SetBinding(IsEnabledProperty, new Binding("IsNotBusy"));
        editor.Children.Add(fields);
        var save = new WrapPanel { Margin = new(0,20,0,0) };
        var saveButton = ActionButton("Mentés", "SaveCommand"); saveButton.SetResourceReference(StyleProperty, "PrimaryButton");
        save.Children.Add(saveButton); save.Children.Add(ActionButton("Archiválás", "ArchiveCommand"));
        var dossier = ActionButton("Dosszié", "DossierCommand");
        dossier.SetBinding(VisibilityProperty, new Binding("HasDossier") { Converter = new BooleanToVisibilityConverter() });
        dossier.SetBinding(IsEnabledProperty, new Binding("CanOpenDossier"));
        save.Children.Add(dossier); editor.Children.Add(save);
        editorBorder.Child = editor; Grid.SetColumn(editorBorder,1); body.Children.Add(editorBorder);
        body.SetBinding(VisibilityProperty, new Binding("ShowPartnerList") { Converter = new BooleanToVisibilityConverter() });
        root.Children.Add(body);
        root.Children.Add(DossierPage());
        Content = root;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not EntityPageViewModel vm) return;
            search.ToolTip = vm.Labels.SearchHint;
            if (vm.Definition.ModuleId == "catalog" && vm.Definition.Id == "products") root.Children.Insert(1, new TextBlock { Text = vm.Labels.SearchHint, TextWrapping = TextWrapping.Wrap, Margin = new(0,0,0,10) });
            create.Content = vm.Labels.NewLabel;
            filter.ItemsSource = vm.Filters;
            if (filter.SelectedValue is null) filter.SelectedValue = vm.Filters[0].Value;
            ListGrid.Show(grid, vm);
            fields.Children.Clear(); fields.RowDefinitions.Clear();
            var position = 0;
            foreach (var field in vm.Fields)
            {
                var index = position++;
                fields.RowDefinitions.Add(new() { Height = GridLength.Auto });
                var panel = new StackPanel { Margin = new(index % 2 == 0 ? 0 : 7,0,index % 2 == 0 ? 7 : 0,14), DataContext = field };
                if (field.Definition.Kind == "boolean")
                {
                    var check = new CheckBox { Content = field.Label, Margin = new(0,8,0,4) };
                    check.SetBinding(CheckBox.IsCheckedProperty, new Binding("Checked") { Mode = BindingMode.TwoWay }); panel.Children.Add(check);
                }
                else
                {
                    panel.Children.Add(new TextBlock { Text = field.Label, Margin = new(0,0,0,7) });
                    var input = Input(); input.SetBinding(TextBox.TextProperty, new Binding("Text") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                    if (field.Definition.Kind == "multiline") { input.AcceptsReturn = true; input.TextWrapping = TextWrapping.Wrap; input.MinHeight = 85; }
                    panel.Children.Add(input);
                }
                Grid.SetRow(panel,index/2); Grid.SetColumn(panel,index%2); fields.Children.Add(panel);
            }
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(EntityPageViewModel.DossierRevision)) RenderDossier(vm);
                if (args.PropertyName == nameof(EntityPageViewModel.LayoutRevision)) ListGrid.Show(grid, vm);
            };
            _ = vm.LoadAsync();
        };
        SizeChanged += (_, _) =>
        {
            var narrow = ActualWidth < 980;
            Grid.SetColumn(editorBorder,narrow ? 0 : 1); Grid.SetRow(editorBorder,narrow ? 1 : 0);
            Grid.SetColumnSpan(listPanel,narrow ? 2 : 1); Grid.SetColumnSpan(editorBorder,narrow ? 2 : 1);
            editorBorder.Margin = narrow ? new Thickness(0,22,0,0) : new Thickness(20,0,0,0);
        };
    }
    private WrapPanel cardHost = null!;
    private StackPanel sectionHost = null!;
    private UIElement DossierPage()
    {
        var panel = new StackPanel();
        var back = new Button { Content = "Vissza a partnerekhez", Margin = new(0, 0, 8, 14), HorizontalAlignment = HorizontalAlignment.Left };
        back.SetBinding(Button.CommandProperty, new Binding("DossierBackCommand"));
        panel.Children.Add(back);
        var title = new TextBlock { FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        title.SetBinding(TextBlock.TextProperty, new Binding("DossierTitle")); panel.Children.Add(title);
        var subtitle = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 16) };
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        subtitle.SetBinding(TextBlock.TextProperty, new Binding("DossierSubtitle")); panel.Children.Add(subtitle);
        var filterBox = new Border { Padding = new(16), CornerRadius = new(12), BorderThickness = new(1), Margin = new(0, 0, 0, 16) };
        filterBox.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        filterBox.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");
        var period = new WrapPanel();
        var periodLabel = new TextBlock { Text = "Időszak szűrése", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 10) };
        period.Children.Add(periodLabel);
        period.Children.Add(DateInput("PeriodFrom")); period.Children.Add(DateInput("PeriodTo"));
        var filter = ActionButton("Szűrés", "DossierFilterCommand"); filter.SetResourceReference(StyleProperty, "PrimaryButton");
        period.Children.Add(filter);
        period.Children.Add(ActionButton("Mind", "PeriodAllCommand"));
        period.Children.Add(ActionButton("Idén", "PeriodYearCommand"));
        period.Children.Add(ActionButton("Tavaly", "PeriodPreviousYearCommand"));
        period.Children.Add(ActionButton("3 hónap", "PeriodQuarterCommand"));
        filterBox.Child = period; panel.Children.Add(filterBox);
        var notice = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) };
        notice.SetBinding(TextBlock.TextProperty, new Binding("DossierNotice")); panel.Children.Add(notice);
        cardHost = new WrapPanel(); sectionHost = new StackPanel();
        panel.Children.Add(cardHost); panel.Children.Add(sectionHost);
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroll.SetBinding(VisibilityProperty, new Binding("IsDossierOpen") { Converter = new BooleanToVisibilityConverter() });
        return scroll;
    }
    private static DatePicker DateInput(string path)
    {
        var picker = new DatePicker { Width = 150, Margin = new(0, 0, 8, 10) };
        picker.SetBinding(DatePicker.SelectedDateProperty, new Binding(path) { Mode = BindingMode.TwoWay });
        return picker;
    }
    private void RenderDossier(EntityPageViewModel vm)
    {
        cardHost.Children.Clear(); sectionHost.Children.Clear();
        foreach (var card in vm.DossierCards)
        {
            var box = new Border { Padding = new(12), Margin = new(0, 0, 8, 8), CornerRadius = new(10), MinWidth = 150 };
            box.SetResourceReference(Border.BackgroundProperty, "ElevatedBrush");
            var stack = new StackPanel();
            var label = new TextBlock { Text = card.Label, FontSize = 12 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            stack.Children.Add(label);
            stack.Children.Add(new TextBlock { Text = card.Value, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new(0, 4, 0, 0) });
            box.Child = stack; cardHost.Children.Add(box);
        }
        foreach (var section in vm.DossierSections)
        {
            var block = new StackPanel { Margin = new(0, 8, 0, 0) };
            block.Children.Add(new TextBlock { Text = section.Title, FontWeight = FontWeights.SemiBold, FontSize = 16 });
            var source = new TextBlock { Text = section.Source, Margin = new(0, 4, 0, 4), FontSize = 12 };
            source.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            block.Children.Add(source);
            block.Children.Add(new TextBlock { Text = section.Detail, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) });
            foreach (var line in section.Lines) block.Children.Add(line.Items.Length > 0 ? InvoiceRow(line) : PlainRow(line));
            sectionHost.Children.Add(block);
        }
    }
    private static UIElement InvoiceRow(EntityPageViewModel.DossierLineRow line)
    {
        var expander = new Expander { IsExpanded = false, Margin = new(0, 4, 0, 0) };
        expander.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
        var header = new Grid();
        header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = line.Title, FontWeight = FontWeights.SemiBold });
        var extra = new TextBlock { Text = line.Detail + " · kattints a tételekhez", FontSize = 12 };
        extra.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        text.Children.Add(extra);
        var amount = new TextBlock { Text = line.Amount, FontWeight = FontWeights.SemiBold, Margin = new(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(amount, 1); header.Children.Add(text); header.Children.Add(amount);
        expander.Header = header;
        var items = new StackPanel { Margin = new(28, 6, 0, 8) };
        foreach (var item in line.Items)
        {
            var row = new Grid { Margin = new(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = item.Name, TextWrapping = TextWrapping.Wrap });
            var quantity = new TextBlock { Text = item.Quantity, Margin = new(12, 0, 12, 0) };
            quantity.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            Grid.SetColumn(quantity, 1);
            var net = new TextBlock { Text = item.Amount };
            Grid.SetColumn(net, 2);
            row.Children.Add(quantity); row.Children.Add(net); items.Children.Add(row);
        }
        expander.Content = items;
        return expander;
    }
    private static Grid PlainRow(EntityPageViewModel.DossierLineRow line)
    {
        var row = new Grid { Margin = new(0, 6, 0, 0) };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = line.Title, TextWrapping = TextWrapping.Wrap });
        var extra = new TextBlock { Text = line.Detail, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        extra.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        text.Children.Add(extra);
        var amount = new TextBlock { Text = line.Amount, FontWeight = FontWeights.SemiBold, Margin = new(12, 0, 0, 0) };
        Grid.SetColumn(amount, 1); row.Children.Add(text); row.Children.Add(amount);
        return row;
    }
    private static TextBox Input()
    {
        var box = new TextBox { Padding = new(10), MinHeight = 38, BorderThickness = new(1) };
        box.SetResourceReference(Control.BackgroundProperty, "ElevatedBrush"); box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        box.SetResourceReference(Control.BorderBrushProperty, "StrokeBrush"); return box;
    }
    private static Button ActionButton(string label, string command)
    {
        var button = new Button { Content = label, Margin = new(0,0,8,10) };
        button.SetBinding(Button.CommandProperty, new Binding(command));
        button.SetBinding(IsEnabledProperty, new Binding("IsNotBusy"));
        return button;
    }
}
