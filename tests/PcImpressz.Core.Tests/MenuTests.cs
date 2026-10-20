using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;

namespace PcImpressz.Core.Tests;

internal static class MenuTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return Test("workspace menu groups module pages and nests settings", () =>
        {
            var menu = WorkspaceMenu.Compose(
            [
                Page("catalog", "categories", "Kategóriák", "\uE8FD"),
                Page("catalog", "units", "Mértékegységek", "\uE8EF"),
                Page("catalog", "products", "Terméktörzs", "\uE7B8"),
                Page("partners", "partners", "Partnertörzs", "\uE716"),
                Page("stock", "receipts", "Bevételezés", "\uE7BF"),
                Page("stock", "issues", "Kiadás", "\uE73E")
            ]);
            Check.Equal("Áttekintés|Terméktörzs|Partnertörzs|Kiadás|Beállítások", string.Join("|", menu.Select(item => item.Title)));
            var catalog = menu[1];
            Check.Equal("catalog.products", catalog.PageKey);
            Check.Equal("Kategóriák|Mértékegységek", string.Join("|", catalog.Children.Select(item => item.Title)));
            Check.Equal("catalog.categories|catalog.units", string.Join("|", catalog.Children.Select(item => item.PageKey)));
            Check.Equal(0, menu[2].Children.Count);
            Check.Equal("partners.partners", menu[2].PageKey);
            Check.Equal("Bevételezés", menu[3].Children[0].Title);
            Check.Equal("stock.issues", menu[3].PageKey);
            Check.Equal("Megjelenés|Licenc|Modulok", string.Join("|", menu[4].Children.Select(item => item.Title)));
            Check.Equal("appearance|license|modules", string.Join("|", menu[4].Children.Select(item => item.PageKey)));
        });
        yield return Test("module point does not repeat its own page title", () =>
        {
            var menu = WorkspaceMenu.Compose(
            [
                Page("catalog", "categories", "Kategóriák", "\uE8FD"),
                Page("catalog", "products", "Terméktörzs", "\uE7B8"),
                Page("catalog", "alias", "Terméktörzs", "\uE7B8")
            ]);
            Check.Equal(1, menu.Count(item => item.Title == "Terméktörzs"));
            Check.Equal(0, menu[1].Children.Count(item => item.Title == "Terméktörzs"));
            Check.Equal("Kategóriák", menu[1].Children[0].Title);
        });
        yield return Test("shell menu stays usable before modules arrive", () =>
        {
            var menu = WorkspaceMenu.Compose([]);
            Check.Equal("Áttekintés|Beállítások", string.Join("|", menu.Select(item => item.Title)));
            Check.Equal("home", menu[0].PageKey);
            Check.Equal("settings", menu[1].PageKey);
        });
    }

    private static EntityPageDescriptor Page(string module, string id, string title, string icon) =>
        new(id, module, title, title + " leírása", icon, [], [], new("list", "save", "archive", "export"));

    private static (string, Func<Task>) Test(string name, Action test) => (name, () => { test(); return Task.CompletedTask; });
}
