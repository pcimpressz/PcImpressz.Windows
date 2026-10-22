using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PcImpressz.Shell.Customization;
namespace PcImpressz.Shell.Windows;
internal static class BarcodeLabelPrinter
{
    internal static FixedPage Build(string barcode,string name)
    {
        var pattern=Barcode128.Pattern(barcode);const double width=40*96/25.4,height=12*96/25.4;
        var page=new FixedPage{Width=width,Height=height,Background=Brushes.White};
        var title=new TextBlock{Text=name,FontSize=6,Foreground=Brushes.Black,Width=width-8,TextTrimming=TextTrimming.CharacterEllipsis};FixedPage.SetLeft(title,4);FixedPage.SetTop(title,1);page.Children.Add(title);
        var module=(width-8)/(pattern.Length+20);var x=4+10*module;
        foreach(var bit in pattern){if(bit=='1'){var bar=new System.Windows.Shapes.Rectangle{Width=module+0.01,Height=24,Fill=Brushes.Black};FixedPage.SetLeft(bar,x);FixedPage.SetTop(bar,10);page.Children.Add(bar);}x+=module;}
        var text=new TextBlock{Text=barcode,FontSize=7,Foreground=Brushes.Black,Width=width,TextAlignment=TextAlignment.Center};FixedPage.SetTop(text,35);page.Children.Add(text);return page;
    }
    public static void Print(string barcode,string name)
    {
        var page=Build(barcode,name);var dialog=new PrintDialog();
        if(string.IsNullOrWhiteSpace(InvoiceListPreferences.LabelPrinter)){if(dialog.ShowDialog()!=true)throw new OperationCanceledException("A címkenyomtatás megszakítva.");InvoiceListPreferences.LabelPrinter=dialog.PrintQueue.FullName;}
        else
        {
            using var server=new LocalPrintServer();dialog.PrintQueue=server.GetPrintQueues().FirstOrDefault(q=>q.FullName.Equals(InvoiceListPreferences.LabelPrinter,StringComparison.OrdinalIgnoreCase))??throw new ArgumentException("A beállított címkenyomtató nem található a Windowsban.");
        }
        dialog.PrintTicket=new PrintTicket{PageMediaSize=new PageMediaSize(page.Width,page.Height),CopyCount=1};
        var document=new FixedDocument();var content=new PageContent();((System.Windows.Markup.IAddChild)content).AddChild(page);document.Pages.Add(content);dialog.PrintDocument(document.DocumentPaginator,"PC-Impressz vonalkódcímke");
    }
}
