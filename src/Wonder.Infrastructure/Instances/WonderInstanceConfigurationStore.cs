// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Text.Json;
using System.Text.Json.Serialization;
using Wonder.Core;
using static Wonder.Infrastructure.LocalFiles;

namespace Wonder.Infrastructure;

/// <summary>The portable, credential-free authority for a Wonder-created instance.</summary>
public static class WonderInstanceConfigurationStore
{
    public const string FileName = "wonder.instance.json";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        AllowDuplicateProperties = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32
    };

    public static void Create(string gameRoot, string instanceName, string versionId, string? javaPath, bool installInCollection = false, bool shareGameDirectory = false)
    {
        var root = FullRoot(gameRoot);
        var path = Path.Combine(root, FileName);
        if (File.Exists(path))
        {
            var existing = Read(root);
            if (existing.Name != instanceName || existing.VersionId != versionId)
                throw new IOException("此目录已有不同的 Wonder 实例配置，未覆盖。");
            return;
        }
        if (installInCollection && CollectionRoot(root) is null || shareGameDirectory && !installInCollection)
            throw new ArgumentException("新实例必须位于所选游戏目录的 instances 文件夹下。");
        var settings = new InstanceSettings { JavaPath = javaPath };
        settings.Validate();
        var document = new Configuration
        {
            Id = Guid.NewGuid().ToString("N"), Name = instanceName, VersionId = CheckedSegment(versionId),
            CreatedAt = DateTimeOffset.UtcNow, CreatedBy = new("Wonder Minecraft Launcher", LauncherBuildInfo.Version),
            LaunchSettings = EncodeSettings(root, settings), InstallInCollection = installInCollection, ShareGameDirectory = shareGameDirectory
        };
        var instance = ToInstance(root, document);
        document.GameVersion = instance.GameVersion; document.ModLoaders = instance.ModLoaders;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, Options);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, false); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static GameInstance Read(string gameRoot)
    {
        var root = FullRoot(gameRoot);
        return ToInstance(root, Load(root));
    }

    /// <summary>Returns bytes for the caller's transaction; preserves immutable creation provenance.</summary>
    public static byte[] Serialize(GameInstance instance)
    {
        instance.Validate();
        if (instance.Source != InstanceSource.Wonder) throw new ArgumentException("仅 Wonder 原生实例可以保存此配置。");
        var root = FullRoot(instance.RootDirectory);
        var document = Load(root);
        if (document.Id != instance.Id) throw new InvalidDataException("Wonder 实例标识与配置文件不匹配。");
        document.Name = instance.Name; document.VersionId = CheckedSegment(instance.VersionId);
        document.LaunchSettings = EncodeSettings(root, instance.Overrides);
        var validated = ToInstance(root, document);
        document.GameVersion = validated.GameVersion; document.ModLoaders = validated.ModLoaders;
        return JsonSerializer.SerializeToUtf8Bytes(document, Options);
    }

    private static Configuration Load(string root)
    {
        var path = Path.Combine(root, FileName);
        RejectLinkedPath(path, root);
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Wonder 实例配置超过大小限制。");
        try
        {
            var result = JsonSerializer.Deserialize<Configuration>(File.ReadAllBytes(path), Options)
                ?? throw new InvalidDataException("Wonder 实例配置为空。");
            ValidateConfiguration(result); return result;
        }
        catch (JsonException error) { throw new InvalidDataException("Wonder 实例配置格式无效：" + error.Message, error); }
    }

    private static void ValidateConfiguration(Configuration configuration)
    {
        if (configuration.Format != "wonder.instance" || configuration.SchemaVersion != 1 || !Guid.TryParseExact(configuration.Id, "N", out _) ||
            configuration.CreatedAt == default || configuration.CreatedBy is null || string.IsNullOrWhiteSpace(configuration.CreatedBy.Name) ||
            configuration.CreatedBy.Name.Length > 120 || configuration.CreatedBy.Name.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(configuration.CreatedBy.Version) || configuration.CreatedBy.Version.Length > 100 || configuration.CreatedBy.Version.Any(char.IsControl) ||
            configuration.LaunchSettings is null || configuration.ModLoaders is null || configuration.ShareGameDirectory && !configuration.InstallInCollection)
            throw new InvalidDataException("Wonder 实例配置格式、标识或创建信息无效。");
        if (string.IsNullOrWhiteSpace(configuration.Name) || configuration.Name.Length > 120 || configuration.Name.Any(char.IsControl))
            throw new InvalidDataException("Wonder 实例名称无效。");
        CheckedSegment(configuration.VersionId);
    }

    private static GameInstance ToInstance(string root, Configuration configuration)
    {
        ValidateConfiguration(configuration);
        var versions = Path.Combine(root, "versions");
        var metadata = Path.Combine(versions, configuration.VersionId, configuration.VersionId + ".json");
        ValidateMetadata(root, versions, configuration.VersionId);
        var settings = DecodeSettings(root, configuration.LaunchSettings);
        var collection = configuration.InstallInCollection ? CollectionRoot(root) : null;
        var gameDirectory = configuration.ShareGameDirectory ? collection ?? root : root;
        var instance = new GameInstance
        {
            Id = configuration.Id, Name = configuration.Name, Source = InstanceSource.Wonder,
            SourcePath = root, RootDirectory = root, CollectionDirectory = collection, DefaultGameDirectory = gameDirectory, VersionId = configuration.VersionId,
            GameVersion = InstanceVersionMetadata.ReadStandard(metadata, versions),
            ModLoaders = InstanceVersionMetadata.ReadModLoaders([metadata], versions),
            VersionsDirectory = versions, LibraryDirectory = Path.Combine(root, "libraries"), AssetsDirectory = Path.Combine(root, "assets"),
            MetadataPaths = [metadata], Overrides = settings, ConfigSource = Path.Combine(root, FileName),
            PresetIconPath = InstanceIcons.ForStandard(Path.GetDirectoryName(metadata)!, settings.GameDirectory ?? gameDirectory, root)
        };
        instance.Validate(); return instance;
    }

    internal static string? CollectionRoot(string root)
    {
        if (!WonderPath.Comparer.Equals(Path.GetFileName(root), ".minecraft")) return null;
        var container = Path.GetDirectoryName(root);
        var instances = container is null ? null : Path.GetDirectoryName(container);
        return instances is not null && WonderPath.Comparer.Equals(Path.GetFileName(instances), "instances") ? Path.GetDirectoryName(instances) : null;
    }

    private static void ValidateMetadata(string root, string versions, string version)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            CheckedSegment(version);
            if (!visited.Add(version) || visited.Count > 32) throw new InvalidDataException("Wonder 实例版本继承存在循环或过深。");
            var path = Path.Combine(versions, version, version + ".json"); RejectLinkedPath(path, root);
            var metadata = Json(path);
            if (metadata.ValueKind != JsonValueKind.Object || metadata.Get("id").Text() != version)
                throw new InvalidDataException("Wonder 实例引用的版本描述无效。");
            var inherited = metadata.Get("inheritsFrom").Text();
            if (inherited is null) return;
            version = inherited;
        }
    }

    private static InstanceSettings EncodeSettings(string root, InstanceSettings settings) => settings with
    { JavaPath = IsJavaCommand(settings.JavaPath) ? settings.JavaPath : EncodePath(root, settings.JavaPath), GameDirectory = EncodePath(root, settings.GameDirectory) };
    private static InstanceSettings DecodeSettings(string root, InstanceSettings settings) => settings with
    { JavaPath = IsJavaCommand(settings.JavaPath) ? settings.JavaPath : DecodePath(root, settings.JavaPath), GameDirectory = DecodePath(root, settings.GameDirectory) };
    private static bool IsJavaCommand(string? path) => path is "java" or "java.exe" or "javaw.exe";
    private static string? EncodePath(string root, string? path)
    {
        if (path is null) return null;
        if (!Path.IsPathFullyQualified(path)) return Relative(root, path);
        var full = Path.GetFullPath(path);
        return Inside(root, full) ? Path.GetRelativePath(root, full).Replace('\\', '/') : full;
    }
    private static string? DecodePath(string root, string? path)
    {
        if (path is null) return null;
        if (Path.IsPathFullyQualified(path)) return Path.GetFullPath(path);
        return Path.GetFullPath(Path.Combine(root, Relative(root, path)));
    }
    private static string Relative(string root, string path)
    {
        if (path == ".") return path;
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Contains(':') ||
            path.Split('/').Any(part => part is "" or "." or ".." || part.Any(char.IsControl)))
            throw new InvalidDataException("Wonder 实例相对路径无效或越界。");
        foreach (var segment in path.Split('/')) CheckedSegment(segment);
        var full = Path.GetFullPath(Path.Combine(root, path));
        if (!Inside(root, full)) throw new InvalidDataException("Wonder 实例相对路径越界。");
        return path;
    }
    private static bool Inside(string root, string path) => WonderPath.Comparer.Equals(root, path) || path.StartsWith(root + Path.DirectorySeparatorChar, WonderPath.Comparison);
    private static string FullRoot(string root)
    {
        if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("Wonder 实例根目录必须为绝对路径。");
        var result = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        RejectLinkedPath(result, result); return result;
    }
    private static string CheckedSegment(string value)
    {
        if (value is null) throw new InvalidDataException("Wonder 实例缺少版本标识。");
        Segment(value);
        if (value.Any(char.IsControl) || value.EndsWith('.') || value.EndsWith(' ') || value.Contains(':')) throw new InvalidDataException("Wonder 实例标识无效。");
        return value;
    }
    private static void RejectLinkedPath(string path, string root)
    {
        for (var current = path; current is not null && Inside(root, current); current = Path.GetDirectoryName(current))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Wonder 实例配置或元数据目录包含符号链接。");
        }
    }
    private sealed class Configuration
    {
        [JsonRequired] public string Format { get; set; } = "wonder.instance";
        [JsonRequired] public int SchemaVersion { get; set; } = 1;
        [JsonRequired] public string Id { get; set; } = "";
        [JsonRequired] public string Name { get; set; } = "";
        [JsonRequired] public string VersionId { get; set; } = "";
        public string GameVersion { get; set; } = "";
        public List<InstalledModLoader> ModLoaders { get; set; } = [];
        [JsonRequired] public Creator? CreatedBy { get; set; }
        [JsonRequired] public DateTimeOffset CreatedAt { get; set; }
        [JsonRequired] public InstanceSettings LaunchSettings { get; set; } = new();
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool InstallInCollection { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool ShareGameDirectory { get; set; }
    }
    private sealed record Creator(string Name, string Version);
}
