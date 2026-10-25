// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text.Json;
using System.Text.RegularExpressions;
using Wonder.Core;
using static Wonder.Infrastructure.LocalFiles;

namespace Wonder.Infrastructure;

internal static partial class InstanceVersionMetadata
{
    public static List<InstalledModLoader> ReadModLoaders(GameInstance instance) => ReadModLoaders(
        instance.MetadataPaths, instance.VersionsDirectory,
        instance.Source is InstanceSource.Prism or InstanceSource.MultiMc ? Path.Combine(instance.SourcePath, "mmc-pack.json") : null);

    public static List<InstalledModLoader> ReadModLoaders(IEnumerable<string> metadataPaths, string versionsDirectory, string? packPath = null)
    {
        var loaders = new Dictionary<string, string>(StringComparer.Ordinal);
        if (packPath is not null) TryRead(() =>
        {
            var pack = Json(packPath);
            if (pack.Get("formatVersion").Number() != 1) return;
            foreach (var entry in pack.Get("components").Items().Take(128))
            {
                if (entry.Get("disabled").Boolean() == true) continue;
                var name = entry.Get("uid").Text() switch
                {
                    "net.fabricmc.fabric-loader" => "Fabric", "org.quiltmc.quilt-loader" => "Quilt",
                    "net.minecraftforge" => "Forge", "net.neoforged" => "NeoForge", "optifine.OptiFine" => "OptiFine", _ => null
                };
                AddLoader(loaders, name, entry.Get("version").Text());
            }
        });
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in metadataPaths.Take(128)) ReadLoaderMetadata(path, versionsDirectory, loaders, visited, 0);
        // NeoForge can retain Forge bootstrap/support libraries. They are not another installed loader.
        if (loaders.ContainsKey("NeoForge")) loaders.Remove("Forge");
        return new[] { "Fabric", "Quilt", "Forge", "NeoForge", "OptiFine" }
            .Where(loaders.ContainsKey).Select(name => new InstalledModLoader(name, loaders[name])).ToList();
    }

    private static void ReadLoaderMetadata(string path, string versionsDirectory, Dictionary<string, string> loaders, HashSet<string> visited, int depth) => TryRead(() =>
    {
        if (depth >= 32 || !visited.Add(Path.GetFullPath(path))) return;
        var json = Json(path);
        var arguments = json.Get("arguments").Get("game").Items().Take(4096).ToArray();
        for (var index = 0; index + 1 < arguments.Length; index++)
        {
            var name = arguments[index].Text() switch { "--fml.forgeVersion" => "Forge", "--fml.neoForgeVersion" => "NeoForge", _ => null };
            AddLoader(loaders, name, arguments[index + 1].Text());
        }
        foreach (var library in json.Get("libraries").Items().Take(4096))
        {
            var coordinate = library.Get("name").Text()?.Split(':');
            if (coordinate is not { Length: >= 3 and <= 4 }) continue;
            var name = (coordinate[0], coordinate[1]) switch
            {
                ("net.fabricmc", "fabric-loader") => "Fabric", ("org.quiltmc", "quilt-loader") => "Quilt",
                ("net.minecraftforge", "forge" or "fmlloader") => "Forge",
                ("net.neoforged", "neoforge" or "forge") => "NeoForge", ("optifine", "OptiFine") => "OptiFine", _ => null
            };
            AddLoader(loaders, name, coordinate[2]);
        }
        if (NonEmpty(json.Get("inheritsFrom").Text()) is { } parent)
            ReadLoaderMetadata(Under(versionsDirectory, Path.Combine(Segment(parent), parent + ".json")), versionsDirectory, loaders, visited, depth + 1);
    });

    private static void AddLoader(Dictionary<string, string> loaders, string? name, string? rawVersion)
    {
        if (name is null) return;
        var version = ExplicitVersion(rawVersion);
        if (version.Length == 0 || version.Any(char.IsWhiteSpace)) return;
        var separator = version.IndexOf(name == "OptiFine" ? '_' : '-');
        if (name is "Forge" or "NeoForge" or "OptiFine" && separator > 0 && separator + 1 < version.Length && VanillaVersion(version[..separator]).Length > 0 &&
            (name == "OptiFine" || char.IsAsciiDigit(version[separator + 1])))
        {
            var minecraft = version[..separator];
            version = version[(separator + 1)..];
            // Older Forge artifacts repeat the Minecraft version as a branch suffix.
            if (version.EndsWith("-" + minecraft, StringComparison.Ordinal)) version = version[..^(minecraft.Length + 1)];
        }
        loaders.TryAdd(name, version);
    }

    public static string Read(GameInstance instance)
    {
        if (instance.Source is InstanceSource.Prism or InstanceSource.MultiMc)
            return TryRead(() => ReadPrism(Path.Combine(instance.SourcePath, "mmc-pack.json")));
        foreach (var path in instance.MetadataPaths)
        {
            var version = ReadStandard(path, instance.VersionsDirectory);
            if (version.Length > 0) return version;
        }
        return "";
    }

    public static string ReadStandard(string metadataPath, string versionsDirectory)
    {
        var version = TryRead(() => ReadInherited(metadataPath, versionsDirectory, new(StringComparer.OrdinalIgnoreCase), 0));
        if (version.Length > 0) return version;
        return TryRead(() => ExplicitVersion(Ini(Path.Combine(Path.GetDirectoryName(metadataPath)!, "PCL", "Setup.ini"), ':')
            .GetValueOrDefault("VersionVanillaName")));
    }

    private static string ReadInherited(string path, string versionsDirectory, HashSet<string> visited, int depth)
    {
        if (depth >= 32 || !visited.Add(Path.GetFullPath(path))) return "";
        var json = Json(path);
        if (json.ValueKind != JsonValueKind.Object) return "";
        var client = ExplicitVersion(json.Get("clientVersion").Text());
        if (client.Length > 0) return client;
        if (NonEmpty(json.Get("inheritsFrom").Text()) is { } parent)
        {
            var inherited = TryRead(() => ReadInherited(Under(versionsDirectory, Path.Combine(Segment(parent), parent + ".json")), versionsDirectory, visited, depth + 1));
            if (inherited.Length > 0) return inherited;
            // A missing parent file still leaves an explicit vanilla version reference.
            if (VanillaVersion(parent) is { Length: > 0 } vanillaParent) return vanillaParent;
        }
        var id = VanillaVersion(json.Get("id").Text());
        return id.Length > 0 ? id : VanillaVersion(Path.GetFileNameWithoutExtension(path));
    }

    private static string ReadPrism(string path)
    {
        var pack = Json(path);
        if (pack.Get("formatVersion").Number() != 1) return "";
        foreach (var entry in pack.Get("components").Items().Take(128))
            if (entry.Get("uid").Text() == "net.minecraft" && entry.Get("disabled").Boolean() != true)
                return ExplicitVersion(entry.Get("version").Text());
        return "";
    }

    private static string ExplicitVersion(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && !value.Any(char.IsControl) ? value.Trim() : "";
    private static string VanillaVersion(string? value) => value is { Length: > 0 and <= 200 } && VanillaId().IsMatch(value) ? value : "";

    // Never infer a version from a prefix of a user-named pack or loader ID.
    [GeneratedRegex(@"\A(?:[0-9]+\.[0-9]+(?:\.[0-9]+)?(?:-(?:pre|rc)[0-9]+)?|[0-9]{2}w[0-9]{2}[a-z]|[ab][0-9]+\.[0-9]+(?:\.[0-9]+)?(?:_[0-9]+)?)\z", RegexOptions.CultureInvariant)]
    private static partial Regex VanillaId();

    private static string TryRead(Func<string> read)
    {
        try { return read(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException or UnauthorizedAccessException or InvalidOperationException)
        { return ""; }
    }

    private static void TryRead(Action read)
    {
        try { read(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException or UnauthorizedAccessException or InvalidOperationException)
        { }
    }
}
