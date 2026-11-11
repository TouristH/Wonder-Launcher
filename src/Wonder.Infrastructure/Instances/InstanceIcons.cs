// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Core;

namespace Wonder.Infrastructure;

/// <summary>Reads local instance artwork only; never downloads icons or rewrites source launchers.</summary>
public static class InstanceIcons
{
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".ico"];

    public static string? FindPreset(GameInstance instance)
    {
        if (instance.PresetIconPath is { } saved && File.Exists(saved)) return saved;
        return instance.Source is InstanceSource.Prism or InstanceSource.MultiMc
            ? ForPrism(instance.SourcePath, instance.RootDirectory)
            : ForStandard(Path.Combine(instance.VersionsDirectory, instance.VersionId), instance.DefaultGameDirectory, instance.RootDirectory);
    }

    internal static string? ForStandard(string versionDirectory, string gameDirectory, string root)
    {
        var candidates = new[] { Path.Combine(versionDirectory, "icon.png"), Path.Combine(versionDirectory, ".hmcl", "icon.png"),
            Path.Combine(versionDirectory, "PCL", "Logo.png"), Path.Combine(versionDirectory, "pack.png") };
        // A shared .minecraft directory is not an individual pack's icon source.
        if (!string.Equals(Path.TrimEndingDirectorySeparator(gameDirectory), Path.TrimEndingDirectorySeparator(root), WonderPath.Comparison))
            candidates = [.. candidates, Path.Combine(gameDirectory, "icon.png"), Path.Combine(gameDirectory, "pack.png")];
        return candidates.FirstOrDefault(File.Exists);
    }

    internal static string? ForPrism(string directory, string root)
    {
        try
        {
            var key = LocalFiles.Ini(Path.Combine(directory, "instance.cfg")).GetValueOrDefault("iconKey");
            // iconKey is a name, not an arbitrary path or URL from a downloaded pack.
            if (!string.IsNullOrWhiteSpace(key) && key.Length <= 200 && key is not "." and not ".." &&
                key.IndexOfAny(['/', '\\', ':', '\0']) < 0 && key.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
            {
                foreach (var extension in Extensions)
                    foreach (var folder in new[] { Path.Combine(root, "icons"), directory })
                    {
                        var candidate = Path.Combine(folder, key + extension);
                        if (File.Exists(candidate)) return candidate;
                    }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        return new[] { Path.Combine(directory, "icon.png"), Path.Combine(directory, "pack.png"),
            Path.Combine(directory, ".minecraft", "icon.png"), Path.Combine(directory, "minecraft", "icon.png") }.FirstOrDefault(File.Exists);
    }
}
