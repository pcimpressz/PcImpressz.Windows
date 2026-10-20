using System.Windows;
using System.Windows.Media;
using PcImpressz.Shell.Customization;

namespace PcImpressz.Shell.Windows;

internal static class ThemeManager
{
    public static event Action? Applied;

    public static void Apply(SkinDefinition skin, AppearancePreferences preferences)
    {
        SkinValidation.Validate(skin);
        var p = skin.Palette;
        var colors = new Dictionary<string, string>
        {
            ["Background"] = p.Background, ["Surface"] = p.Surface, ["Elevated"] = p.Elevated,
            ["Sidebar"] = p.Sidebar, ["Stroke"] = p.Stroke, ["Text"] = p.Text, ["Muted"] = p.Muted,
            ["Accent"] = preferences.AccentOverride ?? p.Accent
        };
        var resources = Application.Current.Resources;
        foreach (var (name, value) in colors)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            brush.Freeze(); resources[name + "Brush"] = brush;
        }
        resources["AccentTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            SkinValidation.AccentForeground(colors["Accent"])));
        var accent = (Color)ColorConverter.ConvertFromString(colors["Accent"]);
        resources["AccentSoftBrush"] = new SolidColorBrush(Color.FromArgb(28, accent.R, accent.G, accent.B));
        var scale = preferences.FontScale;
        foreach (var (name, size) in new[] { ("CaptionSize", 11d), ("BodySize", 13d), ("SubtitleSize", 16d),
                     ("TitleSize", 25d), ("DisplaySize", 34d) })
            resources[name] = size * scale;
        resources["CardPadding"] = new Thickness(preferences.CompactDensity ? 18 : 26);
        resources["ControlPadding"] = new Thickness(16, preferences.CompactDensity ? 8 : 12, 16,
            preferences.CompactDensity ? 8 : 12);
        resources["NavigationWidth"] = preferences.CollapsedNavigation ? 78d : 240d;
        resources["NavigationLabelVisibility"] = preferences.CollapsedNavigation ? Visibility.Collapsed : Visibility.Visible;
        Applied?.Invoke();
    }
}
