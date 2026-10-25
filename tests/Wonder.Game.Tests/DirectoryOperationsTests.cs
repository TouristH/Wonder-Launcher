// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Application;
using Wonder.Core;
using Wonder.Infrastructure;

namespace Wonder.Game.Tests;

public sealed class DirectoryOperationsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("Sure")]
    [InlineData("sure ")]
    [InlineData("SURE")]
    public async Task Deletion_requires_exact_confirmation_without_touching_files(string word)
    {
        using var fixture = new Fixture();
        var instance = await fixture.Import(); var plan = fixture.Service.PrepareDeleteInstance(instance.Id);
        Assert.False(await fixture.Service.DeleteAsync(plan, word));
        Assert.True(Directory.Exists(plan.Path)); Assert.Equal(0, fixture.Files.Recycles); Assert.Single(fixture.Service.Catalog.Instances);
    }

    [Fact]
    public async Task Delete_recycles_only_version_directory_and_clears_selected_target()
    {
        using var f = new Fixture(); var instance = await f.Import();
        var plan = f.Service.PrepareDeleteInstance(instance.Id);
        Assert.Contains("共享", plan.Description);
        Assert.True(await f.Service.DeleteAsync(plan, "sure"), f.Service.Error);
        Assert.False(Directory.Exists(plan.Path)); Assert.True(File.Exists(Path.Combine(f.Root, "saves", "keep.txt")));
        Assert.Empty(f.Service.Catalog.Instances); Assert.Null(f.Service.SelectedInstance);
        Assert.True(File.Exists(Path.Combine(f.Files.RecycledPath!, "one.json")));
    }

    [Fact]
    public async Task Empty_registered_directory_survives_reload_can_be_renamed_and_recycled_separately()
    {
        using var f = new Fixture(); var instance = await f.Import();
        Assert.True(await f.Service.DeleteAsync(f.Service.PrepareDeleteInstance(instance.Id), "sure"), f.Service.Error);
        f.Service.Reload();
        Assert.Empty(f.Service.Catalog.Instances); Assert.Equal(f.Root, Assert.Single(f.Service.Catalog.GameDirectories));
        var destination = Path.Combine(f.Base, "empty-game");
        Assert.True(await f.Service.MoveDirectoryAsync(f.Root, destination), f.Service.Error);
        f.Service.Reload(); Assert.Equal(destination, Assert.Single(f.Service.Catalog.GameDirectories));
        var plan = f.Service.PrepareDeleteDirectory(destination); Assert.Equal(0, plan.AffectedCount);
        Assert.True(await f.Service.DeleteAsync(plan, "sure"), f.Service.Error);
        f.Service.Reload(); Assert.Empty(f.Service.Catalog.GameDirectories); Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task Root_delete_covers_its_instances_and_storage_failure_preserves_recovery_information()
    {
        using var f = new Fixture(); await f.Import(); f.WriteVersion("two"); await f.Service.ImportAsync(f.Root);
        var plan = f.Service.PrepareDeleteDirectory(f.Root); Assert.Equal(2, plan.AffectedCount);
        f.Store.FailSave = true;
        Assert.False(await f.Service.DeleteAsync(plan, "sure"));
        Assert.True(f.Service.IsReadBlocked); Assert.Equal(2, f.Service.Catalog.Instances.Count);
        Assert.Contains("回收站还原", f.Service.Error); Assert.False(Directory.Exists(f.Root));
    }

    [Fact]
    public async Task Refused_recycle_keeps_catalog_and_files()
    {
        using var f = new Fixture(); var instance = await f.Import(); f.Files.FailRecycle = true;
        Assert.False(await f.Service.DeleteAsync(f.Service.PrepareDeleteInstance(instance.Id), "sure"));
        Assert.Single(f.Service.Catalog.Instances); Assert.True(Directory.Exists(instance.SourcePath));
    }

    [Fact]
    public async Task Source_changed_after_confirmation_is_not_deleted()
    {
        using var f = new Fixture(); var instance = await f.Import(); var plan = f.Service.PrepareDeleteInstance(instance.Id);
        Directory.SetLastWriteTimeUtc(plan.Path, DateTime.UtcNow.AddHours(1));
        Assert.False(await f.Service.DeleteAsync(plan, "sure")); Assert.Equal(0, f.Files.Recycles);
    }

    [Fact]
    public async Task Official_profiles_never_delete_root_and_shared_or_inherited_versions_are_blocked()
    {
        using var f = new Fixture();
        File.WriteAllText(Path.Combine(f.Root, "launcher_profiles.json"), """{"profiles":{"a":{"name":"A","lastVersionId":"one"},"b":{"name":"B","lastVersionId":"one"}}}""");
        await f.Service.ImportAsync(f.Root);
        Assert.All(f.Service.Catalog.Instances, instance => Assert.Throws<InvalidOperationException>(() => f.Service.PrepareDeleteInstance(instance.Id)));
        File.Delete(Path.Combine(f.Root, "launcher_profiles.json"));
        using var other = new Fixture(); var item = await other.Import(); other.WriteVersion("child", "one");
        Assert.Throws<InvalidOperationException>(() => other.Service.PrepareDeleteInstance(item.Id));
        Assert.True(Directory.Exists(other.Root));
    }

    [Fact]
    public async Task Unregistered_sibling_jar_reference_prevents_shared_client_deletion()
    {
        using var f = new Fixture(); var item = await f.Import(); f.WriteVersion("child");
        File.WriteAllText(Path.Combine(f.Root, "versions", "child", "child.json"), """{"id":"child","jar":"one"}""");
        Assert.Throws<InvalidOperationException>(() => f.Service.PrepareDeleteInstance(item.Id));
        Assert.True(Directory.Exists(item.SourcePath)); Assert.Equal(0, f.Files.Recycles);
    }

    [Fact]
    public async Task Moving_updates_paths_preserves_ids_overrides_and_refresh_does_not_duplicate_profiles()
    {
        using var f = new Fixture();
        var oldJava = Path.Combine(f.Root, "runtime", "java.exe");
        File.WriteAllText(Path.Combine(f.Root, "launcher_profiles.json"), System.Text.Json.JsonSerializer.Serialize(new
        { profiles = new { a = new { name = "A", lastVersionId = "one", gameDir = Path.Combine(f.Root, "custom"), javaDir = oldJava }, b = new { name = "B", lastVersionId = "one" } } }));
        await f.Service.ImportAsync(f.Root); var ids = f.Service.Catalog.Instances.Select(x => x.Id).ToArray();
        var targetId = ids[0]; f.Service.UpdateInstance(targetId, "自定义名称", new()
        {
            MaximumMemoryMb = 7168,
            JvmArguments = $"-Droot=\"{f.Root}\" -Dlog={f.Root.Replace('\\', '/')}/logs -Dother={f.Root}-other",
            GameArguments = $"--directory \"{f.Root}\""
        });
        var destination = Path.Combine(f.Base, "改名后的目录");
        Assert.True(await f.Service.MoveDirectoryAsync(f.Root, destination), f.Service.Error);
        Assert.False(Directory.Exists(f.Root)); Assert.True(File.Exists(Path.Combine(destination, "versions", "one", "one.json")));
        Assert.Equal(ids, f.Service.Catalog.Instances.Select(x => x.Id));
        await f.Service.RefreshAsync(targetId); await f.Service.ImportAsync(destination); f.Service.Reload();
        Assert.Null(f.Service.Error); Assert.Equal(2, f.Service.Catalog.Instances.Count);
        var updated = f.Service.Catalog.Instances.Single(x => x.Id == targetId);
        Assert.Equal("自定义名称", updated.Name); Assert.Equal(7168, updated.Overrides.MaximumMemoryMb);
        Assert.Equal(Path.Combine(destination, "custom"), updated.DefaultGameDirectory);
        Assert.Equal(Path.Combine(destination, "runtime", "java.exe"), updated.ImportedSettings.JavaPath);
        Assert.Equal(destination, updated.RootDirectory);
        Assert.Equal($"-Droot=\"{destination}\" -Dlog={destination.Replace('\\', '/')}/logs -Dother={f.Root}-other", updated.Overrides.JvmArguments);
        Assert.Equal($"--directory \"{destination}\"", updated.Overrides.GameArguments);
    }

    [Fact]
    public async Task Failed_move_save_rolls_files_back_and_existing_target_is_untouched()
    {
        using var f = new Fixture(); var instance = await f.Import(); var target = Path.Combine(f.Base, "target");
        Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");
        Assert.False(await f.Service.MoveDirectoryAsync(f.Root, target)); Assert.True(File.Exists(Path.Combine(target, "keep.txt")));
        f.Store.FailSave = true;
        Assert.False(await f.Service.MoveDirectoryAsync(f.Root, Path.Combine(f.Base, "new")));
        Assert.True(Directory.Exists(f.Root)); Assert.False(Directory.Exists(Path.Combine(f.Base, "new")));
        Assert.Equal(instance.RootDirectory, f.Service.SelectedInstance!.RootDirectory); Assert.Contains("已还原", f.Service.Error);
    }

    [Fact]
    public async Task Running_launch_and_program_data_ancestors_cannot_be_moved_or_deleted()
    {
        using var f = new Fixture(); var item = await f.Import();
        var active = f.Service.LaunchSelectedAsync();
        await f.Launcher.Started.Task;
        Assert.Throws<InvalidOperationException>(() => f.Service.PrepareDeleteInstance(item.Id));
        Assert.False(await f.Service.MoveDirectoryAsync(f.Root, Path.Combine(f.Base, "new")));
        f.Launcher.Exit.SetResult(0); await active;
        Assert.False(await f.Service.MoveDirectoryAsync(f.Root, Path.Combine(f.Base, "wonder", "nested")));
        var operations = new InstanceFileOperations();
        Assert.Throws<IOException>(() => operations.InspectDirectory(Path.GetPathRoot(f.Root)!));
    }

    [Fact]
    public void Recycle_sink_aborts_permanent_delete_fallback()
    {
        var sink = new InstanceFileOperations.RecycleSink();
        Assert.True(sink.PreDeleteItem(0, IntPtr.Zero) < 0);
        Assert.Equal(0, sink.PreDeleteItem(0x80, IntPtr.Zero));
    }

    [Fact]
    public async Task Native_windows_recycle_moves_only_a_disposable_test_folder()
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Path.Combine(Path.GetTempPath(), "Wonder.RecycleSmoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "disposable-smoke.txt"), "Wonder test file only");
        var operations = new InstanceFileOperations();
        await operations.RecycleDirectoryAsync(folder, operations.InspectDirectory(folder));
        Assert.False(Directory.Exists(folder));
    }

    private sealed class Fixture : IDisposable
    {
        public string Base { get; } = Path.Combine(Path.GetTempPath(), "Wonder.DirectoryTests", Guid.NewGuid().ToString("N"));
        public string Root => Path.Combine(Base, "game");
        public Store Store { get; } = new();
        public Files Files { get; }
        public Launcher Launcher { get; } = new();
        public InstanceService Service { get; }
        public Fixture()
        {
            WriteVersion("one"); Directory.CreateDirectory(Path.Combine(Root, "saves")); File.WriteAllText(Path.Combine(Root, "saves", "keep.txt"), "save");
            Files = new(Base); Service = new(Store, new InstanceDiscovery(), Launcher, Path.Combine(Base, "wonder", "cache"), Files);
        }
        public void WriteVersion(string name, string? parent = null)
        {
            var dir = Path.Combine(Root, "versions", name); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, name + ".json"), System.Text.Json.JsonSerializer.Serialize(new { id = name, clientVersion = "1.20.1", inheritsFrom = parent }));
        }
        public async Task<GameInstance> Import() { await Service.ImportAsync(Root); Assert.Null(Service.Error); return Assert.Single(Service.Catalog.Instances); }
        public void Dispose() { if (Directory.Exists(Base)) Directory.Delete(Base, true); }
    }
    private sealed class Store : IInstanceStore
    {
        private InstanceCatalog _catalog = new(); public bool FailSave;
        public InstanceCatalog Load() => _catalog;
        public void Save(InstanceCatalog catalog) { if (FailSave) throw new IOException("test disk failure"); _catalog = catalog; }
    }
    private sealed class Files(string root) : IInstanceFileOperations
    {
        private readonly InstanceFileOperations _real = new();
        public int Recycles; public bool FailRecycle; public string? RecycledPath;
        public string InspectDirectory(string path) => _real.InspectDirectory(path);
        public void EnsureGameRoot(string path) => _real.EnsureGameRoot(path);
        public void EnsureNoVersionDependents(GameInstance item, string path) => _real.EnsureNoVersionDependents(item, path);
        public Task MoveDirectoryAsync(string path, string destination, string identity) => _real.MoveDirectoryAsync(path, destination, identity);
        public Task RecycleDirectoryAsync(string path, string identity)
        {
            if (FailRecycle) throw new IOException("test recycling unavailable");
            Assert.Equal(identity, InspectDirectory(path)); Recycles++;
            RecycledPath = Path.Combine(root, "recycled-" + Guid.NewGuid().ToString("N")); Directory.Move(path, RecycledPath); return Task.CompletedTask;
        }
    }
    private sealed class Launcher : IGameLauncher
    {
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<int> Exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<LaunchPlan> BuildPlanAsync(LaunchRequest request, CancellationToken ct = default) => Task.FromResult(new LaunchPlan("java", request.Instance.DefaultGameDirectory, [], request.Instance.Id));
        public Task<int> RunAsync(LaunchPlan plan, Action started, Action<string> output, CancellationToken ct = default) { started(); Started.SetResult(); return Exit.Task; }
    }
}
