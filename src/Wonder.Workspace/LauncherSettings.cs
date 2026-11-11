// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text.Json.Serialization;
using Wonder.Core;

namespace Wonder.Workspace;

public enum LauncherTheme { System, Dark, Light }
public enum AccentColor { Mint, Blue, Amber, Rose }
public enum InstanceLayout { Grid, List }

public sealed record LauncherSettings
{
    [JsonRequired] public int Version { get; init; } = 1;
    [JsonRequired] public LauncherTheme Theme { get; init; } = LauncherTheme.Dark;
    [JsonRequired] public AccentColor Accent { get; init; } = AccentColor.Mint;
    [JsonRequired] public int WorkspacePadding { get; init; } = 22;
    public int ComponentScalePercent { get; init; } = 100;
    public bool ShowAreaNumbers { get; init; }
    public InstanceLayout InstanceLayout { get; init; } = InstanceLayout.Grid;
    public DownloadSource DownloadSource { get; init; } = DownloadSource.Automatic;
    public int ConcurrentDownloads { get; init; } = 8;
    public int DownloadConnections { get; init; } = 4;
    public int DownloadRetries { get; init; } = 3;
    [JsonRequired] public int HoldDelayMilliseconds { get; init; } = 450;
    [JsonRequired] public int SidebarExtent { get; init; } = 320;
    [JsonRequired] public int SidebarCloseDelayMilliseconds { get; init; } = 280;

    public void Validate()
    {
        if (Version != 1)
            throw new InvalidDataException("设置文件版本不受支持。");
        if (!Enum.IsDefined(Theme) || !Enum.IsDefined(Accent))
            throw new InvalidDataException("设置中的主题或强调色无效。");
        if (!Enum.IsDefined(InstanceLayout))
            throw new InvalidDataException("实例排列方式无效。");
        if (!Enum.IsDefined(DownloadSource) || ConcurrentDownloads is < 1 or > 32 ||
            DownloadConnections is < 1 or > 16 || DownloadRetries is < 0 or > 8)
            throw new InvalidDataException("下载源或下载并发参数无效。");
        if (ComponentScalePercent is < 50 or > 200)
            throw new InvalidDataException("组件大小必须在 50% 到 200% 之间。");
        if (WorkspacePadding is < 0 or > 40 || HoldDelayMilliseconds is < 250 or > 1000 ||
            SidebarExtent is < 200 or > 600 || SidebarCloseDelayMilliseconds is < 100 or > 1000)
            throw new InvalidDataException("设置中的工作区边距、长按时间或侧边栏选项超出允许范围。");
    }
}
