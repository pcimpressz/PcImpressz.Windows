using System.Text.Json;
using PcImpressz.Contracts;
namespace PcImpressz.Core.Storage;
public sealed partial class CompanyDatabase
{
    private sealed partial class Session
    {
        public void EnsureOrderSchema()
        {
            using var v=Command("PRAGMA user_version");if(Convert.ToInt32(v.ExecuteScalar())>=3)return;
            Run("""
                CREATE TABLE stock_reservations(document_id TEXT PRIMARY KEY,source_id TEXT NOT NULL,product_id TEXT NOT NULL,warehouse TEXT NOT NULL,
                    quantity INTEGER NOT NULL CHECK(typeof(quantity)='integer' AND quantity>0),expires_at INTEGER NOT NULL);
                CREATE INDEX reservation_product ON stock_reservations(product_id,warehouse,expires_at);
                CREATE INDEX reservation_source ON stock_reservations(source_id);
                CREATE TABLE customer_orders(id TEXT PRIMARY KEY,body TEXT NOT NULL CHECK(json_valid(body)),search TEXT NOT NULL,status TEXT NOT NULL,updated_at INTEGER NOT NULL);
                CREATE INDEX orders_status_updated ON customer_orders(status,updated_at DESC,id);
                CREATE INDEX orders_updated ON customer_orders(updated_at DESC,id);
                CREATE VIRTUAL TABLE order_fts USING fts5(search,content='customer_orders',content_rowid='rowid',tokenize='trigram');
                CREATE TRIGGER order_ai AFTER INSERT ON customer_orders BEGIN INSERT INTO order_fts(rowid,search) VALUES(new.rowid,new.search); END;
                CREATE TRIGGER order_ad AFTER DELETE ON customer_orders BEGIN INSERT INTO order_fts(order_fts,rowid,search) VALUES('delete',old.rowid,old.search); END;
                CREATE TRIGGER order_au AFTER UPDATE ON customer_orders BEGIN INSERT INTO order_fts(order_fts,rowid,search) VALUES('delete',old.rowid,old.search);INSERT INTO order_fts(rowid,search) VALUES(new.rowid,new.search); END;
                """);
            foreach(var json in List("inventory","reservation/")){var r=JsonSerializer.Deserialize<InventoryReservation>(json,CompanyDocuments.Json)!;IndexReservation("reservation/"+r.SourceId.ToString("N")+"/"+r.LineId.ToString("N"),json);}
            foreach(var json in List("sales","order/")){var o=JsonSerializer.Deserialize<CustomerOrder>(json,CompanyDocuments.Json)!;IndexOrder("order/"+o.Id.ToString("N"),json);}
            Run("PRAGMA user_version=3");
        }
        private void IndexReservation(string key,string json)
        {
            var r=JsonSerializer.Deserialize<InventoryReservation>(json,CompanyDocuments.Json)??throw new ArgumentException("Hiányzó foglalás.");
            if(r.Quantity<=0||VatMath.Round(r.Quantity,4)!=r.Quantity)throw new ArgumentException("Érvénytelen foglalási mennyiség.");
            using var c=Command("INSERT INTO stock_reservations(document_id,source_id,product_id,warehouse,quantity,expires_at) VALUES($key,$source,$product,$warehouse,$quantity,$expires) ON CONFLICT(document_id) DO UPDATE SET source_id=excluded.source_id,product_id=excluded.product_id,warehouse=excluded.warehouse,quantity=excluded.quantity,expires_at=excluded.expires_at");
            c.Parameters.AddWithValue("$key",key);c.Parameters.AddWithValue("$source",r.SourceId.ToString());c.Parameters.AddWithValue("$product",r.ProductId.ToString());c.Parameters.AddWithValue("$warehouse",r.Warehouse);c.Parameters.AddWithValue("$quantity",Fixed(r.Quantity,10000));c.Parameters.AddWithValue("$expires",r.ExpiresAt.ToUnixTimeSeconds());c.ExecuteNonQuery();
        }
        public decimal ReservedQuantity(Guid productId,string warehouse,DateTimeOffset at)
        {
            using var c=Command("SELECT COALESCE(SUM(quantity),0) FROM stock_reservations WHERE product_id=$id AND warehouse=$warehouse AND expires_at>$now");
            c.Parameters.AddWithValue("$id",productId.ToString());c.Parameters.AddWithValue("$warehouse",warehouse);c.Parameters.AddWithValue("$now",at.ToUnixTimeSeconds());return Convert.ToInt64(c.ExecuteScalar())/10000m;
        }
        private void IndexOrder(string key,string json)
        {
            var o=JsonSerializer.Deserialize<CustomerOrder>(json,CompanyDocuments.Json)??throw new ArgumentException("Hiányzó rendelés.");
            using var c=Command("INSERT INTO customer_orders(id,body,search,status,updated_at) VALUES($id,$body,$search,$status,$updated) ON CONFLICT(id) DO UPDATE SET body=excluded.body,search=excluded.search,status=excluded.status,updated_at=excluded.updated_at");
            c.Parameters.AddWithValue("$id",key);c.Parameters.AddWithValue("$body",json);c.Parameters.AddWithValue("$search",SearchText.Fold(o.Number+" "+o.CustomerName+" "+o.Notes));c.Parameters.AddWithValue("$status",o.Status);c.Parameters.AddWithValue("$updated",o.UpdatedAt.UtcTicks);c.ExecuteNonQuery();
        }
        public OrderIndexPage Orders(OrderQuery query,DateTimeOffset at)
        {
            var search=SearchText.Fold(query.Search);var where=query.Status=="all"?"1":query.Status=="open"?"status IN ('open','reserved','partial')":"status=$status";
            if(query.Status=="reserved")where+=" AND COALESCE(unixepoch(json_extract(body,'$.reservationUntil')),0)>$now";
            if(search.Length>0)where+=" AND instr(search,$search)>0";
            if(search.Length>=3)where+=" AND rowid IN(SELECT rowid FROM order_fts WHERE order_fts MATCH $fts)";
            void Bind(Microsoft.Data.Sqlite.SqliteCommand c){c.Parameters.AddWithValue("$now",at.ToUnixTimeSeconds());c.Parameters.AddWithValue("$status",query.Status);c.Parameters.AddWithValue("$search",search);c.Parameters.AddWithValue("$fts","\""+search.Replace("\"","\"\"")+"\"");}
            using var count=Command("SELECT COUNT(*) FROM customer_orders WHERE "+where);Bind(count);var total=Convert.ToInt32(count.ExecuteScalar());
            using var page=Command("SELECT body FROM customer_orders WHERE "+where+" ORDER BY updated_at DESC,id LIMIT $take OFFSET $skip");Bind(page);page.Parameters.AddWithValue("$take",query.PageSize);page.Parameters.AddWithValue("$skip",(long)query.Page*query.PageSize);
            using var r=page.ExecuteReader();var rows=new List<string>();while(r.Read())rows.Add(r.GetString(0));return new(rows.ToArray(),total);
        }
    }
}
