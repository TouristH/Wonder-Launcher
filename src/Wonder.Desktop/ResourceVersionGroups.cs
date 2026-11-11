// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Text.RegularExpressions;
using Wonder.Core;

namespace Wonder.Desktop;

internal sealed record ResourceVersionGroup(string MinecraftVersion, string Loader, IReadOnlyList<ResourceVersion> Releases);
internal sealed record ResourceVersionFamily(string Name, IReadOnlyList<ResourceVersionGroup> Groups);

internal static partial class ResourceVersionGroups
{
    internal const string OtherVersions = "快照及其他";

    public static IReadOnlyList<ResourceVersionFamily> Create(IReadOnlyList<ResourceVersion> releases)
    {
        var groups = releases
            .SelectMany(release => release.GameVersions.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).DefaultIfEmpty("未标注版本"), (release, version) => (release, version))
            .SelectMany(x => x.release.Loaders.Where(y => !string.IsNullOrWhiteSpace(y)).Distinct(StringComparer.OrdinalIgnoreCase).DefaultIfEmpty(""), (x, loader) => (x.release, x.version, loader: loader.ToLowerInvariant()))
            .GroupBy(x => (x.version, x.loader))
            .Select(g => new ResourceVersionGroup(g.Key.version, g.Key.loader,
                g.Select(x => x.release).DistinctBy(x => x.Id).OrderByDescending(x => x.PublishedAt).ToArray()))
            .OrderByDescending(x => SortKey(x.MinecraftVersion))
            .ThenByDescending(x => x.MinecraftVersion, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Loader, StringComparer.OrdinalIgnoreCase);
        return groups.GroupBy(x => FamilyName(x.MinecraftVersion))
            .Select(x => new ResourceVersionFamily(x.Key, x.ToArray())).ToArray();
    }

    public static (ResourceVersionFamily? Family, ResourceVersionGroup? Group) Focus(IReadOnlyList<ResourceVersionFamily> families, string? minecraftVersion, string? loader)
    {
        var family = string.IsNullOrWhiteSpace(minecraftVersion) ? null : families.FirstOrDefault(x => x.Name == FamilyName(minecraftVersion));
        family ??= families.FirstOrDefault();
        if (family is null) return (null, null);
        var exact = family.Groups.Where(x => x.MinecraftVersion == minecraftVersion).ToArray();
        var candidates = exact.Length == 0 ? family.Groups : exact;
        return (family, candidates.FirstOrDefault(x => x.Loader == loader) ?? candidates.FirstOrDefault());
    }

    public static string DescribeCoverage(IReadOnlyList<ResourceVersionFamily> families)
    {
        var versions = families.SelectMany(x => x.Groups).Select(x => x.MinecraftVersion)
            .Where(x => x != "未标注版本").Distinct(StringComparer.Ordinal).ToArray();
        var numeric = versions.Where(x => FamilyName(x) != OtherVersions).ToArray();
        if (numeric.Length == 0) return versions.Length == 0 ? "未标注" : string.Join("、", versions.Take(5)) + (versions.Length > 5 ? $" 等 {versions.Length} 个版本" : "");
        if (versions.Length == 1) return versions[0];
        var newest = numeric.MaxBy(SortKey);
        var oldest = numeric.MinBy(SortKey);
        return newest == oldest ? $"{newest} 等 {versions.Length} 个版本" : $"{oldest} 至 {newest} · {versions.Length} 个版本";
    }

    private static string FamilyName(string version)
    {
        var match = NumericVersion().Match(version);
        if (!match.Success) return OtherVersions;
        return match.Groups[1].Value == "1" ? $"1.{match.Groups[2].Value}.X" : $"{match.Groups[1].Value}.X";
    }

    private static (int Major, int Minor, int Patch, bool Final) SortKey(string version)
    {
        var match = NumericVersion().Match(version);
        if (!match.Success) return (-1, 0, 0, false);
        int.TryParse(match.Groups[1].ValueSpan, out var major);
        int.TryParse(match.Groups[2].ValueSpan, out var minor);
        int.TryParse(match.Groups[3].ValueSpan, out var patch);
        return (major, minor, patch, match.Length == version.Length);
    }

    [GeneratedRegex(@"^([0-9]+)\.([0-9]+)(?:\.([0-9]+))?(?=$|[- _])", RegexOptions.CultureInvariant)]
    private static partial Regex NumericVersion();
}
