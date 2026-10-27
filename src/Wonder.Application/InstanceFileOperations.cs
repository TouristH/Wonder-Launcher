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
        lock (_gate)
        {
            RequireFileOperationAvailable();
            var item = Require(_catalog, id);
            var directory = item.Source is InstanceSource.Prism or InstanceSource.MultiMc ? item.SourcePath : Path.Combine(item.VersionsDirectory, item.VersionId);
            directory = Full(directory);
            if (!Within(directory, item.RootDirectory) || Same(directory, item.RootDirectory)) throw new InvalidOperationException("无法确定独立实例目录，拒绝删除共享游戏目录。");
            ValidateOwnedPath(directory);
            EnsureNoOtherReferences(directory, [id]);
            _files!.EnsureNoVersionDependents(item, directory);
            var note = "将移入回收站此实例目录及其中的存档、模组和配置，并移除 1 个实例登记。";
            if (!Within(item.EffectiveSettings.GameDirectory!, directory)) note += "\n此实例使用目录之外的游戏数据；共享或外部游戏目录、资源和依赖保持原样。";
            return new(_planOwner, item.Name, directory, note, [id], _files.InspectDirectory(directory), id);
        }
    }

    public InstanceDeletePlan PrepareDeleteDirectory(string rootDirectory)
    {
        lock (_gate)
        {
            RequireFileOperationAvailable();
            var path = Full(rootDirectory);
            var members = _catalog.Instances.Where(x => Same(x.RootDirectory, path)).ToArray();
            if (!_catalog.GameDirectories.Any(root => Same(root, path))) throw new ArgumentException("游戏目录未登记。");
            ValidateOwnedPath(path); _files!.EnsureGameRoot(path); EnsureNoOtherReferences(path, members.Select(x => x.Id).ToArray());
            return new(_planOwner, Path.GetFileName(path), path, $"将移入回收站整个游戏目录，包括其中的游戏、存档、模组、资源与配置，并移除 {members.Length} 个实例登记。目录内其他启动器文件也会一起移入回收站。", members.Select(x => x.Id).Order().ToArray(), _files!.InspectDirectory(path), null);
        }
    }

    public async Task<bool> DeleteAsync(InstanceDeletePlan plan, string confirmation)
    {
        InstanceCatalog next;
        lock (_gate)
        {
            try
            {
                if (confirmation != "sure") throw new ArgumentException("请准确输入小写 sure。");
                if (!ReferenceEquals(plan.Owner, _planOwner)) throw new ArgumentException("删除确认已失效，请重新打开。");
                var current = plan.InstanceId is { } id ? PrepareDeleteInstance(id) : PrepareDeleteDirectory(plan.Path);
                if (!Same(current.Path, plan.Path) || current.Identity != plan.Identity || !current.Ids.SequenceEqual(plan.Ids)) throw new InvalidOperationException("目录或实例发生变化，请重新核对后删除。");
                next = Copy(_catalog); next.Instances.RemoveAll(x => plan.Ids.Contains(x.Id));
                if (plan.InstanceId is null) next.GameDirectories.RemoveAll(root => Same(root, plan.Path));
                if (next.SelectedInstanceId is { } selected && plan.Ids.Contains(selected)) next.SelectedInstanceId = null;
                next.Validate(); _fileOperation = _operation = true; Error = null;
            }
            catch (Exception ex) { Error = ex.Message; Notify(); return false; }
        }
        Notify();
        var recycled = false;
        try
        {
            await _files!.RecycleDirectoryAsync(plan.Path, plan.Identity); recycled = true;
            lock (_gate) { _store.Save(Copy(next)); _catalog = next; Error = null; StatusMessage = "已移入系统回收站。"; }
            return true;
        }
        catch (Exception ex)
        {
            if (recycled) lock (_gate) _loadFailed = true;
            Error = recycled ? "文件已移入回收站，但登记保存失败。原登记已保留，请从系统回收站还原该目录后重试。\n" + plan.Path + "\n" + ex.Message : "未完成回收，登记已保留：" + ex.Message;
            return false;
        }
        finally { lock (_gate) _fileOperation = _operation = false; Notify(); }
    }

    public async Task<bool> MoveDirectoryAsync(string rootDirectory, string targetFullPath)
    {
        InstanceCatalog next; string source, target, identity;
        lock (_gate)
        {
            try
            {
                RequireFileOperationAvailable();
                if (!Path.IsPathFullyQualified(rootDirectory) || !Path.IsPathFullyQualified(targetFullPath)) throw new ArgumentException("目录路径必须是完整路径。");
                source = Full(rootDirectory); target = Full(targetFullPath);
                if (!_catalog.GameDirectories.Any(root => Same(root, source))) throw new ArgumentException("游戏目录未登记。");
                _files!.EnsureGameRoot(source);
                EnsureNoOtherReferences(source, _catalog.Instances.Where(x => Same(x.RootDirectory, source)).Select(x => x.Id).ToArray());
                ValidateOwnedPath(source); ValidateOwnedPath(target);
                if (Same(source, target)) return true;
                if (Within(source, target) || Within(target, source)) throw new ArgumentException("新旧目录不能互相包含。");
                if (_catalog.GameDirectories.Any(root => !Same(root, source) && (Within(target, root) || Within(root, target)))) throw new ArgumentException("目标位置与其他已登记游戏目录重叠。");
                identity = _files!.InspectDirectory(source);
                next = Copy(_catalog);
                next.GameDirectories = next.GameDirectories.Select(root => MapPath(root, source, target)).ToList();
                next.Instances = next.Instances.Select(x => RebaseInstance(x, source, target)).ToList();
                next.GlobalSettings = RebaseSettings(next.GlobalSettings, source, target);
                next.Validate(); _fileOperation = _operation = true; Error = null;
            }
            catch (Exception ex) { Error = ex.Message; Notify(); return false; }
        }
        Notify(); var moved = false;
        try
        {
            await _files!.MoveDirectoryAsync(source, target, identity); moved = true;
            lock (_gate) { _store.Save(Copy(next)); _catalog = next; Error = null; StatusMessage = "游戏目录已修改：" + target; }
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
        finally { lock (_gate) _fileOperation = _operation = false; Notify(); }
    }

    private void RequireFileOperationAvailable()
    {
        if (_loadFailed) throw new InvalidOperationException("实例索引读取失败，请先修复再进行文件操作。");
        if (IsBusy) throw new InvalidOperationException("实例正在启动、运行或读取，暂不能修改游戏文件。");
        if (_files is null) throw new InvalidOperationException("当前环境未提供游戏文件操作服务。");
    }
    private void ValidateOwnedPath(string path)
    {
        var dataDirectory = Path.GetDirectoryName(Full(_cacheDirectory))!;
        if (Within(dataDirectory, path) || Within(path, dataDirectory) || Within(AppContext.BaseDirectory, path) || Within(path, AppContext.BaseDirectory)) throw new InvalidOperationException("不能修改包含 Wonder 程序或数据的目录。");
    }
    private void EnsureNoOtherReferences(string path, string[] excluded)
    {
        if (_catalog.GameDirectories.Any(root => !Same(root, path) && Within(root, path)))
            throw new InvalidOperationException("此目录包含其他已登记游戏目录，请先分别处理。");
        foreach (var other in _catalog.Instances.Where(x => !excluded.Contains(x.Id)))
            if (new[] { other.SourcePath, other.DefaultGameDirectory, other.LibraryDirectory, other.AssetsDirectory, other.VersionsDirectory, other.LocalLibraryDirectory, other.ResolveSettings(_catalog.GlobalSettings).GameDirectory, other.ResolveSettings(_catalog.GlobalSettings).JavaPath }.Concat(other.MetadataPaths).Any(p => p is not null && Path.IsPathFullyQualified(p) && Within(p, path)))
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
        return item.ConfigSource.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? item.ConfigSource[prefix.Length..] : null;
    }
    private static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static bool Same(string left, string right) => string.Equals(Full(left), Full(right), StringComparison.OrdinalIgnoreCase);
    private static bool Within(string path, string root) => Same(path, root) || Full(path).StartsWith(Full(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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
            text = System.Text.RegularExpressions.Regex.Replace(text, pattern, _ => newPath, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
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
        LibraryDirectory = MapPath(item.LibraryDirectory, from, to), AssetsDirectory = MapPath(item.AssetsDirectory, from, to),
        VersionsDirectory = MapPath(item.VersionsDirectory, from, to), LocalLibraryDirectory = item.LocalLibraryDirectory is { } local ? MapPath(local, from, to) : null,
        MetadataPaths = item.MetadataPaths.Select(p => MapPath(p, from, to)).ToList(), ConfigSource = MapText(item.ConfigSource, from, to)!,
        Overrides = RebaseSettings(item.Overrides, from, to),
        PreviousRootDirectories = Same(item.RootDirectory, from) ? item.PreviousRootDirectories.Append(from).Distinct(StringComparer.OrdinalIgnoreCase).ToList() : [.. item.PreviousRootDirectories]
    };
}
