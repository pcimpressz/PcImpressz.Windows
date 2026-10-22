using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PcImpressz.Contracts;

namespace PcImpressz.Core.Storage;
public sealed partial class CompanyDatabase
{
    private sealed partial class Session
    {
        private void Run(string sql){using var c=Command(sql);c.ExecuteNonQuery();}
        public void EnsureIndexes()
        {
            using var v=Command("PRAGMA user_version");
            if(Convert.ToInt32(v.ExecuteScalar())>=2)return;
            Run("""
                CREATE TABLE catalog_products(id TEXT PRIMARY KEY, body TEXT NOT NULL CHECK(json_valid(body)),
                    name TEXT NOT NULL,sku TEXT NOT NULL,mpn TEXT NOT NULL,barcode TEXT NOT NULL,category TEXT NOT NULL,
                    unit TEXT NOT NULL,revision TEXT NOT NULL,search TEXT NOT NULL,active INTEGER NOT NULL,
                    purchaseable INTEGER NOT NULL,salable INTEGER NOT NULL,net INTEGER,gross INTEGER);
                CREATE UNIQUE INDEX catalog_sku_active ON catalog_products(sku) WHERE active=1;
                CREATE INDEX catalog_name ON catalog_products(active,name COLLATE HU,id);
                CREATE INDEX catalog_barcode ON catalog_products(barcode);
                CREATE INDEX catalog_mpn ON catalog_products(mpn);
                CREATE INDEX catalog_category ON catalog_products(active,category);
                CREATE INDEX catalog_price ON catalog_products(active,net);
                CREATE VIRTUAL TABLE catalog_fts USING fts5(search,content='catalog_products',content_rowid='rowid',tokenize='trigram');
                CREATE TRIGGER catalog_ai AFTER INSERT ON catalog_products BEGIN INSERT INTO catalog_fts(rowid,search) VALUES(new.rowid,new.search); END;
                CREATE TRIGGER catalog_ad AFTER DELETE ON catalog_products BEGIN INSERT INTO catalog_fts(catalog_fts,rowid,search) VALUES('delete',old.rowid,old.search); END;
                CREATE TRIGGER catalog_au AFTER UPDATE ON catalog_products BEGIN
                  INSERT INTO catalog_fts(catalog_fts,rowid,search) VALUES('delete',old.rowid,old.search);
                  INSERT INTO catalog_fts(rowid,search) VALUES(new.rowid,new.search); END;
                CREATE TABLE catalog_audit(sequence INTEGER PRIMARY KEY,body TEXT NOT NULL CHECK(json_valid(body)));
                CREATE TABLE stock_balances(product_id TEXT NOT NULL,warehouse TEXT NOT NULL,name TEXT NOT NULL,unit TEXT NOT NULL,
                    quantity INTEGER NOT NULL CHECK(typeof(quantity)='integer'),PRIMARY KEY(product_id,warehouse));
                CREATE TABLE stock_history(product_id TEXT PRIMARY KEY);
                """);
            using(var c=Command("SELECT body FROM documents WHERE module='catalog' AND id='state'"))
                if(c.ExecuteScalar() is string old)ReplaceCatalog(old);
            // Preserve the legacy id order so displayed names agree with the previous ledger projection.
            foreach(var json in List("inventory","movement/"))ProjectStock(json);
            Run("PRAGMA user_version=2");
        }
        public string? CatalogMetadata()
        {using var c=Command("SELECT body FROM documents WHERE module='catalog' AND id='state'");return c.ExecuteScalar() as string;}
        private string? CatalogSnapshot()
        {
            var body=CatalogMetadata();if(body is null)return null;
            var node=JsonNode.Parse(body)!.AsObject();
            // Old imports can still seed a full snapshot; ReplaceCatalog splits it atomically.
            node["products"]=new JsonArray(AllProducts().Select(x=>JsonNode.Parse(x)).ToArray());
            using var c=Command("SELECT body FROM catalog_audit ORDER BY sequence");using var r=c.ExecuteReader();var audit=new JsonArray();
            while(r.Read())audit.Add(JsonNode.Parse(r.GetString(0)));node["audit"]=audit;return node.ToJsonString();
        }
        private string[] AllProducts()
        {using var c=Command("SELECT body FROM catalog_products ORDER BY rowid");using var r=c.ExecuteReader();var rows=new List<string>();while(r.Read())rows.Add(r.GetString(0));return rows.ToArray();}
        private void ReplaceCatalog(string body)
        {
            using var doc=JsonDocument.Parse(body);var root=doc.RootElement;
            if(System.Text.Encoding.UTF8.GetByteCount(body)>64*1024*1024)throw new IOException("Az adattár túl nagy.");
            Run("DELETE FROM catalog_products; DELETE FROM catalog_audit;");
            foreach(var row in root.GetProperty("products").EnumerateArray())PutProduct(row.GetRawText());
            foreach(var row in root.GetProperty("audit").EnumerateArray())AppendAudit(row.GetRawText());
            var node=JsonNode.Parse(body)!.AsObject();node["products"]=new JsonArray();node["audit"]=new JsonArray();
            using var c=Command("INSERT INTO documents(module,id,body) VALUES('catalog','state',$body) ON CONFLICT(module,id) DO UPDATE SET body=excluded.body");
            c.Parameters.AddWithValue("$body",node.ToJsonString());c.ExecuteNonQuery();
        }
        public string? Product(Guid id)
        {using var c=Command("SELECT body FROM catalog_products WHERE id=$id");c.Parameters.AddWithValue("$id",id.ToString());return c.ExecuteScalar() as string;}
        public bool ActiveSkuExists(string sku,Guid? except)
        {using var c=Command("SELECT EXISTS(SELECT 1 FROM catalog_products WHERE active=1 AND sku=$sku AND id<>$id)");c.Parameters.AddWithValue("$sku",SearchText.Fold(sku));c.Parameters.AddWithValue("$id",except?.ToString()??"");return Convert.ToInt32(c.ExecuteScalar())!=0;}
        private void AppendAudit(string body)
        {using var c=Command("INSERT INTO catalog_audit(body) VALUES($body)");c.Parameters.AddWithValue("$body",body);c.ExecuteNonQuery();}
        public void SaveProduct(string body,string audit)
        {
            PutProduct(body);AppendAudit(audit);
            Run("UPDATE documents SET body=json_set(body,'$.revision',json_extract(body,'$.revision')+1) WHERE module='catalog' AND id='state'");
        }
        private static long Fixed(decimal value,int scale)=>checked((long)(value*scale));
        private void PutProduct(string body)
        {
            using var doc=JsonDocument.Parse(body);var root=doc.RootElement;var d=root.GetProperty("data");
            string Text(string key)=>d.TryGetProperty(key,out var value)?value.GetString()??"":"";
            bool Flag(string key)=>d.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.True;
            decimal? net=d.TryGetProperty("saleNetUnitPrice",out var price)&&price.ValueKind==JsonValueKind.Number?price.GetDecimal():null;
            var rate=d.TryGetProperty("saleVatRate",out var vat)?vat.GetDecimal():27m;
            using var c=Command("""
                INSERT INTO catalog_products(id,body,name,sku,mpn,barcode,category,unit,revision,search,active,purchaseable,salable,net,gross)
                VALUES($id,$body,$name,$sku,$mpn,$barcode,$category,$unit,$revision,$search,$active,$purchaseable,$salable,$net,$gross)
                ON CONFLICT(id) DO UPDATE SET body=excluded.body,name=excluded.name,sku=excluded.sku,mpn=excluded.mpn,barcode=excluded.barcode,
                    category=excluded.category,unit=excluded.unit,revision=excluded.revision,search=excluded.search,active=excluded.active,
                    purchaseable=excluded.purchaseable,salable=excluded.salable,net=excluded.net,gross=excluded.gross
                """);
            c.Parameters.AddWithValue("$id",root.GetProperty("id").GetGuid().ToString());c.Parameters.AddWithValue("$body",body);
            c.Parameters.AddWithValue("$name",Text("name"));
            foreach(var (column,key) in new[]{("sku","sku"),("mpn","manufacturerPartNumber"),("barcode","barcode"),("category","categoryCode"),("unit","unitCode"),("revision","revision")})c.Parameters.AddWithValue("$"+column,SearchText.Fold(Text(key)));
            c.Parameters.AddWithValue("$search",string.Join('\n',new[]{"name","sku","manufacturerPartNumber","barcode","categoryCode","unitCode","description","parameters"}.Select(k=>SearchText.Fold(Text(k)))));
            c.Parameters.AddWithValue("$active",Flag("isActive")?1:0);c.Parameters.AddWithValue("$purchaseable",Flag("isPurchaseable")?1:0);c.Parameters.AddWithValue("$salable",Flag("isSalable")?1:0);
            c.Parameters.AddWithValue("$net",net is decimal n?Fixed(n,10000):DBNull.Value);
            c.Parameters.AddWithValue("$gross",net is decimal g?Fixed(VatMath.Round(g*(1+rate/100m)),10000):DBNull.Value);c.ExecuteNonQuery();
        }
        public ProductIndexPage Products(ProductIndexQuery q)
        {
            if(q.Offset<0||q.Limit is <1 or >200||q.Terms.Length>12||q.Categories is {Length:>40})throw new ArgumentException("Érvénytelen indexelt lekérdezés.");
            var conditions=new List<string>{"p.active="+(q.Filter=="archived"?"0":"1")};var args=new List<(string,object)>();
            string Param(object value){var name="$p"+args.Count;args.Add((name,value));return name;}
            if(q.NoCategoryMatch)conditions.Add("0");
            if(q.Filter=="purchaseable")conditions.Add("p.purchaseable=1");if(q.Filter=="salable")conditions.Add("p.salable=1");
            if(q.Categories is {Length:>0})conditions.Add("p.category IN ("+string.Join(',',q.Categories.Select(x=>Param(SearchText.Fold(x))))+")");
            if(q.MinPrice is decimal min)conditions.Add("p.net >= "+Param(checked((long)decimal.Ceiling(min*10000))));
            if(q.MaxPrice is decimal max)conditions.Add("p.net <= "+Param(checked((long)decimal.Floor(max*10000))));
            var quantity="COALESCE((SELECT SUM(b.quantity) FROM stock_balances b WHERE b.product_id=p.id),0)";
            if(q.Filter=="in-stock")conditions.Add(quantity+">0");if(q.Filter=="out-of-stock")conditions.Add(quantity+"<=0");
            if(q.MinStock is decimal mins)conditions.Add(quantity+">="+Param(checked((long)decimal.Ceiling(mins*10000))));
            if(q.MaxStock is decimal maxs)conditions.Add(quantity+"<="+Param(checked((long)decimal.Floor(maxs*10000))));
            foreach(var term in q.Terms)
            {
                var column=term.Field switch{"" or "checkout"=>"p.search","nev"=>"json_extract(p.body,'$.data.name')","sku" or "cikkszam"=>"p.sku","mpn" or "gyartoi"=>"p.mpn","vonalkod" or "ean"=>"p.barcode","kategoria"=>"p.category","egyseg"=>"p.unit","revizio"=>"p.revision",_=>throw new ArgumentException("Ismeretlen keresési mező.")};
                // All searchable fields are normalized, including names via a registered deterministic function.
                if(term.Field=="nev")column="fold("+column+")";
                if(term.Field is "" or "checkout")
                {
                    var columns=new[]{"fold(p.name)","p.sku","p.mpn","p.barcode","p.category","p.unit","fold(json_extract(p.body,'$.data.description'))","fold(json_extract(p.body,'$.data.parameters'))"};
                    if(term.Field=="checkout")columns=columns.Take(4).ToArray();
                    var v=Param(term.Value);conditions.Add("("+string.Join(" OR ",columns.Select(x=>term.Exact?x+"="+v:"instr("+x+","+v+")>0"))+")");
                }
                else {var v=Param(term.Value);conditions.Add(term.Exact?column+"="+v:"instr("+column+","+v+")>0");}
                if(term.Value.Length>=3&&term.Field!="revizio")
                {var fts=Param("\""+term.Value.Replace("\"","\"\"")+"\"");conditions.Add("p.rowid IN (SELECT rowid FROM catalog_fts WHERE catalog_fts MATCH "+fts+")");}
            }
            var from=" FROM catalog_products p WHERE "+string.Join(" AND ",conditions);
            string order=q.Sort switch{"" or "name"=>"p.name COLLATE HU","sku"=>"p.sku COLLATE HU","manufacturerPartNumber"=>"p.mpn COLLATE HU","categoryCode"=>"p.category COLLATE HU","unitCode"=>"p.unit COLLATE HU","revision"=>"p.revision COLLATE HU","barcode"=>"p.barcode COLLATE HU","quantity"=>quantity,"saleNet"=>"COALESCE(p.net,-10000)","saleGross"=>"COALESCE(p.gross,-10000)","isPurchaseable"=>"CASE p.purchaseable WHEN 1 THEN 'Igen' ELSE 'Nem' END COLLATE HU","isSalable"=>"CASE p.salable WHEN 1 THEN 'Igen' ELSE 'Nem' END COLLATE HU",_=>"p.name COLLATE HU"};
            order+=q.Descending&&q.Sort.Length>0?" DESC":" ASC";
            if(q.Sort.Length==0&&q.Terms.Length==1){var exact=Param(q.Terms[0].Value);order="CASE WHEN p.sku="+exact+" OR p.mpn="+exact+" OR p.barcode="+exact+" THEN 0 ELSE 1 END,"+order;}
            void Bind(Microsoft.Data.Sqlite.SqliteCommand c){foreach(var (key,value) in args)c.Parameters.AddWithValue(key,value);}
            using var count=Command("SELECT COUNT(*)"+from);Bind(count);var total=Convert.ToInt32(count.ExecuteScalar());
            using var page=Command("SELECT p.id,p.body,"+quantity+from+" ORDER BY "+order+",p.id LIMIT $limit OFFSET $offset");Bind(page);page.Parameters.AddWithValue("$limit",q.Limit);page.Parameters.AddWithValue("$offset",q.Offset);
            using var reader=page.ExecuteReader();var rows=new List<string>();var quantities=new Dictionary<Guid,decimal>();
            while(reader.Read()){rows.Add(reader.GetString(1));quantities.Add(Guid.Parse(reader.GetString(0)),reader.GetInt64(2)/10000m);}return new(rows.ToArray(),total,quantities);
        }
        private void UpdateStock(string key,string json)
        {
            using var c=Command("SELECT body FROM documents WHERE module='inventory' AND id=$id");c.Parameters.AddWithValue("$id",key);
            if(c.ExecuteScalar() is string old){if(old!=json)throw new ArgumentException("A készletmozgás nem írható felül; használj ellenmozgást.");return;}
            ProjectStock(json);
        }
        private void ProjectStock(string json)
        {
            using var doc=JsonDocument.Parse(json);var d=doc.RootElement;var qty=d.GetProperty("quantity").GetDecimal();
            if(VatMath.Round(qty,4)!=qty)throw new ArgumentException("A készletmennyiség legfeljebb négy tizedes lehet.");
            using var c=Command("""
                INSERT INTO stock_balances(product_id,warehouse,name,unit,quantity) VALUES($id,$warehouse,$name,$unit,$q)
                ON CONFLICT(product_id,warehouse) DO UPDATE SET quantity=stock_balances.quantity+excluded.quantity,name=excluded.name,unit=excluded.unit;
                INSERT OR IGNORE INTO stock_history(product_id) VALUES($id);
                """);
            c.Parameters.AddWithValue("$id",d.GetProperty("productId").GetGuid().ToString());c.Parameters.AddWithValue("$warehouse",d.GetProperty("warehouse").GetString());c.Parameters.AddWithValue("$name",d.GetProperty("productName").GetString());c.Parameters.AddWithValue("$unit",d.GetProperty("unitCode").GetString());c.Parameters.AddWithValue("$q",Fixed(qty,10000));c.ExecuteNonQuery();
        }
        public IndexedStockBalance[] StockBalances(Guid? productId=null)
        {
            using var c=Command("SELECT product_id,name,unit,warehouse,quantity FROM stock_balances"+(productId is null?"":" WHERE product_id=$id"));
            if(productId is Guid id)c.Parameters.AddWithValue("$id",id.ToString());using var r=c.ExecuteReader();var rows=new List<IndexedStockBalance>();
            while(r.Read())rows.Add(new(Guid.Parse(r.GetString(0)),r.GetString(1),r.GetString(2),r.GetString(3),r.GetInt64(4)/10000m));return rows.ToArray();
        }
        public bool HasStockHistory(Guid id)
        {using var c=Command("SELECT EXISTS(SELECT 1 FROM stock_history WHERE product_id=$id)");c.Parameters.AddWithValue("$id",id.ToString());return Convert.ToInt32(c.ExecuteScalar())!=0;}
    }
}
