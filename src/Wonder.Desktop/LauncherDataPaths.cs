// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

namespace Wonder.Desktop;

internal static class LauncherDataPaths
{
    // A portable macOS bundle must not write into its own application/signature contents.
    internal static string RootDirectory => ResolveRoot(AppContext.BaseDirectory, OperatingSystem.IsMacOS());

    internal static string ResolveRoot(string baseDirectory, bool macOS)
    {
        var directory = new DirectoryInfo(baseDirectory);
        if (macOS && directory.Name == "MacOS" && directory.Parent?.Name == "Contents" &&
            directory.Parent.Parent is { } bundle && bundle.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase) &&
            bundle.Parent is { } portableDirectory)
            return Path.Combine(portableDirectory.FullName, "WonderData");
        return Path.Combine(directory.FullName, "WonderData");
    }
    internal static string WorkspacePath => Path.Combine(RootDirectory, "workspace.json");
    internal static string SettingsPath => Path.Combine(RootDirectory, "settings.json");
    internal static string PluginsDirectory => Path.Combine(RootDirectory, "plugins");
}
