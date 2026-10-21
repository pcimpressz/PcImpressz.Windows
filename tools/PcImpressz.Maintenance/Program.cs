using PcImpressz.Contracts;
using PcImpressz.Maintenance;
using PcImpressz.Core.Companies;
using PcImpressz.Core.Storage;

try
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("A karbantartó Windows rendszert igényel.");
    if(args.Length<3 || args[0] is not ("backup-all" or "restore" or "seed-demo" or "recover-ui-admin"))
        throw new ArgumentException("Használat: backup-all <adatmappa> <mentésmappa> | restore <adatmappa> <mentésfájl> <új cégnév> | seed-demo <adatmappa> <demócég GUID> <mentésmappa> | recover-ui-admin <adatmappa> <cég GUID> <felhasználónév>");
    var data=Path.GetFullPath(args[1]);
    if(!Directory.Exists(data))throw new IOException("Az adatmappa nem található.");
    using var instanceLock=new FileStream(Path.Combine(data,"host.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None,1,FileOptions.DeleteOnClose);
    var companies=new CompanyStore(data);var database=new CompanyDatabase(companies.DataDirectory);var backups=new CompanyBackupService(companies,database);
    if(args[0]=="recover-ui-admin"&&args.Length==4)
    {
        if(Console.IsInputRedirected)throw new ArgumentException("A helyreállításhoz interaktív terminál szükséges; a jelszót ne add át parancssorban vagy fájlban.");
        var company=Guid.Parse(args[2]);
        if(!companies.List().Any(c=>c.Id==company))throw new ArgumentException("Ismeretlen cég.");
        using var identity=System.Security.Principal.WindowsIdentity.GetCurrent();
        var sid=identity.User?.Value??throw new InvalidOperationException("A Windows-felhasználó nem azonosítható.");
        var credentials=new CompanyDatabase(id=>Path.Combine(data,"security",id.ToString("N")));
        var securityFolder=Path.Combine(data,"security",company.ToString("N"));
        if(!File.Exists(Path.Combine(securityFolder,"company.db")))throw new ArgumentException("Nincs helyreállítandó admin. A felületen hozd létre az első admint.");
        var snapshot=Path.Combine(securityFolder,"admin-recovery-before-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".db");
        var administration=new PcImpressz.Core.Security.UiAdministrationService(database,credentials,TimeProvider.System);
        administration.RequireRecoveryOwner(company,sid);
        credentials.Backup(company,snapshot);
        Console.Write("Új jelszó (legalább 12 karakter, nem látható): ");var password=ReadSecret();
        Console.Write("Új jelszó ismét: ");if(ReadSecret()!=password)throw new ArgumentException("A két jelszó nem egyezik.");
        administration.RecoverLocalAdmin(new(company,args[3],password),sid);
        Console.WriteLine("Admin hozzáférés helyreállítva. A korábbi hitelesítési adatbázis mentése: "+snapshot);
    }
    else
    if(args[0]=="seed-demo"&&args.Length==4)
    {
        var company=Guid.Parse(args[2]);DemoDataSeeder.RequireDemo(companies,company);
        var already=database.Execute(company,tx=>tx.Get("demo",DemoDataSeeder.Marker) is not null);
        if(!already)
        {
            var destination=Path.GetFullPath(args[3]);Directory.CreateDirectory(destination);
            var file=Path.Combine(destination,"demo-feltoltes-elott-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".pcibak");
            backups.Create(company,file);Console.WriteLine("Feltöltés előtti ellenőrzött mentés: "+file);
        }
        var actor=System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("A felhasználó nem azonosítható.");
        var report=DemoDataSeeder.Seed(companies,database,company,TimeProvider.System,actor+"/demo-seed-v1");
        Console.WriteLine(report.AlreadySeeded?"A mintacsomag már be van töltve; nincs duplikálás vagy felülírás.":$"Mintaadatok feltöltve: {report.Partners} partner, {report.Products} termék, {report.Receipts} bevételezés, {report.Sales} eladás.");
    }
    else if(args[0]=="backup-all"&&args.Length==3)
    {
        var destination=Path.GetFullPath(args[2]);Directory.CreateDirectory(destination);
        var count=0;
        foreach(var company in companies.List())
        {
            var file=Path.Combine(destination,"ceg-"+company.Id.ToString("N")+"-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".pcibak");
            backups.Create(company.Id,file);Console.WriteLine("Mentés ellenőrizve: "+file);count++;
        }
        Console.WriteLine("Elkészült, ellenőrzött cégmentések: "+count);
    }
    else if(args[0]=="restore"&&args.Length==4)
    {
        var restored=backups.Restore(args[2],args[3],false);Console.WriteLine("Új cégként visszaállítva: "+restored.Id);
    }
    else throw new ArgumentException("Érvénytelen kapcsolók.");
    return 0;
}
catch(Exception ex) when(ex is not OutOfMemoryException)
{
    Console.Error.WriteLine("A művelet nem fejeződött be: "+ex.Message);return 1;
}

static string ReadSecret()
{
    var result=new System.Text.StringBuilder();
    while(true)
    {
        var key=Console.ReadKey(intercept:true);
        if(key.Key==ConsoleKey.Enter){Console.WriteLine();return result.ToString();}
        if(key.Key==ConsoleKey.Escape)throw new OperationCanceledException("Helyreállítás megszakítva.");
        if(key.Key==ConsoleKey.Backspace){if(result.Length>0)result.Length--;continue;}
        if(!char.IsControl(key.KeyChar)){if(result.Length>=200)throw new ArgumentException("A jelszó legfeljebb 200 karakter lehet.");result.Append(key.KeyChar);}
    }
}
