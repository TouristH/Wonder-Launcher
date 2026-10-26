// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

namespace Wonder.Core;

public enum InstanceSource { Official, Hmcl, Pcl, Prism, MultiMc }

public sealed record InstanceSettings
{
    public string? JavaPath { get; init; }
    public string? GameDirectory { get; init; }
    public string? JvmArguments { get; init; }
    public string? GameArguments { get; init; }
    public int? MinimumMemoryMb { get; init; }
    public int? MaximumMemoryMb { get; init; }
    public int? WindowWidth { get; init; }
    public int? WindowHeight { get; init; }
    public bool? FullScreen { get; init; }

    public static InstanceSettings Defaults { get; } = new()
    {
        MinimumMemoryMb = 512, MaximumMemoryMb = 4096, WindowWidth = 854, WindowHeight = 480,
        FullScreen = false, JvmArguments = "", GameArguments = ""
    };

    public InstanceSettings Apply(InstanceSettings overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        return new()
        {
            JavaPath = overrides.JavaPath ?? JavaPath, GameDirectory = overrides.GameDirectory ?? GameDirectory,
            JvmArguments = overrides.JvmArguments ?? JvmArguments, GameArguments = overrides.GameArguments ?? GameArguments,
            MinimumMemoryMb = overrides.MinimumMemoryMb ?? MinimumMemoryMb, MaximumMemoryMb = overrides.MaximumMemoryMb ?? MaximumMemoryMb,
            WindowWidth = overrides.WindowWidth ?? WindowWidth, WindowHeight = overrides.WindowHeight ?? WindowHeight,
            FullScreen = overrides.FullScreen ?? FullScreen
        };
    }

    public void Validate()
    {
        if (MinimumMemoryMb is <= 0 or > 1_048_576 || MaximumMemoryMb is <= 0 or > 1_048_576 ||
            MinimumMemoryMb is { } minimum && MaximumMemoryMb is { } maximum && minimum > maximum)
            throw new ArgumentException("内存必须为正数，且最小内存不能超过最大内存。");
        if (WindowWidth is <= 0 or > 32_768 || WindowHeight is <= 0 or > 32_768)
            throw new ArgumentException("窗口宽高必须在 1 到 32768 之间。");
        ValidateText(JavaPath, 32_767, "Java 路径");
        ValidateText(JvmArguments, 32_768, "JVM 参数");
        ValidateText(GameArguments, 32_768, "游戏参数");
        if (JavaPath is not null && string.IsNullOrWhiteSpace(JavaPath)) throw new ArgumentException("Java 路径为空时请使用继承设置。");
        if (GameDirectory is not null) ValidatePath(GameDirectory, "游戏目录");
    }

    internal static void ValidateText(string? value, int maximum, string label)
    {
        if (value is not null && (value.Length > maximum || value.Contains('\0')))
            throw new ArgumentException($"{label}过长或包含空字符。");
    }

    internal static void ValidatePath(string value, string label)
    {
        ValidateText(value, 32_767, label);
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
            throw new ArgumentException($"{label}必须为完整路径。");
    }
}

public sealed record InstalledModLoader(string Name, string Version);

public sealed record GameInstance
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public string RootDirectory { get; init; } = "";
    public string DefaultGameDirectory { get; init; } = "";
    public string VersionId { get; init; } = "";
    public string GameVersion { get; init; } = "";
    public List<InstalledModLoader> ModLoaders { get; init; } = [];
    public string VersionLabel => string.Join(" · ", new[] { string.IsNullOrWhiteSpace(GameVersion) ? "版本待识别" : "Minecraft " + GameVersion }
        .Concat(ModLoaders.Select(loader => loader.Name + " " + loader.Version)));
    public string LibraryDirectory { get; init; } = "";
    public string AssetsDirectory { get; init; } = "";
    public string VersionsDirectory { get; init; } = "";
    public string? LocalLibraryDirectory { get; init; }
    public InstanceSource Source { get; init; }
    public List<string> MetadataPaths { get; init; } = [];
    public InstanceSettings ImportedSettings { get; init; } = new();
    public InstanceSettings Overrides { get; init; } = new();
    public List<string> Warnings { get; init; } = [];
    public string ConfigSource { get; init; } = "";
    public string? SourceProfileId { get; init; }
    public List<string> PreviousRootDirectories { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public InstanceSettings GlobalSettings { get; init; } = new();
    public InstanceSettings EffectiveSettings => ResolveSettings(GlobalSettings);
    public InstanceSettings ResolveSettings(InstanceSettings globalSettings) => (InstanceSettings.Defaults with { GameDirectory = DefaultGameDirectory })
        .Apply(ImportedSettings).Apply(globalSettings).Apply(Overrides);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 80 || !Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
            throw new ArgumentException("实例标识无效。");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 120 || Name.Contains('\0')) throw new ArgumentException("实例名称必须为 1 到 120 个字符。");
        if (!Enum.IsDefined(Source)) throw new ArgumentException("实例来源无效。");
        if (string.IsNullOrWhiteSpace(VersionId) || VersionId.Length > 200 || VersionId.Contains('\0')) throw new ArgumentException("游戏版本标识无效。");
        if (GameVersion is null || GameVersion.Length > 200 || GameVersion.Any(char.IsControl)) throw new ArgumentException("Minecraft 版本无效。");
        if (ModLoaders is null || ModLoaders.Count > 16) throw new ArgumentException("模组加载器列表无效。");
        foreach (var loader in ModLoaders)
            if (loader is null || string.IsNullOrWhiteSpace(loader.Name) || loader.Name.Length > 80 || loader.Name.Any(char.IsControl) ||
                string.IsNullOrWhiteSpace(loader.Version) || loader.Version.Length > 200 || loader.Version.Any(char.IsControl))
                throw new ArgumentException("模组加载器名称或版本无效。");
        InstanceSettings.ValidatePath(SourcePath, "来源路径");
        InstanceSettings.ValidatePath(RootDirectory, "根目录");
        InstanceSettings.ValidatePath(DefaultGameDirectory, "默认游戏目录");
        InstanceSettings.ValidatePath(LibraryDirectory, "依赖库目录");
        InstanceSettings.ValidatePath(AssetsDirectory, "资源目录");
        InstanceSettings.ValidatePath(VersionsDirectory, "版本目录");
        if (LocalLibraryDirectory is not null) InstanceSettings.ValidatePath(LocalLibraryDirectory, "本地依赖库目录");
        if (MetadataPaths is null || MetadataPaths.Count > 128) throw new ArgumentException("实例元数据列表无效。");
        foreach (var path in MetadataPaths) InstanceSettings.ValidatePath(path, "元数据路径");
        if (Warnings is null || Warnings.Count > 128) throw new ArgumentException("实例诊断列表无效。");
        foreach (var warning in Warnings) InstanceSettings.ValidateText(warning, 4096, "实例诊断");
        InstanceSettings.ValidateText(ConfigSource, 32_768, "配置来源");
        InstanceSettings.ValidateText(SourceProfileId, 1024, "来源配置标识");
        if (PreviousRootDirectories is null || PreviousRootDirectories.Count > 64) throw new ArgumentException("目录移动记录无效。");
        foreach (var path in PreviousRootDirectories) InstanceSettings.ValidatePath(path, "原目录");
        ArgumentNullException.ThrowIfNull(ImportedSettings);
        ArgumentNullException.ThrowIfNull(Overrides);
        ImportedSettings.Validate();
        Overrides.Validate();
        EffectiveSettings.Validate();
    }
}

