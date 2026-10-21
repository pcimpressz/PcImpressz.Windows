using PcImpressz.Contracts;
using PcImpressz.Maintenance;
using PcImpressz.Core.Companies;
using PcImpressz.Core.Storage;

try
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("A karbantartó Windows rendszert igényel.");
    if(args.Length<3 || args[0] is not ("backup-all" or "restore" or "seed-demo"))
        throw new ArgumentException("Használat: backup-all <adatmappa> <mentésmappa> | restore <adatmappa> <mentésfájl> <új cégnév> | seed-demo <adatmappa> <demócég GUID> <mentésmappa>");
    var data=Path.GetFullPath(args[1]);
    if(!Directory.Exists(data))throw new IOException("Az adatmappa nem található.");
    using var instanceLock=new FileStream(Path.Combine(data,"host.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None,1,FileOptions.DeleteOnClose);
    var companies=new CompanyStore(data);var database=new CompanyDatabase(companies.DataDirectory);var backups=new CompanyBackupService(companies,database);
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
