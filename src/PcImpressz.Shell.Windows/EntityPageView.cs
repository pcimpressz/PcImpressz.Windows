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
        var filter = new ComboBox { Width = 180, Margin = new(0,0,10,10), DisplayMemberPath = "Label", SelectedValuePath = "Value" };
        filter.SetBinding(ComboBox.SelectedValueProperty, new Binding("Filter") { Mode = BindingMode.TwoWay });
        toolbar.Children.Add(filter);
        toolbar.Children.Add(ActionButton("Keresés", "SearchCommand"));
        var create = ActionButton("Új", "NewCommand"); toolbar.Children.Add(create);
        toolbar.Children.Add(ActionButton("CSV export", "ExportCommand"));
        var import = ActionButton("Import InvenTree-ből", "ImportCommand");
        import.SetBinding(VisibilityProperty, new Binding("CanImport") { Converter = new BooleanToVisibilityConverter() });
        toolbar.Children.Add(import);
        root.Children.Add(toolbar);
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0,0,0,16) };
        message.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); message.SetBinding(TextBlock.TextProperty, new Binding("Message"));
        root.Children.Add(message);
        var body = new Grid(); body.ColumnDefinitions.Add(new() { Width = new(3, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new() { Width = new(2, GridUnitType.Star) });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto }); body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var listPanel = new StackPanel();
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
            CanUserDeleteRows = false, CanUserSortColumns = false, SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow, HeadersVisibility = DataGridHeadersVisibility.Column,
            MinHeight = 360, MaxHeight = 480, GridLinesVisibility = DataGridGridLinesVisibility.None,
            BorderThickness = new(0), FontSize = 13, RowHeight = 44, ColumnHeaderHeight = 40 };
        grid.SetResourceReference(Control.FontSizeProperty, "BodySize");
        grid.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush"); grid.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        grid.SetResourceReference(DataGrid.RowBackgroundProperty, "SurfaceBrush");
        grid.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty, "ElevatedBrush");
        grid.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Rows"));
        grid.SetBinding(DataGrid.SelectedItemProperty, new Binding("SelectedRow") { Mode = BindingMode.TwoWay });
        var headerStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("ElevatedBrush")));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("MutedBrush")));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12,8,8,8)));
        grid.ColumnHeaderStyle = headerStyle;
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
        editor.Children.Add(fields);
        var save = new WrapPanel { Margin = new(0,20,0,0) };
        var saveButton = ActionButton("Mentés", "SaveCommand"); saveButton.SetResourceReference(StyleProperty, "PrimaryButton");
        save.Children.Add(saveButton); save.Children.Add(ActionButton("Archiválás", "ArchiveCommand")); editor.Children.Add(save);
        editorBorder.Child = editor; Grid.SetColumn(editorBorder,1); body.Children.Add(editorBorder); root.Children.Add(body);
        Content = root;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not EntityPageViewModel vm) return;
            search.ToolTip = vm.Labels.SearchHint;
            create.Content = vm.Labels.NewLabel;
            filter.ItemsSource = vm.Filters;
            if (filter.SelectedValue is null) filter.SelectedValue = vm.Filters[0].Value;
            grid.Columns.Clear();
            foreach (var column in vm.Definition.Columns)
                grid.Columns.Add(new DataGridTextColumn { Header = column.Label, Binding = new Binding("[" + column.Id + "]"), Width = column.Width });
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
