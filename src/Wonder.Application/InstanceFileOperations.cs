// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Core;

namespace Wonder.Application;

public interface IInstanceFileOperations
{
    string InspectDirectory(string path);
    void EnsureGameRoot(string path);
    void EnsureNoVersionDependents(GameInstance instance, string directory);
    Task RecycleDirectoryAsync(string path, string expectedIdentity);
    Task MoveDirectoryAsync(string path, string destination, string expectedIdentity);
}

public sealed class InstanceDeletePlan
{
    public string Title { get; }
    public string Path { get; }
    public string Description { get; }
    public int AffectedCount => Ids.Length;
    internal string[] Ids { get; }
    internal string Identity { get; }
    internal string? InstanceId { get; }
    internal object Owner { get; }
    internal InstanceDeletePlan(object owner, string title, string path, string description, string[] ids, string identity, string? instanceId)
    { Owner = owner; Title = title; Path = path; Description = description; Ids = ids; Identity = identity; InstanceId = instanceId; }
}

public sealed partial class InstanceService
{
    private readonly IInstanceFileOperations? _files;
    private readonly object _planOwner = new();
    private bool _fileOperation;

    public InstanceDeletePlan PrepareDeleteInstance(string id)
    {
        InstanceCatalog catalog;
        lock (_gate) { RequireFileOperationAvailable(); catalog = _catalog; }
        return PrepareDeleteInstance(catalog, id);
    }

    public InstanceDeletePlan PrepareDeleteDirectory(string rootDirectory)
    {
        InstanceCatalog catalog;
        lock (_gate) { RequireFileOperationAvailable(); catalog = _catalog; }
        return PrepareDeleteDirectory(catalog, rootDirectory);
    }

    private InstanceDeletePlan PrepareDeleteInstance(InstanceCatalog catalog, string id)
    {
        var item = Require(catalog, id);
        if (item.NativeConfigurationDiagnostic is not null) throw new InvalidOperationException("Wonder 实例配置不可用，修复配置并重新读取后才能删除实例目录。");
        var directory = item.Source == InstanceSource.Wonder ? item.RootDirectory : item.Source is InstanceSource.Prism or InstanceSource.MultiMc ? item.SourcePath : Path.Combine(item.VersionsDirectory, item.VersionId);
        directory = Full(directory);
        if (!Within(directory, item.RootDirectory) || Same(directory, item.RootDirectory) && item.Source != InstanceSource.Wonder) throw new InvalidOperationException("无法确定独立实例目录，拒绝删除共享游戏目录。");
        if (item.Source == InstanceSource.Wonder && item.CollectionDirectory is { } collection &&
            WonderPath.Comparer.Equals(Path.GetFileName(directory), ".minecraft") &&
            Path.GetDirectoryName(directory) is { } container && Path.GetDirectoryName(container) is { } instances &&
            Same(instances, Path.Combine(collection, "instances"))) directory = container;
        ValidateOwnedPath(directory);
        EnsureNoOtherReferences(catalog, directory, [id]);
        if (item.Source == InstanceSource.Wonder) _files!.EnsureGameRoot(item.RootDirectory);
        else _files!.EnsureNoVersionDependents(item, directory);
        var note = "将移入回收站此实例目录及其中的存档、模组和配置，并移除 1 个实例登记。";
        if (!Within(item.ResolveSettings(catalog.GlobalSettings).GameDirectory!, directory)) note += "\n此实例使用目录之外的游戏数据；共享或外部游戏目录、资源和依赖保持原样。";
        return new(_planOwner, item.Name, directory, note, [id], _files.InspectDirectory(directory), id);
    }

