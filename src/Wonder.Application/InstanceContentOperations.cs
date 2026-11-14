// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Core;

namespace Wonder.Application;

public interface IInstanceContentFiles
{
    IReadOnlyList<InstanceContent> ReadContent(string gameDirectory, InstanceContentKind kind, CancellationToken ct);
    byte[]? ReadContentIcon(string gameDirectory, InstanceContent item, CancellationToken ct);
    void SetContentEnabled(string gameDirectory, InstanceContent item, bool enabled);
    Task RecycleContentAsync(string gameDirectory, InstanceContent item);
}

public sealed partial class InstanceService
{
    public Task<IReadOnlyList<InstanceContent>> ReadContentAsync(string id, InstanceContentKind kind, CancellationToken ct = default)
    {
        var directory = ContentDirectory(id);
        var files = ContentFiles();
        return Task.Run(() => files.ReadContent(directory, kind, ct), ct);
    }

    public Task<byte[]?> ReadContentIconAsync(string id, InstanceContent item, CancellationToken ct = default)
    {
        var directory = ContentDirectory(id);
        var files = ContentFiles();
        return Task.Run(() => files.ReadContentIcon(directory, item, ct), ct);
    }

    public Task<bool> SetContentEnabledAsync(string id, InstanceContent item, bool enabled) => RunFileOperationAsync(catalog =>
    {
        var directory = ResolveContentDirectory(catalog, id);
        ValidateContentScope(item.Path);
        ContentFiles().SetContentEnabled(directory, item, enabled);
        return Task.FromResult(true);
    });

    public Task<bool> DeleteContentAsync(string id, InstanceContent item) => RunFileOperationAsync(async catalog =>
    {
        var directory = ResolveContentDirectory(catalog, id);
        ValidateContentScope(item.Path);
        await ContentFiles().RecycleContentAsync(directory, item).ConfigureAwait(false);
        return true;
    });

    private IInstanceContentFiles ContentFiles() => _files as IInstanceContentFiles ??
        throw new InvalidOperationException("当前环境未提供实例资源管理服务。");

    private string ContentDirectory(string id)
    {
        lock (_gate)
        {
            if (_loadFailed) throw new InvalidOperationException("实例索引读取失败，请修复后重新读取。");
            return ResolveContentDirectory(_catalog, id);
        }
    }

    private static string ResolveContentDirectory(InstanceCatalog catalog, string id)
    {
        var instance = Require(catalog, id);
        if (instance.NativeConfigurationDiagnostic is not null)
            throw new InvalidOperationException("Wonder 实例配置不可用，请修复后重新读取。");
        return instance.ResolveSettings(catalog.GlobalSettings).GameDirectory ?? instance.DefaultGameDirectory;
    }

    private void ValidateContentScope(string path)
    {
        var dataDirectory = Path.GetDirectoryName(Full(_cacheDirectory))!;
        if (Within(path, dataDirectory) || Within(dataDirectory, path) || Within(AppContext.BaseDirectory, path))
            throw new InvalidOperationException("不能修改 Wonder 程序或数据目录。");
    }
}
