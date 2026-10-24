using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PcImpressz.Contracts;
namespace PcImpressz.Shell.Windows;

internal sealed record QuotePrintLine(string Name, decimal Quantity, decimal Net, decimal VatRate, string Note);
internal sealed record QuotePrintDocument(string Company, string Number, string Customer, string ValidUntil, string Notes, string Email, IReadOnlyList<QuotePrintLine> Lines, string? LogoPath, string? SignaturePath);

internal static class QuoteBranding
{
    public static string Folder(string dataDirectory, Guid company)
    {
        var root = Path.GetFullPath(Path.Combine(dataDirectory, "companies", company.ToString("N")));
        var folder = Path.GetFullPath(Path.Combine(root, "branding"));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!folder.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("A képmappa a cég mappáján kívülre esne.");
        return folder;
    }
    public static string? Logo(string folder) => Existing(Path.Combine(folder, "logo.jpg"));
    public static string? Signature(string folder) => Existing(Path.Combine(folder, "signature.jpg"));
    public static void Save(string folder, string fileName, string source)
    {
        if (fileName is not ("logo.jpg" or "signature.jpg")) throw new ArgumentException("Ismeretlen képfájl.");
        var info = new FileInfo(source);
        if (!info.Exists || info.Length is < 32 or > 2_000_000) throw new ArgumentException("A kép legfeljebb 2 MB-os PNG vagy JPEG lehet.");
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("A kiválasztott fájl hivatkozás.");
        BitmapImage image;
        try
        {
            image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(info.FullName);
            image.EndInit();
            image.Freeze();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { throw new ArgumentException("A fájl nem olvasható kép."); }
        if (image.PixelWidth is < 1 or > 4000 || image.PixelHeight is < 1 or > 4000) throw new ArgumentException("A kép mérete túl nagy.");
        var max = fileName == "logo.jpg" ? 800d : 600d;
        BitmapSource frame = image;
        if (image.PixelWidth > max || image.PixelHeight > max)
        {
            var scale = Math.Min(max / image.PixelWidth, max / image.PixelHeight);
            frame = new TransformedBitmap(image, new ScaleTransform(scale, scale));
            frame.Freeze();
        }
        Directory.CreateDirectory(folder);
        if ((new DirectoryInfo(folder).Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("A képmappa hivatkozás.");
        var target = Path.Combine(folder, fileName);
        var temp = target + ".tmp";
        using (var stream = File.Create(temp))
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
            encoder.Frames.Add(BitmapFrame.Create(frame));
            encoder.Save(stream);
        }
        File.Move(temp, target, true);
    }
    public static void Remove(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) File.Delete(path);
    }
    private static string? Existing(string path) => File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0 ? path : null;
}

internal static class QuotePaper
{
    private static readonly CultureInfo Hu = CultureInfo.GetCultureInfo("hu-HU");
    public static IReadOnlyList<FrameworkElement> Pages(QuotePrintDocument document)
    {
        var header = Header(document);
        var footer = Footer(document);
        header.Measure(new Size(698, 4000));
        footer.Measure(new Size(698, 800));
        var rows = document.Lines.Select(Row).ToList();
        foreach (var row in rows) row.Measure(new Size(698, 4000));
        var pages = new List<List<FrameworkElement>>();
        var bucket = new List<FrameworkElement> { header };
        var used = header.DesiredSize.Height;
        var limit = 1123d - 96;
        var footerHeight = footer.DesiredSize.Height + 16;
        foreach (var row in rows)
        {
            var height = row.DesiredSize.Height + 6;
            if (used + height + 24 > limit && bucket.Count > 1)
            {
                pages.Add(bucket);
                bucket = [];
                used = 0;
            }
            bucket.Add(row);
            used += height;
        }
        if (used + footerHeight > limit && bucket.Count > 0)
        {
            pages.Add(bucket);
            bucket = [];
        }
        bucket.Add(footer);
        pages.Add(bucket);
        return pages.Select(Sheet).ToArray();
    }
    private static Border Sheet(List<FrameworkElement> children)
    {
        var stack = new StackPanel();
        foreach (var child in children) stack.Children.Add(child);
        return new Border { Width = 794, Height = 1123, Background = Brushes.White, Padding = new Thickness(48), Child = stack, SnapsToDevicePixels = true };
    }
    private static FrameworkElement Header(QuotePrintDocument document)
    {
        var root = new StackPanel();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var logo = Picture(document.LogoPath, 180, 72);
        if (logo is not null) { DockPanel.SetDock(logo, Dock.Right); top.Children.Add(logo); }
        var titles = new StackPanel();
        titles.Children.Add(Text(string.IsNullOrWhiteSpace(document.Company) ? "Árajánlat" : document.Company, 22, bold: true));
        titles.Children.Add(Text("ÁRAJÁNLAT", 13, bold: true, brush: new SolidColorBrush(Color.FromRgb(0x1F, 0x8A, 0x5B))));
        top.Children.Add(titles);
        root.Children.Add(top);
        root.Children.Add(Line());
        root.Children.Add(Text("Ajánlat: " + document.Number, 14, bold: true));
        root.Children.Add(Text("Vevő: " + (string.IsNullOrWhiteSpace(document.Customer) ? "—" : document.Customer), 14));
        root.Children.Add(Text("Érvényes: " + document.ValidUntil, 14));
        root.Children.Add(Line());
        root.Children.Add(TableHeader());
        return root;
    }
    private static FrameworkElement Row(QuotePrintLine line)
    {
        var amount = VatMath.Line(line.Quantity, line.Net, line.VatRate);
        var root = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        var grid = new Grid();
        foreach (var width in new[] { 250d, 70, 110, 50, 120 }) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        grid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        AddCell(grid, line.Name, 0, bold: true);
        AddCell(grid, line.Quantity.ToString("0.##", Hu), 1);
        AddCell(grid, Money(line.Net), 2);
        AddCell(grid, line.VatRate.ToString("0.##", Hu) + "%", 3);
        AddCell(grid, Money(amount.Gross), 4);
        root.Children.Add(grid);
        if (!string.IsNullOrWhiteSpace(line.Note)) root.Children.Add(Text(line.Note, 12, brush: Brushes.DimGray));
        return root;
    }
    private static Grid TableHeader()
    {
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 4) };
        foreach (var width in new[] { 250d, 70, 110, 50, 120 }) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        grid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        var headers = new[] { "Tétel", "Menny.", "Nettó", "Áfa", "Bruttó" };
        for (var i = 0; i < headers.Length; i++) AddCell(grid, headers[i], i, bold: true);
        return grid;
    }
    private static FrameworkElement Footer(QuotePrintDocument document)
    {
        var root = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        root.Children.Add(Line());
        decimal net = 0, vat = 0, gross = 0;
        foreach (var line in document.Lines)
        {
            var amount = VatMath.Line(line.Quantity, line.Net, line.VatRate);
            net += amount.Net; vat += amount.Vat; gross += amount.Gross;
        }
        root.Children.Add(Text("Nettó összesen: " + Money(net), 13));
        root.Children.Add(Text("Áfa összesen: " + Money(vat), 13));
        root.Children.Add(Text("Bruttó összesen: " + Money(gross), 16, bold: true));
        if (!string.IsNullOrWhiteSpace(document.Notes))
        {
            root.Children.Add(Text("Megjegyzés", 13, bold: true));
            root.Children.Add(Text(document.Notes, 13));
        }
        var sign = Picture(document.SignaturePath, 200, 80);
        if (sign is not null) { sign.Margin = new Thickness(0, 18, 0, 0); sign.HorizontalAlignment = HorizontalAlignment.Right; root.Children.Add(sign); }
        root.Children.Add(Text("Az ajánlat a megadott érvényességig szól, és nem helyettesíti a számlát.", 11, brush: Brushes.DimGray));
        return root;
    }
    private static void AddCell(Grid grid, string value, int column, bool bold = false)
    {
        var text = Text(value, 12, bold);
        Grid.SetColumn(text, column);
        grid.Children.Add(text);
    }
    private static TextBlock Text(string value, double size, bool bold = false, Brush? brush = null) => new()
    {
        Text = value, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = brush ?? Brushes.Black, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 8, 2)
    };
    private static Border Line() => new() { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x8A, 0x5B)), Margin = new Thickness(0, 8, 0, 8) };
    private static string Money(decimal value) => value.ToString("N2", Hu) + " Ft";
    private static Image? Picture(string? path, double maxWidth, double maxHeight)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return new Image { Source = bitmap, MaxWidth = maxWidth, MaxHeight = maxHeight, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }
}

