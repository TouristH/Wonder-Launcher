// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Avalonia.Controls;
using Wonder.Application;
using Wonder.Infrastructure;
using Wonder.Core;
using System.Text.Json;

namespace Wonder.Desktop;

public sealed partial class MainWindow
{
    private readonly DownloadEngine _downloadEngine;
    private readonly DownloadService _downloads;
    private DownloadCenterWindow? _downloadCenter;

    private void OpenDownloadCenter()
    {
        _workspace.CancelGesture();
        if (_downloadCenter is not null)
        {
            if (_downloadCenter.WindowState == WindowState.Minimized) _downloadCenter.WindowState = WindowState.Normal;
            _downloadCenter.Activate(); return;
        }
        _downloadCenter = new DownloadCenterWindow(_downloads, _instances, OpenDownloadSettings) { FontFamily = FontFamily };
        _downloadCenter.Closed += (_, _) => _downloadCenter = null;
        _downloadCenter.Show(this);
    }

    private void OpenDownloadSettings()
    {
        OpenSettings();
        _settingsWindow?.ShowLauncherSettings();
    }

    private async Task ImportDownloadedInstanceAsync(string path)
    {
        if (_instances.IsReadBlocked) throw new InvalidOperationException("实例索引读取失败，原文件已保留");
        await _instances.ImportInstalledAsync(path);
        if (_instances.Error is { } error) throw new InvalidOperationException(error);
        var version = System.IO.Path.GetFileName(path);
        var root = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(path))!;
        var installed = _instances.Catalog.Instances.FirstOrDefault(x => x.VersionId == version && WonderPath.Comparer.Equals(x.RootDirectory, root));
        if (installed is null) throw new InvalidOperationException("实例管理正忙，安装文件已保留");
        var markerPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(root)!, ".wonder-complete.json");
        // Native instance configuration is authoritative, including edits made after installation.
        // The completion marker only supplies settings for older downloads without that configuration.
        if (installed.Source != InstanceSource.Wonder && System.IO.File.Exists(markerPath))
        {
            using var marker = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(markerPath));
            var data = marker.RootElement;
            var name = data.TryGetProperty("instanceName", out var title) ? title.GetString() : null;
            var java = data.TryGetProperty("javaPath", out var javaPath) ? javaPath.GetString() : null;
            _instances.UpdateInstance(installed.Id, name ?? installed.Name, installed.Overrides with { JavaPath = java ?? installed.Overrides.JavaPath });
            if (_instances.Error is { } updateError) throw new InvalidOperationException(updateError);
        }
    }
}
