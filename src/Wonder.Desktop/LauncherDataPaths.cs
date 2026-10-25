// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

namespace Wonder.Desktop;

internal static class LauncherDataPaths
{
    // AppContext.BaseDirectory remains the executable directory for single-file builds.
    internal static string RootDirectory => Path.Combine(AppContext.BaseDirectory, "WonderData");
    internal static string WorkspacePath => Path.Combine(RootDirectory, "workspace.json");
    internal static string SettingsPath => Path.Combine(RootDirectory, "settings.json");
    internal static string PluginsDirectory => Path.Combine(RootDirectory, "plugins");
}
