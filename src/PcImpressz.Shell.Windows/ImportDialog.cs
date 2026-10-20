using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace PcImpressz.Shell.Windows;

internal static class ImportDialog
{
    public static void Show(EntityPageViewModel page)
    {
        var window = new Window { Title = "InvenTree-partnerek importálása", Width = 650, Height = 680,
            MinWidth = 550, MinHeight = 570, Owner = Application.Current.MainWindow, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(Window.BackgroundProperty,"SurfaceBrush"); window.SetResourceReference(Window.ForegroundProperty,"TextBrush");
        var panel = new StackPanel { Margin = new(28) };
        panel.Children.Add(new TextBlock { Text = "Hozd át a meglévő partnereidet.", FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new(0,0,0,12) });
        panel.Children.Add(new TextBlock { Text = "Csak olvasunk az InvenTree-ből. Az import a kiválasztott helyi cégbe kerül, a már meglévő partnereket nem írja felül.", TextWrapping = TextWrapping.Wrap, Margin = new(0,0,0,20) });
        panel.Children.Add(new TextBlock { Text = "InvenTree címe", Margin = new(0,0,0,7) });
        var url = new TextBox { Text = "https://raktar.bukacsalad.hu", Padding = new(10), Margin = new(0,0,0,16) };
        url.SetResourceReference(Control.BackgroundProperty,"ElevatedBrush"); url.SetResourceReference(Control.ForegroundProperty,"TextBrush");
        panel.Children.Add(url);
        panel.Children.Add(new TextBlock { Text = "Olvasási jogú API-kulcs (csak ehhez a lekéréshez)", Margin = new(0,0,0,7) });
        var token = new PasswordBox { Padding = new(10), Margin = new(0,0,0,16) };
        token.SetResourceReference(Control.BackgroundProperty,"ElevatedBrush"); token.SetResourceReference(Control.ForegroundProperty,"TextBrush");
        panel.Children.Add(token);
        var localHttp = new CheckBox { Content = "Nem titkosított helyi HTTP kapcsolat engedélyezése", Margin = new(0,0,0,20) };
        panel.Children.Add(localHttp);
        var previewButton = new Button { Content = "Előnézet betöltése", Margin = new(0,0,0,16) }; panel.Children.Add(previewButton);
        var summary = new TextBlock { Text = "A kulcsot nem mentjük el. A címek és a partner-alapadatok kerülnek átvételre.", TextWrapping = TextWrapping.Wrap, Margin = new(0,0,0,12) };
        panel.Children.Add(summary);
        var sample = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0,0,0,18) }; panel.Children.Add(sample);
        var commit = new Button { Content = "Új partnerek importálása", IsEnabled = false };
        commit.SetResourceReference(FrameworkElement.StyleProperty,"PrimaryButton"); panel.Children.Add(commit);
        Guid? previewId = null;
        previewButton.Click += async (_, _) =>
        {
            previewButton.IsEnabled = false; commit.IsEnabled = false; previewId = null; summary.Text = "Kapcsolódás és előnézet készítése…";
            try
            {
                var result = await page.ExecuteAsync(page.Definition.Commands.ImportPreview!, new { serverUrl = url.Text,
                    apiToken = token.Password, allowLocalHttp = localHttp.IsChecked == true });
                previewId = result.GetProperty("previewId").GetGuid();
                var added = result.GetProperty("newCount").GetInt32();
                summary.Text = added + " új partner · " + result.GetProperty("skippedCount").GetInt32() + " már meglévő/egyező · "
                    + result.GetProperty("invalidCount").GetInt32() + " hibás rekord.";
                foreach (var warning in result.GetProperty("warnings").EnumerateArray()) summary.Text += "\n" + warning.GetString();
                sample.Text = string.Join("\n", result.GetProperty("sample").EnumerateArray().Select(p =>
                    p.GetProperty("name").GetString() + " · " + p.GetProperty("city").GetString()));
                commit.IsEnabled = added > 0;
            }
            catch (Exception ex) { summary.Text = ex.Message; }
            finally { token.Clear(); previewButton.IsEnabled = true; }
        };
        commit.Click += async (_, _) =>
        {
            if (previewId is null) return;
            commit.IsEnabled = false; previewButton.IsEnabled = false;
            try
            {
                var result = await page.ExecuteAsync(page.Definition.Commands.ImportCommit!, new { previewId });
                page.Notify(result.GetProperty("imported").GetInt32() + " partner importálva.");
                window.Close(); await page.LoadAsync();
            }
            catch (Exception ex) { summary.Text = ex.Message; previewButton.IsEnabled = true; }
        };
        window.Closed += (_, _) => token.Clear();
        window.Content = new ScrollViewer { Content = panel }; window.ShowDialog();
    }
}
