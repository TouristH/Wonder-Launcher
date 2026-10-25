// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Application;
using Wonder.Core;
using Xunit;

namespace Wonder.Game.Tests;

public sealed class InstanceServiceTests
{
    [Fact]
    public async Task Global_settings_apply_between_imported_and_instance_overrides_and_survive_reload()
    {
        var item = InstanceSettingsTests.ValidInstance() with
        {
            ImportedSettings = new() { JavaPath = "imported-java", MaximumMemoryMb = 2048, WindowWidth = 900 },
            Overrides = new() { WindowWidth = 1400 }
        };
        var store = new FakeStore { Catalog = Catalog(item) };
        var launcher = new FakeLauncher();
        var service = Create(store, launcher: launcher);
        service.UpdateGlobalSettings(new() { JavaPath = "global-java", MaximumMemoryMb = 8192, WindowWidth = 1200, JvmArguments = "-Dglobal=true" });
        service.Reload();
        var effective = service.SelectedInstance!.EffectiveSettings;
        Assert.Equal("global-java", effective.JavaPath);
        Assert.Equal(8192, effective.MaximumMemoryMb);
        Assert.Equal(1400, effective.WindowWidth);
        Assert.Equal(item.DefaultGameDirectory, effective.GameDirectory);
        Assert.Equal(effective, Assert.Single(service.Catalog.Instances).EffectiveSettings);
        Assert.Equal(item.ImportedSettings, Assert.Single(store.Catalog.Instances).ImportedSettings);
        Assert.Equal(item.Overrides, Assert.Single(store.Catalog.Instances).Overrides);
        await service.LaunchSelectedAsync();
        Assert.Equal(effective, launcher.Request!.Instance.EffectiveSettings);
        service.ResetGlobalSettings();
        Assert.Equal(2048, service.SelectedInstance.EffectiveSettings.MaximumMemoryMb);
        Assert.Equal(1400, service.SelectedInstance.EffectiveSettings.WindowWidth);
    }

    [Fact]
    public void Invalid_global_settings_and_conflicting_instance_changes_do_not_persist()
    {
        var item = InstanceSettingsTests.ValidInstance() with { Overrides = new() { MinimumMemoryMb = 3000 } };
        var store = new FakeStore { Catalog = Catalog(item) };
        var service = Create(store);
        service.UpdateGlobalSettings(new() { MaximumMemoryMb = 2048 });
        Assert.Equal(0, store.Saves);
        Assert.NotNull(service.Error);
        service.UpdateGlobalSettings(new() { GameDirectory = item.RootDirectory });
        Assert.Equal(0, store.Saves);
        service.UpdateGlobalSettings(new() { MinimumMemoryMb = 2000 });
        service.UpdateSettings(item.Id, new() { MaximumMemoryMb = 1024 });
        Assert.Equal(1, store.Saves);
        Assert.Equal(3000, service.SelectedInstance!.EffectiveSettings.MinimumMemoryMb);
    }

    [Fact]
    public void Global_partial_settings_validate_against_each_instances_inherited_values()
    {
        var item = InstanceSettingsTests.ValidInstance() with { ImportedSettings = new() { MaximumMemoryMb = 16384 } };
        var store = new FakeStore { Catalog = Catalog(item) };
        var service = Create(store);
        service.UpdateGlobalSettings(new() { MinimumMemoryMb = 8192 });
        Assert.Null(service.Error);
        Assert.Equal(8192, service.SelectedInstance!.EffectiveSettings.MinimumMemoryMb);
        Assert.Equal(16384, service.SelectedInstance.EffectiveSettings.MaximumMemoryMb);
        service.Reload();
        Assert.Null(service.Error);
    }

