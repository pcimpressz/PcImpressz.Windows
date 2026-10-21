using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PcImpressz.Contracts;
using PcImpressz.Core.Companies;

namespace PcImpressz.Core.Storage;

public sealed record CompanyBackupManifest(int Version,string CompanyName,bool IsDemo,DateTimeOffset CreatedAt,string DatabaseSha256);
public sealed class CompanyBackupService(CompanyStore companies, CompanyDatabase database)
{
    private const long MaximumBytes=256L*1024*1024;
    public string Create(Guid companyId,string destination)
    {
        var company=companies.List().SingleOrDefault(c=>c.Id==companyId) ?? throw new ArgumentException("Ismeretlen cég.");
        destination=Path.GetFullPath(destination);
        if(File.Exists(destination)) throw new IOException("A mentés már létezik; válassz új fájlnevet.");
        var dataFolder=Path.GetFullPath(companies.DataDirectory(companyId))+Path.DirectorySeparatorChar;
        if(destination.StartsWith(dataFolder,StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("A mentést a cég adatmappáján kívülre mentsd.");
        CompanyDatabase.RejectLinks(Path.GetDirectoryName(destination)!);
        var temporary=Path.Combine(Path.GetDirectoryName(destination)!,".pcimpressz-backup-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var db=Path.Combine(temporary,"company.db");database.Backup(companyId,db);
            if(new FileInfo(db).Length>MaximumBytes) throw new IOException("A mentés meghaladja a jelenlegi 256 MiB korlátot.");
            using var stream=File.OpenRead(db);var hash=Convert.ToHexString(SHA256.HashData(stream));
            var manifest=new CompanyBackupManifest(1,company.Name,company.IsDemo,DateTimeOffset.UtcNow,hash);
            File.WriteAllText(Path.Combine(temporary,"manifest.json"),JsonSerializer.Serialize(manifest,CompanyDocuments.Json));
            var zip=temporary+".pcibak";
            try { ZipFile.CreateFromDirectory(temporary,zip,CompressionLevel.Optimal,false); Validate(zip,Path.Combine(temporary,"verify.db")); File.Move(zip,destination); }
            finally { if(File.Exists(zip)) File.Delete(zip); }
            return destination;
        }
        finally { Cleanup(temporary); }
    }
    public CompanyInfo Restore(string source,string newName,bool isDemo)
    {
        var temporary=Path.Combine(Path.GetTempPath(),"pcimpressz-restore-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var staged=Path.Combine(temporary,"company.db");Validate(Path.GetFullPath(source),staged);
            // Registry is written last. Existing company data is never overwritten.
            return companies.Create(newName,isDemo,folder=> { Directory.CreateDirectory(folder);File.Copy(staged,Path.Combine(folder,"company.db")); });
        }
        finally { Cleanup(temporary); }
    }
    private static void Validate(string archive,string destination)
    {
        if(new FileInfo(archive).Length>MaximumBytes) throw new IOException("A mentés túl nagy.");
        using var zip=ZipFile.OpenRead(archive);
        if(zip.Entries.Count!=2 || zip.Entries.Any(e=>e.FullName is not ("manifest.json" or "company.db")))
            throw new IOException("Ismeretlen mentéstartalom.");
        var manifestEntry=zip.GetEntry("manifest.json") ?? throw new IOException("Hiányzó mentésleíró.");
        var databaseEntry=zip.GetEntry("company.db") ?? throw new IOException("Hiányzó adatbázis.");
        if(manifestEntry.Length>8192 || databaseEntry.Length>MaximumBytes || databaseEntry.Length<1) throw new IOException("Érvénytelen mentésméret.");
        using var metadata=manifestEntry.Open();var manifest=JsonSerializer.Deserialize<CompanyBackupManifest>(metadata,CompanyDocuments.Json);
        if(manifest is null || manifest.Version!=1 || manifest.DatabaseSha256.Length!=64) throw new IOException("Ismeretlen mentésverzió.");
        using(var input=databaseEntry.Open()) using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write))
        {
            var buffer=new byte[65536];long total=0;int count;
            while((count=input.Read(buffer))>0) {total+=count;if(total>MaximumBytes)throw new IOException("A kibontott mentés túl nagy.");output.Write(buffer,0,count);}
            if(total!=databaseEntry.Length) throw new IOException("Csonka mentés.");
        }
        using(var read=File.OpenRead(destination))
            if(!string.Equals(Convert.ToHexString(SHA256.HashData(read)),manifest.DatabaseSha256,StringComparison.Ordinal)) throw new IOException("A mentés ellenőrzőösszege eltér.");
        CompanyDatabase.ValidateBackup(destination);
    }
    private static void Cleanup(string directory)
    {
        // This method receives only a freshly created, exact GUID directory from this service.
        var resolved=Path.GetFullPath(directory);
        if(!Path.GetFileName(resolved).StartsWith(".pcimpressz-backup-",StringComparison.Ordinal)
            && !Path.GetFileName(resolved).StartsWith("pcimpressz-restore-",StringComparison.Ordinal)) throw new IOException("Érvénytelen ideiglenes útvonal.");
        if(Directory.Exists(resolved)) { CompanyDatabase.RejectLinks(resolved);Directory.Delete(resolved,true); }
    }
}
