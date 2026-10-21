using Microsoft.Extensions.Logging;
using PcImpressz.Contracts;
using PcImpressz.Core.Commands;
using PcImpressz.Core.Security;

namespace PcImpressz.Core.Modules;

public sealed class ModuleRuntime(
    SignedDocumentVerifier packageVerifier,
    CommandDispatcher dispatcher,
    TimeProvider timeProvider,
    ILogger<ModuleRuntime> logger,
    Func<Guid, string, string>? companyDataDirectory = null, ICompanyDatabase? database = null) : IAsyncDisposable
{
    private readonly List<(string Id, IModule Module, ModuleLoadContext Context)> _running = [];
    private readonly List<ModuleReport> _reports = [];
    private readonly Dictionary<string, string[]> _dependencyClosures = new(StringComparer.Ordinal);
    private bool _started;
    private readonly List<EntityPageDescriptor> _pages = [];
    public IReadOnlyList<EntityPageDescriptor> Pages => _pages.AsReadOnly();
    public IReadOnlyList<ModuleReport> Reports => _reports.AsReadOnly();

    // Startup/shutdown are serialized by the composition root. Hot reload is intentionally absent.
    public async Task StartAsync(string modulesDirectory, CancellationToken cancellationToken = default)
    {
        if (_started) throw new InvalidOperationException("Module runtime has already started.");
        _started = true;
        Directory.CreateDirectory(modulesDirectory);
        ModulePackageVerifier.RejectLink(modulesDirectory);
        var packages = new List<VerifiedModule>();
        var verifier = new ModulePackageVerifier(packageVerifier);
        foreach (var directory in Directory.EnumerateDirectories(modulesDirectory).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { packages.Add(verifier.Verify(directory)); }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                _reports.Add(new(Path.GetFileName(directory), null, ModuleStatus.Rejected, ex.Message));
                logger.LogWarning("Module package rejected: {Directory}, {Reason}", Path.GetFileName(directory), ex.Message);
            }
        }
        var duplicateIds = packages.GroupBy(p => p.Manifest.Id).Where(g => g.Count() > 1)
            .Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var package in packages.Where(p => duplicateIds.Contains(p.Manifest.Id)))
            Report(package, ModuleStatus.Rejected, "Több csomag használja ugyanazt a modulazonosítót.");
        var pending = packages.Where(p => !duplicateIds.Contains(p.Manifest.Id))
            .ToDictionary(p => p.Manifest.Id, StringComparer.Ordinal);
        var active = new Dictionary<string, Version>(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            var progress = false;
            foreach (var package in pending.Values.ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!package.Manifest.Dependencies.All(d => active.TryGetValue(d.ModuleId, out var v)
                        && v >= Version.Parse(d.MinimumVersion) && v < Version.Parse(d.MaximumVersionExclusive)))
                    continue;
                pending.Remove(package.Manifest.Id);
                progress = true;
                if (await ActivateAsync(package, cancellationToken))
                    active.Add(package.Manifest.Id, Version.Parse(package.Manifest.Version));
            }
            if (progress) continue;
            foreach (var package in pending.Values)
                Report(package, ModuleStatus.Blocked, "Hiányzó, hibás, nem kompatibilis vagy körkörös modulfüggőség.");
            break;
        }
    }

    private async Task<bool> ActivateAsync(VerifiedModule package, CancellationToken cancellationToken)
    {
        var context = new ModuleLoadContext(package.Assemblies);
        IModule? module = null;
        var staging = new StagingContext(package.Manifest.Id, timeProvider, companyDataDirectory, database);
        try
        {
            var assembly = context.LoadFromStream(new MemoryStream(package.Assemblies[package.Manifest.EntryAssembly], false));
            var type = assembly.GetType(package.Manifest.EntryType, throwOnError: true)!;
            module = Activator.CreateInstance(type) as IModule
                ?? throw new InvalidOperationException("Entry type must implement IModule.");
            await module.StartAsync(staging, cancellationToken);
            staging.Close();
            var requiredModules = package.Manifest.Dependencies.SelectMany(d =>
                _dependencyClosures[d.ModuleId].Prepend(d.ModuleId)).Distinct(StringComparer.Ordinal).ToArray();
            ValidatePages(staging.Pages, package.Manifest.Id);
            dispatcher.RegisterModule(package.Manifest.Id, staging.Handlers, requiredModules);
            _pages.AddRange(staging.Pages);
            _dependencyClosures.Add(package.Manifest.Id, requiredModules);
            _running.Add((package.Manifest.Id, module, context));
            Report(package, ModuleStatus.Active, "Aláírt modul betöltve; a parancsokhoz külön jogosultságellenőrzés tartozik.");
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            staging.Close();
            if (module is not null)
            {
                try { await module.StopAsync(CancellationToken.None); }
                catch (Exception stopError) when (stopError is not OutOfMemoryException)
                { logger.LogWarning("Failed module cleanup: {Module}, {Reason}", package.Manifest.Id, stopError.Message); }
            }
            context.Unload();
            if (ex is OperationCanceledException) throw;
            Report(package, ModuleStatus.Failed, ex.Message);
            return false;
        }
    }

    private void Report(VerifiedModule package, ModuleStatus status, string reason)
    {
        _reports.Add(new(package.Manifest.Id, package.Manifest.Version, status, reason));
        logger.LogInformation("Module {Module}: {Status}", package.Manifest.Id, status);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (id, module, context) in _running.AsEnumerable().Reverse())
        {
            dispatcher.UnregisterModule(id);
            try { await module.StopAsync(CancellationToken.None); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { logger.LogError("Module shutdown failed: {Module}, {Reason}", id, ex.Message); }
            finally { context.Unload(); }
        }
        _running.Clear();
        _dependencyClosures.Clear();
        _pages.Clear();
        for (var i = 0; i < _reports.Count; i++)
            if (_reports[i].Status == ModuleStatus.Active)
                _reports[i] = _reports[i] with { Status = ModuleStatus.Stopped, Reason = "A modul leállt." };
    }

    private static void ValidatePages(IReadOnlyList<EntityPageDescriptor> pages, string moduleId)
    {
        if (pages.Count > 10) throw new InvalidOperationException("Túl sok modulnézet.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in pages)
        {
            if (page.ModuleId != moduleId || !Identifiers.IsValid(page.Id) || !ids.Add(page.Id)
                || page.ViewKind is not ("entity" or "receiving" or "inventory" or "sales")
                || (page.DossierCommand is not null && !Identifiers.IsValid(page.DossierCommand))
                || page.Fields.Length is < 1 or > 40 || page.Columns.Length is < 1 or > 15
                || page.Fields.Select(f => f.Id).Distinct().Count() != page.Fields.Length
                || page.Fields.Any(f => (string.IsNullOrEmpty(f.Id) || f.Id.Length > 80 || f.Id.Any(c => !char.IsAsciiLetterOrDigit(c))) || f.Kind is not ("text" or "multiline" or "boolean" or "decimal")))
                throw new InvalidOperationException("Érvénytelen modulnézet.");
        }
    }

    private sealed class StagingContext(string moduleId, TimeProvider time,
        Func<Guid, string, string>? companyDirectory, ICompanyDatabase? database) : IModuleContext
    {
        private readonly List<ICommandHandler> _handlers = [];
        private readonly List<EntityPageDescriptor> _pages = [];
        public IReadOnlyList<EntityPageDescriptor> Pages { get { lock (_gate) return _pages.ToArray(); } }
        public string GetCompanyDataDirectory(Guid companyId) => companyDirectory?.Invoke(companyId, moduleId)
            ?? throw new InvalidOperationException("A céges adattár nincs konfigurálva.");
        public void RegisterPage(EntityPageDescriptor page)
        {
            lock (_gate)
            {
                if (_closed) throw new InvalidOperationException("Page registration is closed.");
                _pages.Add(page);
            }
        }
        private readonly object _gate = new();
        private bool _closed;
        public ICompanyDatabase? Database => database;
        public string ModuleId => moduleId;
        public TimeProvider TimeProvider => time;
        public IReadOnlyList<ICommandHandler> Handlers { get { lock (_gate) return _handlers.ToArray(); } }
        public void Close() { lock (_gate) _closed = true; }
        public void RegisterCommand(ICommandHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            lock (_gate)
            {
                if (_closed) throw new InvalidOperationException("Command registration is closed.");
                _handlers.Add(handler);
            }
        }
    }
}
