using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;

namespace PcImpressz.Shell.Windows;

public interface IColumnList
{
    EntityPageDescriptor Definition { get; }
    int LayoutRevision { get; }
    string SortColumn { get; }
    bool SortDescending { get; }
    IReadOnlyList<EntityColumn> VisibleColumns { get; }
    bool IsColumnVisible(string id);
    bool SetColumnVisible(string id, bool visible);
    void ToggleSort(string columnId);
}

internal sealed class ColumnList
{
    private readonly ListLayoutStore _store;
    public EntityPageDescriptor Definition { get; }
    public string Key { get; }
    public string SortColumn { get; private set; } = "";
    public bool SortDescending { get; private set; }
    public int Revision { get; private set; }
    public event Action? Changed;
    public event Action? SortChanged;

    public ColumnList(ListLayoutStore store, EntityPageDescriptor definition)
    {
        (_store, Definition) = (store, definition);
        Key = definition.ModuleId + "." + definition.Id;
        Read();
        store.Changed += OnStore;
    }

    public IReadOnlyList<EntityColumn> VisibleColumns =>
        Definition.Columns.Where(column => IsColumnVisible(column.Id)).ToArray();
    public bool IsColumnVisible(string id) =>
        _store.For(Key, Definition.Columns).Columns.FirstOrDefault(column => column.Id == id)?.Visible ?? false;
    public bool SetColumnVisible(string id, bool visible) => _store.TrySetVisible(Key, Definition.Columns, id, visible);
    public void ToggleSort(string id) => _store.ToggleSort(Key, Definition.Columns, id);

    private void OnStore(string key)
    {
        if (!string.Equals(key, Key, StringComparison.Ordinal)) return;
        var sort = (SortColumn, SortDescending);
        Read();
        Changed?.Invoke();
        if (sort != (SortColumn, SortDescending)) SortChanged?.Invoke();
    }

    private void Read()
    {
        var page = _store.For(Key, Definition.Columns);
        SortColumn = page.SortColumn;
        SortDescending = page.SortDescending;
        Revision++;
    }
}

internal sealed class TypedSearch(Action reload, CancellationToken cancel)
{
    private int _version;
    public void Schedule()
    {
        var version = ++_version;
        _ = Run(version);
    }
    public void CancelPending() => _version++;
    private async Task Run(int version)
    {
        try { await Task.Delay(280, cancel); }
        catch (OperationCanceledException) { return; }
        if (version == _version) reload();
    }
}

internal sealed class ListLoader(Func<int, Task> load, CancellationToken cancel)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _generation;
    public bool IsCurrent(int generation) => generation == _generation;
    public Task Run()
    {
        var generation = ++_generation;
        return Load(generation);
    }
    private async Task Load(int generation)
    {
        try { await _gate.WaitAsync(cancel); }
        catch (OperationCanceledException) { return; }
        try
        {
            if (generation == _generation) await load(generation);
        }
        finally { _gate.Release(); }
    }
}
