// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text.Json;

namespace Wonder.Extensibility;

public sealed partial class PluginManager
{
    /// <summary>Removes the installed package, retaining its independent configuration and user data.</summary>
    public void Uninstall(string id) => Change(() =>
    {
        var local = RequireInstalled(id);
        if (_failedStops.Contains(id))
            throw new InvalidOperationException("此插件在本次启动中曾停用失败，安装文件仍保留，请重启启动器后再删除。");
        CheckWritable();
        CheckLink(_dataDirectory);
        CheckLink(RootDirectory);
        var relative = Path.GetRelativePath(RootDirectory, local.Directory);
        if (!IsPathSegment(relative) || !string.Equals(relative, id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("插件安装目录不在本地插件根目录内。");
        CheckUninstallTree(local.Directory);

        var stagingRoot = Path.Combine(_dataDirectory, ".plugin-staging");
        CheckLink(stagingRoot);
        Directory.CreateDirectory(stagingRoot);
        var removed = Path.Combine(stagingRoot, "removed-" + Guid.NewGuid().ToString("N"));

        var enabled = new HashSet<string>(_enabled, StringComparer.Ordinal);
        var consents = new Dictionary<string, string>(_consents, StringComparer.Ordinal);
        var changed = enabled.Remove(id);
        changed |= consents.Remove(id);
        if (changed)
        {
            WriteAtomic(_settingsPath, JsonSerializer.Serialize(new PluginSettings(enabled.Order(StringComparer.Ordinal).ToArray(), consents), Json));
            _enabled = enabled;
            _consents = consents;
        }
        Stop(id);
        if (_failures.TryGetValue(id, out var failure))
            throw new InvalidOperationException(failure + " 安装文件仍保留，请重启启动器后再删除。");

        // A failed cleanup must not leave a partially deleted package discoverable or enabled.
        CheckLink(RootDirectory);
        CheckLink(local.Directory);
        CheckLink(stagingRoot);
        Directory.Move(local.Directory, removed);
        _installed.Remove(id);
        _failures.Remove(id);
        _logs.Remove(id);
        try { Directory.Delete(removed, recursive: true); }
        catch (Exception error) when (IsFileError(error))
        {
            throw new IOException($"插件已卸载，但部分程序文件未能清理。配置与数据已保留，请关闭启动器后清理：{removed}", error);
        }
    });

    private static void CheckUninstallTree(string directory)
    {
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.TryPop(out var current))
        {
            CheckLink(current);
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                CheckLink(entry);
                if (Directory.Exists(entry)) pending.Push(entry);
            }
        }
    }
}
