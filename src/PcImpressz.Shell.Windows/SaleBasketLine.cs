using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
namespace PcImpressz.Shell.Windows;
internal sealed class SaleBasketLine:Observable,IDataErrorInfo
{
    private string _quantityText,_grossText;private decimal _net,_vat;private bool _missingPrice;
    public SaleBasketLine(Guid ProductId,string Name,string UnitCode,decimal Quantity,decimal NetUnitPrice,decimal VatRate)
    {this.ProductId=ProductId;this.Name=Name;this.UnitCode=UnitCode;_quantityText=Quantity.ToString(CultureInfo.GetCultureInfo("hu-HU"));_net=NetUnitPrice;_vat=VatRate;_grossText=decimal.Round(NetUnitPrice*(1+VatRate/100m),2,MidpointRounding.AwayFromZero).ToString("0.00",CultureInfo.GetCultureInfo("hu-HU"));}
    public Guid ProductId{get;}public string Name{get;}public string UnitCode{get;}
    public string QuantityText{get=>_quantityText;set{_quantityText=value;NotifyValues();}}
    public string GrossPriceText{get=>_grossText;set{_grossText=value;if(Parse(value,out var amount)&&amount>=0&&amount<=1_270_000_000&&decimal.Round(amount,2)==amount){_net=decimal.Round(amount/(1+_vat/100m),4,MidpointRounding.AwayFromZero);_missingPrice=false;}NotifyValues();}}
    public decimal Quantity=>Parse(_quantityText,out var q)?q:0;
    public decimal NetUnitPrice=>_net;
    public decimal VatRate{get=>_vat;set{_vat=value;if(Parse(_grossText,out var amount)&&new decimal[]{0,5,18,27}.Contains(value))_net=decimal.Round(amount/(1+value/100m),4,MidpointRounding.AwayFromZero);NotifyValues();}}
    public string GrossDisplay=>MoneyDisplay.Format(Gross);
    public string GrossPriceDisplay=>MoneyDisplay.Format(decimal.Round(_net*(1+_vat/100m),2,MidpointRounding.AwayFromZero));
    public decimal Gross=>Error.Length>0?0:decimal.Round(Quantity*_net,2,MidpointRounding.AwayFromZero)+decimal.Round(decimal.Round(Quantity*_net,2,MidpointRounding.AwayFromZero)*_vat/100m,2,MidpointRounding.AwayFromZero);
    public ICommand RemoveCommand{get;set;}=new RelayCommand(()=>{});
    public void RequirePrice(){_missingPrice=true;NotifyValues();}
    public string Error=>!Parse(_quantityText,out var q)||q<=0||q>1_000_000||decimal.Round(q,4)!=q?"A mennyiség pozitív szám legyen, legfeljebb 4 tizedessel.":_missingPrice?"Adj meg eladási árat.":!Parse(_grossText,out var p)||p<0||p>1_270_000_000||decimal.Round(p,2)!=p?"A bruttó egységár 0 vagy pozitív szám legyen, legfeljebb 2 tizedessel.":!new decimal[]{0,5,18,27}.Contains(_vat)?"Érvénytelen áfakulcs.":"";
    public string this[string columnName]=>columnName is nameof(QuantityText) or nameof(GrossPriceText) or nameof(VatRate)?Error:"";
    private static bool Parse(string text,out decimal value)=>decimal.TryParse(text.Replace(" ","").Replace(',','.'),NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out value);
    private void NotifyValues(){foreach(var p in new[]{nameof(QuantityText),nameof(GrossPriceText),nameof(Quantity),nameof(NetUnitPrice),nameof(VatRate),nameof(Gross),nameof(GrossDisplay),nameof(GrossPriceDisplay),nameof(Error)})Changed(p);}
}
