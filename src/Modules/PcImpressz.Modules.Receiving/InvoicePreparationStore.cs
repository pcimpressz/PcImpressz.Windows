using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PcImpressz.Contracts;

namespace PcImpressz.Modules.Receiving;
public sealed record InvoicePreparationRequest(Guid CompanyId,string Number,string SupplierTaxNumber);
public sealed record InvoicePreparationSave(Guid CompanyId,string Number,string SupplierTaxNumber,long Version,JsonElement Data);
public sealed record InvoicePreparation(long Version,JsonElement Data,DateTimeOffset At,string Actor);
public sealed class InvoicePreparationStore(ICompanyDatabase database,TimeProvider time)
{
    private static string Key(string number,string tax)
    {
        if(string.IsNullOrWhiteSpace(number)||number.Length>80||number.Any(char.IsControl)||ReceivingText.TaxKey(tax).Length!=8)throw new ArgumentException("Érvénytelen számlaazonosító.");
        return "preparation/"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ReceivingText.TaxKey(tax)+"\n"+number.Trim())));
    }
    public InvoicePreparation? Read(InvoicePreparationRequest request)=>database.Execute(request.CompanyId,tx=>tx.Read<InvoicePreparation>("receiving",Key(request.Number,request.SupplierTaxNumber)));
    public InvoicePreparation Save(InvoicePreparationSave request,string actor)=>database.Execute(request.CompanyId,tx=>
    {
        var key=Key(request.Number,request.SupplierTaxNumber);
        if(request.Data.ValueKind!=JsonValueKind.Object||Encoding.UTF8.GetByteCount(request.Data.GetRawText())>4_000_000)throw new ArgumentException("Túl nagy vagy hibás számla-előkészítés.");
        var old=tx.Read<InvoicePreparation>("receiving",key);
        if((old?.Version??0)!=request.Version)throw new ReceivingConflictException("Másik munkatárs közben módosította az előkészítést. Nyisd meg újra a számlát; a jelenlegi módosításaid még nincsenek mentve.");
        var value=new InvoicePreparation((old?.Version??0)+1,request.Data.Clone(),time.GetUtcNow(),actor);tx.Write("receiving",key,value);return value;
    });
}
