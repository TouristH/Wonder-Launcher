// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Infrastructure;

namespace Wonder.Game.Tests;

public sealed class InstanceDiscoveryTests
{
    [Fact]
    public async Task Hmcl_new_preset_inheritance_and_custom_java_are_resolved()
    {
        using var f = new ImporterFixtures(); var dir = f.Version();
        f.Json("hmcl/config/game-settings.json", new { presets = new[] { new { id = "preset-one", javaType = "CUSTOM", customJavaPath = "C:/Java/bin/java.exe", autoMemory = false, maxMemory = 6144, width = 1280.0, runningDirectory = "", windowType = "FULLSCREEN" } } });
        f.Json(".minecraft/versions/1.20.1/.hmcl/config/instance-game-settings.json", new { parent = "preset-one", overrideProperties = new[] { "maxMemory" }, maxMemory = 8192, width = 320 });
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(dir)).Instances);
        Assert.Equal(8192, instance.ImportedSettings.MaximumMemoryMb);
        Assert.Equal(1280, instance.ImportedSettings.WindowWidth);
        Assert.Equal("C:/Java/bin/java.exe", instance.ImportedSettings.JavaPath);
        Assert.True(instance.ImportedSettings.FullScreen);
        Assert.Equal(Path.Combine(f.Root, ".minecraft"), instance.DefaultGameDirectory);
        Assert.Empty(instance.Warnings);
    }

    [Fact]
    public async Task Hmcl_auto_java_does_not_use_a_stale_saved_custom_path()
    {
        using var f = new ImporterFixtures(); var dir = f.Version();
        f.Json(".minecraft/versions/1.20.1/hmclversion.cfg", new { usesGlobal = false, javaVersionType = 1, javaDir = "C:/stale/java.exe" });
        Assert.Null(Assert.Single((await new InstanceDiscovery().DiscoverAsync(dir)).Instances).ImportedSettings.JavaPath);
    }

    [Fact]
    public async Task Hmcl_legacy_global_only_uses_a_matching_game_root()
    {
        using var f = new ImporterFixtures(); var dir = f.Version();
        f.Json(".minecraft/versions/1.20.1/hmclversion.cfg", new { usesGlobal = true, maxMemory = 8192 });
        f.Json("hmcl.json", new { profiles = new { good = new { gameDir = f.DirectoryAt(".minecraft"), global = new { autoMemory = false, maxMemory = 2048 } }, other = new { gameDir = f.DirectoryAt("another-game"), global = new { autoMemory = false, maxMemory = 16384 } } } });
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(dir)).Instances);
        Assert.Equal(2048, instance.ImportedSettings.MaximumMemoryMb);
    }

    [Fact]
    public async Task Pcl_forced_java_and_global_custom_memory_are_imported()
    {
        using var f = new ImporterFixtures(); var dir = f.Version();
        f.Write(".minecraft/versions/1.20.1/PCL/Setup.ini", "VersionRamType:2\nVersionArgumentIndieV2:False\n");
        f.Write("PCL/Setup.ini", "LaunchRamType:1\nLaunchRamCustom:25\nLaunchAdvanceJvm:-Dshared=true\n");
        f.Json(".minecraft/versions/1.20.1/PCL/config.json", new { InstanceForcedJava = new { Folder = "C:/Java/bin" } });
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(dir)).Instances);
        Assert.Equal(8192, instance.ImportedSettings.MaximumMemoryMb);
        Assert.Equal(Path.Combine("C:/Java/bin", "java.exe"), instance.ImportedSettings.JavaPath);
        Assert.Equal("-Dshared=true", instance.ImportedSettings.JvmArguments);
    }

    [Fact]
    public async Task Multimc_global_memory_is_used_when_local_override_is_disabled()
    {
        using var f = new ImporterFixtures();
        f.Write("MultiMC/multimc.cfg", "[General]\nMaxMemAlloc=3072\nMinMemAlloc=256\n");
        f.Write("MultiMC/instances/World/instance.cfg", "[General]\nname=Multi 世界\nOverrideMemory=false\nMaxMemAlloc=9999\n");
        f.Json("MultiMC/instances/World/mmc-pack.json", new { formatVersion = 1, components = new[] { new { uid = "net.minecraft", version = "1.20.1" } } });
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(f.DirectoryAt("MultiMC"))).Instances);
        Assert.Equal("MultiMc", instance.Source.ToString()); Assert.Equal(3072, instance.ImportedSettings.MaximumMemoryMb);
    }

    [Fact]
    public async Task Two_official_profiles_using_one_version_keep_separate_settings()
    {
        using var f = new ImporterFixtures(); f.Version();
        f.Json(".minecraft/launcher_profiles.json", new { profiles = new { first = new { name = "First", lastVersionId = "1.20.1", gameDir = f.DirectoryAt("first") }, second = new { name = "Second", lastVersionId = "1.20.1", gameDir = f.DirectoryAt("second") } } });
        var instances = (await new InstanceDiscovery().DiscoverAsync(f.DirectoryAt(".minecraft"))).Instances;
        Assert.Equal(2, instances.Count); Assert.NotEqual(instances[0].Id, instances[1].Id); Assert.NotEqual(instances[0].DefaultGameDirectory, instances[1].DefaultGameDirectory);
    }

    [Fact]
    public async Task Standard_root_and_version_folder_have_same_identity_and_do_not_modify_sources()
    {
        using var f = new ImporterFixtures();
        var version = f.Version();
        var file = Path.Combine(version, "1.20.1.json");
        var before = File.ReadAllBytes(file);
        var discovery = new InstanceDiscovery();
        var fromRoot = Assert.Single((await discovery.DiscoverAsync(f.DirectoryAt(".minecraft"))).Instances);
        var fromVersion = Assert.Single((await discovery.DiscoverAsync(version)).Instances);
        Assert.Equal(fromRoot.Id, fromVersion.Id);
        Assert.Equal("1.20.1", fromVersion.VersionId);
        Assert.Equal(before, File.ReadAllBytes(file));
        Assert.Equal(file, Assert.Single(fromVersion.MetadataPaths));
    }

    [Fact]
    public async Task Official_profile_imports_configuration_without_account_data()
    {
        using var f = new ImporterFixtures(); f.Version();
        f.Json(".minecraft/launcher_profiles.json", new { profiles = new { test = new { name = "游戏", lastVersionId = "1.20.1", gameDir = f.DirectoryAt("world"), javaDir = "C:/Java/bin/javaw.exe", javaArgs = "-Xmx3G -Dhello=true", resolution = new { width = 1280, height = 720 } } }, authenticationDatabase = new { secret = "DO_NOT_IMPORT" } });
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(f.DirectoryAt(".minecraft"))).Instances);
        Assert.Equal("游戏", instance.Name);
        Assert.Equal(3072, instance.ImportedSettings.MaximumMemoryMb);
        Assert.Equal(1280, instance.ImportedSettings.WindowWidth);
        Assert.Equal(f.DirectoryAt("world"), instance.DefaultGameDirectory);
        Assert.DoesNotContain("DO_NOT_IMPORT", System.Text.Json.JsonSerializer.Serialize(instance));
    }

    [Fact]
    public async Task Hmcl_legacy_isolation_and_explicit_settings_are_imported()
    {
        using var f = new ImporterFixtures(); var version = f.Version();
        f.Json(".minecraft/versions/1.20.1/hmclversion.cfg", new { usesGlobal = false, gameDirType = 1, autoMemory = false, maxMemory = 4096, javaDir = "C:/Java/bin/java.exe", javaVersionType = 4, javaArgs = "-Dflag=true", minecraftArgs = "--demo", width = 1400, height = 800, fullscreen = true, precalledCommand = "DO_NOT_RUN" });
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(version)).Instances);
        Assert.Equal("Hmcl", instance.Source.ToString());
        Assert.Equal(version, instance.DefaultGameDirectory);
        Assert.Equal(4096, instance.ImportedSettings.MaximumMemoryMb);
        Assert.Equal("--demo", instance.ImportedSettings.GameArguments);
        Assert.DoesNotContain("DO_NOT_RUN", System.Text.Json.JsonSerializer.Serialize(instance));
    }

    [Fact]
    public async Task Hmcl_new_schema_respects_override_properties_and_empty_isolated_directory()
    {
        using var f = new ImporterFixtures(); var version = f.Version();
        f.Json(".minecraft/versions/1.20.1/.hmcl/config/instance-game-settings.json", new { parent = "missing-preset", overrideProperties = new[] { "maxMemory", "autoMemory", "runningDirectory", "jvmOptions" }, maxMemory = 3072, autoMemory = false, runningDirectory = "", jvmOptions = "-Dnew=true", width = 999 });
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(version)).Instances);
        Assert.Equal(version, instance.DefaultGameDirectory);
        Assert.Equal(3072, instance.ImportedSettings.MaximumMemoryMb);
        Assert.Null(instance.ImportedSettings.WindowWidth);
        Assert.Contains(instance.Warnings, value => value.Contains("继承"));
    }

    [Theory]
    [InlineData(12, 1536)]
    [InlineData(15, 3072)]
    [InlineData(25, 8192)]
    [InlineData(33, 16384)]
    [InlineData(34, 18432)]
    public async Task Pcl_memory_slider_is_converted_to_mib(int slider, int expected)
    {
        using var f = new ImporterFixtures(); var version = f.Version();
        f.Write(".minecraft/versions/1.20.1/PCL/Setup.ini", $"VersionRamType:1\nVersionRamCustom:{slider}\nVersionArgumentIndieV2:True\nVersionAdvanceJvm:-Dvalue=a=b\n");
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(version)).Instances);
        Assert.Equal("Pcl", instance.Source.ToString());
        Assert.Equal(expected, instance.ImportedSettings.MaximumMemoryMb);
        Assert.Equal(version, instance.DefaultGameDirectory);
        Assert.Equal("-Dvalue=a=b", instance.ImportedSettings.JvmArguments);
    }

    [Fact]
    public async Task Prism_resolves_shared_metadata_and_global_settings_and_local_patch_precedence()
    {
        using var f = new ImporterFixtures(); var root = f.Prism();
        var patch = f.Json("Prism/instances/Pack/patches/org.lwjgl3.json", new { uid = "org.lwjgl3", version = "3.3.1" });
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(Path.Combine(root, "minecraft"))).Instances);
        Assert.Equal("Prism", instance.Source.ToString());
        Assert.Equal("测试整合包", instance.Name);
        Assert.Equal(4096, instance.ImportedSettings.MaximumMemoryMb);
        Assert.Equal("C:/Java/bin/javaw.exe", instance.ImportedSettings.JavaPath);
        Assert.Equal(2, instance.MetadataPaths.Count);
        Assert.Equal(patch, instance.MetadataPaths[1]);
        Assert.Equal(Path.Combine(f.Root, "Prism", "libraries"), instance.LibraryDirectory);
        Assert.Equal(Path.Combine(root, "minecraft"), instance.DefaultGameDirectory);
    }

    [Fact]
    public async Task Malformed_candidate_does_not_hide_valid_sibling()
    {
        using var f = new ImporterFixtures(); f.Version("good");
        f.Write(".minecraft/versions/broken/broken.json", "{broken");
        var found = await new InstanceDiscovery().DiscoverAsync(f.DirectoryAt(".minecraft"));
        Assert.Equal("good", Assert.Single(found.Instances).VersionId);
        Assert.NotEmpty(found.Diagnostics);
    }

    [Fact]
    public async Task Missing_prism_metadata_remains_manageable_with_explicit_warning()
    {
        using var f = new ImporterFixtures(); var root = f.Prism();
        f.Json("Prism/instances/Pack/mmc-pack.json", new { formatVersion = 1, components = new[] { new { uid = "net.minecraft", version = "1.99" } } });
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(root)).Instances);
        Assert.Empty(instance.MetadataPaths);
        Assert.Contains(instance.Warnings, value => value.Contains("缺少"));
    }
}
