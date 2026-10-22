using System.Globalization;

namespace PcImpressz.Modules.Receiving;

public static class ReceiptFilters
{
    private static readonly HashSet<string> Columns=["number","supplierName","supplierTaxId","issuedOn","sourceLabel","statusLabel","net","gross"];
    public static void Validate(ReceiptQuery query)
    {
        if(query.Supplier?.Length>200||query.Columns?.Count>Columns.Count||query.Columns?.Any(c=>!Columns.Contains(c.Key)||c.Value is null||c.Value.Length>100||c.Value.Any(char.IsControl))==true)throw new ArgumentException("Érvénytelen számlaoszlop-szűrő.");
        static DateOnly? Day(string? value)=>string.IsNullOrWhiteSpace(value)?null:DateOnly.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)?date:throw new ArgumentException("A dátumszűrő éééé-hh-nn alakú legyen.");
        var from=Day(query.DateFrom);var to=Day(query.DateTo);
        if(from>to||query.MinGross>query.MaxGross||query.MinGross is < -1_000_000_000_000_000m or > 1_000_000_000_000_000m||query.MaxGross is < -1_000_000_000_000_000m or > 1_000_000_000_000_000m)throw new ArgumentException("A szűrés alsó határa nem lehet nagyobb a felsőnél.");
    }
    private static bool Contains(string value,string? text)=>string.IsNullOrWhiteSpace(text)||ReceivingText.Fold(value).Contains(ReceivingText.Fold(text.Trim()),StringComparison.Ordinal);
    public static bool Matches(ReceiptData data,ReceiptQuery query)
    {
        if(!Contains(data.SupplierName+" "+data.SupplierTaxId,query.Supplier)||query.DateFrom is {Length:>0} from&&string.CompareOrdinal(data.IssuedOn,from)<0||query.DateTo is {Length:>0} to&&string.CompareOrdinal(data.IssuedOn,to)>0||data.Gross<query.MinGross||data.Gross>query.MaxGross)return false;
        if(query.Columns is null)return true;
        foreach(var (id,text) in query.Columns)
        {
            var value=id switch{"number"=>data.Number,"supplierName"=>data.SupplierName,"supplierTaxId"=>data.SupplierTaxId,"issuedOn"=>data.IssuedOn,"sourceLabel"=>ReceivingText.SourceLabel(data.Source),"statusLabel"=>ReceivingText.StatusLabel(data.Status),"net"=>data.Net.ToString(CultureInfo.InvariantCulture),"gross"=>data.Gross.ToString(CultureInfo.InvariantCulture),_=>""};
            if(!Contains(value,id is "net" or "gross"?text.Replace(" ","").Replace(',','.'):text))return false;
        }
        return true;
    }
}
