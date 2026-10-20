using PcImpressz.Contracts;
using PcImpressz.Core.Companies;
using PcImpressz.Core.Modules;

namespace PcImpressz.Core.Tests;

internal static class SettingsFeatureTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("module switch persists and the shell frame stays available", () =>
        {
            using var dir = new TestDirectory();
            var store = new ModuleEnablementStore(dir.Path);
            Check.True(store.IsEnabled("partners"));
            store.SetEnabled("partners", false);
            store.SetEnabled("catalog", false);
            store.SetEnabled("catalog", true);
            var reloaded = new ModuleEnablementStore(dir.Path);
            Check.True(!reloaded.IsEnabled("partners"));
            Check.True(reloaded.IsEnabled("catalog"));
            Check.Throws<ArgumentException>(() => reloaded.SetEnabled("home", false));
            Check.Throws<ArgumentException>(() => reloaded.SetEnabled("settings", false));
            Check.Throws<ArgumentException>(() => reloaded.SetEnabled("license", false));
            Check.Throws<ArgumentException>(() => reloaded.SetEnabled("appearance", false));
            Check.Throws<ArgumentException>(() => reloaded.SetEnabled("modules", false));
            Check.True(!new ModuleEnablementStore(dir.Path).IsEnabled("partners"));
            return Task.CompletedTask;
        });
        yield return ("demo clear removes only demo company data", () =>
        {
            using var dir = new TestDirectory();
            var companies = new CompanyStore(dir.Path);
            var live = companies.Create("Éles cég", false);
            var demo = companies.Create("Demó cég", true);
            var otherDemo = companies.Create("Másik demó", true);
            new ModuleEnablementStore(dir.Path).SetEnabled("partners", false);
            var appearance = Path.Combine(dir.Path, "appearance.json");
            var license = Path.Combine(dir.Path, "license.jws");
            File.WriteAllText(appearance, "megjelenes");
            File.WriteAllText(license, "licenc");
            Write(dir.Path, live, Path.Combine("modules", "partners", "partners.json"), "eles");
            Write(dir.Path, live, Path.Combine("modules", "catalog", "catalog.json"), "eles-termek");
            Write(dir.Path, demo, Path.Combine("modules", "partners", "partners.json"), "demo-partner");
            Write(dir.Path, demo, Path.Combine("modules", "catalog", "catalog.json"), "demo-termek");
            Write(dir.Path, demo, "jegyzet.txt", "egyeb");
            Write(dir.Path, otherDemo, Path.Combine("modules", "partners", "partners.json"), "masik");
            var registry = File.ReadAllText(Path.Combine(dir.Path, "companies.json"));
            var enablement = File.ReadAllText(Path.Combine(dir.Path, "module-enablement.json"));
            var cleared = companies.ClearDemoData();
            Check.Equal(2, cleared.DemoCompanies);
            Check.Equal(2, cleared.ClearedDirectories);
            Check.Equal("eles", File.ReadAllText(CompanyFile(dir.Path, live, Path.Combine("modules", "partners", "partners.json"))));
            Check.Equal("eles-termek", File.ReadAllText(CompanyFile(dir.Path, live, Path.Combine("modules", "catalog", "catalog.json"))));
            Check.True(!Directory.Exists(CompanyRoot(dir.Path, demo.Id)));
            Check.True(!Directory.Exists(CompanyRoot(dir.Path, otherDemo.Id)));
            Check.Equal(registry, File.ReadAllText(Path.Combine(dir.Path, "companies.json")));
            Check.Equal(3, companies.List().Length);
            Check.True(companies.List().Any(company => company.Id == demo.Id && company.IsDemo));
            Check.Equal("megjelenes", File.ReadAllText(appearance));
            Check.Equal("licenc", File.ReadAllText(license));
            Check.Equal(enablement, File.ReadAllText(Path.Combine(dir.Path, "module-enablement.json")));
            Check.True(!new ModuleEnablementStore(dir.Path).IsEnabled("partners"));
            var fresh = companies.ModuleDataDirectory(demo.Id, "partners");
            Directory.CreateDirectory(fresh);
            File.WriteAllText(Path.Combine(fresh, "partners.json"), "uj");
            Check.Equal("uj", File.ReadAllText(Path.Combine(fresh, "partners.json")));
            var empty = companies.Create("Üres demó", true);
            var afterCreate = File.ReadAllText(Path.Combine(dir.Path, "companies.json"));
            var second = companies.ClearDemoData();
            Check.Equal(3, second.DemoCompanies);
            Check.Equal(1, second.ClearedDirectories);
            Check.Equal(afterCreate, File.ReadAllText(Path.Combine(dir.Path, "companies.json")));
            Check.True(File.Exists(CompanyFile(dir.Path, live, Path.Combine("modules", "partners", "partners.json"))));
            Check.True(!Directory.Exists(CompanyRoot(dir.Path, empty.Id)));
            return Task.CompletedTask;
        });
    }

    private static void Write(string root, CompanyInfo company, string relative, string content)
    {
        var path = CompanyFile(root, company, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string CompanyFile(string root, CompanyInfo company, string relative) =>
        Path.Combine(CompanyRoot(root, company.Id), relative);

    private static string CompanyRoot(string root, Guid companyId) =>
        Path.Combine(root, "companies", companyId.ToString("N"));
}
