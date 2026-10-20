using System.Text.Json;

namespace PcImpressz.Core.Modules;

public sealed class ModuleEnablementStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly HashSet<string> _disabled;

    public ModuleEnablementStore(string directory)
    {
        _path = Path.Combine(Path.GetFullPath(directory), "module-enablement.json");
        _disabled = Load();
    }

    public static bool IsBuiltinFrame(string moduleId) =>
        moduleId is "home" or "settings" or "appearance" or "license" or "modules";

    public bool IsEnabled(string moduleId)
    {
        lock (_gate) return !_disabled.Contains(moduleId);
    }

    public IReadOnlySet<string> DisabledIds
    {
        get { lock (_gate) return _disabled.ToHashSet(StringComparer.Ordinal); }
    }

    public void SetEnabled(string moduleId, bool enabled)
    {
        if (IsBuiltinFrame(moduleId))
            throw new ArgumentException("Az áttekintés, a beállítások, a licenc és a megjelenés a keret része, mindig elérhető.");
        Identifiers.Require(moduleId, "module id");
        lock (_gate)
        {
            if (enabled) _disabled.Remove(moduleId);
            else _disabled.Add(moduleId);
            Save();
        }
    }

    private HashSet<string> Load()
    {
        if (!File.Exists(_path)) return new(StringComparer.Ordinal);
        if (new FileInfo(_path).Length > 16_384) throw new IOException("Túl nagy modulbeállítás.");
        var file = JsonSerializer.Deserialize<ModuleEnablementFile>(File.ReadAllText(_path))
            ?? throw new IOException("Sérült modulbeállítás.");
        if (file.Disabled is null
            || file.Disabled.Any(id => !Identifiers.IsValid(id) || IsBuiltinFrame(id))
            || file.Disabled.Distinct(StringComparer.Ordinal).Count() != file.Disabled.Length)
            throw new IOException("Sérült modulbeállítás.");
        return file.Disabled.ToHashSet(StringComparer.Ordinal);
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new ModuleEnablementFile(_disabled.Order(StringComparer.Ordinal).ToArray())));
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record ModuleEnablementFile(string[] Disabled);
}
