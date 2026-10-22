using System.Text;
using System.Text.Json;
using PcImpressz.Contracts;

namespace PcImpressz.Modules.Receiving;

public sealed record ManualInvoiceDraft(Guid Id,long Version,JsonElement Data,DateTimeOffset UpdatedAt,string Actor);
public sealed record ManualDraftSave(Guid CompanyId,Guid Id,long Version,JsonElement Data);
public sealed record ManualDraftRequest(Guid CompanyId,Guid Id,long Version=0);

// Partial forms are documents, not receipts: they may lack supplier, number and prices.
public sealed class ManualInvoiceDraftStore(ICompanyDatabase database,TimeProvider time)
{
    private static string Key(Guid id)=>id!=Guid.Empty?"manual-draft/"+id.ToString("N"):throw new ArgumentException("Hiányzó piszkozat-azonosító.");
    public ManualInvoiceDraft[] List(Guid company)=>database.Execute(company,tx=>tx.List("receiving","manual-draft/").Select(body=>JsonSerializer.Deserialize<ManualInvoiceDraft>(body,CompanyDocuments.Json)!).OrderByDescending(d=>d.UpdatedAt).ToArray());
    public ManualInvoiceDraft Read(ManualDraftRequest request)=>database.Execute(request.CompanyId,tx=>tx.Read<ManualInvoiceDraft>("receiving",Key(request.Id))??throw new ArgumentException("A piszkozat már nem található."));
    public ManualInvoiceDraft Save(ManualDraftSave request,string actor)=>database.Execute(request.CompanyId,tx=>
    {
        if(request.Data.ValueKind!=JsonValueKind.Object||Encoding.UTF8.GetByteCount(request.Data.GetRawText())>4_000_000)throw new ArgumentException("Túl nagy vagy hibás piszkozat.");
        var key=Key(request.Id);var old=tx.Read<ManualInvoiceDraft>("receiving",key);
        if((old?.Version??0)!=request.Version)throw new ReceivingConflictException("A piszkozat közben módosult. A jelenlegi szerkesztést nem írtuk felül; nyisd meg újra a mentett piszkozatot.");
        var saved=new ManualInvoiceDraft(request.Id,request.Version+1,request.Data.Clone(),time.GetUtcNow(),actor);tx.Write("receiving",key,saved);return saved;
    });
    public void Delete(ManualDraftRequest request)=>database.Execute(request.CompanyId,tx=>
    {
        var key=Key(request.Id);var old=tx.Read<ManualInvoiceDraft>("receiving",key);
        if(old is null)return false;
        if(old.Version!=request.Version)throw new ReceivingConflictException("A piszkozat közben módosult; frissítsd a listát a törlés előtt.");
        tx.Delete("receiving",key);return true;
    });
}
