// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text.Json;
using Wonder.Core;
using Wonder.Infrastructure;

namespace Wonder.Game.Tests;

public sealed class InstanceModLoaderTests
{
    [Theory]
    [InlineData("net.fabricmc:fabric-loader:0.16.9", "Fabric 0.16.9")]
    [InlineData("org.quiltmc:quilt-loader:0.27.1", "Quilt 0.27.1")]
    [InlineData("net.minecraftforge:forge:1.20.1-47.3.0", "Forge 47.3.0")]
    [InlineData("net.minecraftforge:fmlloader:1.20.1-47.3.0", "Forge 47.3.0")]
    [InlineData("net.minecraftforge:forge:1.7.10-10.13.4.1614-1.7.10", "Forge 10.13.4.1614")]
    [InlineData("net.neoforged:neoforge:21.1.172:universal", "NeoForge 21.1.172")]
    [InlineData("net.neoforged:forge:1.20.1-47.1.106", "NeoForge 47.1.106")]
    [InlineData("optifine:OptiFine:1.20.1_HD_U_I6", "OptiFine HD_U_I6")]
    public async Task Standard_summary_reads_loader_library_coordinates(string library, string expected)
    {
        using var f = new ImporterFixtures(); var dir = f.Version("自定义整合包");
        f.Json(".minecraft/versions/自定义整合包/自定义整合包.json", new { clientVersion = "1.20.1", libraries = new[] { new { name = library } } });
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(dir)).Instances);
        Assert.Equal("Minecraft 1.20.1 · " + expected, instance.VersionLabel);
        var loader = Assert.Single(instance.ModLoaders);
        Assert.Equal(expected, loader.Name + " " + loader.Version);
    }

    [Fact]
    public async Task Inherited_metadata_deduplicates_loader_and_keeps_child_version()
    {
        using var f = new ImporterFixtures(); var dir = f.Version("Pack");
        f.Json(".minecraft/versions/Pack/Pack.json", new { clientVersion = "1.20.1", inheritsFrom = "base", libraries = new[]
        {
            new { name = "net.minecraftforge:fmlloader:1.20.1-47.3.0" },
            new { name = "net.minecraftforge:forge:1.20.1-47.3.0:universal" }
        } });
        f.Json(".minecraft/versions/base/base.json", new { inheritsFrom = "Pack", libraries = new[]
        {
            new { name = "net.minecraftforge:forge:1.20.1-47.2.0" },
            new { name = "optifine:OptiFine:1.20.1_HD_U_I6" }
        } });
        Assert.Equal("Minecraft 1.20.1 · Forge 47.3.0 · OptiFine HD_U_I6", Assert.Single((await new InstanceDiscovery().DiscoverAsync(dir)).Instances).VersionLabel);
    }

    [Fact]
    public async Task NeoForge_argument_is_authoritative_and_forge_support_libraries_do_not_add_a_second_loader()
    {
        using var f = new ImporterFixtures(); var dir = f.Version("Pack");
        f.Json(".minecraft/versions/Pack/Pack.json", new { clientVersion = "1.21.1", arguments = new { game = new[] { "--fml.neoForgeVersion", "21.1.172" } }, libraries = new[]
        {
            new { name = "net.neoforged.fancymodloader:loader:4.0.39" },
            new { name = "net.minecraftforge:fmlloader:1.20.1-47.1.0" }
        } });
        Assert.Equal("Minecraft 1.21.1 · NeoForge 21.1.172", Assert.Single((await new InstanceDiscovery().DiscoverAsync(dir)).Instances).VersionLabel);
    }

    [Fact]
    public async Task Loader_names_and_support_libraries_alone_do_not_claim_an_installed_loader()
    {
        using var f = new ImporterFixtures(); var dir = f.Version("Forge-47.3.0-Fabric-0.16.9");
        f.Json(".minecraft/versions/Forge-47.3.0-Fabric-0.16.9/Forge-47.3.0-Fabric-0.16.9.json", new { clientVersion = "1.20.1", libraries = new[]
        {
            new { name = "net.minecraftforge:eventbus:6.0.5" }, new { name = "net.fabricmc:fabric-api:0.1" },
            new { name = "net.fabricmc:fabric-loader:\n0.16.9" }
        } });
        Assert.Equal("Minecraft 1.20.1", Assert.Single((await new InstanceDiscovery().DiscoverAsync(dir)).Instances).VersionLabel);
    }

    [Fact]
    public async Task Prism_uses_enabled_pack_components_without_downloaded_metadata()
    {
        using var f = new ImporterFixtures(); var dir = f.Prism();
        f.Json("Prism/instances/Pack/mmc-pack.json", new { formatVersion = 1, components = new[]
        {
            new { uid = "net.minecraft", version = "1.20.1", disabled = false },
            new { uid = "net.fabricmc.fabric-loader", version = "0.16.9", disabled = false },
            new { uid = "net.minecraftforge", version = "47.3.0", disabled = true }
        } });
        Assert.Equal("Minecraft 1.20.1 · Fabric 0.16.9", Assert.Single((await new InstanceDiscovery().DiscoverAsync(dir)).Instances).VersionLabel);
    }

    [Fact]
    public void Legacy_catalog_hydrates_loaders_even_with_known_game_version_without_writing_any_files()
    {
        using var f = new ImporterFixtures(); var dir = f.Version("Pack");
        var metadata = f.Json(".minecraft/versions/Pack/Pack.json", new { clientVersion = "1.20.1", libraries = new[] { new { name = "net.minecraftforge:fmlloader:1.20.1-47.3.0" } } });
        var instance = InstanceSettingsTests.ValidInstance() with { SourcePath = dir, GameVersion = "1.20.1", VersionsDirectory = f.DirectoryAt(".minecraft/versions"), MetadataPaths = [metadata] };
        var json = JsonSerializer.SerializeToNode(new InstanceCatalog { Instances = [instance] }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IgnoreReadOnlyProperties = true })!;
        json["instances"]![0]!.AsObject().Remove("modLoaders");
        var catalog = f.Write("catalog.json", json.ToJsonString());
        var originalCatalog = File.ReadAllBytes(catalog); var originalMetadata = File.ReadAllBytes(metadata);
        Assert.Equal("Minecraft 1.20.1 · Forge 47.3.0", Assert.Single(new InstanceStore(catalog).Load().Instances).VersionLabel);
        Assert.Equal(originalCatalog, File.ReadAllBytes(catalog));
        Assert.Equal(originalMetadata, File.ReadAllBytes(metadata));
    }

    [Theory]
    [InlineData("org.quiltmc.quilt-loader", "0.27.1", "Quilt")]
    [InlineData("net.minecraftforge", "47.3.0", "Forge")]
    [InlineData("net.neoforged", "21.1.172", "NeoForge")]
    public void Legacy_prism_catalog_hydrates_loader_components_without_metadata_paths(string uid, string version, string name)
    {
        using var f = new ImporterFixtures(); var dir = f.Prism();
        f.Json("Prism/instances/Pack/mmc-pack.json", new { formatVersion = 1, components = new[]
        {
            new { uid = "net.minecraft", version = "1.20.1" }, new { uid, version }
        } });
        var instance = InstanceSettingsTests.ValidInstance() with { Source = InstanceSource.Prism, SourcePath = dir, MetadataPaths = [] };
        var store = new InstanceStore(Path.Combine(f.Root, "catalog.json")); store.Save(new() { Instances = [instance] });
        Assert.Equal("Minecraft 1.20.1 · " + name + " " + version, Assert.Single(store.Load().Instances).VersionLabel);
    }

    [Fact]
    public void Catalog_preserves_known_loaders_when_source_is_unavailable()
    {
        using var f = new ImporterFixtures();
        var instance = InstanceSettingsTests.ValidInstance() with { GameVersion = "1.20.1", ModLoaders = [new("Forge", "47.3.0")] };
        var store = new InstanceStore(Path.Combine(f.Root, "catalog.json")); store.Save(new() { Instances = [instance] });
        var loaded = Assert.Single(store.Load().Instances);
        Assert.Equal(instance.ModLoaders, loaded.ModLoaders);
        Assert.Equal("Minecraft 1.20.1 · Forge 47.3.0", loaded.VersionLabel);
    }

    [Fact]
    public void Instance_rejects_invalid_loader_metadata_before_catalog_save()
    {
        var valid = InstanceSettingsTests.ValidInstance();
        Assert.Throws<ArgumentException>(() => (valid with { ModLoaders = null! }).Validate());
        Assert.Throws<ArgumentException>(() => (valid with { ModLoaders = [null!] }).Validate());
        Assert.Throws<ArgumentException>(() => (valid with { ModLoaders = [new("Forge", "47.3.0\n")] }).Validate());
        Assert.Throws<ArgumentException>(() => (valid with { ModLoaders = [new("", "47.3.0")] }).Validate());
    }
}
