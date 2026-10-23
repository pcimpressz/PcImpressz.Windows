using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Data;

namespace PcImpressz.Shell.Windows;

/// <summary>Presentation-only navigation captions. Navigation model, commands and
/// persisted business data are not modified.</summary>
public sealed class NavigationLabelOverrides : IValueConverter
{
    private static readonly string PathName = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PcImpressz", "WindowsEdition", "visual-navigation-labels.json");
    private static readonly Dictionary<string, string> Captions = Read();

    private static Dictionary<string, string> Read()
    {
        try
        {
            if (!File.Exists(PathName) || new FileInfo(PathName).Length > 32768)
                return new(StringComparer.Ordinal);
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(PathName));
            if (values is null || values.Count > 150 || values.Any(x => x.Key.Length > 100 || x.Value.Length > 100))
                return new(StringComparer.Ordinal);
            return new Dictionary<string, string>(values, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(StringComparer.Ordinal);
        }
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var original = value as string ?? "";
        return Captions.TryGetValue(original, out var replacement) ? replacement : original;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;

    public static bool TryGetOriginal(System.Windows.Controls.TextBlock text, out string original)
    {
        original = "";
        if (BindingOperations.GetBinding(text, System.Windows.Controls.TextBlock.TextProperty) is not Binding binding
            || binding.Converter is not NavigationLabelOverrides
            || binding.Path?.Path != "Title") return false;
        var prop = text.DataContext?.GetType().GetProperty("Title");
        if (prop?.GetValue(text.DataContext) is not string title || title.Length > 100) return false;
        original = title;
        return true;
    }

    public static void Set(string original, string replacement)
    {
        if (original.Length > 100 || replacement.Length > 100) return;
        if (replacement == original) Captions.Remove(original);
        else Captions[original] = replacement;
    }

    public static void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName)!);
        var tmp = PathName + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Captions, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(PathName)) File.Replace(tmp, PathName, PathName + ".bak");
        else File.Move(tmp, PathName);
    }

    public static void Reset()
    {
        Captions.Clear();
        if (File.Exists(PathName)) File.Delete(PathName);
    }
}