    [Fact]
    public async Task Global_changes_do_not_change_in_progress_launch_or_lose_failed_save_draft()
    {
        var launcher = new FakeLauncher { WaitForBuild = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var store = new FakeStore { Catalog = Catalog(InstanceSettingsTests.ValidInstance()) };
        var service = Create(store, launcher: launcher);
        service.UpdateGlobalSettings(new() { MaximumMemoryMb = 6000 });
        var launch = service.LaunchSelectedAsync();
        await launcher.BuildEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        service.UpdateGlobalSettings(new() { MaximumMemoryMb = 8000 });
        Assert.Equal(6000, launcher.Request!.Instance.EffectiveSettings.MaximumMemoryMb);
        Assert.Equal(8000, service.SelectedInstance!.EffectiveSettings.MaximumMemoryMb);
        store.SaveError = new IOException("disk full");
        service.UpdateGlobalSettings(new() { MaximumMemoryMb = 9000 });
        Assert.Equal(8000, service.Catalog.GlobalSettings.MaximumMemoryMb);
        Assert.Contains("disk full", service.Error);
        launcher.WaitForBuild.SetResult();
        await launch;
    }

    [Fact]
    public async Task Duplicate_import_and_refresh_keep_wonder_name_and_overrides()
    {
        var store = new FakeStore();
        var discovery = new FakeDiscovery();
        var original = InstanceSettingsTests.ValidInstance();
        discovery.Result = new([original], []);
        var service = Create(store, discovery);
        await service.ImportAsync(original.SourcePath);
        service.Rename(original.Id, "我的生存服");
        service.UpdateSettings(original.Id, new() { MaximumMemoryMb = 8192 });
        discovery.Result = new([original with { Name = "外部改名", ImportedSettings = new() { WindowWidth = 1200 } }], ["来源说明"]);
        await service.ImportAsync(original.SourcePath);
        await service.RefreshAsync(original.Id);
        var instance = Assert.Single(service.Catalog.Instances);
        Assert.Equal("我的生存服", instance.Name);
        Assert.Equal(8192, instance.EffectiveSettings.MaximumMemoryMb);
        Assert.Equal(1200, instance.EffectiveSettings.WindowWidth);
        Assert.Equal(original.Id, service.Catalog.SelectedInstanceId);
    }

    [Fact]
    public async Task Failed_load_blocks_saves_until_explicit_successful_reload()
    {
        var store = new FakeStore { LoadError = new IOException("broken index") };
        var discovery = new FakeDiscovery { Result = new([InstanceSettingsTests.ValidInstance()], []) };
        var service = Create(store, discovery);
        Assert.Contains("broken index", service.Error);
        await service.ImportAsync("anything");
        service.SetOfflineUsername("NewName");
        Assert.Equal(0, store.Saves);
        Assert.Empty(service.Catalog.Instances);
        store.LoadError = null;
        service.Reload();
        await service.ImportAsync("anything");
        Assert.Single(service.Catalog.Instances);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public void Save_failure_rolls_back_change_and_keeps_error()
    {
        var store = new FakeStore { Catalog = Catalog(InstanceSettingsTests.ValidInstance()), SaveError = new IOException("disk full") };
        var service = Create(store);
        service.Rename("instance-one", "unsaved");
        Assert.Equal("测试实例", service.SelectedInstance!.Name);
        Assert.Contains("disk full", service.Error);
    }

    [Fact]
    public async Task Failed_discovery_does_not_replace_existing_configuration()
    {
        var store = new FakeStore { Catalog = Catalog(InstanceSettingsTests.ValidInstance()) };
        var service = Create(store, new FakeDiscovery { Error = new IOException("source missing") });
        await service.RefreshAsync("instance-one");
        Assert.Equal("测试实例", service.SelectedInstance!.Name);
        Assert.Equal(0, store.Saves);
        Assert.Contains("source missing", service.Error);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("invalid name")]
    [InlineData("名字")]
    [InlineData("12345678901234567")]
    public void Invalid_username_is_not_saved(string name)
    {
        var store = new FakeStore();
        var service = Create(store);
        service.SetOfflineUsername(name);
        Assert.Equal("Player", service.Catalog.OfflineUsername);
        Assert.Equal(0, store.Saves);
        Assert.NotNull(service.Error);
    }

    [Fact]
    public void Public_catalog_snapshots_cannot_mutate_service_state()
    {
        var service = Create(new FakeStore { Catalog = Catalog(InstanceSettingsTests.ValidInstance()) });
        service.Catalog.Instances.Clear();
        service.SelectedInstance!.MetadataPaths.Clear();
        Assert.Single(service.Catalog.Instances);
        Assert.NotEmpty(service.SelectedInstance.MetadataPaths);
    }

    [Fact]
    public async Task Launch_uses_frozen_selection_settings_and_username_and_rejects_duplicate_start()
    {
        var first = InstanceSettingsTests.ValidInstance();
        var second = InstanceSettingsTests.ValidInstance("instance-two");
        var store = new FakeStore { Catalog = Catalog(first, second) };
        var launcher = new FakeLauncher { WaitForBuild = new(TaskCreationOptions.RunContinuationsAsynchronously), WaitForExit = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var service = Create(store, launcher: launcher);
        var pending = service.LaunchSelectedAsync();
        await launcher.BuildEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(service.IsBusy);
        Assert.Equal(LaunchPhase.Preparing, service.LaunchPhase);
        service.Select(second.Id);
        service.UpdateSettings(first.Id, new() { MaximumMemoryMb = 8192 });
        service.SetOfflineUsername("Changed");
        await service.LaunchSelectedAsync();
        Assert.Equal(1, launcher.Builds);
        Assert.Equal(first.Id, launcher.Request!.Instance.Id);
        Assert.Equal(4096, launcher.Request.Instance.EffectiveSettings.MaximumMemoryMb);
        Assert.Equal("Player", launcher.Request.OfflineUsername);
        launcher.WaitForBuild.SetResult();
        await launcher.RunEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(LaunchPhase.Running, service.LaunchPhase);
        service.Remove(first.Id);
        Assert.Equal(2, service.Catalog.Instances.Count);
        service.CancelPreparation();
        Assert.False(launcher.RunCancelled);
        launcher.WaitForExit.SetResult(0);
        await pending;
        Assert.False(service.IsBusy);
        Assert.Equal(LaunchPhase.Exited, service.LaunchPhase);
    }

    [Fact]
    public async Task Preparation_can_be_cancelled_without_starting_a_process()
    {
        var launcher = new FakeLauncher { WaitForBuild = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var service = Create(new FakeStore { Catalog = Catalog(InstanceSettingsTests.ValidInstance()) }, launcher: launcher);
        var pending = service.LaunchSelectedAsync();
        await launcher.BuildEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        service.CancelPreparation();
        await pending;
        Assert.Equal(0, launcher.Runs);
        Assert.Equal(LaunchPhase.Idle, service.LaunchPhase);
        Assert.False(service.IsBusy);
    }

    [Fact]
    public async Task Logs_are_bounded_and_subscriber_failure_does_not_break_launch()
    {
        var launcher = new FakeLauncher { EmitManyLines = true };
        var service = Create(new FakeStore { Catalog = Catalog(InstanceSettingsTests.ValidInstance()) }, launcher: launcher);
        service.Changed += (_, _) => throw new InvalidOperationException("broken view");
        var notifications = 0;
        service.Changed += (_, _) => notifications++;
        await service.LaunchSelectedAsync();
        Assert.Equal(LaunchPhase.Exited, service.LaunchPhase);
        Assert.True(notifications > 0);
        Assert.InRange(service.LogLines.Count, 1, 200);
        Assert.All(service.LogLines, line => Assert.InRange(line.Length, 0, 2048));
    }

    private static InstanceCatalog Catalog(params GameInstance[] instances) => new() { Instances = instances.ToList(), SelectedInstanceId = instances.FirstOrDefault()?.Id };
    private static InstanceService Create(FakeStore store, FakeDiscovery? discovery = null, FakeLauncher? launcher = null) =>
        new(store, discovery ?? new(), launcher ?? new(), Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Wonder.GameCache")));

    private sealed class FakeStore : IInstanceStore
    {
        public InstanceCatalog Catalog = new();
        public Exception? LoadError, SaveError;
        public int Saves;
        public InstanceCatalog Load() => LoadError is null ? Catalog : throw LoadError;
        public void Save(InstanceCatalog value) { if (SaveError is not null) throw SaveError; Saves++; Catalog = value; }
    }
    private sealed class FakeDiscovery : IInstanceDiscovery
    {
        public DiscoveryResult Result = new([], []);
        public Exception? Error;
        public Task<DiscoveryResult> DiscoverAsync(string selectedPath, CancellationToken ct = default) =>
            Error is null ? Task.FromResult(Result) : Task.FromException<DiscoveryResult>(Error);
    }
    private sealed class FakeLauncher : IGameLauncher
    {
        public int Builds, Runs;
        public bool RunCancelled, EmitManyLines;
        public LaunchRequest? Request;
        public TaskCompletionSource BuildEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RunEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? WaitForBuild;
        public TaskCompletionSource<int>? WaitForExit;
        public async Task<LaunchPlan> BuildPlanAsync(LaunchRequest request, CancellationToken ct = default)
        {
            Builds++;
            Request = request;
            BuildEntered.TrySetResult();
            if (WaitForBuild is not null) await WaitForBuild.Task.WaitAsync(ct);
            return new("java", request.Instance.DefaultGameDirectory, ["Main"], request.Instance.Id);
        }
        public async Task<int> RunAsync(LaunchPlan plan, Action started, Action<string> output, CancellationToken ct = default)
        {
            Runs++;
            started();
            RunEntered.TrySetResult();
            if (EmitManyLines) for (var i = 0; i < 230; i++) output(new string('a', 3000));
            try { return WaitForExit is null ? 0 : await WaitForExit.Task.WaitAsync(ct); }
            catch (OperationCanceledException) { RunCancelled = true; throw; }
        }
    }
}
