// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Application;
using Wonder.Core;
using Wonder.Infrastructure;

namespace Wonder.Game.Tests;

public sealed class WonderOfficialProfileIdentityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Changed_profile_version_preserves_existing_registration(bool reimport, bool legacyProfileId)
    {
        using var fixture = new ImporterFixtures();
        fixture.Version("1.20.1");
        WriteProfile(fixture, "1.20.1");
        var original = Assert.Single((await new InstanceDiscovery().DiscoverAsync(fixture.DirectoryAt(".minecraft"))).Instances) with
        {
            Id = "existing-wonder-id", Name = "我的世界", Overrides = new() { MaximumMemoryMb = 8192 },
            SourceProfileId = legacyProfileId ? null : "survival"
        };
        var store = new Store(original);
        var service = Create(fixture, store);
        fixture.Version("1.21.1");
        WriteProfile(fixture, "1.21.1", "-Xmx3G -Dupdated=true");

        if (reimport) await service.ImportAsync(Path.Combine(original.SourcePath, ".") + Path.DirectorySeparatorChar);
        else await service.RefreshAsync(original.Id);

        Assert.Null(service.Error);
        service.Reload();
        var updated = Assert.Single(service.Catalog.Instances, item => item.SourceProfileId == "survival");
        Assert.Equal(original.Id, updated.Id);
        Assert.Equal("我的世界", updated.Name);
        Assert.Equal(original.Overrides, updated.Overrides);
        Assert.Equal(original.Id, service.Catalog.SelectedInstanceId);
        Assert.Equal("1.21.1", updated.VersionId);
        Assert.Equal("1.21.1", updated.GameVersion);
        Assert.Equal(3072, updated.ImportedSettings.MaximumMemoryMb);
        Assert.Equal("-Xmx3G -Dupdated=true", updated.ImportedSettings.JvmArguments);
        Assert.Equal(Path.Combine(fixture.Root, ".minecraft", "versions", "1.21.1", "1.21.1.json"), Assert.Single(updated.MetadataPaths));

        await service.ImportAsync(original.SourcePath);
        Assert.Null(service.Error);
        Assert.Single(service.Catalog.Instances, item => item.SourceProfileId == "survival");
        Assert.Equal(original.Id, service.Catalog.SelectedInstanceId);
    }

    [Fact]
    public async Task Profiles_sharing_a_version_remain_independent_when_one_changes_version()
    {
        using var fixture = new ImporterFixtures();
        fixture.Version("1.20.1");
        fixture.Json(".minecraft/launcher_profiles.json", new { profiles = new { first = new { lastVersionId = "1.20.1" }, second = new { lastVersionId = "1.20.1" } } });
        var service = Create(fixture, new Store());
        await service.ImportAsync(fixture.DirectoryAt(".minecraft"));
        var first = service.Catalog.Instances.Single(item => item.SourceProfileId == "first");
        var second = service.Catalog.Instances.Single(item => item.SourceProfileId == "second");
        service.Select(second.Id);
        service.UpdateInstance(first.Id, "第一个存档", new() { MaximumMemoryMb = 6144 });
        fixture.Version("1.21.1");
        fixture.Json(".minecraft/launcher_profiles.json", new { profiles = new { first = new { lastVersionId = "1.21.1" }, second = new { lastVersionId = "1.20.1" } } });

        await service.RefreshAsync(first.Id);
        await service.ImportAsync(first.SourcePath);

        Assert.Null(service.Error);
        Assert.Equal(2, service.Catalog.Instances.Count);
        var changed = service.Catalog.Instances.Single(item => item.Id == first.Id);
        Assert.Equal("1.21.1", changed.VersionId);
        Assert.Equal("第一个存档", changed.Name);
        Assert.Equal(6144, changed.Overrides.MaximumMemoryMb);
        Assert.Equal("1.20.1", service.Catalog.Instances.Single(item => item.Id == second.Id).VersionId);
        Assert.Equal(second.Id, service.Catalog.SelectedInstanceId);
    }

    [Fact]
    public async Task Standalone_version_directories_remain_distinct_from_each_other_and_profiles()
    {
        using var fixture = new ImporterFixtures();
        var firstPath = fixture.Version("1.20.1");
        var secondPath = fixture.Version("1.21.1");
        var service = Create(fixture, new Store());
        await service.ImportAsync(firstPath);
        await service.ImportAsync(secondPath);
        WriteProfile(fixture, "1.20.1");
        await service.ImportAsync(fixture.DirectoryAt(".minecraft"));
        await service.ImportAsync(secondPath);

        Assert.Null(service.Error);
        Assert.Equal(3, service.Catalog.Instances.Count);
        Assert.Single(service.Catalog.Instances, item => item.SourceProfileId == "survival");
        Assert.Equal(2, service.Catalog.Instances.Count(item => item.SourceProfileId is null));
        Assert.Equal(3, service.Catalog.Instances.Select(item => item.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("prismlauncher.cfg", InstanceSource.Prism)]
    [InlineData("multimc.cfg", InstanceSource.MultiMc)]
    public async Task Pack_version_changes_preserve_registration(string config, InstanceSource expectedSource)
    {
        using var fixture = new ImporterFixtures();
        fixture.Write("packs/" + config, "[General]\nMaxMemAlloc=2048\n");
        fixture.Write("packs/instances/world/instance.cfg", "[General]\nname=World\n");
        fixture.Json("packs/instances/world/mmc-pack.json", new { formatVersion = 1, components = new[] { new { uid = "net.minecraft", version = "1.20.1" } } });
        var service = Create(fixture, new Store());
        await service.ImportAsync(fixture.DirectoryAt("packs"));
        var original = Assert.Single(service.Catalog.Instances);
        service.Rename(original.Id, "整合包");
        fixture.Json("packs/instances/world/mmc-pack.json", new { formatVersion = 1, components = new[] { new { uid = "net.minecraft", version = "1.21.1" } } });

        await service.RefreshAsync(original.Id);
        await service.ImportAsync(original.SourcePath);

        Assert.Null(service.Error);
        var updated = Assert.Single(service.Catalog.Instances);
        Assert.Equal(original.Id, updated.Id);
        Assert.Equal("整合包", updated.Name);
        Assert.Equal("1.21.1", updated.VersionId);
        Assert.Equal(expectedSource, updated.Source);
    }

    [Fact]
    public async Task Explicit_refresh_updates_only_the_requested_existing_duplicate()
    {
        using var fixture = new ImporterFixtures();
        fixture.Version("1.20.1");
        WriteProfile(fixture, "1.20.1");
        var original = Assert.Single((await new InstanceDiscovery().DiscoverAsync(fixture.DirectoryAt(".minecraft"))).Instances);
        var first = original with { Id = "first-old-id", Name = "保留记录", Overrides = new() { MaximumMemoryMb = 2048 } };
        var second = original with { Id = "second-old-id", Name = "刷新记录", Overrides = new() { MaximumMemoryMb = 8192 } };
        var service = Create(fixture, new Store(first, second));
        service.Select(second.Id);
        fixture.Version("1.21.1");
        WriteProfile(fixture, "1.21.1");

        await service.RefreshAsync(second.Id);

        Assert.Null(service.Error);
        Assert.Equal(2, service.Catalog.Instances.Count);
        Assert.Equal("1.20.1", service.Catalog.Instances.Single(item => item.Id == first.Id).VersionId);
        Assert.Equal(first.Overrides, service.Catalog.Instances.Single(item => item.Id == first.Id).Overrides);
        var updated = service.Catalog.Instances.Single(item => item.Id == second.Id);
        Assert.Equal("1.21.1", updated.VersionId);
        Assert.Equal(second.Name, updated.Name);
        Assert.Equal(second.Overrides, updated.Overrides);
        Assert.Equal(second.Id, service.Catalog.SelectedInstanceId);
    }

    [Fact]
    public async Task Reimport_with_ambiguous_existing_profiles_preserves_all_records()
    {
        using var fixture = new ImporterFixtures();
        fixture.Version("1.20.1");
        WriteProfile(fixture, "1.20.1");
        var original = Assert.Single((await new InstanceDiscovery().DiscoverAsync(fixture.DirectoryAt(".minecraft"))).Instances);
        var store = new Store(original with { Id = "first-old-id", Overrides = new() { MaximumMemoryMb = 2048 } },
            original with { Id = "second-old-id", Overrides = new() { MaximumMemoryMb = 8192 } });
        var service = Create(fixture, store);
        fixture.Version("1.21.1");
        WriteProfile(fixture, "1.21.1");

        await service.ImportAsync(original.SourcePath);

        Assert.Contains("多个", service.Error);
        Assert.Equal(0, store.Saves);
        Assert.Equal(2, service.Catalog.Instances.Count);
        Assert.All(service.Catalog.Instances, item => Assert.Equal("1.20.1", item.VersionId));
        Assert.Equal(new int?[] { 2048, 8192 }, service.Catalog.Instances.Select(item => item.Overrides.MaximumMemoryMb));
    }

    [Fact]
    public async Task Reimport_prefers_exact_id_over_another_record_with_the_same_profile()
    {
        using var fixture = new ImporterFixtures();
        fixture.Version("1.20.1");
        WriteProfile(fixture, "1.20.1");
        var original = Assert.Single((await new InstanceDiscovery().DiscoverAsync(fixture.DirectoryAt(".minecraft"))).Instances);
        var store = new Store(original with { Id = "older-wonder-id", Overrides = new() { MaximumMemoryMb = 2048 } },
            original with { Overrides = new() { MaximumMemoryMb = 8192 } });
        var service = Create(fixture, store);
        WriteProfile(fixture, "1.20.1", "-Xmx3G");

        await service.ImportAsync(original.SourcePath);

        Assert.Null(service.Error);
        Assert.Equal(2, service.Catalog.Instances.Count);
        Assert.Null(service.Catalog.Instances.Single(item => item.Id == "older-wonder-id").ImportedSettings.MaximumMemoryMb);
        var updated = service.Catalog.Instances.Single(item => item.Id == original.Id);
        Assert.Equal(3072, updated.ImportedSettings.MaximumMemoryMb);
        Assert.Equal(8192, updated.Overrides.MaximumMemoryMb);
    }

    private static void WriteProfile(ImporterFixtures fixture, string version, string? arguments = null) =>
        fixture.Json(".minecraft/launcher_profiles.json", new { profiles = new { survival = new { name = "外部名称", lastVersionId = version, javaArgs = arguments } } });

    private static InstanceService Create(ImporterFixtures fixture, Store store) =>
        new(store, new InstanceDiscovery(), new UnusedLauncher(), Path.Combine(fixture.Root, "wonder", "cache"));

    private sealed class Store(params GameInstance[] instances) : IInstanceStore
    {
        private InstanceCatalog _catalog = new() { Instances = instances.ToList(), SelectedInstanceId = instances.FirstOrDefault()?.Id };
        public int Saves;
        public InstanceCatalog Load() => _catalog;
        public void Save(InstanceCatalog catalog) { _catalog = catalog; Saves++; }
    }

    private sealed class UnusedLauncher : IGameLauncher
    {
        public Task<LaunchPlan> BuildPlanAsync(LaunchRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> RunAsync(LaunchPlan plan, Action started, Action<string> output, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