    private InstanceDeletePlan PrepareDeleteDirectory(InstanceCatalog catalog, string rootDirectory)
    {
        var path = Full(rootDirectory);
        var members = catalog.Instances.Where(x => Same(x.GameDirectoryGroup, path)).ToArray();
        if (!catalog.GameDirectories.Any(root => Same(root, path))) throw new ArgumentException("游戏目录未登记。");
        ValidateOwnedPath(path); _files!.EnsureGameRoot(path); EnsureNoOtherReferences(catalog, path, members.Select(x => x.Id).ToArray());
        return new(_planOwner, Path.GetFileName(path), path, $"将移入回收站整个游戏目录，包括其中的游戏、存档、模组、资源与配置，并移除 {members.Length} 个实例登记。目录内其他启动器文件也会一起移入回收站。", members.Select(x => x.Id).Order().ToArray(), _files.InspectDirectory(path), null);
    }

    public Task<bool> DeleteAsync(InstanceDeletePlan plan, string confirmation) => RunFileOperationAsync(catalog => DeleteFileAsync(catalog, plan, confirmation));

    private async Task<bool> DeleteFileAsync(InstanceCatalog catalog, InstanceDeletePlan plan, string confirmation)
    {
        if (confirmation != "sure") throw new ArgumentException("请准确输入小写 sure。");
        if (!ReferenceEquals(plan.Owner, _planOwner)) throw new ArgumentException("删除确认已失效，请重新打开。");
        var current = plan.InstanceId is { } id ? PrepareDeleteInstance(catalog, id) : PrepareDeleteDirectory(catalog, plan.Path);
        if (!Same(current.Path, plan.Path) || current.Identity != plan.Identity || !current.Ids.SequenceEqual(plan.Ids)) throw new InvalidOperationException("目录或实例发生变化，请重新核对后删除。");
        var next = Copy(catalog); next.Instances.RemoveAll(x => plan.Ids.Contains(x.Id));
        if (plan.InstanceId is null) next.GameDirectories.RemoveAll(root => Same(root, plan.Path));
        if (next.SelectedInstanceId is { } selected && plan.Ids.Contains(selected)) next.SelectedInstanceId = null;
        next.Validate();
        var recycled = false;
        try
        {
            await _files!.RecycleDirectoryAsync(plan.Path, plan.Identity); recycled = true;
            _store.Save(Copy(next), Copy(catalog));
            lock (_gate) { _catalog = next; Error = null; StatusMessage = "已移入系统回收站。"; }
            return true;
        }
        catch (Exception ex)
        {
            if (recycled) lock (_gate) _loadFailed = true;
            Error = recycled ? "文件已移入回收站，但登记保存失败。原登记已保留，请从系统回收站还原该目录后重试。\n" + plan.Path + "\n" + ex.Message : "未完成回收，登记已保留：" + ex.Message;
            return false;
        }
    }

    public Task<bool> MoveDirectoryAsync(string rootDirectory, string targetFullPath) => RunFileOperationAsync(catalog => MoveDirectoryCoreAsync(catalog, rootDirectory, targetFullPath));

