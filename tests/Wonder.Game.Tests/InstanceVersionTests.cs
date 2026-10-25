// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;
using Wonder.Core;
using Wonder.Infrastructure;

namespace Wonder.Game.Tests;

public sealed class InstanceVersionTests
{
    [Fact]
    public async Task Client_version_identifies_custom_pack_without_changing_launch_identity()
    {
        using var f = new ImporterFixtures(); var dir = f.Version("我的整合包");
        f.Json(".minecraft/versions/我的整合包/我的整合包.json", new { id = "1.12.2", clientVersion = "1.21.1", inheritsFrom = "1.20.1" });
        f.Write(".minecraft/versions/我的整合包/PCL/Setup.ini", "VersionVanillaName:1.19.4\n");
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(dir)).Instances);
        Assert.Equal("1.21.1", instance.GameVersion);
        Assert.Equal("Minecraft 1.21.1", instance.VersionLabel);
        Assert.Equal("我的整合包", instance.Name);
        Assert.Equal("我的整合包", instance.VersionId);
    }

    [Fact]
    public async Task Inheritance_follows_custom_parent_to_vanilla_metadata()
    {
        using var f = new ImporterFixtures(); var dir = f.Version("我的整合包");
        f.Json(".minecraft/versions/我的整合包/我的整合包.json", new { id = "我的整合包", inheritsFrom = "custom-loader" });
        f.Json(".minecraft/versions/custom-loader/custom-loader.json", new { id = "custom-loader", inheritsFrom = "vanilla-base" });
        f.Json(".minecraft/versions/vanilla-base/vanilla-base.json", new { id = "1.20.1" });
        Assert.Equal("1.20.1", Assert.Single((await new InstanceDiscovery().DiscoverAsync(dir)).Instances).GameVersion);
    }

    [Fact]
    public async Task Missing_vanilla_parent_metadata_still_uses_exact_parent_version()
    {
        using var f = new ImporterFixtures(); var dir = f.Version("我的整合包");
        f.Json(".minecraft/versions/我的整合包/我的整合包.json", new { inheritsFrom = "1.20.1" });
        Assert.Equal("1.20.1", Assert.Single((await new InstanceDiscovery().DiscoverAsync(dir)).Instances).GameVersion);
    }

    [Theory]
    [InlineData("1.20.1", "1.20.1")]
    [InlineData("1.21", "1.21")]
    [InlineData("24w14a", "24w14a")]
    [InlineData("1.21-pre1", "1.21-pre1")]
    [InlineData("1.21-rc1", "1.21-rc1")]
    [InlineData("1.20.1-自定义整合包", "")]
    [InlineData("fabric-loader-0.16.9-1.21.1", "")]
    [InlineData("1.20.1 forge", "")]
    [InlineData("我的整合包", "")]
    public async Task Folder_version_fallback_requires_an_entire_vanilla_id(string id, string expected)
    {
        using var f = new ImporterFixtures(); var dir = f.Version(id);
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(dir)).Instances);
        Assert.Equal(expected, instance.GameVersion);
        if (expected.Length == 0) Assert.Equal("版本待识别", instance.VersionLabel);
    }

    [Fact]
    public async Task Pcl_cached_vanilla_name_is_used_when_custom_metadata_has_no_version()
    {
        using var f = new ImporterFixtures(); var dir = f.Version("我的整合包");
        f.Write(".minecraft/versions/我的整合包/PCL/Setup.ini", "VersionVanillaName:1.20.1\n");
        Assert.Equal("1.20.1", Assert.Single((await new InstanceDiscovery().DiscoverAsync(dir)).Instances).GameVersion);
    }

    [Fact]
    public async Task Inheritance_cycle_leaves_version_unknown_and_instance_manageable()
    {
        using var f = new ImporterFixtures(); var dir = f.Version("one");
        f.Json(".minecraft/versions/one/one.json", new { inheritsFrom = "two" });
        f.Json(".minecraft/versions/two/two.json", new { inheritsFrom = "one" });
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(dir)).Instances);
        Assert.Equal("", instance.GameVersion);
        Assert.Equal("版本待识别", instance.VersionLabel);
    }

    [Fact]
    public async Task Prism_uses_enabled_minecraft_component_even_when_metadata_is_missing()
    {
        using var f = new ImporterFixtures(); var dir = f.Prism();
        f.Json("Prism/instances/Pack/mmc-pack.json", new { formatVersion = 1, components = new[]
        {
            new { uid = "net.minecraft", version = "1.19.4", disabled = true },
            new { uid = "net.minecraft", version = "1.21.1", disabled = false },
            new { uid = "net.fabricmc.fabric-loader", version = "0.16.9", disabled = false }
        } });
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(dir)).Instances);
        Assert.Equal("1.21.1", instance.GameVersion);
        Assert.Equal("测试整合包", instance.Name);
    }

    [Fact]
    public void Legacy_catalog_hydrates_version_without_writing_catalog_or_source_or_losing_user_choices()
    {
        using var f = new ImporterFixtures(); var dir = f.Version("自定义文件夹");
        var metadata = f.Json(".minecraft/versions/自定义文件夹/自定义文件夹.json", new { id = "自定义文件夹", clientVersion = "1.21.1" });
        var instance = InstanceSettingsTests.ValidInstance() with
        {
            Id = "preserved-id", Name = "用户重命名", VersionId = "自定义文件夹", Source = InstanceSource.Pcl,
            SourcePath = dir, VersionsDirectory = f.DirectoryAt(".minecraft/versions"), MetadataPaths = [metadata],
            ImportedSettings = new() { WindowWidth = 1280 }, Overrides = new() { MaximumMemoryMb = 6144 }
        };
        var path = WriteLegacyCatalog(f, instance);
        var catalogBytes = File.ReadAllBytes(path); var sourceBytes = File.ReadAllBytes(metadata);
        var loaded = new InstanceStore(path).Load(); var hydrated = Assert.Single(loaded.Instances);
        Assert.Equal("1.21.1", hydrated.GameVersion);
        Assert.Equal(instance.Id, hydrated.Id);
        Assert.Equal(instance.Name, hydrated.Name);
        Assert.Equal(instance.VersionId, hydrated.VersionId);
        Assert.Equal(instance.ImportedSettings, hydrated.ImportedSettings);
        Assert.Equal(instance.Overrides, hydrated.Overrides);
        Assert.Equal(instance.Id, loaded.SelectedInstanceId);
        Assert.Equal("PlayerOne", loaded.OfflineUsername);
        Assert.Equal(catalogBytes, File.ReadAllBytes(path));
        Assert.Equal(sourceBytes, File.ReadAllBytes(metadata));
    }

    [Fact]
    public void Legacy_prism_catalog_hydrates_version_from_pack_even_without_component_metadata_paths()
    {
        using var f = new ImporterFixtures(); var dir = f.Prism();
        var instance = InstanceSettingsTests.ValidInstance() with { SourcePath = dir, Source = InstanceSource.Prism, MetadataPaths = [] };
        var path = WriteLegacyCatalog(f, instance);
        Assert.Equal("1.20.1", Assert.Single(new InstanceStore(path).Load().Instances).GameVersion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{broken")]
    [InlineData("[]")]
    [InlineData("{\"clientVersion\":123,\"inheritsFrom\":\"../outside\"}")]
    public void Legacy_catalog_tolerates_missing_or_malformed_metadata(string? json)
    {
        using var f = new ImporterFixtures();
        var metadata = Path.Combine(f.Root, "source", "custom.json");
        if (json is not null) f.Write("source/custom.json", json);
        var instance = InstanceSettingsTests.ValidInstance() with { VersionId = "custom-pack", MetadataPaths = [metadata] };
        var path = WriteLegacyCatalog(f, instance); var bytes = File.ReadAllBytes(path);
        var loaded = Assert.Single(new InstanceStore(path).Load().Instances);
        Assert.Equal("", loaded.GameVersion);
        Assert.Equal("版本待识别", loaded.VersionLabel);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Catalog_keeps_known_game_version_when_source_metadata_is_unavailable()
    {
        using var f = new ImporterFixtures(); var instance = InstanceSettingsTests.ValidInstance() with { GameVersion = "1.21.1" };
        var store = new InstanceStore(Path.Combine(f.Root, "catalog.json"));
        store.Save(new() { Instances = [instance] });
        Assert.Equal("1.21.1", Assert.Single(store.Load().Instances).GameVersion);
    }

    private static string WriteLegacyCatalog(ImporterFixtures fixture, GameInstance instance)
    {
        var catalog = new InstanceCatalog { Instances = [instance], SelectedInstanceId = instance.Id, OfflineUsername = "PlayerOne" };
        var json = JsonSerializer.SerializeToNode(catalog, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IgnoreReadOnlyProperties = true })!;
        json["instances"]![0]!.AsObject().Remove("gameVersion");
        return fixture.Write("catalog.json", json.ToJsonString());
    }
}
