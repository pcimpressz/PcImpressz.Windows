using PcImpressz.Contracts;

namespace PcImpressz.Modules.Receiving;

public sealed record InvoiceBlock(Guid Id,string Name,string Prefix,long StartNumber);
public sealed record InvoicingSettings(string Provider,Guid? DefaultBlockId,InvoiceBlock[] Blocks);
public sealed record InvoicingSettingsSave(Guid CompanyId,InvoicingSettings Settings,string? ApiKey=null,bool TestMode=true);

public sealed class InvoicingSettingsStore(ICompanyDatabase database)
{
    public static InvoicingSettings Defaults=>new("bizonylat",null,[]);
    public InvoicingSettings Read(Guid company)=>database.Execute(company,tx=>tx.Read<InvoicingSettings>("receiving","invoicing-settings")??Defaults);
    public InvoicingSettings Save(Guid company,InvoicingSettings settings)
    {
        if(settings.Provider is not ("bizonylat" or "szamlazz" or "billingo"))throw new ArgumentException("Ismeretlen számlázó szolgáltató.");
        if(settings.Blocks is null||settings.Blocks.Length>20||settings.Blocks.Select(b=>b.Id).Distinct().Count()!=settings.Blocks.Length)throw new ArgumentException("Legfeljebb 20 különböző számlatömb lehet.");
        if(settings.DefaultBlockId is {} chosen&&!settings.Blocks.Any(b=>b.Id==chosen))throw new ArgumentException("Az alapértelmezett számlatömb nem található.");
        var blocks=settings.Blocks.Select(b=>b with{Name=b.Name.Trim(),Prefix=b.Prefix.Trim().ToUpperInvariant()}).ToArray();
        if(blocks.Any(b=>b.Id==Guid.Empty||b.Name.Length is <1 or >80||b.Prefix.Length is <1 or >20||b.Prefix.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'&&c!='_')||b.StartNumber is <1 or >999999999))throw new ArgumentException("A tömbhöz név, 1–20 betűből/számból álló előtag és 1–999999999 kezdősorszám kell.");
        if(blocks.Select(b=>b.Prefix).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=blocks.Length)throw new ArgumentException("A számlatömbök előtagjai legyenek különbözőek.");
        var saved=settings with{Blocks=blocks};
        return database.Execute(company,tx=>
        {
            var old=tx.Read<InvoicingSettings>("receiving","invoicing-settings");
            if(old is not null&&tx.List("receiving","outgoing-invoice/").Any())
                foreach(var used in old.Blocks)
                    if(!blocks.Any(b=>b.Id==used.Id&&b.Prefix==used.Prefix&&b.StartNumber==used.StartNumber))throw new ArgumentException("Kiállított számlák mellett a meglévő tömb előtagja és kezdősorszáma már nem módosítható vagy törölhető.");
            tx.Write("receiving","invoicing-settings",saved);return saved;
        });
    }
}