    private async Task<bool> MoveDirectoryCoreAsync(InstanceCatalog catalog, string rootDirectory, string targetFullPath)
    {
        if (!Path.IsPathFullyQualified(rootDirectory) || !Path.IsPathFullyQualified(targetFullPath)) throw new ArgumentException("目录路径必须是完整路径。");
        var source = Full(rootDirectory); var target = Full(targetFullPath);
        if (!catalog.GameDirectories.Any(root => Same(root, source))) throw new ArgumentException("游戏目录未登记。");
        _files!.EnsureGameRoot(source);
        EnsureNoOtherReferences(catalog, source, catalog.Instances.Where(x => Same(x.GameDirectoryGroup, source)).Select(x => x.Id).ToArray());
        ValidateOwnedPath(source); ValidateOwnedPath(target);
        if (Same(source, target)) return true;
        if (Within(source, target) || Within(target, source)) throw new ArgumentException("新旧目录不能互相包含。");
        if (catalog.GameDirectories.Any(root => !Same(root, source) && (Within(target, root) || Within(root, target)))) throw new ArgumentException("目标位置与其他已登记游戏目录重叠。");
        var identity = _files.InspectDirectory(source);
        var next = Copy(catalog);
        next.GameDirectories = next.GameDirectories.Select(root => MapPath(root, source, target)).ToList();
        next.Instances = next.Instances.Select(x => RebaseInstance(x, source, target)).ToList();
        next.GlobalSettings = RebaseSettings(next.GlobalSettings, source, target);
        next.Validate();
        var moved = false;
        try
        {
            await _files!.MoveDirectoryAsync(source, target, identity); moved = true;
            _store.Save(Copy(next), Copy(catalog));
            lock (_gate)
            {
                _catalog = next;
                if (_browseDirectory is not null) _browseDirectory = MapPath(_browseDirectory, source, target);
                Error = null; StatusMessage = "游戏目录已修改：" + target;
            }
            return true;
        }
        catch (Exception ex)
        {
            if (moved)
            {
                try { await _files!.MoveDirectoryAsync(target, source, _files.InspectDirectory(target)); Error = "登记未保存，文件夹已还原至原位置。\n" + ex.Message; }
                catch (Exception rollback) { Error = $"登记保存失败，文件夹未能还原。请将 {target} 移回 {source} 后重试。\n{ex.Message}\n{rollback.Message}"; lock (_gate) _loadFailed = true; }
            }
            else Error = "目录未修改：" + ex.Message;
            return false;
        }
    }

    private async Task<bool> RunFileOperationAsync(Func<InstanceCatalog, Task<bool>> operation)
    {
        await _catalogWriter.WaitAsync().ConfigureAwait(false);
        var started = false;
        try
        {
            InstanceCatalog catalog;
            lock (_gate)
            {
                RequireFileOperationAvailable(writerHeld: true);
                catalog = _catalog;
                _fileOperation = _operation = started = true;
                Error = null;
            }
            Notify();
            return await Task.Run(() => operation(catalog)).ConfigureAwait(false);
        }
        catch (Exception ex) { lock (_gate) Error = ex.Message; return false; }
        finally
        {
            if (started) lock (_gate) _fileOperation = _operation = false;
            _catalogWriter.Release();
            Notify();
        }
    }

