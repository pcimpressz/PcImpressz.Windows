using System.Globalization;
using PcImpressz.Contracts;
namespace PcImpressz.Shell.Windows;

internal sealed class QuoteEditLine:Observable
{
    private static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
    private readonly Action _changed;
    private bool _sync;
    private decimal _net,_gross,_vat=27,_purchase,_margin;
    private bool _hasPurchase,_hasMargin;
    private string _name="",_quantity="1",_purchaseText="",_marginText="",_grossText="0.00",_vatText="27",_note="";
    public QuoteEditLine(Action changed)=>_changed=changed;
    public Guid ProductId{get;set;}
    public string Name{get=>_name;set{if(_name==value)return;_name=value;Changed();Touch();}}
    public string Quantity{get=>_quantity;set{if(_quantity==value)return;_quantity=value;Changed();Changed(nameof(LineGross));Touch();}}
    public string Purchase{get=>_purchaseText;set{if(_purchaseText==value)return;_purchaseText=value;Changed();if(_sync)return;ApplyPurchase();}}
    public string Margin{get=>_marginText;set{if(_marginText==value)return;_marginText=value;Changed();if(_sync)return;ApplyMargin();}}
    public string Gross{get=>_grossText;set{if(_grossText==value)return;_grossText=value;Changed();if(_sync)return;ApplyGross();}}
    public string Vat{get=>_vatText;set{if(_vatText==value)return;_vatText=value;Changed();if(_sync)return;ApplyVat();}}
    public string Note{get=>_note;set{if(_note==value)return;_note=value;Changed();Touch();}}
    public string Net=>_net.ToString("0.00",Inv);
    public decimal NetValue=>_net;
    public decimal VatValue=>_vat;
    public decimal? PurchaseValue=>_hasPurchase?_purchase:null;
    public decimal? MarginValue=>_hasMargin?_margin:null;
    public string NoteValue=>_note.Trim();
    public string LineGross
    {
        get
        {
            if(!TryDec(_quantity,out var qty)||qty<=0||!VatMath.IsSupportedRate(_vat))return "";
            return VatMath.Line(qty,_net,_vat).Gross.ToString("N0",CultureInfo.GetCultureInfo("hu-HU"))+" Ft";
        }
    }
    public void Load(string name,decimal quantity,decimal? purchase,decimal? margin,decimal net,decimal vat,string? note)
    {
        _sync=true;
        _name=name;_quantity=quantity.ToString("0.##",Inv);_note=note??"";
        _vat=VatMath.IsSupportedRate(vat)?vat:27;_vatText=_vat.ToString("0.##",Inv);
        _net=net<0?0:net;_gross=QuotePricing.GrossFromNet(_net,_vat);_grossText=_gross.ToString("0.00",Inv);
        if(purchase is {} cost&&cost>=0){_hasPurchase=true;_purchase=cost;_purchaseText=cost.ToString("0.00",Inv);}
        else{_hasPurchase=false;_purchase=0;_purchaseText="";}
        if(margin is {} given&&given is >=-100 and <=1000){_hasMargin=true;_margin=given;_marginText=given.ToString("0.##",Inv);}
        else if(_hasPurchase&&_purchase>0&&QuotePricing.Margin(_purchase,_net) is {} computed){_hasMargin=true;_margin=computed;_marginText=computed.ToString("0.##",Inv);}
        else{_hasMargin=false;_margin=0;_marginText="";}
        Changed(nameof(Name));Changed(nameof(Quantity));Changed(nameof(Purchase));Changed(nameof(Margin));Changed(nameof(Gross));Changed(nameof(Vat));Changed(nameof(Note));Changed(nameof(Net));Changed(nameof(LineGross));
        _sync=false;_changed();
    }
    public void EnsureClosed()
    {
        if(string.IsNullOrWhiteSpace(_name))throw new ArgumentException("A tétel megnevezése hiányzik.");
        if(!TryDec(_quantity,out var qty)||qty<=0)throw new ArgumentException("A mennyiség pozitív szám legyen.");
        if(!string.IsNullOrWhiteSpace(_purchaseText)&&(!TryDec(_purchaseText,out var purchase)||purchase<0))throw new ArgumentException("A nettó beszerzési ár nem lehet negatív.");
        if(!string.IsNullOrWhiteSpace(_marginText)&&(!TryDec(_marginText,out var margin)||margin is < -100 or > 1000))throw new ArgumentException("A haszonkulcs -100 és 1000 százalék között lehet.");
        if(!TryDec(_grossText,out var gross)||gross<0)throw new ArgumentException("A bruttó egységár nem lehet negatív.");
        if(!TryDec(_vatText,out var vat)||!VatMath.IsSupportedRate(vat))throw new ArgumentException("Az áfa 0, 5, 18 vagy 27 százalék lehet.");
        if(_note.Trim().Length>500)throw new ArgumentException("A sor megjegyzése legfeljebb 500 karakter.");
    }
    private void ApplyPurchase()
    {
        if(string.IsNullOrWhiteSpace(_purchaseText)){_hasPurchase=false;_purchase=0;Touch();return;}
        if(!TryDec(_purchaseText,out var purchase)||purchase<0){Touch();return;}
        _hasPurchase=true;_purchase=purchase;
        if(_hasMargin&&purchase>0){_net=QuotePricing.SaleNet(purchase,_margin);PublishGross(false);}
        else if(purchase>0&&QuotePricing.Margin(purchase,_net) is {} margin){_hasMargin=true;_margin=margin;_marginText=margin.ToString("0.##",Inv);Changed(nameof(Margin));}
        Changed(nameof(LineGross));Touch();
    }
    private void ApplyMargin()
    {
        if(string.IsNullOrWhiteSpace(_marginText)){_hasMargin=false;Touch();return;}
        if(!TryDec(_marginText,out var margin)||margin is < -100 or > 1000){Touch();return;}
        _hasMargin=true;_margin=margin;
        if(_hasPurchase&&_purchase>0){_net=QuotePricing.SaleNet(_purchase,margin);PublishGross(false);}
        else Touch();
    }
    private void ApplyGross()
    {
        if(!TryDec(_grossText,out var gross)||gross<0||!VatMath.IsSupportedRate(_vat)){Touch();return;}
        _gross=gross;_net=QuotePricing.NetFromGross(gross,_vat);Changed(nameof(Net));
        if(_hasPurchase&&_purchase>0&&QuotePricing.Margin(_purchase,_net) is {} margin){_hasMargin=true;_margin=margin;_marginText=margin.ToString("0.##",Inv);Changed(nameof(Margin));}
        Changed(nameof(LineGross));Touch();
    }
    private void ApplyVat()
    {
        if(!TryDec(_vatText,out var vat)||!VatMath.IsSupportedRate(vat)){Touch();return;}
        _vat=vat;PublishGross(false);
    }
    private void PublishGross(bool rewriteMargin)
    {
        _gross=QuotePricing.GrossFromNet(_net,_vat);_grossText=_gross.ToString("0.00",Inv);Changed(nameof(Gross));Changed(nameof(Net));Changed(nameof(LineGross));
        if(rewriteMargin&&_hasPurchase&&_purchase>0&&QuotePricing.Margin(_purchase,_net) is {} margin){_hasMargin=true;_margin=margin;_marginText=margin.ToString("0.##",Inv);Changed(nameof(Margin));}
        Touch();
    }
    private void Touch(){if(!_sync)_changed();}
    private static bool TryDec(string text,out decimal value)=>decimal.TryParse(text.Trim().Replace(',','.'),NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,Inv,out value);
}
