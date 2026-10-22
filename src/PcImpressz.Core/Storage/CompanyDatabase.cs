using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PcImpressz.Contracts;

namespace PcImpressz.Core.Storage;

public sealed partial class CompanyDatabase(Func<Guid, string> companyFolder) : ICompanyDatabase
{
    public const int SchemaVersion = 3;
    public string DatabasePath(Guid company) => Path.Combine(companyFolder(company), "company.db");
    public T Execute<T>(Guid companyId, Func<ICompanyTransaction,T> action)
    {
        using var connection=Open(companyId);
        PreserveBeforeMigration(connection, companyId);
        using var transaction=connection.BeginTransaction(deferred:false);
        var session=new Session(connection,transaction);
        EnsureSchema(session);
        try { MigrateLegacy(companyFolder(companyId),session); }
        catch(Exception ex) when(ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new IOException("A régi cégadat sérült vagy nem támogatott; a migráció visszagörgetve, az eredeti fájlok megmaradtak.",ex); }
        var result=action(session);
        transaction.Commit();
        _initialized[companyId]=true;
        return result;
    }
    private void PreserveBeforeMigration(SqliteConnection source,Guid company)
    {
        using var version=source.CreateCommand();version.CommandText="PRAGMA user_version";
        var old=Convert.ToInt32(version.ExecuteScalar());if(old is <1 or >=SchemaVersion)return;
        var folder=Path.Combine(companyFolder(company),".migration-backups");Directory.CreateDirectory(folder);RejectLinks(folder);
        var file=Path.Combine(folder,$"company-v{old}-before-v{SchemaVersion}.db");
        if(File.Exists(file)){ValidateBackup(file);return;}
        var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var target=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=temporary,Pooling=false}.ToString())){target.Open();source.BackupDatabase(target);}
            ValidateBackup(temporary);File.Move(temporary,file);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    private SqliteConnection Open(Guid company)
    {
        var folder=companyFolder(company);
        Directory.CreateDirectory(folder);
        RejectLinks(folder);
        var file=Path.Combine(folder,"company.db");
        if(File.Exists(file)) RejectLinks(file);
        var connection=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=file, Pooling=false, ForeignKeys=true, DefaultTimeout=10 }.ToString());
        try
        {
            connection.Open();
            connection.CreateFunction<string?,string>("fold",SearchText.Fold,isDeterministic:true);
            connection.CreateCollation("HU", (a,b) => System.Globalization.CultureInfo.GetCultureInfo("hu-HU").CompareInfo.Compare(a,b,System.Globalization.CompareOptions.IgnoreCase));
            using var cmd=connection.CreateCommand();
            cmd.CommandText="PRAGMA user_version";
            if(Convert.ToInt32(cmd.ExecuteScalar())>SchemaVersion) throw new IOException("Az adatbázis újabb programverzióval készült.");
            cmd.CommandText="PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=10000;";
            cmd.ExecuteNonQuery();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }
    public static void RejectLinks(string path)
    {
        for(var item=Path.GetFullPath(path); !string.IsNullOrEmpty(item); item=Path.GetDirectoryName(item))
            if((Directory.Exists(item)||File.Exists(item)) && (File.GetAttributes(item)&FileAttributes.ReparsePoint)!=0)
                throw new IOException("Az adatútvonal hivatkozást tartalmaz.");
    }
    public T Read<T>(Guid companyId, Func<ICompanyTransaction,T> action)
    {
        if (!_initialized.ContainsKey(companyId)) Execute(companyId, _ => true);
        using var connection=Open(companyId);
        using var guard=connection.CreateCommand();guard.CommandText="PRAGMA query_only=ON";guard.ExecuteNonQuery();
        using var transaction=connection.BeginTransaction(deferred:true);
        var result=action(new Session(connection,transaction));transaction.Commit();return result;
    }
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid,bool> _initialized=new();
    private static void EnsureSchema(Session session)
    {
        using var cmd=session.Command("PRAGMA user_version");
        var version=Convert.ToInt32(cmd.ExecuteScalar());
        if(version>SchemaVersion) throw new IOException("Az adatbázis újabb programverzióval készült; nem módosítható.");
        if(version==0)
        {
            using var create=session.Command("CREATE TABLE documents(module TEXT NOT NULL, id TEXT NOT NULL, body TEXT NOT NULL CHECK(json_valid(body)), PRIMARY KEY(module,id)); PRAGMA user_version=1;");
            create.ExecuteNonQuery();
        }
        session.EnsureIndexes();
        session.EnsureOrderSchema();
    }
    private static void MigrateLegacy(string folder,Session session)
    {
        if(session.Get("system","legacy-v1") is not null) return;
        foreach(var (module,file,arrays) in new[]{
            ("partners","partners.json",new[]{"partners","audit"}),
            ("catalog","catalog.json",new[]{"categories","units","products","audit"}),
            ("receiving","receipts.json",new[]{"receipts","audit"})})
        {
            var path=Path.Combine(folder,"modules",module,file);
            if(!File.Exists(path)) continue;
            RejectLinks(path);
            if(new FileInfo(path).Length>64*1024*1024) throw new IOException("A migrálandó adattár túl nagy.");
            var body=File.ReadAllText(path);
            using var doc=JsonDocument.Parse(body);
            var root=doc.RootElement;
            if(root.GetProperty("schemaVersion").GetInt32()!=1 || root.GetProperty("revision").GetInt64()<0
                || arrays.Any(a=>!root.TryGetProperty(a,out var v)||v.ValueKind!=JsonValueKind.Array))
                throw new IOException("Nem támogatott vagy sérült migrációs forrás: "+module);
            foreach(var array in arrays.Where(a=>a!="audit"))
            {
                var ids=new HashSet<Guid>();
                foreach(var row in root.GetProperty(array).EnumerateArray())
                    if(!row.TryGetProperty("id",out var id) || !id.TryGetGuid(out var guid) || guid==Guid.Empty || !ids.Add(guid)
                       || row.GetProperty("version").GetInt64()<1 || row.GetProperty("data").ValueKind!=JsonValueKind.Object)
                        throw new IOException("Sérült rekord a migrációs forrásban: "+module);
                if(module=="receiving" && root.GetProperty(array).EnumerateArray().Any(row=>row.GetProperty("data").GetProperty("lines").ValueKind!=JsonValueKind.Array))
                    throw new IOException("Hiányzó számlatételek a migrációs forrásban.");
            }
            session.Put(module,"state",body);
        }
        var dossier=Path.Combine(folder,"modules","partners","dossiers.json");
        if(File.Exists(dossier))
        {
            RejectLinks(dossier);
            if(new FileInfo(dossier).Length>32*1024*1024) throw new IOException("A dossziémigráció forrása túl nagy.");
            var body=File.ReadAllText(dossier);using var doc=JsonDocument.Parse(body);
            if(doc.RootElement.GetProperty("schemaVersion").GetInt32()!=1 || doc.RootElement.GetProperty("items").ValueKind!=JsonValueKind.Array)
                throw new IOException("Sérült dossziémigrációs forrás.");
            session.Put("partners","dossiers",body);
        }
        session.Write("system","legacy-v1",new { completedAt=DateTimeOffset.UtcNow });
    }
    public void Backup(Guid company,string destination)
    {
        Execute(company,_=>true);
        using var source=Open(company);
        using var target=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=destination, Pooling=false }.ToString());
        target.Open();
        target.CreateCollation("HU", (a,b) => System.Globalization.CultureInfo.GetCultureInfo("hu-HU").CompareInfo.Compare(a,b,System.Globalization.CompareOptions.IgnoreCase));
        source.BackupDatabase(target);
        using var check=target.CreateCommand(); check.CommandText="PRAGMA integrity_check";
        if(!Equals(check.ExecuteScalar(),"ok")) throw new IOException("A mentés integritásellenőrzése sikertelen.");
    }
    public static void ValidateBackup(string file)
    {
        using var c=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=file,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
        c.Open();c.CreateCollation("HU", (a,b) => System.Globalization.CultureInfo.GetCultureInfo("hu-HU").CompareInfo.Compare(a,b,System.Globalization.CompareOptions.IgnoreCase));using var cmd=c.CreateCommand();cmd.CommandText="PRAGMA integrity_check";
        if(!Equals(cmd.ExecuteScalar(),"ok")) throw new IOException("Sérült adatbázismentés.");
        cmd.CommandText="PRAGMA user_version";
        if(Convert.ToInt32(cmd.ExecuteScalar()) is <1 or >SchemaVersion) throw new IOException("Nem támogatott mentésverzió.");
        cmd.CommandText="SELECT count(*) FROM documents WHERE NOT json_valid(body)";
        if(Convert.ToInt64(cmd.ExecuteScalar())!=0) throw new IOException("Sérült dokumentum a mentésben.");
    }
    private sealed partial class Session(SqliteConnection connection,SqliteTransaction transaction):ICompanyTransaction, IIndexedCompanyTransaction
    {
        public SqliteCommand Command(string sql)
        {var c=connection.CreateCommand();c.Transaction=transaction;c.CommandText=sql;return c;}
        public string? Get(string module,string key)
        {if(module=="catalog"&&key=="state")return CatalogSnapshot();using var c=Command("SELECT body FROM documents WHERE module=$m AND id=$id");c.Parameters.AddWithValue("$m",module);c.Parameters.AddWithValue("$id",key);return c.ExecuteScalar() as string;}
        public void Put(string module,string key,string json)
        {
            if(module=="catalog"&&key=="state"){ReplaceCatalog(json);return;}
            if(module=="inventory"&&key.StartsWith("reservation/",StringComparison.Ordinal))IndexReservation(key,json);
            if(module=="sales"&&key.StartsWith("order/",StringComparison.Ordinal))IndexOrder(key,json);
            if(module=="inventory"&&key.StartsWith("movement/",StringComparison.Ordinal))UpdateStock(key,json);
            if(string.IsNullOrWhiteSpace(module)||string.IsNullOrWhiteSpace(key)||Encoding.UTF8.GetByteCount(json)>64*1024*1024) throw new ArgumentException("Érvénytelen dokumentum.");
            using var c=Command("INSERT INTO documents(module,id,body) VALUES($m,$id,$body) ON CONFLICT(module,id) DO UPDATE SET body=excluded.body");
            c.Parameters.AddWithValue("$m",module);c.Parameters.AddWithValue("$id",key);c.Parameters.AddWithValue("$body",json);c.ExecuteNonQuery();
        }
        public void Delete(string module,string key)
        {if(module=="inventory"&&key.StartsWith("reservation/",StringComparison.Ordinal)){using var r=Command("DELETE FROM stock_reservations WHERE document_id=$id");r.Parameters.AddWithValue("$id",key);r.ExecuteNonQuery();}
        if(module=="sales"&&key.StartsWith("order/",StringComparison.Ordinal))throw new ArgumentException("A rendelés nem törölhető; használd a lemondást.");
        if(module=="inventory"&&key.StartsWith("movement/",StringComparison.Ordinal))throw new ArgumentException("A készletmozgás nem törölhető; használj ellenmozgást.");using var c=Command("DELETE FROM documents WHERE module=$m AND id=$id");c.Parameters.AddWithValue("$m",module);c.Parameters.AddWithValue("$id",key);c.ExecuteNonQuery();}
        public IReadOnlyList<string> List(string module,string prefix)
        {using var c=Command("SELECT body FROM documents WHERE module=$m AND id >= $prefix AND id < $end ORDER BY id");c.Parameters.AddWithValue("$m",module);c.Parameters.AddWithValue("$prefix",prefix);c.Parameters.AddWithValue("$end",prefix+"\uffff");using var reader=c.ExecuteReader();var rows=new List<string>();while(reader.Read())rows.Add(reader.GetString(0));return rows;}
    }
}
