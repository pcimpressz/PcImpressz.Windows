using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace PcImpressz.Shell.Windows;

internal static class DossierDialog
{
    public static void Show(EntityPageViewModel page, Guid partnerId)
    {
        var window = new Window
        {
            Title = "Partner dosszié", Width = 720, Height = 640, MinWidth = 520, MinHeight = 420,
            Owner = Application.Current.MainWindow, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        window.SetResourceReference(Window.BackgroundProperty, "SurfaceBrush");
        window.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        var panel = new StackPanel { Margin = new(28) };
        var title = new TextBlock { Text = "Partner dosszié", FontSize = 23, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var subtitle = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 16) };
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var notice = new TextBlock { Text = "Betöltés…", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 18) };
        var cards = new WrapPanel();
        var sections = new StackPanel();
        panel.Children.Add(title); panel.Children.Add(subtitle); panel.Children.Add(notice);
        panel.Children.Add(cards); panel.Children.Add(sections);
        window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        window.Loaded += async (_, _) =>
        {
            try
            {
                var result = await page.ExecuteAsync("dossier", new { id = partnerId });
                title.Text = Text(result, "title");
                window.Title = title.Text;
                subtitle.Text = Text(result, "subtitle");
                notice.Text = Text(result, "notice");
                foreach (var card in result.GetProperty("cards").EnumerateArray()) cards.Children.Add(Card(card));
                foreach (var section in result.GetProperty("sections").EnumerateArray()) sections.Children.Add(Section(section));
            }
            catch (Exception ex) { notice.Text = ex.Message; }
        };
        window.ShowDialog();
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static Border Card(JsonElement card)
    {
        var box = new Border { Padding = new(12), Margin = new(0, 0, 8, 8), CornerRadius = new(10), MinWidth = 140 };
        box.SetResourceReference(Border.BackgroundProperty, "ElevatedBrush");
        var stack = new StackPanel();
        var label = new TextBlock { Text = Text(card, "label"), FontSize = 12 };
        label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        stack.Children.Add(label);
        stack.Children.Add(new TextBlock { Text = Text(card, "value"), FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new(0, 4, 0, 0) });
        box.Child = stack;
        return box;
    }

    private static StackPanel Section(JsonElement section)
    {
        var block = new StackPanel { Margin = new(0, 16, 0, 0) };
        block.Children.Add(new TextBlock { Text = Text(section, "title"), FontWeight = FontWeights.SemiBold });
        var source = new TextBlock { Text = Text(section, "source"), Margin = new(0, 4, 0, 4), FontSize = 12 };
        source.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var detail = new TextBlock { Text = Text(section, "detail"), TextWrapping = TextWrapping.Wrap };
        block.Children.Add(source); block.Children.Add(detail);
        if (section.TryGetProperty("lines", out var lines))
            foreach (var line in lines.EnumerateArray()) block.Children.Add(Line(line));
        return block;
    }

    private static Grid Line(JsonElement line)
    {
        var row = new Grid { Margin = new(0, 8, 0, 0) };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = Text(line, "title"), TextWrapping = TextWrapping.Wrap });
        var extra = new TextBlock { Text = Text(line, "detail"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
        extra.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        text.Children.Add(extra);
        var amount = new TextBlock { Text = Text(line, "amount"), FontWeight = FontWeights.SemiBold, Margin = new(12, 0, 0, 0) };
        Grid.SetColumn(amount, 1);
        row.Children.Add(text); row.Children.Add(amount);
        return row;
    }
}
