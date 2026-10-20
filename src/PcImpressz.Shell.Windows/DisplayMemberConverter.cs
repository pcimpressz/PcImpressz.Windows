using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PcImpressz.Shell.Windows;

public sealed class DisplayMemberConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is null || values[0] == DependencyProperty.UnsetValue) return "";
        var combo = values[1] as ComboBox;
        if (combo is null && values[1] is DependencyObject container)
            combo = ItemsControl.ItemsControlFromItemContainer(container) as ComboBox;
        var item = values[0];
        var path = combo?.DisplayMemberPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var segment in path.Split('.'))
            {
                item = TypeDescriptor.GetProperties(item)[segment]?.GetValue(item);
                if (item is null) return "";
            }
        }
        return item.ToString() ?? "";
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
