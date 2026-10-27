// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Reflection;

namespace Wonder.Core;

public static class LauncherBuildInfo
{
    // Use a known Wonder assembly so tests and alternate hosts cannot supply their own version.
    public static string Version { get; } = typeof(LauncherBuildInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? throw new InvalidOperationException("缺少 Wonder 构建版本元数据。");
}
