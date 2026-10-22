using System.Text.Json;
using PcImpressz.Contracts;
namespace PcImpressz.Modules.Catalog;
public sealed partial class CatalogRepository
{
    public ProductIndexPage? IndexedProducts(CatalogQuery query,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();if(database is null)return null;
        var search=new ProductSearch(query.Search);
        if(query.MinPrice>query.MaxPrice||query.MinStock>query.MaxStock)throw new ArgumentException("Érvénytelen szűrés.");
        var offset=(int)Math.Min(int.MaxValue,(long)query.Page*query.PageSize);
        return database.Read(query.CompanyId,tx=>tx is IIndexedCompanyTransaction index
            ? index.Products(new(search.Terms,query.Filter,query.Sort,query.Descending,offset,query.PageSize,query.MinPrice,query.MaxPrice,query.MinStock,query.MaxStock,query.CategoryCodes,query.NoCategoryMatch)) : null);
    }
    public async Task<ProductRecord?> GetProduct(Guid company,Guid id,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        if(database is not null)
        {
            var result=database.Read(company,tx=>(Indexed:tx is IIndexedCompanyTransaction,Record:(tx as IIndexedCompanyTransaction)?.Product(id)));
            if(result.Indexed)return result.Record is null?null:JsonSerializer.Deserialize<ProductRecord>(result.Record,CatalogJson.Options);
        }
        return (await Snapshot(company,ct)).Products.SingleOrDefault(p=>p.Id==id);
    }
    private Task<ProductRecord> SaveIndexedOrLegacy(ProductSave request,string actor,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(database is not null)
        {
            var saved=database.Execute(request.CompanyId,tx=>
            {
                if(tx is not IIndexedCompanyTransaction index||index.CatalogMetadata() is not {} metadata)return null;
                var state=JsonSerializer.Deserialize<CatalogState>(metadata,CatalogJson.Options)??throw new IOException("Sérült terméktörzs.");
                var data=CatalogValidation.NormalizeProduct(request.Data??throw new ArgumentException("Hiányzó termékadat."));
                var existing=request.Id is Guid id&&index.Product(id) is {} body?JsonSerializer.Deserialize<ProductRecord>(body,CatalogJson.Options):null;
                if(request.Id is null&&request.Version!=0||request.Id is not null&&(existing is null||existing.Version!=request.Version))throw new CatalogConflictException("A rekord közben módosult vagy nem található. Frissítsd a listát.");
                if(data.SalePrices is null)data=data with{SalePrices=CatalogValidation.NormalizeSalePrices(existing?.Data.SalePrices)??[]};
                EnsureProductLinks(state,data);
                if(existing is not null&&existing.Data.UnitCode!=data.UnitCode&&index.HasStockHistory(existing.Id))throw new ArgumentException("Készletmozgással rendelkező termék mértékegysége nem módosítható.");
                if(data.IsActive&&index.ActiveSkuExists(data.Sku,existing?.Id))throw new ArgumentException("Ezzel a cikkszámmal már létezik aktív termék.");
                var now=time.GetUtcNow();var record=new ProductRecord(existing?.Id??Guid.NewGuid(),(existing?.Version??0)+1,data,existing?.CreatedAt??now,existing?.CreatedBy??actor,now,actor,existing?.Source,existing?.ExternalId);
                var audit=new CatalogAudit("product",record.Id,existing is null?"create":"update",record.Version,now,actor);
                index.SaveProduct(JsonSerializer.Serialize(record,CatalogJson.Options),JsonSerializer.Serialize(audit,CatalogJson.Options));return record;
            });
            if(saved is not null)return Task.FromResult(saved);
        }
        return Locked(request.CompanyId,(scope,state)=>WriteProduct(scope,state,request,actor),ct);
    }
}
