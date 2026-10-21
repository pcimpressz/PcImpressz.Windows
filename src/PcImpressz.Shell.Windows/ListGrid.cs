using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using PcImpressz.Contracts;

namespace PcImpressz.Shell.Windows;

internal static class ListGrid
{
    private static readonly IValueConverter CellText = new CellTextConverter();

    public static void Prepare(DataGrid grid, Func<IColumnList?> page)
    {
        grid.CanUserSortColumns = true;
        grid.CanUserReorderColumns = false;
        grid.Sorting += (_, args) =>
        {
            args.Handled = true;
            if (args.Column.SortMemberPath is { Length: > 0 } id)
                page()?.ToggleSort(id);
        };
    }

    public static void Show(DataGrid grid, IColumnList page)
    {
        grid.Columns.Clear();
        foreach (var column in page.VisibleColumns)
        {
            var header = new TextBlock { Text = column.Label };
            if (column.Id == "quantity")
                header.ToolTip = "A termék készlete az összes raktárban. Raktárankénti bontás a Készlet oldalon.";
            var text = new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding("[" + column.Id + "]") { Converter = CellText },
                Width = new DataGridLength(column.Width),
                SortMemberPath = column.Id
            };
            if (column.Id == page.SortColumn)
                text.SortDirection = page.SortDescending ? System.ComponentModel.ListSortDirection.Descending : System.ComponentModel.ListSortDirection.Ascending;
            grid.Columns.Add(text);
        }
    }

    public static TextBlock MatchCount()
    {
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 10), FontWeight = FontWeights.SemiBold };
        text.SetBinding(TextBlock.TextProperty, new Binding("MatchCount"));
        return text;
    }

    public static Button ColumnButton(Func<IColumnList?> page)
    {
        var button = new Button
        {
            Width = 40, Height = 38, Margin = new(0, 0, 8, 10), Padding = new(0), ToolTip = "Oszlopok",
            Content = new TextBlock
            {
                Text = "\uE713", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        var stack = new StackPanel();
        var border = new Border { Child = stack, Padding = new(12), BorderThickness = new(1), MinWidth = 230, MaxWidth = 320 };
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");
        var popup = new Popup { PlacementTarget = button, Placement = PlacementMode.Bottom, StaysOpen = false, Child = border };
        button.Click += (_, _) =>
        {
            var current = page();
            if (current is null) return;
            stack.Children.Clear();
            stack.Children.Add(new TextBlock { Text = "Látható oszlopok", FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 8) });
            foreach (var column in current.Definition.Columns)
            {
                var box = new CheckBox { Content = column.Label, IsChecked = current.IsColumnVisible(column.Id), Margin = new(0, 3, 0, 3) };
                var id = column.Id;
                var mute = false;
                box.Checked += (_, _) =>
                {
                    if (mute) return;
                    if (current.SetColumnVisible(id, true)) return;
                    mute = true; box.IsChecked = current.IsColumnVisible(id); mute = false;
                };
                box.Unchecked += (_, _) =>
                {
                    if (mute) return;
                    if (current.SetColumnVisible(id, false)) return;
                    mute = true; box.IsChecked = true; mute = false;
                };
                stack.Children.Add(box);
            }
            var note = new TextBlock
            {
                Text = "A kereső névrészletre szűkít. A fejléc rendez. Ugyanez a Beállításokban is megvan.",
                TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 0), FontSize = 12
            };
            note.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            stack.Children.Add(note);
            popup.IsOpen = true;
        };
        return button;
    }

    private sealed class CellTextConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
        {
            bool flag => flag ? "Igen" : "Nem",
            null => "",
            _ => value
        };
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