internal static class QuotePrint
{
    public static void Show(QuotePrintDocument document)
    {
        var pages = QuotePaper.Pages(document);
        var window = Chrome("Árajánlat nyomtatási képe");
        var note = Hint("A lap a Beállításokban feltöltött logót és aláírást használja. A beszerzési ár és a haszonkulcs nem kerül a vevő elé. A végleges mintasablon később cseréli az elrendezést.");
        var open = new Button { Content = "PDF megnyitása", Margin = new Thickness(0, 0, 8, 8) };
        var close = new Button { Content = "Bezárás", Margin = new Thickness(0, 0, 8, 8), IsCancel = true };
        open.Click += (_, _) => { try { OpenPdf(Render(document)); } catch (Exception ex) when (ex is not OutOfMemoryException) { note.Text = ex.Message; } };
        close.Click += (_, _) => window.Close();
        window.Content = Shell(note, new[] { open, close }, pages);
        window.ShowDialog();
    }
    public static void ShowMail(QuotePrintDocument document, OutgoingMailAccount mail, Action<string> report)
    {
        var pages = QuotePaper.Pages(document);
        var window = Chrome("Árajánlat e-mail előnézete");
        var to = Box(document.Email);
        var subject = Box("Árajánlat " + document.Number + (string.IsNullOrWhiteSpace(document.Company) ? "" : " – " + document.Company));
        var body = Box(Letter(document, mail));
        body.AcceptsReturn = true; body.MinHeight = 160; body.TextWrapping = TextWrapping.Wrap; body.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var note = Hint(mail.CanSend ? "A küldés a Beállítások e-mail lapján megadott postafiókból indul. A csatolmány a nyomtatási kép." : "Előnézet kész. Küldéshez töltsd ki az SMTP-kiszolgálót, a felhasználót és a jelszót a Beállítások e-mail lapján." + (mail.Problem.Length == 0 ? "" : " " + mail.Problem));
        var send = new Button { Content = "Küldés", Margin = new Thickness(0, 0, 8, 8), IsEnabled = mail.CanSend };
        var open = new Button { Content = "Csatolt PDF megnyitása", Margin = new Thickness(0, 0, 8, 8) };
        var close = new Button { Content = "Bezárás", Margin = new Thickness(0, 0, 8, 8), IsCancel = true };
        byte[]? pdf = null;
        open.Click += (_, _) => { try { pdf ??= Render(document); OpenPdf(pdf); } catch (Exception ex) when (ex is not OutOfMemoryException) { note.Text = ex.Message; } };
        send.Click += async (_, _) =>
        {
            if (!MailAddress.TryCreate(to.Text.Trim(), out var address)) { note.Text = "Érvényes címzett e-mail-cím kell."; return; }
            send.IsEnabled = false;
            note.Text = "Küldés…";
            try
            {
                pdf ??= Render(document);
                var file = SafeFile(document.Number);
                await Task.Run(() => Send(mail, address.Address, subject.Text.Trim(), body.Text, pdf, file));
                report("Az árajánlat elküldve: " + address.Address);
                window.Close();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { note.Text = "A levél nem ment el. " + ex.Message; send.IsEnabled = mail.CanSend; }
        };
        close.Click += (_, _) => window.Close();
        var form = new StackPanel();
        form.Children.Add(Label("Címzett")); form.Children.Add(to);
        form.Children.Add(Label("Tárgy")); form.Children.Add(subject);
        form.Children.Add(Label("Levél")); form.Children.Add(body);
        form.Children.Add(Hint("Csatolmány: a nyomtatási kép PDF-ben."));
        var root = Shell(note, new[] { send, open, close }, pages);
        var dock = (DockPanel)root;
        DockPanel.SetDock(form, Dock.Top);
        dock.Children.Insert(1, form);
        window.Content = dock;
        window.ShowDialog();
    }
    private static string Letter(QuotePrintDocument document, OutgoingMailAccount mail)
    {
        var greeting = string.IsNullOrWhiteSpace(document.Customer) ? "Tisztelt Partnerünk!" : "Tisztelt " + document.Customer + "!";
        var validity = string.IsNullOrWhiteSpace(document.ValidUntil) ? "" : " Az ajánlat " + document.ValidUntil + " napig érvényes.";
        var notes = string.IsNullOrWhiteSpace(document.Notes) ? "" : "\n\n" + document.Notes.Trim();
        var sign = string.IsNullOrWhiteSpace(mail.SignatureName) ? document.Company : mail.SignatureName;
        var web = string.IsNullOrWhiteSpace(mail.Website) ? "" : "\n" + mail.Website.Trim();
        return greeting + "\n\nMellékeljük árajánlatunkat (" + document.Number + ")." + validity + notes + "\n\nÜdvözlettel:\n" + sign + web;
    }
    private static Window Chrome(string title)
    {
        var window = new Window { Title = title, Width = 920, Height = 780, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Application.Current?.MainWindow };
        window.SetResourceReference(Window.BackgroundProperty, "SurfaceBrush");
        window.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        return window;
    }
    private static DockPanel Shell(TextBlock note, IEnumerable<Button> buttons, IReadOnlyList<FrameworkElement> pages)
    {
        var root = new DockPanel { Margin = new Thickness(16) };
        var bar = new WrapPanel();
        foreach (var button in buttons) bar.Children.Add(button);
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(note, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(note);
        var host = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var page in pages)
        {
            page.LayoutTransform = new ScaleTransform(0.72, 0.72);
            host.Children.Add(new Border { Child = page, Margin = new Thickness(0, 0, 0, 16), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left });
        }
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = host });
        return root;
    }
    private static TextBlock Hint(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        block.SetResourceReference(FrameworkElement.StyleProperty, "Muted");
        return block;
    }
    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 8, 0, 4) };
    private static TextBox Box(string text)
    {
        var box = new TextBox { Text = text, MinHeight = 34, Margin = new Thickness(0, 0, 0, 4) };
        box.SetResourceReference(Control.BackgroundProperty, "ElevatedBrush");
        box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        return box;
    }
    public static byte[] Render(QuotePrintDocument document)
    {
        var pages = QuotePaper.Pages(document);
        var host = new StackPanel();
        foreach (var page in pages) host.Children.Add(page);
        var window = new Window { Width = 40, Height = 40, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -3200, Top = -3200, Content = host };
        window.Show();
        try
        {
            window.UpdateLayout();
            var images = new List<PdfImage>();
            foreach (var page in pages)
            {
                page.LayoutTransform = Transform.Identity;
                page.Measure(new Size(794, 1123));
                page.Arrange(new Rect(0, 0, 794, 1123));
                const double dpi = 144;
                var scale = dpi / 96d;
                var width = (int)Math.Ceiling(794 * scale);
                var height = (int)Math.Ceiling(1123 * scale);
                var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
                bitmap.Render(page);
                var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = new MemoryStream();
                encoder.Save(stream);
                images.Add(new(stream.ToArray(), width, height));
            }
            return PdfImages.Build(images);
        }
        finally { window.Close(); }
    }
    private static void OpenPdf(byte[] pdf)
    {
        var folder = Path.Combine(Path.GetTempPath(), "PcImpressz");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "ajanlat-" + Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(path, pdf);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    private static string SafeFile(string number)
    {
        var clean = new string(number.Select(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-').ToArray());
        if (clean.Length == 0) clean = "ajanlat";
        return clean + ".pdf";
    }
#pragma warning disable SYSLIB0014
    private static void Send(OutgoingMailAccount mail, string to, string subject, string body, byte[] pdf, string fileName)
    {
        using var message = new MailMessage(mail.User, to, subject, body);
        message.Attachments.Add(new Attachment(new MemoryStream(pdf), fileName, "application/pdf"));
        using var client = new SmtpClient(mail.Host, mail.Port) { EnableSsl = mail.Port == 587 || mail.Ssl, Credentials = new NetworkCredential(mail.User, mail.Password), Timeout = 30_000 };
        client.Send(message);
    }
#pragma warning restore SYSLIB0014
}

public sealed class OutgoingMailAccount
{
    public string Host { get; init; } = "";
    public int Port { get; init; } = 587;
    public bool Ssl { get; init; }
    public string User { get; init; } = "";
    public string Password { get; init; } = "";
    public string SignatureName { get; init; } = "";
    public string Website { get; init; } = "";
    public string Problem { get; init; } = "";
    public bool CanSend => Problem.Length == 0 && Host.Length > 0 && User.Length > 0 && Password.Length > 0;
}
internal sealed record PdfImage(byte[] Jpeg, int Width, int Height);
internal static class PdfImages
{
    public static byte[] Build(IReadOnlyList<PdfImage> pages)
    {
        if (pages.Count is < 1 or > 20) throw new ArgumentException("A PDF 1 és 20 oldal között lehet.");
        using var stream = new MemoryStream();
        void Write(string text) => stream.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        void ObjectStart() => offsets.Add(stream.Position);
        ObjectStart();
        Write("1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n");
        var kids = string.Join(' ', Enumerable.Range(0, pages.Count).Select(i => (3 + i * 3) + " 0 R"));
        ObjectStart();
        Write("2 0 obj << /Type /Pages /Count " + pages.Count + " /Kids [" + kids + "] >> endobj\n");
        for (var i = 0; i < pages.Count; i++)
        {
            var pageId = 3 + i * 3;
            var contentId = pageId + 1;
            var imageId = pageId + 2;
            var content = "q 595.28 0 0 841.89 0 0 cm /Im0 Do Q\n";
            ObjectStart();
            Write(pageId + " 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 595.28 841.89] /Contents " + contentId + " 0 R /Resources << /XObject << /Im0 " + imageId + " 0 R >> >> >> endobj\n");
            ObjectStart();
            Write(contentId + " 0 obj << /Length " + Encoding.ASCII.GetByteCount(content) + " >> stream\n" + content + "endstream endobj\n");
            ObjectStart();
            Write(imageId + " 0 obj << /Type /XObject /Subtype /Image /Width " + pages[i].Width + " /Height " + pages[i].Height + " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + pages[i].Jpeg.Length + " >> stream\n");
            stream.Write(pages[i].Jpeg);
            Write("\nendstream endobj\n");
        }
        var xref = stream.Position;
        Write("xref\n0 " + offsets.Count + "\n");
        for (var i = 0; i < offsets.Count; i++)
            Write(i == 0 ? "0000000000 65535 f \n" : offsets[i].ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");
        Write("trailer << /Size " + offsets.Count + " /Root 1 0 R >>\nstartxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF");
        return stream.ToArray();
    }
}
