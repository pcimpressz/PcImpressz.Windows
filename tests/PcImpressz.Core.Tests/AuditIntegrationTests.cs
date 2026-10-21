using System.Text.Json;
using PcImpressz.Contracts;
using PcImpressz.Modules.Partners;
using PcImpressz.Modules.Catalog;

namespace PcImpressz.Core.Tests;

internal static class AuditIntegrationTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("standalone partner and catalog modules expose no external import commands", async () =>
        {
            foreach (var module in new IModule[] { new PartnersModule(), new CatalogModule() })
            {
                var probe = new Probe();
                await module.StartAsync(probe, default);
                Check.True(probe.Commands.Count > 0);
                Check.True(probe.Commands.All(c => !c.Name.Contains("import", StringComparison.OrdinalIgnoreCase)));
                Check.True(probe.Pages.All(p => p.Commands.ImportPreview is null && p.Commands.ImportCommit is null));
                await module.StopAsync(default);
            }
        });
        yield return ("retired external provenance survives partner editing", async () =>
        {
            using var dir = new TestDirectory();
            var company = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var record = new PartnerRecord(Guid.NewGuid(), 1, new PartnerData { Name = "Régi partner" },
                now, "operator", now, "operator", "legacy-offline-origin", 42);
            File.WriteAllText(Path.Combine(dir.Path, "partners.json"), JsonSerializer.Serialize(
                new PartnerState(1, 1, [record], []), PartnerJson.Options));
            var repository = new PartnerRepository(_ => dir.Path, TimeProvider.System);
            var saved = await repository.Save(new(company, record.Id, 1, record.Data with { Name = "Módosított partner" }), "operator");
            Check.Equal(record.Source!, saved.Source!);
            Check.Equal(42, saved.ExternalId!.Value);
            Check.Equal("Módosított partner", (await repository.Snapshot(company)).Partners.Single().Data.Name);
        });
        yield return ("oversized legacy write is rejected before replacing readable data", () =>
        {
            using var dir = new TestDirectory();
            var path = Path.Combine(dir.Path, "partners.json");
            File.WriteAllText(path, "{\"original\":true}");
            var scope = new ModuleStorageScope(dir.Path, null);
            Check.Throws<IOException>(() => scope.Write("partners", new string('x', 64 * 1024 * 1024 + 1)));
            Check.Equal("{\"original\":true}", scope.Read("partners", "partners.json")!);
            return Task.CompletedTask;
        });
    }

    private sealed class Probe : IModuleContext
    {
        public string ModuleId => "audit";
        public TimeProvider TimeProvider => TimeProvider.System;
        public List<CommandDescriptor> Commands { get; } = [];
        public List<EntityPageDescriptor> Pages { get; } = [];
        public void RegisterCommand(ICommandHandler handler) => Commands.Add(handler.Descriptor);
        public void RegisterPage(EntityPageDescriptor page) => Pages.Add(page);
        public string GetCompanyDataDirectory(Guid companyId) => throw new NotSupportedException();
    }
}
