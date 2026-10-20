using System.Reflection;
using System.Runtime.Loader;
using PcImpressz.Contracts;

namespace PcImpressz.Core.Modules;

internal sealed class ModuleLoadContext(Dictionary<string, byte[]> assemblies)
    : AssemblyLoadContext(isCollectible: true)
{
    private static readonly HashSet<string> FrameworkAssemblies =
        (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")) ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(Path.GetFileNameWithoutExtension).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

    protected override Assembly? Load(AssemblyName name)
    {
        var contracts = typeof(IModule).Assembly;
        if (name.Name == contracts.GetName().Name) return contracts;
        if (name.Name is not null && assemblies.TryGetValue(name.Name + ".dll", out var bytes))
            return LoadFromStream(new MemoryStream(bytes, writable: false));
        if (name.Name is not null && FrameworkAssemblies.Contains(name.Name)) return null;
        throw new FileNotFoundException("Dependency is not in the signed module package.", name.Name);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName) =>
        throw new NotSupportedException("Native module dependencies are not supported in Core v1.");
}
