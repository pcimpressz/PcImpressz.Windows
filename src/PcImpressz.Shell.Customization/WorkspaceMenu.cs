using PcImpressz.Contracts;

namespace PcImpressz.Shell.Customization;

public sealed record MenuEntry(string Id, string Title, string Icon, string Description, string PageKey, IReadOnlyList<MenuEntry> Children);

public static class WorkspaceMenu
{
    public static IReadOnlyList<MenuEntry> Compose(IReadOnlyList<EntityPageDescriptor> pages,
        IReadOnlySet<string>? disabledModuleIds = null)
    {
        if (disabledModuleIds is { Count: > 0 })
            pages = pages.Where(page => !disabledModuleIds.Contains(page.ModuleId)).ToArray();
        var modules = pages.GroupBy(page => page.ModuleId, StringComparer.Ordinal).Select(Module).ToArray();
        return
        [
            Shell("home", "Áttekintés", "\uE80F", "A munkaterületed állapota egy helyen."),
            .. modules,
            Shell("settings", "Beállítások", "\uE713", "Helyi működés, adatok és diagnosztika.",
                Shell("appearance", "Megjelenés", "\uE790", "Egy munkafelület, ami hozzád igazodik."),
                Shell("license", "Licenc", "\uE72E", "A telepítéshez tartozó használati jogosultságok."),
                Shell("modules", "Modulok", "\uE74C", "A telepített bővítmények és állapotuk."))
        ];
    }

    private static MenuEntry Module(IGrouping<string, EntityPageDescriptor> group)
    {
        var pages = group.ToArray();
        var primary = pages.FirstOrDefault(page => string.Equals(page.Id, group.Key, StringComparison.Ordinal))
            ?? pages.FirstOrDefault(page => page.Title.EndsWith("törzs", StringComparison.OrdinalIgnoreCase))
            ?? pages[^1];
        var children = pages.Where(page => !ReferenceEquals(page, primary)
            && !string.Equals(page.Title, primary.Title, StringComparison.OrdinalIgnoreCase)).Select(Page).ToArray();
        return Page(primary) with { Children = children };
    }

    private static MenuEntry Page(EntityPageDescriptor page)
    {
        var key = page.ModuleId + "." + page.Id;
        return new(key, page.Title, page.Icon, page.Description, key, []);
    }

    private static MenuEntry Shell(string id, string title, string icon, string description, params MenuEntry[] children) =>
        new(id, title, icon, description, id, children);
}