    private void RequireFileOperationAvailable(bool writerHeld = false)
    {
        if (_loadFailed) throw new InvalidOperationException("实例索引读取失败，请先修复再进行文件操作。");
        if (_operation || _pendingLoad || _launchId is not null || !writerHeld && _pendingSaves != 0)
            throw new InvalidOperationException("实例正在启动、运行或读取，暂不能修改游戏文件。");
        if (_files is null) throw new InvalidOperationException("当前环境未提供游戏文件操作服务。");
    }
    private void ValidateOwnedPath(string path)
    {
        var dataDirectory = Path.GetDirectoryName(Full(_cacheDirectory))!;
        if (Within(dataDirectory, path) || Within(path, dataDirectory) || Within(AppContext.BaseDirectory, path) || Within(path, AppContext.BaseDirectory)) throw new InvalidOperationException("不能修改包含 Wonder 程序或数据的目录。");
    }
    private static void EnsureNoOtherReferences(InstanceCatalog catalog, string path, string[] excluded)
    {
        if (catalog.GameDirectories.Any(root => !Same(root, path) && Within(root, path)))
            throw new InvalidOperationException("此目录包含其他已登记游戏目录，请先分别处理。");
        foreach (var other in catalog.Instances.Where(x => !excluded.Contains(x.Id)))
            if (new[] { other.SourcePath, other.DefaultGameDirectory, other.LibraryDirectory, other.AssetsDirectory, other.VersionsDirectory, other.LocalLibraryDirectory, other.ResolveSettings(catalog.GlobalSettings).GameDirectory, other.ResolveSettings(catalog.GlobalSettings).JavaPath }.Concat(other.MetadataPaths).Any(p => p is not null && Path.IsPathFullyQualified(p) && Within(p, path)))
                throw new InvalidOperationException("此目录仍被其他实例引用，不能删除：" + other.Name);
    }
    private static bool SameSource(GameInstance left, GameInstance right)
    {
        if (!Same(left.SourcePath, right.SourcePath)) return false;
        var profile = Profile(left);
        return profile == Profile(right) &&
            (profile is not null || left.Source is InstanceSource.Prism or InstanceSource.MultiMc || left.VersionId == right.VersionId);
    }
    private static string? Profile(GameInstance item)
    {
        if (item.SourceProfileId is not null) return item.SourceProfileId;
        var prefix = Path.Combine(item.RootDirectory, "launcher_profiles.json") + " · ";
        return item.ConfigSource.StartsWith(prefix, WonderPath.Comparison) ? item.ConfigSource[prefix.Length..] : null;
    }
    private static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static bool Same(string left, string right) => string.Equals(Full(left), Full(right), WonderPath.Comparison);
    private static bool Within(string path, string root) => Same(path, root) || Full(path).StartsWith(Full(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, WonderPath.Comparison);
    private static string MapPath(string path, string from, string to) => Path.IsPathFullyQualified(path) && Within(path, from) ? Same(path, from) ? Full(to) : Path.Combine(to, Path.GetRelativePath(from, path)) : path;
    private static string? MapText(string? text, string from, string to)
    {
        if (text is null) return null;
        var source = Full(from); var target = Full(to);
        foreach (var forwardSlashes in new[] { false, true })
        {
            var oldPath = forwardSlashes ? source.Replace('\\', '/') : source;
            var newPath = forwardSlashes ? target.Replace('\\', '/') : target;
            var pattern = System.Text.RegularExpressions.Regex.Escape(oldPath) + "(?=$|[\\\\/\\s\"'])";
            text = System.Text.RegularExpressions.Regex.Replace(text, pattern, _ => newPath, (OperatingSystem.IsWindows() ? System.Text.RegularExpressions.RegexOptions.IgnoreCase : System.Text.RegularExpressions.RegexOptions.None) | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }
        return text;
    }
    private static InstanceSettings RebaseSettings(InstanceSettings settings, string from, string to) => settings with
    {
        JavaPath = settings.JavaPath is { } java ? MapPath(java, from, to) : null,
        GameDirectory = settings.GameDirectory is { } game ? MapPath(game, from, to) : null,
        JvmArguments = MapText(settings.JvmArguments, from, to), GameArguments = MapText(settings.GameArguments, from, to)
    };
    private static GameInstance RebaseImportedSettings(GameInstance item, string from, string to) => item with
    { DefaultGameDirectory = MapPath(item.DefaultGameDirectory, from, to), ImportedSettings = RebaseSettings(item.ImportedSettings, from, to) };
    private static GameInstance RebaseInstance(GameInstance item, string from, string to) => RebaseImportedSettings(item, from, to) with
    {
        SourcePath = MapPath(item.SourcePath, from, to), RootDirectory = MapPath(item.RootDirectory, from, to),
        CollectionDirectory = item.CollectionDirectory is null ? null : MapPath(item.CollectionDirectory, from, to),
        LibraryDirectory = MapPath(item.LibraryDirectory, from, to), AssetsDirectory = MapPath(item.AssetsDirectory, from, to),
        VersionsDirectory = MapPath(item.VersionsDirectory, from, to), LocalLibraryDirectory = item.LocalLibraryDirectory is { } local ? MapPath(local, from, to) : null,
        PresetIconPath = item.PresetIconPath is { } icon ? MapPath(icon, from, to) : null,
        MetadataPaths = item.MetadataPaths.Select(p => MapPath(p, from, to)).ToList(), ConfigSource = MapText(item.ConfigSource, from, to)!,
        Overrides = RebaseSettings(item.Overrides, from, to),
        PreviousRootDirectories = Same(item.RootDirectory, from) ? item.PreviousRootDirectories.Append(from).Distinct(WonderPath.Comparer).ToList() : [.. item.PreviousRootDirectories]
    };
}
