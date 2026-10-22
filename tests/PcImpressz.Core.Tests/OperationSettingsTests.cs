using System.Text;
using PcImpressz.Shell.Customization;

namespace PcImpressz.Core.Tests;

internal static class OperationSettingsTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("operation settings save and reload without putting secrets in the public file", () =>
        {
            using var dir = new TestDirectory();
            var store = new OperationSettingsStore(dir.Path);
            const string secret = "bizonylat-kulcs-123456";
            var changed = store.Save(new Dictionary<string, string>
            {
                ["bizonylat_url"] = "https://pelda.hu",
                ["szamla_elotag"] = "BOLT",
                ["szamla_email_kuldes"] = "false",
                ["bizonylat_kulcs"] = secret,
                ["nyomtato_port"] = "9101"
            });
            Check.True(changed.Contains("bizonylat_url") && changed.Contains("bizonylat_kulcs"));
            var settings = File.ReadAllText(Path.Combine(dir.Path, OperationSettingsStore.SettingsFileName));
            var secrets = File.ReadAllBytes(Path.Combine(dir.Path, OperationSettingsStore.SecretsFileName));
            Check.True(!settings.Contains(secret, StringComparison.Ordinal));
            Check.True(!Encoding.UTF8.GetString(secrets).Contains(secret, StringComparison.Ordinal));
            Check.True(!settings.Contains("nav-connection", StringComparison.Ordinal));
            var reloaded = new OperationSettingsStore(dir.Path);
            Check.Equal("https://pelda.hu", reloaded.Load().Values["bizonylat_url"]);
            Check.Equal("BOLT", reloaded.Load().Values["szamla_elotag"]);
            Check.Equal("false", reloaded.Load().Values["szamla_email_kuldes"]);
            Check.Equal("9101", reloaded.Load().Values["nyomtato_port"]);
            Check.True(reloaded.SecretEquals("bizonylat_kulcs", secret));
            Check.True(reloaded.Load().SecretHints["bizonylat_kulcs"].StartsWith("Be van állítva", StringComparison.Ordinal));
            Check.Equal(0, reloaded.Save(new Dictionary<string, string> { ["bizonylat_kulcs"] = "  " }).Count);
            Check.True(new OperationSettingsStore(dir.Path).SecretEquals("bizonylat_kulcs", secret));
            reloaded.Save(new Dictionary<string, string> { ["bizonylat_url"] = "" });
            Check.Equal("https://api.bizonylat.hu", new OperationSettingsStore(dir.Path).Load().Values["bizonylat_url"]);
            reloaded.Save(new Dictionary<string, string> { ["nyomtato_port"] = "0" });
            Check.Equal("9100", new OperationSettingsStore(dir.Path).Load().Values["nyomtato_port"]);
            Check.Throws<InvalidDataException>(() => reloaded.Save(new Dictionary<string, string> { ["smtp_port"] = "abc" }));
            Check.Equal("9100", new OperationSettingsStore(dir.Path).Load().Values["nyomtato_port"]);
            return Task.CompletedTask;
        });
        yield return ("planted secret is stripped and unknown settings stay", () =>
        {
            using var dir = new TestDirectory();
            var path = Path.Combine(dir.Path, OperationSettingsStore.SettingsFileName);
            File.WriteAllText(path, """
            {"schemaVersion":1,"values":{"szamla_elotag":"REGI","bizonylat_kulcs":"nem-ide","egyeb_megjegyzes":"megmarad"}}
            """);
            var store = new OperationSettingsStore(dir.Path);
            store.Save(new Dictionary<string, string> { ["szamla_elotag"] = "UJ" });
            var settings = File.ReadAllText(path);
            Check.True(settings.Contains("UJ", StringComparison.Ordinal));
            Check.True(settings.Contains("megmarad", StringComparison.Ordinal));
            Check.True(!settings.Contains("bizonylat_kulcs", StringComparison.Ordinal));
            Check.True(!settings.Contains("nem-ide", StringComparison.Ordinal));
            File.WriteAllText(path, "{");
            Check.Throws<InvalidDataException>(() => store.Save(new Dictionary<string, string> { ["szamla_elotag"] = "NEM" }));
            Check.Equal("{", File.ReadAllText(path));
            return Task.CompletedTask;
        });
        yield return ("local backup copies data and skips the lock and its own folder", () =>
        {
            using var data = new TestDirectory();
            using var outside = new TestDirectory();
            var store = new OperationSettingsStore(data.Path);
            File.WriteAllText(Path.Combine(data.Path, "jegyzet.txt"), "adat");
            File.WriteAllText(Path.Combine(data.Path, "host.lock"), "lock");
            var company = Path.Combine(data.Path, "companies", "ceg", "modules", "receiving");
            Directory.CreateDirectory(company);
            File.WriteAllBytes(Path.Combine(company, "nav-connection.bin"), "NAV-TITOK"u8.ToArray());
            var copied = store.CreateBackup(outside.Path);
            Check.Equal("adat", File.ReadAllText(Path.Combine(copied.Directory, "jegyzet.txt")));
            Check.Equal("NAV-TITOK", Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(copied.Directory, "companies", "ceg", "modules", "receiving", "nav-connection.bin"))));
            Check.True(!File.Exists(Path.Combine(copied.Directory, "host.lock")));
            Check.True(copied.Skipped.Contains("host.lock"));
            Check.True(File.Exists(Path.Combine(data.Path, "companies", "ceg", "modules", "receiving", "nav-connection.bin")));
            var nestedRoot = Path.Combine(data.Path, "mentesek");
            var nested = store.CreateBackup(nestedRoot);
            Check.True(File.Exists(Path.Combine(nested.Directory, "jegyzet.txt")));
            Check.True(!Directory.Exists(Path.Combine(nested.Directory, "mentesek")));
            Check.Equal("A mappa írható.", store.CheckFolder(outside.Path));
            Check.Throws<InvalidDataException>(() => store.Save(new Dictionary<string, string> { ["mentes_mappa"] = "relatív" }));
            return Task.CompletedTask;
        });
        yield return ("operation menu groups stay beside the shell pages and omit nav", () =>
        {
            Check.Equal("billing|printer|backup|wholesale|mail", string.Join("|", OperationCatalog.Groups.Select(group => group.Id)));
            Check.True(OperationCatalog.Groups.SelectMany(group => group.Sections).SelectMany(section => section.Fields)
                .All(field => !field.Key.Contains("nav", StringComparison.Ordinal) && field.Label != "Adószám"));
            using var dir = new TestDirectory();
            var loaded = new OperationSettingsStore(dir.Path).Load();
            Check.Equal("https://api.bizonylat.hu", loaded.Values["bizonylat_url"]);
            Check.Equal("https://webApi.hrp.hu/Gateway", loaded.Values["hrp_gateway_url"]);
            Check.Equal("587", loaded.Values["smtp_port"]);
            Check.Equal("Nincs beállítva.", loaded.SecretHints["smtp_jelszo"]);
            return Task.CompletedTask;
        });
    }
}