public sealed class InstanceCatalog
{
    public int Version { get; set; } = 1;
    public List<GameInstance> Instances { get; set; } = [];
    public List<string> GameDirectories { get; set; } = [];
    public string? SelectedInstanceId { get; set; }
    public string OfflineUsername { get; set; } = "Player";
    public InstanceSettings GlobalSettings { get; set; } = new();

    public void Validate()
    {
        if (Version != 1 || Instances is null || Instances.Count > 1024) throw new ArgumentException("实例索引版本或内容无效。");
        if (GameDirectories is null || GameDirectories.Count > 1024) throw new ArgumentException("游戏目录列表无效。");
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in GameDirectories)
        {
            InstanceSettings.ValidatePath(directory, "游戏目录");
            if (!directories.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)))) throw new ArgumentException("游戏目录重复。");
        }
        ArgumentNullException.ThrowIfNull(GlobalSettings);
        GlobalSettings.Validate();
        if (GlobalSettings.GameDirectory is not null) throw new ArgumentException("游戏目录只能针对单个实例设置。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var instance in Instances)
        {
            ArgumentNullException.ThrowIfNull(instance);
            (instance with { GlobalSettings = GlobalSettings }).Validate();
            if (!ids.Add(instance.Id)) throw new ArgumentException("实例标识重复。");
        }
        if (SelectedInstanceId is not null && !ids.Contains(SelectedInstanceId)) throw new ArgumentException("所选实例不存在。");
        ValidateOfflineUsername(OfflineUsername);
    }

    public static void ValidateOfflineUsername(string username)
    {
        if (username is null || username.Length is < 3 or > 16 || !username.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new ArgumentException("离线用户名必须为 3–16 位英文字母、数字或下划线。");
    }
}

public sealed record DiscoveryResult(IReadOnlyList<GameInstance> Instances, IReadOnlyList<string> Diagnostics);
public sealed record LaunchRequest(GameInstance Instance, string OfflineUsername, string CacheDirectory)
{
    public GameSession? Session { get; init; }
}

public sealed class LaunchPlan
{
    public string JavaPath { get; }
    public string WorkingDirectory { get; }
    public IReadOnlyList<string> Arguments { get; }
    public string InstanceId { get; }
    public IReadOnlyList<string> Notices { get; }
    private readonly CredentialRedactor _redactor;
    public bool HasAuthenticatedAccount { get; }
    public string? AuthlibInjectorPath { get; }
    public string? AuthlibInjectorSha256 { get; }
    public string RedactOutput(string value) => _redactor.Redact(value);
    public LaunchPlan(string javaPath, string workingDirectory, IEnumerable<string> arguments, string instanceId, IEnumerable<string>? notices = null,
        IEnumerable<string>? sensitiveValues = null, bool hasAuthenticatedAccount = false, string? authlibInjectorPath = null, string? authlibInjectorSha256 = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(javaPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        JavaPath = javaPath;
        WorkingDirectory = workingDirectory;
        Arguments = Array.AsReadOnly(arguments.ToArray());
        InstanceId = instanceId;
        Notices = Array.AsReadOnly((notices ?? []).ToArray());
        _redactor = new CredentialRedactor(sensitiveValues ?? []);
        HasAuthenticatedAccount = hasAuthenticatedAccount;
        AuthlibInjectorPath = authlibInjectorPath;
        AuthlibInjectorSha256 = authlibInjectorSha256;
    }
}
