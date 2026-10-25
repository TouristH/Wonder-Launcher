// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Core;
using Xunit;

namespace Wonder.Game.Tests;

public sealed class InstanceSettingsTests
{
    [Fact]
    public void Effective_settings_apply_defaults_imported_and_only_non_null_overrides()
    {
        var instance = ValidInstance() with
        {
            ImportedSettings = new() { MaximumMemoryMb = 6144, WindowWidth = 1280, JvmArguments = "-Dsource=yes", FullScreen = true },
            Overrides = new() { WindowWidth = 960, JvmArguments = "", FullScreen = false }
        };
        var settings = instance.EffectiveSettings;
        Assert.Equal(512, settings.MinimumMemoryMb);
        Assert.Equal(6144, settings.MaximumMemoryMb);
        Assert.Equal(960, settings.WindowWidth);
        Assert.Equal(480, settings.WindowHeight);
        Assert.Equal("", settings.JvmArguments);
        Assert.False(settings.FullScreen);
        Assert.Equal(instance.DefaultGameDirectory, settings.GameDirectory);
    }

    [Fact]
    public void Null_overrides_restore_inheritance_without_changing_source_record()
    {
        var instance = ValidInstance() with { ImportedSettings = new() { MaximumMemoryMb = 8192 } };
        var changed = instance with { Overrides = new() { MaximumMemoryMb = 4096 } };
        var restored = changed with { Overrides = new() };
        Assert.Equal(8192, restored.EffectiveSettings.MaximumMemoryMb);
        Assert.Equal(8192, instance.ImportedSettings.MaximumMemoryMb);
    }

    [Theory]
    [InlineData(1024, 512)]
    [InlineData(0, 1024)]
    [InlineData(512, -1)]
    public void Invalid_memory_configuration_is_rejected(int minimum, int maximum) =>
        Assert.ThrowsAny<ArgumentException>(() => new InstanceSettings { MinimumMemoryMb = minimum, MaximumMemoryMb = maximum }.Validate());

    [Fact]
    public void Instance_checks_merged_settings_and_requires_absolute_paths()
    {
        Assert.ThrowsAny<ArgumentException>(() => (ValidInstance() with { SourcePath = "relative" }).Validate());
        Assert.ThrowsAny<ArgumentException>(() => (ValidInstance() with
        { ImportedSettings = new() { MinimumMemoryMb = 8192 }, Overrides = new() { MaximumMemoryMb = 1024 } }).Validate());
        ValidInstance().Validate();
    }

    [Fact]
    public void Launch_plan_defensively_copies_arguments()
    {
        var args = new List<string> { "-Xmx4096m", "Main", "value with spaces" };
        var plan = new LaunchPlan("java", Path.GetTempPath(), args, "instance-id");
        args[0] = "changed";
        Assert.Equal("-Xmx4096m", plan.Arguments[0]);
        Assert.False(plan.Arguments is string[]);
        if (plan.Arguments is IList<string> mutable) Assert.Throws<NotSupportedException>(() => mutable[0] = "changed");
    }

    internal static GameInstance ValidInstance(string id = "instance-one")
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Wonder.GameFixtures", id));
        return new GameInstance
        {
            Id = id, Name = "测试实例", Source = InstanceSource.Official, SourcePath = root,
            RootDirectory = root, DefaultGameDirectory = root, VersionId = "1.21.1",
            LibraryDirectory = Path.Combine(root, "libraries"), AssetsDirectory = Path.Combine(root, "assets"),
            VersionsDirectory = Path.Combine(root, "versions"), MetadataPaths = [Path.Combine(root, "versions", "1.21.1", "1.21.1.json")]
        };
    }
}
