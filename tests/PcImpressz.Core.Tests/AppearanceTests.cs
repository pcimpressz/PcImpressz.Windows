using System.Text.Json;
using PcImpressz.Contracts;
using PcImpressz.Shell.Customization;

namespace PcImpressz.Core.Tests;

internal static class AppearanceTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return Test("all built-in skins have readable contrast", () =>
        { foreach (var skin in BuiltInSkins.All) SkinValidation.Validate(skin); });
        yield return Test("appearance preferences survive restart", () =>
        {
            using var directory = new TestDirectory();
            var expected = new AppearancePreferences { SkinId = "paper", AccentOverride = "#78B2FF",
                CollapsedNavigation = true, CompactDensity = true, FontScale = 1.25, WindowWidth = 1280, WindowHeight = 800 };
            new AppearanceStore(directory.Path).Save(expected);
            Check.Equal(expected, new AppearanceStore(directory.Path).Load());
        });
        yield return Test("corrupt preferences fall back without destroying original file", () =>
        {
            using var directory = new TestDirectory();
            var path = Path.Combine(directory.Path, "appearance.json"); File.WriteAllText(path, "broken");
            var store = new AppearanceStore(directory.Path);
            Check.Equal(new AppearancePreferences(), store.Load());
            Check.True(store.LastWarning is not null); Check.Equal("broken", File.ReadAllText(path));
        });
        yield return Test("invalid preference values are not persisted", () =>
        {
            using var directory = new TestDirectory();
            var store = new AppearanceStore(directory.Path);
            Check.Throws<InvalidDataException>(() => store.Save(new() { AccentOverride = "javascript:bad" }));
            Check.Throws<InvalidDataException>(() => store.Save(new() { FontScale = double.NaN }));
            Check.Throws<InvalidDataException>(() => store.Save(new() { WindowWidth = -1 }));
        });
        yield return Test("custom skin export and import round-trip", () =>
        {
            using var directory = new TestDirectory();
            var store = new AppearanceStore(directory.Path);
            var skin = BuiltInSkins.All[0] with { Id = "sajat-skin", Name = "Saját skin" };
            var file = Path.Combine(directory.Path, "export.json");
            store.Export(file, skin); Check.Equal(skin, store.Import(file));
            Check.Equal(skin, new AppearanceStore(directory.Path).ReadSkins().Single(s => s.Id == skin.Id));
        });
        yield return Test("skin path traversal is rejected", () =>
        {
            using var directory = new TestDirectory();
            var store = new AppearanceStore(directory.Path);
            Check.Throws<InvalidDataException>(() => store.Export(Path.Combine(directory.Path, "bad.json"),
                BuiltInSkins.All[0] with { Id = "../outside" }));
        });
        yield return Test("unreadable custom palette is rejected", () =>
        {
            var skin = BuiltInSkins.All[0];
            Check.Throws<InvalidDataException>(() => SkinValidation.Validate(skin with
            { Palette = skin.Palette with { Text = skin.Palette.Background } }));
        });
        yield return Test("skin import cannot execute XAML or extra properties", () =>
        {
            using var directory = new TestDirectory();
            var file = Path.Combine(directory.Path, "bad.json");
            var skin = BuiltInSkins.All[0] with { Id = "custom" };
            var json = JsonSerializer.Serialize(skin, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            File.WriteAllText(file, json[..^1] + ",\"xaml\":\"executable\"}");
            Check.Throws<JsonException>(() => new AppearanceStore(directory.Path).Import(file));
        });
        yield return Test("built-in skin cannot be overwritten by import", () =>
        {
            using var directory = new TestDirectory();
            var store = new AppearanceStore(directory.Path);
            var file = Path.Combine(directory.Path, "builtin.json"); store.Export(file, BuiltInSkins.All[0]);
            Check.Throws<InvalidDataException>(() => store.Import(file));
        });
        yield return Test("accent button label retains readable contrast", () =>
        {
            foreach (var color in new[] { "#38D9BC", "#007F71", "#B6A0FF", "#FFFFFF", "#000000" })
                Check.True(SkinValidation.Contrast(color, SkinValidation.AccentForeground(color)) >= 4.5);
        });
        yield return Test("diagnostic channel follows the data directory, not source location", () =>
        {
            var directory = Path.GetFullPath(".");
            Check.Equal(DiagnosticChannel.ForDirectory(directory), DiagnosticChannel.ForDirectory(directory + Path.DirectorySeparatorChar));
            Check.True(DiagnosticChannel.ForDirectory(directory) != DiagnosticChannel.ForDirectory(Path.Combine(directory, "other")));
        });
    }
    private static (string, Func<Task>) Test(string name, Action test) => (name, () => { test(); return Task.CompletedTask; });
}
