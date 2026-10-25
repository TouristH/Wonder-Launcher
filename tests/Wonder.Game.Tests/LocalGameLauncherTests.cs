// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.IO.Compression;
using Wonder.Core;
using Wonder.Infrastructure;

namespace Wonder.Game.Tests;

public sealed class LocalGameLauncherTests
{
    [Fact]
    public async Task Missing_optional_log_configuration_uses_wonder_console_configuration()
    {
        using var f = new ImporterFixtures(); var dir = f.Version("26.2"); f.Write(".minecraft/versions/26.2/26.2.jar", "fixture");
        f.Json(".minecraft/versions/26.2/26.2.json", new { id = "26.2", mainClass = "Main", logging = new { client = new { type = "log4j2-xml", argument = "-Dlog4j.configurationFile=${path}", file = new { id = "client-1.21.2.xml" } } } });
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(dir)).Instances);
        var plan = await Launcher().BuildPlanAsync(new(instance, "Player", f.DirectoryAt("cache")));
        var config = Assert.Single(plan.Arguments, x => x.StartsWith("-Dlog4j.configurationFile="))["-Dlog4j.configurationFile=".Length..];
        Assert.True(File.Exists(config)); Assert.StartsWith(f.DirectoryAt("cache"), config);
        Assert.Contains("<Console", File.ReadAllText(config));
        Assert.False(Directory.Exists(Path.Combine(f.Root, ".minecraft", "assets", "log_configs")));
    }

    [Fact]
    public async Task Quoted_windows_arguments_preserve_backslashes_and_reject_unclosed_quotes()
    {
        using var f = new ImporterFixtures(); var dir = f.Version(); f.Write(".minecraft/versions/1.20.1/1.20.1.jar", "fixture");
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(dir)).Instances) with { Overrides = new() { JvmArguments = "-Dpath=\"C:\\my folder\\\\\" -Dliteral=\\\"quoted\\\"" } };
        var plan = await Launcher().BuildPlanAsync(new(instance, "Player", f.DirectoryAt("cache")));
        Assert.Contains("-Dpath=C:\\my folder\\", plan.Arguments); Assert.Contains("-Dliteral=\"quoted\"", plan.Arguments);
        instance = instance with { Overrides = new() { JvmArguments = "-Dbroken=\"unclosed" } };
        await Assert.ThrowsAsync<InvalidDataException>(() => Launcher().BuildPlanAsync(new(instance, "Player", f.DirectoryAt("cache"))));
    }

    [Fact]
    public async Task Prism_pack_resolves_main_jar_local_library_and_tweakers()
    {
        using var f = new ImporterFixtures(); var dir = f.Prism();
        f.Write("Prism/libraries/com/mojang/minecraft/1.20.1/minecraft-1.20.1-client.jar", "fixture");
        f.Write("Prism/instances/Pack/libraries/local mod.jar", "fixture");
        f.Json("Prism/instances/Pack/patches/org.lwjgl3.json", new Dictionary<string, object>
        {
            ["mainClass"] = "mod.Main", ["+tweakers"] = new[] { "mod.Tweaker" },
            ["+libraries"] = new[] { new Dictionary<string, string> { ["name"] = "test:local:1", ["MMC-hint"] = "local", ["MMC-filename"] = "local mod.jar" } }
        });
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(dir)).Instances);
        var plan = await Launcher().BuildPlanAsync(new(instance, "Player", f.DirectoryAt("cache")));
        Assert.Contains("mod.Main", plan.Arguments); Assert.Contains("mod.Tweaker", plan.Arguments);
        Assert.Contains(plan.Arguments, x => x.Contains("local mod.jar") && x.Contains("minecraft-1.20.1-client.jar"));
    }

    [Fact]
    public async Task Missing_assets_and_outdated_java_fail_with_actionable_errors()
    {
        using var f = new ImporterFixtures(); var dir = f.Version(); f.Write(".minecraft/versions/1.20.1/1.20.1.jar", "fixture");
        f.Json(".minecraft/versions/1.20.1/1.20.1.json", new { id = "1.20.1", mainClass = "Main", assetIndex = new { id = "test" }, javaVersion = new { majorVersion = 21 } });
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(dir)).Instances);
        var oldJava = new LocalGameLauncher((path, _) => Task.FromResult(new JavaRuntime(path, 8, "amd64")));
        Assert.Contains("Java 21", (await Assert.ThrowsAsync<InvalidDataException>(() => oldJava.BuildPlanAsync(new(instance, "Player", f.DirectoryAt("cache"))))).Message);
        Assert.Contains("test.json", (await Assert.ThrowsAsync<FileNotFoundException>(() => Launcher().BuildPlanAsync(new(instance, "Player", f.DirectoryAt("cache"))))).Message);
        f.Json(".minecraft/assets/indexes/test.json", new { objects = new Dictionary<string, object> { ["test"] = new { hash = new string('a', 40) } } });
        Assert.Contains(new string('a', 40), (await Assert.ThrowsAsync<FileNotFoundException>(() => Launcher().BuildPlanAsync(new(instance, "Player", f.DirectoryAt("cache"))))).Message);
    }

    [Fact]
    public async Task Modern_rule_arguments_and_library_overrides_use_selected_platform()
    {
        using var f = new ImporterFixtures(); f.Version("base"); f.Write(".minecraft/versions/base/base.jar", "fixture");
        f.Json(".minecraft/versions/base/base.json", new { id = "base", mainClass = "Main", libraries = new[] { new { name = "g:lib:1" } } });
        f.Json(".minecraft/versions/child/child.json", new { id = "child", inheritsFrom = "base", libraries = new[] { new { name = "g:lib:2" } }, arguments = new { game = new object[] {
            new { rules = new[] { new { action = "allow", features = new { has_custom_resolution = true } } }, value = new[] { "--width", "${resolution_width}" } },
            new { rules = new[] { new { action = "allow", features = new { is_demo_user = true } } }, value = "--demo" }
        } } });
        f.Write(".minecraft/libraries/g/lib/2/lib-2.jar", "fixture");
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(f.DirectoryAt(".minecraft/versions/child"))).Instances);
        var plan = await Launcher().BuildPlanAsync(new(instance, "Player", f.DirectoryAt("cache")));
        Assert.DoesNotContain("--demo", plan.Arguments); Assert.Equal(1, plan.Arguments.Count(x => x == "--width"));
        Assert.Contains(plan.Arguments, x => x.Contains("lib-2.jar")); Assert.DoesNotContain(plan.Arguments, x => x.Contains("lib-1.jar"));
    }

    [Fact]
    public async Task Standard_inheritance_arguments_identity_rules_and_paths_are_resolved()
    {
        using var f = new ImporterFixtures();
        f.Version("base"); f.Write(".minecraft/versions/base/base.jar", "fixture");
        f.Json(".minecraft/versions/base/base.json", new { id = "base", mainClass = "game.Main", arguments = new { game = new[] { "--username", "${auth_player_name}", "--uuid", "${auth_uuid}", "--gameDir", "${game_directory}" }, jvm = new[] { "-cp", "${classpath}" } }, libraries = new object[] { new { name = "example:library:1", rules = new[] { new { action = "allow", os = new { name = "linux" } } } } } });
        f.Json(".minecraft/versions/child/child.json", new { id = "child", inheritsFrom = "base", mainClass = "mod.Main", arguments = new { game = new[] { "--tweakClass", "mod.Tweak" } } });
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(f.DirectoryAt(".minecraft/versions/child"))).Instances);
        instance = instance with { Overrides = new() { JavaPath = Java(), MaximumMemoryMb = 2048, JvmArguments = "-Dmessage=\"hello world\" -Xmx1G" } };
        var plan = await Launcher().BuildPlanAsync(new(instance, "Player", f.DirectoryAt("cache")));
        Assert.Contains("-Xmx2048M", plan.Arguments);
        Assert.DoesNotContain("-Xmx1G", plan.Arguments);
        Assert.Contains("-Dmessage=hello world", plan.Arguments);
        Assert.Contains("mod.Main", plan.Arguments);
        Assert.Contains("mod.Tweak", plan.Arguments);
        Assert.Contains("a01e3843e5213998958af459800e4d11", plan.Arguments);
        Assert.Contains(Path.Combine(f.Root, ".minecraft", "versions", "base", "base.jar"), plan.Arguments);
    }

    [Fact]
    public async Task Missing_libraries_and_inheritance_cycle_fail_before_running()
    {
        using var f = new ImporterFixtures(); var version = f.Version(); f.Write(".minecraft/versions/1.20.1/1.20.1.jar", "fixture");
        f.Json(".minecraft/versions/1.20.1/1.20.1.json", new { id = "1.20.1", mainClass = "Main", libraries = new[] { new { name = "group:missing:1" } } });
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(version)).Instances) with { Overrides = new() { JavaPath = Java() } };
        var error = await Assert.ThrowsAsync<FileNotFoundException>(() => Launcher().BuildPlanAsync(new(instance, "Player", f.DirectoryAt("cache"))));
        Assert.Contains("missing", error.Message);
        f.Json(".minecraft/versions/1.20.1/1.20.1.json", new { inheritsFrom = "1.20.1" });
        await Assert.ThrowsAsync<InvalidDataException>(() => Launcher().BuildPlanAsync(new(instance, "Player", f.DirectoryAt("cache"))));
    }

    [Fact]
    public async Task Prism_missing_component_cannot_silently_launch_a_partial_pack()
    {
        using var f = new ImporterFixtures(); var dir = f.Prism();
        f.Json("Prism/instances/Pack/mmc-pack.json", new { formatVersion = 1, components = new[] { new { uid = "net.minecraft", version = "1.99" } } });
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(dir)).Instances) with { Overrides = new() { JavaPath = Java() } };
        await Assert.ThrowsAsync<InvalidDataException>(() => Launcher().BuildPlanAsync(new(instance, "Player", f.DirectoryAt("cache"))));
    }

    [Fact]
    public async Task Native_archive_traversal_is_rejected()
    {
        using var f = new ImporterFixtures(); var dir = f.Version(); f.Write(".minecraft/versions/1.20.1/1.20.1.jar", "fixture");
        f.Json(".minecraft/versions/1.20.1/1.20.1.json", new { id = "1.20.1", mainClass = "Main", libraries = new[] { new { name = "g:native:1", natives = new { windows = "natives-windows" }, downloads = new { classifiers = new Dictionary<string, object> { ["natives-windows"] = new { path = "bad.jar" } } } } } });
        var archive = Path.Combine(f.DirectoryAt(".minecraft/libraries"), "bad.jar");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) using (var writer = new StreamWriter(zip.CreateEntry("../escape.dll").Open())) writer.Write("bad");
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(dir)).Instances) with { Overrides = new() { JavaPath = Java() } };
        await Assert.ThrowsAsync<InvalidDataException>(() => Launcher().BuildPlanAsync(new(instance, "Player", f.DirectoryAt("cache"))));
        Assert.False(File.Exists(Path.Combine(f.Root, "cache", "escape.dll")));
    }

    [Theory]
    [InlineData("--username SomeoneElse")]
    [InlineData("--accessToken fake")]
    public async Task Custom_arguments_cannot_override_offline_identity(string arguments)
    {
        using var f = new ImporterFixtures(); var dir = f.Version(); f.Write(".minecraft/versions/1.20.1/1.20.1.jar", "fixture");
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(dir)).Instances) with { Overrides = new() { JavaPath = Java(), GameArguments = arguments } };
        await Assert.ThrowsAsync<InvalidDataException>(() => Launcher().BuildPlanAsync(new(instance, "Player", f.DirectoryAt("cache"))));
    }

    private static string Java() => "java";
    private static LocalGameLauncher Launcher() => new((path, _) => Task.FromResult(new JavaRuntime(path, 21, "amd64")));
}
