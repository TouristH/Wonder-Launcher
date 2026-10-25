// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wonder.Application;
using Wonder.Core;
using static Wonder.Infrastructure.LocalFiles;

namespace Wonder.Infrastructure;

public sealed class InstanceDiscovery : IInstanceDiscovery
{
    public Task<DiscoveryResult> DiscoverAsync(string selectedPath, CancellationToken ct = default) => Task.Run(() => Discover(selectedPath, ct), ct);

    private static DiscoveryResult Discover(string selectedPath, CancellationToken ct)
    {
        var selected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selectedPath));
        if (!Directory.Exists(selected)) throw new DirectoryNotFoundException("所选目录不存在：" + selected);
        var found = new List<GameInstance>();
        var diagnostics = new List<string>();
        void Add(Func<GameInstance> create)
        {
            ct.ThrowIfCancellationRequested();
            try { var instance = create(); instance.Validate(); if (found.All(x => x.Id != instance.Id)) found.Add(instance); }
            catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or UnauthorizedAccessException or InvalidOperationException)
            { if (diagnostics.Count < 100) diagnostics.Add(ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message); }
        }
        string? prism = null;
        if (File.Exists(Path.Combine(selected, "mmc-pack.json"))) prism = selected;
        else if (Path.GetFileName(selected) is "minecraft" or ".minecraft" && File.Exists(Path.Combine(Path.GetDirectoryName(selected)!, "mmc-pack.json"))) prism = Path.GetDirectoryName(selected);
        if (prism is not null) Add(() => ReadPrism(prism));
        else if (File.Exists(Path.Combine(selected, Path.GetFileName(selected) + ".json")) && Path.GetFileName(Path.GetDirectoryName(selected)) == "versions")
            Add(() => ReadStandard(Path.GetDirectoryName(Path.GetDirectoryName(selected))!, selected));
        else
        {
            var root = Directory.Exists(Path.Combine(selected, "versions")) ? selected : Directory.Exists(Path.Combine(selected, ".minecraft", "versions")) ? Path.Combine(selected, ".minecraft") : Path.GetFileName(selected) == "versions" ? Path.GetDirectoryName(selected)! : selected;
            var versions = Path.Combine(root, "versions");
            var profiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var profilePath = Path.Combine(root, "launcher_profiles.json");
            if (File.Exists(profilePath))
            {
                try
                {
                    var entries = Json(profilePath).Get("profiles");
                    if (entries.ValueKind == JsonValueKind.Object)
                        foreach (var profile in entries.EnumerateObject().Take(1024))
                        {
                            var id = profile.Value.Get("lastVersionId").Text();
                            if (id is null || id.StartsWith("latest-", StringComparison.Ordinal)) continue;
                            var dir = Under(versions, Segment(id));
                            if (!File.Exists(Path.Combine(dir, id + ".json"))) continue;
                            Add(() => ReadStandard(root, dir, profile.Value, profile.Name));
                            profiles.Add(id);
                        }
                }
                catch (Exception ex) when (ex is IOException or JsonException or ArgumentException) { diagnostics.Add("官方配置读取失败：" + ex.Message); }
            }
            if (Directory.Exists(versions))
                foreach (var dir in Directory.EnumerateDirectories(versions).Take(1024))
                    if (!profiles.Contains(Path.GetFileName(dir))) Add(() => ReadStandard(root, dir));
            var instances = Directory.Exists(Path.Combine(selected, "instances")) ? Path.Combine(selected, "instances") : selected;
            foreach (var dir in Directory.EnumerateDirectories(instances).Take(1024))
                if (File.Exists(Path.Combine(dir, "mmc-pack.json"))) Add(() => ReadPrism(dir));
        }
        if (found.Count == 0 && diagnostics.Count == 0) diagnostics.Add("未找到 versions/<版本>/<版本>.json 或 Prism/MultiMC 的 mmc-pack.json。");
        return new(found, diagnostics);
    }

    private static GameInstance ReadStandard(string root, string versionDirectory, JsonElement profile = default, string? profileId = null)
    {
        var id = Path.GetFileName(versionDirectory);
        var metadataPath = Path.Combine(versionDirectory, id + ".json");
        var json = Json(metadataPath);
        if (json.ValueKind != JsonValueKind.Object) throw new InvalidDataException("版本描述无效：" + metadataPath);
        var settings = new InstanceSettings();
        var gameDir = root;
        var warnings = new List<string>();
        var source = InstanceSource.Official;
        var config = "标准版本描述";
        if (profileId is not null)
        {
            gameDir = ResolveDirectory(profile.Get("gameDir").Text(), root) ?? root;
            var args = profile.Get("javaArgs").Text();
            settings = new() { JavaPath = NonEmpty(profile.Get("javaDir").Text()), JvmArguments = args,
                MinimumMemoryMb = MemoryArgument(args, "ms"), MaximumMemoryMb = MemoryArgument(args, "mx"),
                WindowWidth = Positive(profile.Get("resolution").Get("width").Number()), WindowHeight = Positive(profile.Get("resolution").Get("height").Number()) };
            config = Path.Combine(root, "launcher_profiles.json") + " · " + profileId;
        }
        var hmcl = Path.Combine(versionDirectory, "hmclversion.cfg");
        var hmclNew = Path.Combine(versionDirectory, ".hmcl", "config", "instance-game-settings.json");
        var pcl = Path.Combine(versionDirectory, "PCL", "Setup.ini");
        try
        {
            if (File.Exists(hmclNew) || File.Exists(hmcl))
            {
                source = InstanceSource.Hmcl; config = File.Exists(hmclNew) ? hmclNew : hmcl;
                (settings, gameDir) = LauncherSettings.Hmcl(Json(config), config == hmclNew, root, versionDirectory, warnings);
            }
            else if (File.Exists(pcl) || File.Exists(Path.Combine(versionDirectory, "PCL", "config.json")))
            {
                source = InstanceSource.Pcl; config = pcl;
                (settings, gameDir) = LauncherSettings.Pcl(root, versionDirectory, warnings);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or InvalidOperationException)
        { warnings.Add("启动器配置读取不完整：" + ex.Message); }
        settings = Normalize(settings, warnings);
        return new()
        {
            Id = Identity(versionDirectory + (profileId is null ? "" : "|profile:" + profileId)),
            SourceProfileId = profileId,
            Name = NonEmpty(profile.Get("name").Text()) ?? id, SourcePath = profileId is null ? versionDirectory : root,
            RootDirectory = root, DefaultGameDirectory = gameDir, VersionId = id, Source = source,
            GameVersion = InstanceVersionMetadata.ReadStandard(metadataPath, Path.Combine(root, "versions")),
            ModLoaders = InstanceVersionMetadata.ReadModLoaders([metadataPath], Path.Combine(root, "versions")),
            LibraryDirectory = Path.Combine(root, "libraries"), AssetsDirectory = Path.Combine(root, "assets"), VersionsDirectory = Path.Combine(root, "versions"),
            MetadataPaths = [metadataPath], ImportedSettings = settings, ConfigSource = config, Warnings = warnings
        };
    }

    private static GameInstance ReadPrism(string dir)
    {
        var local = Ini(Path.Combine(dir, "instance.cfg"));
        var root = Path.GetDirectoryName(Path.GetDirectoryName(dir))!;
        // Instance directories may be relocated. Search only the directly related ancestors.
        for (var ancestor = Directory.GetParent(dir); ancestor is not null; ancestor = ancestor.Parent)
        {
            if (File.Exists(Path.Combine(ancestor.FullName, "prismlauncher.cfg")) || File.Exists(Path.Combine(ancestor.FullName, "multimc.cfg"))) { root = ancestor.FullName; break; }
            if (ancestor.FullName == Path.GetPathRoot(dir)) break;
        }
        var prism = File.Exists(Path.Combine(root, "prismlauncher.cfg"));
        var globalPath = Path.Combine(root, prism ? "prismlauncher.cfg" : "multimc.cfg");
        var global = Ini(globalPath);
        string? Value(string key, string flag) => bool.TryParse(local.GetValueOrDefault(flag), out var enabled) && enabled ? local.GetValueOrDefault(key) : global.GetValueOrDefault(key);
        int? Number(string key, string flag) => int.TryParse(Value(key, flag), out var number) ? Positive(number) : null;
        var warnings = new List<string>();
        if (!File.Exists(globalPath)) warnings.Add("未找到启动器全局配置，继承项将使用 Wonder 默认值。");
        var pack = Json(Path.Combine(dir, "mmc-pack.json"));
        if (pack.Get("formatVersion").Number() != 1) throw new InvalidDataException("不支持的 Prism/MultiMC pack 格式。");
        var metadata = new List<string>();
        var version = "未知";
        var gameVersion = "";
        foreach (var entry in pack.Get("components").Items().Take(128))
        {
            if (entry.Get("disabled").Boolean() == true) continue;
            var uid = Segment(entry.Get("uid").Text() ?? throw new InvalidDataException("组件缺少 uid。"));
            var componentVersion = Segment(entry.Get("version").Text() ?? throw new InvalidDataException("组件缺少版本。"));
            if (uid == "net.minecraft") { version = componentVersion; gameVersion = componentVersion; }
            var patch = Path.Combine(dir, "patches", uid + ".json");
            var path = File.Exists(patch) ? patch : Path.Combine(root, "meta", uid, componentVersion + ".json");
            if (File.Exists(path)) { Json(path); metadata.Add(path); }
            else warnings.Add($"缺少组件元数据：{uid} {componentVersion}；请在原启动器补全后刷新。" );
        }
        var settings = new InstanceSettings
        {
            JavaPath = NonEmpty(Value("JavaPath", local.ContainsKey("OverrideJavaLocation") ? "OverrideJavaLocation" : "OverrideJava")),
            JvmArguments = Value("JvmArgs", local.ContainsKey("OverrideJavaArgs") ? "OverrideJavaArgs" : "OverrideJava"),
            MinimumMemoryMb = Number("MinMemAlloc", "OverrideMemory"), MaximumMemoryMb = Number("MaxMemAlloc", "OverrideMemory"),
            WindowWidth = Number("MinecraftWinWidth", "OverrideWindow"), WindowHeight = Number("MinecraftWinHeight", "OverrideWindow")
        };
        if (bool.TryParse(Value("LaunchMaximized", "OverrideWindow") ?? Value("MinecraftWinMaximized", "OverrideWindow"), out var maximized) && maximized) warnings.Add("来源的最大化窗口设置无法等同于全屏；可在 Wonder 中单独设置全屏。");
        return new()
        {
            Id = Identity(dir), Name = NonEmpty(local.GetValueOrDefault("name")) ?? Path.GetFileName(dir), SourcePath = dir, RootDirectory = root,
            DefaultGameDirectory = Path.Combine(dir, Directory.Exists(Path.Combine(dir, ".minecraft")) ? ".minecraft" : "minecraft"), VersionId = version, GameVersion = gameVersion,
            Source = prism ? InstanceSource.Prism : InstanceSource.MultiMc, LibraryDirectory = Path.Combine(root, "libraries"), AssetsDirectory = Path.Combine(root, "assets"),
            VersionsDirectory = Path.Combine(root, "versions"), LocalLibraryDirectory = Path.Combine(dir, "libraries"), MetadataPaths = metadata,
            ModLoaders = InstanceVersionMetadata.ReadModLoaders(metadata, Path.Combine(root, "versions"), Path.Combine(dir, "mmc-pack.json")),
            ImportedSettings = Normalize(settings, warnings), ConfigSource = Path.Combine(dir, "instance.cfg"), Warnings = warnings
        };
    }

    internal static InstanceSettings Normalize(InstanceSettings settings, List<string> warnings)
    {
        if (settings.MaximumMemoryMb is < 512 && settings.MinimumMemoryMb is null) settings = settings with { MinimumMemoryMb = settings.MaximumMemoryMb };
        try { settings.Validate(); return settings; }
        catch (ArgumentException ex) { warnings.Add("来源设置无效，使用 Wonder 默认值；请细调后再启动。" + ex.Message); return new(); }
    }
    internal static string? ResolveDirectory(string? path, string root) => NonEmpty(path) is null ? null : Path.GetFullPath(path!, root);
    private static string Identity(string path) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())))[..32].ToLowerInvariant();
    internal static int? MemoryArgument(string? arguments, string kind)
    {
        var match = System.Text.RegularExpressions.Regex.Matches(arguments ?? "", @"(?:^|\s)-X" + kind + @"(\d+)([mMgG])(?=\s|$)").LastOrDefault();
        return match is not null && int.TryParse(match.Groups[1].Value, out var value) && value <= 1_048_576 / (match.Groups[2].Value.Equals("g", StringComparison.OrdinalIgnoreCase) ? 1024 : 1)
            ? value * (match.Groups[2].Value.Equals("g", StringComparison.OrdinalIgnoreCase) ? 1024 : 1) : null;
    }
}
