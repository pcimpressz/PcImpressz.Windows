using System.Text.Json;
using System.Text.Json.Serialization;
using PcImpressz.Contracts;

namespace PcImpressz.Shell.Customization;

public sealed record ColumnPreference(string Id, bool Visible);
public sealed record PageListPreference(string Key, string SortColumn, bool SortDescending, ColumnPreference[] Columns, int PageSize = 0);
public sealed record ListLayoutDocument(int SchemaVersion, PageListPreference[] Pages);

public sealed class ListLayoutStore(string directory)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public event Action<string>? Changed;
    public string? LastWarning { get; private set; }

    public PageListPreference For(string key, IReadOnlyList<EntityColumn> columns)
    {
        RequireKey(key);
        var saved = Read().Pages.FirstOrDefault(page => page.Key == key);
        return Normalize(key, columns, saved);
    }

    public bool TrySetVisible(string key, IReadOnlyList<EntityColumn> columns, string columnId, bool visible)
    {
        var current = For(key, columns);
        if (current.Columns.All(column => column.Id != columnId)) return false;
        if (!visible && current.Columns.Count(column => column.Visible && column.Id != columnId) == 0) return false;
        var next = current.Columns.Select(column => column.Id == columnId ? column with { Visible = visible } : column).ToArray();
        WritePage(current with { Columns = next });
        return true;
    }

    public void ToggleSort(string key, IReadOnlyList<EntityColumn> columns, string columnId)
    {
        var current = For(key, columns);
        if (columns.All(column => column.Id != columnId)) return;
        var descending = current.SortColumn == columnId && !current.SortDescending;
        WritePage(current with { SortColumn = columnId, SortDescending = descending });
    }

    public void SetPageSize(string key, IReadOnlyList<EntityColumn> columns, int pageSize)
    {
        if (pageSize is not (10 or 20 or 50 or 100)) throw new ArgumentException("A listaoldal 10, 20, 50 vagy 100 sor lehet.");
        var current = For(key, columns);
        if (current.PageSize == pageSize) return;
        WritePage(current with { PageSize = pageSize });
    }

    public void Reset(string key)
    {
        RequireKey(key);
        var pages = Read().Pages.Where(page => page.Key != key).ToArray();
        Persist(new(1, pages));
        Changed?.Invoke(key);
    }

    private void WritePage(PageListPreference page)
    {
        Validate(page);
        var pages = Read().Pages.Where(item => item.Key != page.Key).Append(page)
            .OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        if (pages.Length > 40) throw new InvalidDataException("Túl sok lista beállítása.");
        Persist(new(1, pages));
        Changed?.Invoke(page.Key);
    }

    private ListLayoutDocument Read()
    {
        var path = Path.Combine(directory, "list-layouts.json");
        if (!File.Exists(path)) return new(1, []);
        try
        {
            if (new FileInfo(path).Length > 65_536) throw new InvalidDataException("Túl nagy listabeállítás.");
            var value = JsonSerializer.Deserialize<ListLayoutDocument>(File.ReadAllText(path), Options)
                ?? throw new InvalidDataException("Üres listabeállítás.");
            if (value.SchemaVersion != 1 || value.Pages is null || value.Pages.Length > 40
                || value.Pages.Select(page => page.Key).Distinct(StringComparer.Ordinal).Count() != value.Pages.Length)
                throw new InvalidDataException("Érvénytelen listabeállítás.");
            foreach (var page in value.Pages) Validate(page);
            return value;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            LastWarning = "A mentett listabeállítás nem tölthető be; az alap oszlopokat használjuk.";
            return new(1, []);
        }
    }

    private void Persist(ListLayoutDocument document)
    {
        var path = Path.GetFullPath(Path.Combine(directory, "list-layouts.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(document, Options));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static PageListPreference Normalize(string key, IReadOnlyList<EntityColumn> columns, PageListPreference? saved)
    {
        if (columns.Count is < 1 or > 15 || columns.Select(column => column.Id).Distinct(StringComparer.Ordinal).Count() != columns.Count)
            throw new ArgumentException("Érvénytelen oszloplista.");
        var chosen = columns.Select(column =>
        {
            var stored = saved?.Columns.FirstOrDefault(item => item.Id == column.Id);
            return new ColumnPreference(column.Id, stored?.Visible ?? column.Visible);
        }).ToArray();
        if (chosen.All(column => !column.Visible)) chosen[0] = chosen[0] with { Visible = true };
        var sort = saved?.SortColumn ?? "";
        if (sort.Length > 0 && columns.All(column => column.Id != sort)) sort = "";
        var pageSize = saved?.PageSize ?? 0;
        if (pageSize is not (0 or 10 or 20 or 50 or 100)) pageSize = 0;
        return new(key, sort, sort.Length > 0 && (saved?.SortDescending ?? false), chosen, pageSize);
    }

    private static void Validate(PageListPreference page)
    {
        RequireKey(page.Key);
        if (page.SortColumn is null || page.Columns is null || page.Columns.Length is < 1 or > 15
            || page.Columns.Any(column => column is null)
            || page.Columns.Select(column => column.Id).Distinct(StringComparer.Ordinal).Count() != page.Columns.Length
            || page.Columns.Any(column => !IsColumnId(column.Id))
            || page.SortColumn.Length > 0 && !IsColumnId(page.SortColumn)
            || page.Columns.All(column => !column.Visible)
            || page.PageSize is not (0 or 10 or 20 or 50 or 100))
            throw new InvalidDataException("Érvénytelen listaoszlop.");
    }

    private static void RequireKey(string? key)
    {
        if (key is null || key.Length > 81 || key.Split('.') is not [var module, var page]
            || !IsKeyPart(module) || !IsKeyPart(page))
            throw new ArgumentException("Érvénytelen listakulcs.");
    }

    private static bool IsKeyPart(string value) => value.Length is > 0 and <= 40
        && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')
        && value[0] is >= 'a' and <= 'z' or >= '0' and <= '9'
        && value[^1] is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static bool IsColumnId(string value) => value.Length is > 0 and <= 40
        && value[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z'
        && value.All(char.IsAsciiLetterOrDigit);
}
