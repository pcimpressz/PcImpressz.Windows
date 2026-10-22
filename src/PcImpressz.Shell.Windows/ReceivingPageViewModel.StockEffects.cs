using System.Text.Json;

namespace PcImpressz.Shell.Windows;
internal sealed partial class LineEditor
{
    public decimal? SourceNetAmount {get;set;}
    public decimal? SourceVatAmount {get;set;}
    public decimal? SourceGrossAmount {get;set;}
    public string StockEffect=>_page.LineStockEffect(this);
}
internal sealed partial class ReceivingPageViewModel
{
    private bool _receiptPosted;
    internal string LineStockEffect(LineEditor line)
    {
        if(!line.StockItem||line.IsService||_status=="koltseg")return _receiptPosted||IsLocked?"Nem lett raktárra véve":"Nem kerül raktárra";
        if(TryMoney(line.QuantityText,out var quantity)&&quantity<0)return _receiptPosted?"Készletet csökkentette":"Készletet csökkenti";
        return _receiptPosted?"Raktárra véve":"Készletre jelölve";
    }
    public string ExcludedInvoiceLines
    {
        get
        {
            var excluded=Lines.Where(l=>!l.StockItem||l.IsService||_status=="koltseg").Select(l=>l.Name).ToArray();
            return excluded.Length==0?"":(_receiptPosted||IsLocked?"Nem lett raktárra véve: ":"A számlában szerepel, készletre nem kerül: ")+string.Join("; ",excluded);
        }
    }
}
