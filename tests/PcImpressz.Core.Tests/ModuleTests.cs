using System.Text.Json;
using PcImpressz.Contracts;
using PcImpressz.Core.Commands;
using PcImpressz.Core.Modules;

namespace PcImpressz.Core.Tests;

internal static class ModuleTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return Case("empty module directory starts", async f =>
        { await f.Runtime.StartAsync(f.Directory.Path); Check.Equal(0, f.Runtime.Reports.Count); });
        yield return Case("signed module loads and executes licensed command", async f =>
        {
            f.Package(); await f.Runtime.StartAsync(f.Directory.Path);
            Check.Equal(ModuleStatus.Active, f.Runtime.Reports.Single().Status);
            var result = await f.Dispatcher.ExecuteAsync(Request("write"), Identity("probe.write"));
            Check.True(result.GetProperty("success").GetBoolean());
        });
        yield return Case("module write cannot bypass missing license", async f =>
        {
            f.Package(); f.Crypto.Tokens = null; await f.Runtime.StartAsync(f.Directory.Path);
            await Check.ThrowsAsync<CommandDeniedException>(async () =>
                await f.Dispatcher.ExecuteAsync(Request("write"), Identity("probe.write")));
        });
        yield return Case("user permission required even with valid license", async f =>
        {
            f.Package(); await f.Runtime.StartAsync(f.Directory.Path);
            await Check.ThrowsAsync<CommandDeniedException>(async () =>
                await f.Dispatcher.ExecuteAsync(Request("write"), Identity()));
        });
        yield return Case("expired module still permits authorized recovery export", async f =>
        {
            f.Package(); await f.Runtime.StartAsync(f.Directory.Path);
            f.Crypto.Clock.Now = f.Crypto.Clock.Now.AddHours(2);
            await Check.ThrowsAsync<CommandDeniedException>(async () =>
                await f.Dispatcher.ExecuteAsync(Request("write"), Identity("probe.write")));
            Check.True((await f.Dispatcher.ExecuteAsync(Request("export"), Identity("probe.export")))
                .GetProperty("success").GetBoolean());
            await Check.ThrowsAsync<CommandDeniedException>(async () =>
                await f.Dispatcher.ExecuteAsync(Request("export"), Identity()));
        });
        yield return Case("unsigned module is rejected", async f =>
        {
            f.Package(); File.Delete(Path.Combine(f.Directory.Path, "probe", "manifest.jws"));
            await f.Runtime.StartAsync(f.Directory.Path);
            Check.Equal(ModuleStatus.Rejected, f.Runtime.Reports.Single().Status);
        });
        yield return Case("modified assembly is rejected before loading", async f =>
        {
            f.Package(); File.AppendAllText(Path.Combine(f.Directory.Path, "probe", "PcImpressz.TestModule.dll"), "tamper");
            await f.Runtime.StartAsync(f.Directory.Path);
            Check.Equal(ModuleStatus.Rejected, f.Runtime.Reports.Single().Status);
        });
        yield return Case("unlisted dependency file is rejected", async f =>
        {
            f.Package(); File.WriteAllText(Path.Combine(f.Directory.Path, "probe", "unlisted.dll"), "bad");
            await f.Runtime.StartAsync(f.Directory.Path);
            Check.Equal(ModuleStatus.Rejected, f.Runtime.Reports.Single().Status);
        });
        yield return Case("signed path traversal is rejected", async f =>
        {
            var manifest = f.Package();
            f.SaveManifest("probe", manifest with { Files = new() { ["../outside.dll"] = new string('0', 64) } });
            await f.Runtime.StartAsync(f.Directory.Path);
            Check.Equal(ModuleStatus.Rejected, f.Runtime.Reports.Single().Status);
        });
        yield return Case("incompatible contract version is rejected", async f =>
        {
            f.Package(apiVersion: "99.0"); await f.Runtime.StartAsync(f.Directory.Path);
            Check.Equal(ModuleStatus.Rejected, f.Runtime.Reports.Single().Status);
        });
        yield return Case("missing dependency blocks module", async f =>
        {
            f.Package(dependencies: [Dependency("missing")]); await f.Runtime.StartAsync(f.Directory.Path);
            Check.Equal(ModuleStatus.Blocked, f.Runtime.Reports.Single().Status);
        });
        yield return Case("dependencies load before dependent modules", async f =>
        {
            f.Package("aaa-dependent", [Dependency("zzz-base")]); f.Package("zzz-base");
            await f.Runtime.StartAsync(f.Directory.Path);
            Check.Equal("zzz-base", f.Runtime.Reports[0].Id);
            Check.True(f.Runtime.Reports.All(r => r.Status == ModuleStatus.Active));
        });
        yield return Case("licensed dependent cannot bypass an unlicensed required module", async f =>
        {
            f.Package("probe", [Dependency("base")]); f.Package("base");
            await f.Runtime.StartAsync(f.Directory.Path);
            await Check.ThrowsAsync<CommandDeniedException>(async () =>
                await f.Dispatcher.ExecuteAsync(Request("write"), Identity("probe.write")));
        });
        yield return Case("licensed dependency permits dependent command", async f =>
        {
            f.Package("probe", [Dependency("base")]); f.Package("base");
            f.Crypto.License = f.Crypto.License with
            {
                Modules = [f.Crypto.License.Modules[0], new()
                { ModuleId = "base", Capabilities = ["base.read"], ExpiresAt = f.Crypto.License.Exp }]
            };
            f.Crypto.Refresh();
            await f.Runtime.StartAsync(f.Directory.Path);
            Check.True((await f.Dispatcher.ExecuteAsync(Request("write"), Identity("probe.write")))
                .GetProperty("success").GetBoolean());
        });
        yield return Case("transitive dependencies also require module entitlement", async f =>
        {
            f.Package("probe", [Dependency("middle")]); f.Package("middle", [Dependency("base")]); f.Package("base");
            f.Crypto.License = f.Crypto.License with
            {
                Modules = [f.Crypto.License.Modules[0], new()
                { ModuleId = "middle", Capabilities = ["middle.read"], ExpiresAt = f.Crypto.License.Exp }]
            };
            f.Crypto.Refresh();
            await f.Runtime.StartAsync(f.Directory.Path);
            await Check.ThrowsAsync<CommandDeniedException>(async () =>
                await f.Dispatcher.ExecuteAsync(Request("write"), Identity("probe.write")));
        });
        yield return Case("one expired module does not disable another module", async f =>
        {
            f.Package("probe"); f.Package("other");
            f.Crypto.License = f.Crypto.License with
            {
                Modules = [f.Crypto.License.Modules[0], new()
                { ModuleId = "other", Capabilities = ["probe.write"], ExpiresAt = f.Crypto.Clock.Now.ToUnixTimeSeconds() }]
            };
            f.Crypto.Refresh();
            await f.Runtime.StartAsync(f.Directory.Path);
            Check.True((await f.Dispatcher.ExecuteAsync(Request("write"), Identity("probe.write")))
                .GetProperty("success").GetBoolean());
            var other = Request("write") with { ModuleId = "other" };
            await Check.ThrowsAsync<CommandDeniedException>(async () =>
                await f.Dispatcher.ExecuteAsync(other, Identity("probe.write")));
        });
        yield return Case("cyclic dependencies block both modules", async f =>
        {
            f.Package("one", [Dependency("two")]); f.Package("two", [Dependency("one")]);
            await f.Runtime.StartAsync(f.Directory.Path);
            Check.Equal(2, f.Runtime.Reports.Count);
            Check.True(f.Runtime.Reports.All(r => r.Status == ModuleStatus.Blocked));
        });
        yield return Case("duplicate module ID quarantines both packages", async f =>
        {
            var manifest = f.Package("one"); f.Package("two"); f.SaveManifest("two", manifest);
            await f.Runtime.StartAsync(f.Directory.Path);
            Check.Equal(2, f.Runtime.Reports.Count);
            Check.True(f.Runtime.Reports.All(r => r.Status == ModuleStatus.Rejected));
        });
        yield return Case("failed initialization publishes no partial commands", async f =>
        {
            f.Package(entryType: "PcImpressz.TestModule.FailingModule");
            await f.Runtime.StartAsync(f.Directory.Path);
            Check.Equal(ModuleStatus.Failed, f.Runtime.Reports.Single().Status);
            await Check.ThrowsAsync<CommandDeniedException>(async () =>
                await f.Dispatcher.ExecuteAsync(Request("write"), Identity("probe.write")));
        });
        yield return Case("shutdown unregisters module commands", async f =>
        {
            f.Package(); await f.Runtime.StartAsync(f.Directory.Path); await f.Runtime.DisposeAsync();
            await Check.ThrowsAsync<CommandDeniedException>(async () =>
                await f.Dispatcher.ExecuteAsync(Request("write"), Identity("probe.write")));
        });
    }

    private static ModuleDependency Dependency(string moduleId) => new()
        { ModuleId = moduleId, MinimumVersion = "1.0.0", MaximumVersionExclusive = "2.0.0" };
    private static CommandRequest Request(string name) => new("probe", name, JsonSerializer.SerializeToElement(new { }));
    private static CallerIdentity Identity(params string[] permissions) => new("operator", permissions.ToHashSet(StringComparer.Ordinal));
    private static (string, Func<Task>) Case(string name, Func<RuntimeFixture, Task> test) => (name, async () =>
    { await using var fixture = new RuntimeFixture(); await test(fixture); });
}
