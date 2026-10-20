using System.Text.Json;
using PcImpressz.Contracts;

namespace PcImpressz.Core.Companies;

public sealed class CompanyStore
{
    private readonly string _directory;
    private readonly object _gate = new();
    public CompanyStore(string directory) { _directory = Path.GetFullPath(directory); }
    public CompanyInfo[] List()
    {
        lock (_gate)
        {
            var path = Path.Combine(_directory, "companies.json");
            if (!File.Exists(path)) return [];
            if (new FileInfo(path).Length > 1024 * 1024) throw new IOException("Túl nagy cégjegyzék.");
            var list = JsonSerializer.Deserialize<CompanyInfo[]>(File.ReadAllText(path))
                ?? throw new IOException("Sérült cégjegyzék.");
            if (list.Any(c => c.Id == Guid.Empty || string.IsNullOrWhiteSpace(c.Name))
                || list.Select(c => c.Id).Distinct().Count() != list.Length)
                throw new IOException("Sérült cégjegyzék.");
            return list;
        }
    }
    public CompanyInfo Create(string name, bool isDemo)
    {
        lock (_gate)
        {
            name = name.Trim();
            if (name.Length is < 2 or > 100 || name.Any(char.IsControl))
                throw new ArgumentException("A cégnév 2–100 karakter hosszú legyen.");
            var all = List();
            if (all.Length >= 1000) throw new ArgumentException("A cégjegyzék megtelt.");
            if (all.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Már van ilyen nevű cég.");
            var company = new CompanyInfo(Guid.NewGuid(), name, isDemo, DateTimeOffset.UtcNow);
            Directory.CreateDirectory(_directory);
            var file = Path.Combine(_directory, "companies.json");
            var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(all.Append(company)));
                File.Move(temporary, file, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return company;
        }
    }
    public DemoDataClearance ClearDemoData()
    {
        lock (_gate)
        {
            var demos = List().Where(c => c.IsDemo).ToArray();
            var cleared = 0;
            foreach (var company in demos)
            {
                var folder = CompanyFolder(company.Id);
                if (!Directory.Exists(folder)) continue;
                DeleteTree(folder);
                cleared++;
            }
            return new(demos.Length, cleared);
        }
    }
    private string CompanyFolder(Guid companyId)
    {
        var root = Path.GetFullPath(Path.Combine(_directory, "companies")) + Path.DirectorySeparatorChar;
        var folder = Path.GetFullPath(Path.Combine(_directory, "companies", companyId.ToString("N")));
        if (!folder.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new IOException("A cégmappa a várt helyen kívül van.");
        return folder;
    }
    private static void DeleteTree(string directory)
    {
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A cégmappa hivatkozást tartalmaz, ezért nem törölhető.");
            if ((File.GetAttributes(current) & FileAttributes.Directory) == 0) continue;
            foreach (var entry in Directory.EnumerateFileSystemEntries(current)) pending.Push(entry);
        }
        Directory.Delete(directory, recursive: true);
    }
    public string ModuleDataDirectory(Guid companyId, string moduleId)
    {
        lock (_gate)
        {
            if (!List().Any(c => c.Id == companyId)) throw new ArgumentException("A kiválasztott cég nem létezik.");
            if (moduleId.Length is < 1 or > 100 || moduleId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-'))
                throw new ArgumentException("Érvénytelen modulazonosító.");
            return Path.Combine(_directory, "companies", companyId.ToString("N"), "modules", moduleId);
        }
    }
}
