using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;

namespace PcImpressz.Shell.Windows;

public sealed class ListPageSetting(string key, string title, EntityColumn[] columns)
{
    public string Key { get; } = key;
    public string Title { get; } = title;
    public EntityColumn[] Columns { get; } = columns;
}

public sealed class ColumnChoice : Observable
{
    private readonly Func<ColumnChoice, bool, bool> _apply;
    private bool _visible;
    public ColumnChoice(string id, string label, bool visible, Func<ColumnChoice, bool, bool> apply)
    {
        Id = id; Label = label; _visible = visible; _apply = apply;
    }
    public string Id { get; }
    public string Label { get; }
    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value) return;
            if (!_apply(this, value)) { Changed(); return; }
            _visible = value;
            Changed();
        }
    }
}

public sealed partial class ShellViewModel
{
    private ListPageSetting? _selectedListPage;
    private string _listNotice = "";
    public ObservableCollection<ListPageSetting> ListPages { get; } = [];
    public ObservableCollection<ColumnChoice> ColumnChoices { get; } = [];
    public ICommand ResetListLayoutCommand { get; private set; } = null!;
    public string ListNotice { get => _listNotice; private set { _listNotice = value; Changed(); } }
    public ListPageSetting? SelectedListPage
    {
        get => _selectedListPage;
        set
        {
            if (ReferenceEquals(value, _selectedListPage)) return;
            _selectedListPage = value;
            Changed();
            RefreshColumnChoices();
        }
    }
    public string ListSortLabel
    {
        get
        {
            if (_selectedListPage is null) return "A modulok betöltése után itt állíthatod a listákat.";
            var pref = _layouts.For(_selectedListPage.Key, _selectedListPage.Columns);
            if (pref.SortColumn.Length == 0) return "Rendezés: a lista alapértelmezett sorrendje. A fejléc kattintása ezt felülírja.";
            var label = _selectedListPage.Columns.FirstOrDefault(column => column.Id == pref.SortColumn)?.Label ?? pref.SortColumn;
            return "Rendezés: " + label + (pref.SortDescending ? ", csökkenő." : ", növekvő.");
        }
    }

    private void SyncListSettings()
    {
        var desired = _entityPages.Values.Select(page => page.Definition)
            .Select(definition => new ListPageSetting(definition.ModuleId + "." + definition.Id, definition.Title, definition.Columns))
            .OrderBy(page => page.Title, StringComparer.Create(CultureInfo.GetCultureInfo("hu-HU"), true)).ToArray();
        var selected = _selectedListPage?.Key;
        if (ListPages.Count == desired.Length && ListPages.Select(page => page.Key).SequenceEqual(desired.Select(page => page.Key)))
            return;
        ListPages.Clear();
        foreach (var page in desired) ListPages.Add(page);
        SelectedListPage = ListPages.FirstOrDefault(page => page.Key == selected) ?? ListPages.FirstOrDefault();
    }

    private void RefreshColumnChoices()
    {
        ColumnChoices.Clear();
        if (_selectedListPage is null) { Changed(nameof(ListSortLabel)); return; }
        var pref = _layouts.For(_selectedListPage.Key, _selectedListPage.Columns);
        foreach (var column in _selectedListPage.Columns)
        {
            var visible = pref.Columns.FirstOrDefault(item => item.Id == column.Id)?.Visible ?? column.Visible;
            ColumnChoices.Add(new(column.Id, column.Label, visible, ApplyColumn));
        }
        Changed(nameof(ListSortLabel));
    }

    private bool ApplyColumn(ColumnChoice choice, bool visible)
    {
        if (_selectedListPage is null) return false;
        if (_layouts.TrySetVisible(_selectedListPage.Key, _selectedListPage.Columns, choice.Id, visible))
        {
            ListNotice = "";
            Changed(nameof(ListSortLabel));
            return true;
        }
        ListNotice = "Legalább egy oszlop maradjon látható.";
        return false;
    }

    private void ResetSelectedList()
    {
        if (_selectedListPage is null) return;
        _layouts.Reset(_selectedListPage.Key);
        ListNotice = "Az oszlopok és a rendezés visszaállt az alapra.";
    }
}
