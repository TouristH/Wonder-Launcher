// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Wonder.Core;
using static Wonder.Infrastructure.LocalFiles;

namespace Wonder.Infrastructure;

public sealed partial class MinecraftDownloadCatalog
{
    private static readonly string[] MojangPayloadHosts = ["piston-data.mojang.com", "launcher.mojang.com"];
    private static readonly string[] MojangIndexHosts = ["piston-meta.mojang.com", "launchermeta.mojang.com"];
    private static readonly string[] MavenHosts = ["libraries.minecraft.net", "maven.fabricmc.net", "maven.quiltmc.org",
        "maven.minecraftforge.net", "files.minecraftforge.net", "maven.neoforged.net", "repo.maven.apache.org", "repo1.maven.org"];

    public async Task<string> InstallGameAsync(GameInstallRequest request, DownloadOptions options, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        options.Validate(); StrictSegment(request.InstanceName); StrictSegment(request.MinecraftVersion);
        ValidateInstallCollection(request.TargetDirectory, request.InstallInCollection, request.ShareGameDirectory);
        var transaction = PrepareInstall(request.TargetDirectory, request);
        if (transaction.Completed is { } completed) return completed;
        var root = SafePath(transaction.Stage, ".minecraft"); Directory.CreateDirectory(root);
        var steps = new InstallSteps(progress, 6);
        var version = await InstallGameContent(request, root, options, steps, ct);
        steps.Begin("保存并登记实例");
        return CompleteInstall(transaction.Stage, request.TargetDirectory, version, ct, request.InstallInCollection, request.ShareGameDirectory);
    }

    private async Task<string> InstallGameContent(GameInstallRequest request, string root, DownloadOptions options, InstallSteps steps, CancellationToken ct)
    {
        var progress = steps.Begin("读取游戏版本与准备 Java");
        var versions = await GetMinecraftVersionsAsync(options, ct);
        var selected = versions.FirstOrDefault(x => x.Id == request.MinecraftVersion) ?? throw new InvalidDataException("Minecraft 版本不存在，请刷新版本列表。");
        if (selected.Sha1 is not { Length: 40 } sha1 || !sha1.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("官方版本清单缺少版本描述的 SHA-1。");
        var metadata = await GetJson(selected.Url, options, ct, sha1);
        if (metadata.Get("id").Text() != request.MinecraftVersion || metadata.Get("mainClass").Text() is null) throw new InvalidDataException("Minecraft 版本描述不匹配。");
        var javaPath = string.IsNullOrWhiteSpace(request.JavaPath)
            ? await MojangJavaInstaller.InstallAsync(engine, metadata, root, options, progress, ct)
            : request.JavaPath;
        var savedJava = Path.IsPathFullyQualified(javaPath) && javaPath.StartsWith(root + Path.DirectorySeparatorChar, WonderPath.Comparison)
            ? Path.Combine(Path.GetFullPath(request.TargetDirectory), ".minecraft", Path.GetRelativePath(root, javaPath)) : javaPath;
        await WriteJson(Path.Combine(Path.GetDirectoryName(root)!, ".wonder-launch.json"), JsonSerializer.Serialize(new { instanceName = request.InstanceName, javaPath = savedJava }), ct);
        progress = steps.Begin("安装客户端与依赖库");
        var versionDirectory = SafePath(root, "versions/" + StrictSegment(selected.Id)); Directory.CreateDirectory(versionDirectory);
        await WriteJson(Path.Combine(versionDirectory, selected.Id + ".json"), metadata.GetRawText(), ct);
        await engine.DownloadAsync(Descriptor(metadata.Get("downloads").Get("client"), Path.Combine(versionDirectory, selected.Id + ".jar"), MojangPayloadHosts), options, progress, ct);
        await InstallLibraries(metadata, root, options, progress, ct);
        progress = steps.Begin("下载游戏资源与日志配置");
        var assetIndex = metadata.Get("assetIndex");
        if (assetIndex.ValueKind == JsonValueKind.Object)
        {
            var assetId = StrictSegment(assetIndex.Get("id").Text() ?? "");
            var indexPath = Path.Combine(root, "assets", "indexes", assetId + ".json");
            await engine.DownloadAsync(Descriptor(assetIndex, indexPath, MojangIndexHosts), options with { Source = DownloadSource.Official }, progress, ct);
            var index = Json(indexPath); var files = new List<DownloadFile>();
            if (index.Get("objects").ValueKind != JsonValueKind.Object) throw new InvalidDataException("资源索引缺少 objects。");
            foreach (var item in index.Get("objects").EnumerateObject())
            {
                var hash = item.Value.Get("hash").Text() ?? "";
                if (hash.Length != 40 || !hash.All(char.IsAsciiHexDigit)) throw new InvalidDataException("资源哈希无效。");
                files.Add(new("https://resources.download.minecraft.net/" + hash[..2] + "/" + hash, Path.Combine(root, "assets", "objects", hash[..2], hash), hash, "SHA1", Long(item.Value.Get("size"))));
            }
            await engine.DownloadManyAsync(files.DistinctBy(x => x.Destination).ToArray(), options, progress, ct);
        }
        var log = metadata.Get("logging").Get("client").Get("file");
        if (log.ValueKind == JsonValueKind.Object)
            await engine.DownloadAsync(Descriptor(log, Path.Combine(root, "assets", "log_configs", StrictSegment(log.Get("id").Text() ?? "")), MojangPayloadHosts), options, progress, ct);
        progress = steps.Begin("安装模组加载器");
        var version = await InstallLoader(request, root, javaPath, metadata, options, progress, ct);
        progress = steps.Begin("安装可选组件");
        return await InstallGameAddons(request, version, root, javaPath, options, progress, ct);
    }

    private async Task<string> InstallLoader(GameInstallRequest request, string root, string javaPath, JsonElement metadata,
        DownloadOptions options, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var minecraft = request.MinecraftVersion;
        var loader = request.Loader.ToLowerInvariant();
        if (loader == "vanilla") return minecraft;
        var loaderVersion = StrictSegment(request.LoaderVersion ?? throw new InvalidDataException("请选择模组加载器版本。"));
        if (loader is "fabric" or "quilt")
        {
            var profile = await GetJson($"{LoaderHost(loader)}/v{(loader == "fabric" ? 2 : 3)}/versions/loader/{Escape(minecraft)}/{Escape(loaderVersion)}/profile/json", options, ct);
            var id = StrictSegment(profile.Get("id").Text() ?? "");
            if (profile.Get("inheritsFrom").Text() != minecraft || profile.Get("mainClass").Text() is null) throw new InvalidDataException("加载器版本描述与 Minecraft 版本不匹配。");
            await InstallLibraries(profile, root, options, progress, ct);
            await WriteJson(Path.Combine(root, "versions", id, id + ".json"), profile.GetRawText(), ct);
            return id;
        }
        if (loader is not ("forge" or "neoforge")) throw new InvalidDataException("不支持的模组加载器。");
        var artifact = loader == "forge" || minecraft == "1.20.1" ? "forge" : "neoforge";
        var fullVersion = loaderVersion.StartsWith(minecraft + "-", StringComparison.Ordinal) || artifact != "forge" ? loaderVersion : minecraft + "-" + loaderVersion;
        var origin = loader == "forge" ? "https://maven.minecraftforge.net/net/minecraftforge" : "https://maven.neoforged.net/releases/net/neoforged";
        var installerUrl = $"{origin}/{artifact}/{Escape(fullVersion)}/{artifact}-{Escape(fullVersion)}-installer.jar";
        var installer = Path.Combine(root, "installer", loader + "-installer.jar");
        var installerHash = await OfficialMavenSha1(installerUrl, options, ct);
        await engine.DownloadAsync(new(installerUrl, installer, installerHash), options, progress, ct);
        return await InstallForgeArchive(installer, root, javaPath, metadata, options, progress, ct);
    }

    private async Task<string> OfficialMavenSha1(string url, DownloadOptions options, CancellationToken ct)
    {
        var hashText = await engine.GetStringAsync(url + ".sha1", options with { Source = DownloadSource.Official }, ct);
        var installerHash = hashText.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (installerHash.Length != 40 || !installerHash.All(char.IsAsciiHexDigit)) throw new InvalidDataException("官方 Maven 仓库未提供有效的 SHA-1。");
        return installerHash;
    }

    private async Task InstallLibraries(JsonElement metadata, string root, DownloadOptions options, IProgress<DownloadProgress>? progress, CancellationToken ct,
        bool deferGenerated = false, string? embeddedLibrary = null)
    {
        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch { System.Runtime.InteropServices.Architecture.Arm64 => "aarch64", System.Runtime.InteropServices.Architecture.X86 => "x86", _ => "amd64" };
        var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var files = new List<DownloadFile>();
        foreach (var library in metadata.Get("libraries").Items())
        {
            if (!LaunchArguments.Allowed(library, arch, new Dictionary<string, bool>())) continue;
            var downloads = library.Get("downloads"); var artifact = downloads.Get("artifact");
            if (artifact.ValueKind == JsonValueKind.Object)
            {
                var path = SafePath(Path.Combine(root, "libraries"), artifact.Get("path").Text() ?? LaunchArguments.Maven(library.Get("name").Text() ?? ""));
                if (!string.IsNullOrWhiteSpace(artifact.Get("url").Text())) files.Add(Descriptor(artifact, path, MavenHosts));
                else if (!deferGenerated)
                {
                    if (!File.Exists(path)) throw new InvalidDataException("安装器没有生成依赖库：" + Path.GetFileName(path));
                    if (HashedFile(artifact, "", path) is { } expected && !await MatchesHash(expected, ct))
                        throw new InvalidDataException("安装器生成的依赖库校验失败：" + Path.GetFileName(path));
                }
            }
            else if (downloads.ValueKind == JsonValueKind.Undefined)
            {
                var relative = LaunchArguments.Maven(library.Get("name").Text() ?? "");
                var destination = SafePath(Path.Combine(root, "libraries"), relative);
                // Legacy Forge stores the universal JAR inside the verified installer under an unclassified coordinate.
                if (!WonderPath.Comparer.Equals(destination, embeddedLibrary))
                {
                    var repository = DownloadSourceResolver.RequireUrl(library.Get("url").Text() ?? "https://libraries.minecraft.net/", MavenHosts);
                    if (repository.Query.Length != 0) throw new InvalidDataException("Maven 仓库地址不能包含查询参数。");
                    var baseUri = new Uri(repository.AbsoluteUri.TrimEnd('/') + "/");
                    var escaped = string.Join('/', relative.Split('/').Select(segment => Escape(segment).Replace("%2B", "+", StringComparison.Ordinal)));
                    var url = new Uri(baseUri, escaped).AbsoluteUri;
                    var descriptor = HashedFile(library, url, destination);
                    if (descriptor is null)
                    {
                        // Old verified Forge profiles provide SHA-1 in checksums for libraries without sidecars.
                        var sha1 = library.Get("checksums").Items().Select(value => value.Text())
                            .FirstOrDefault(value => value is { Length: 40 } && value.All(char.IsAsciiHexDigit));
                        descriptor = new(url, destination, sha1 ?? await OfficialMavenSha1(url, options, ct));
                    }
                    files.Add(descriptor);
                }
            }
            var native = library.Get("natives").Get(os).Text()?.Replace("${arch}", arch.Contains("64", StringComparison.Ordinal) ? "64" : "32");
            if (native is not null)
            {
                var classifier = downloads.Get("classifiers").Get(native);
                files.Add(Descriptor(classifier, SafePath(Path.Combine(root, "libraries"), classifier.Get("path").Text() ?? LaunchArguments.Maven(library.Get("name").Text() ?? "", native)), MavenHosts));
            }
        }
        await engine.DownloadManyAsync(files.DistinctBy(x => x.Destination).ToArray(), options, progress, ct);
    }

    private static (string Stage, string? Completed) PrepareInstall(string target, object request)
    {
        if (!Path.IsPathFullyQualified(target)) throw new ArgumentException("安装目标必须是完整路径。");
        target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        // Older stages may contain files installed before official metadata and hashes were required.
        var stage = target + ".verified.wonder-install";
        if (Directory.Exists(target))
        {
            var marker = Path.Combine(target, ".wonder-complete.json");
            if (File.Exists(marker))
            {
                var doc = Json(marker);
                if (doc.Get("fingerprint").Text() == fingerprint)
                {
                    var versionId = StrictSegment(doc.Get("version").Text() ?? "");
                    var completed = Path.Combine(target, ".minecraft", "versions", versionId);
                    if (!File.Exists(Path.Combine(completed, versionId + ".json")))
                        throw new IOException("已完成的实例缺少版本描述，无法确认完整性；请选择新目录重新安装。");
                    return (stage, completed);
                }
            }
            throw new IOException("目标目录已存在；请选择新的实例目录，已有文件不会被覆盖。");
        }
        if (File.Exists(target)) throw new IOException("安装目标已被文件占用。");
        if (Directory.Exists(stage))
        {
            if ((File.GetAttributes(stage) & FileAttributes.ReparsePoint) != 0) throw new IOException("安装暂存目录不能是符号链接。");
            var marker = Path.Combine(stage, ".wonder-request.json");
            if (!File.Exists(marker) || File.ReadAllText(marker) != fingerprint) throw new IOException("此目录存在不同安装任务的临时文件，请选择其他目标目录。");
        }
        else { Directory.CreateDirectory(stage); File.WriteAllText(Path.Combine(stage, ".wonder-request.json"), fingerprint); }
        return (stage, null);
    }
    private static void ValidateInstallCollection(string target, bool grouped, bool shared = false)
    {
        if (!grouped && !shared) return;
        if (!Path.IsPathFullyQualified(target)) throw new ArgumentException("安装目标必须是完整路径。");
        var instancePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        var collection = WonderInstanceConfigurationStore.CollectionRoot(Path.Combine(instancePath, ".minecraft"));
        if (!grouped || collection is null) throw new ArgumentException("目标必须是所选游戏目录下的新实例。");
        SafePath(collection, "instances/" + Path.GetFileName(instancePath));
    }

    private static string CompleteInstall(string stage, string target, string version, CancellationToken ct, bool grouped = false, bool shared = false)
    {
        ct.ThrowIfCancellationRequested();
        var launch = Json(Path.Combine(stage, ".wonder-launch.json"));
        var configuredJava = launch.Get("javaPath").Text();
        if (configuredJava is not null && Path.IsPathFullyQualified(configuredJava) &&
            configuredJava.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)) + Path.DirectorySeparatorChar, WonderPath.Comparison))
            configuredJava = Path.Combine(stage, Path.GetRelativePath(target, configuredJava));
        var stageRoot = Path.Combine(stage, ".minecraft");
        WonderInstanceConfigurationStore.Create(stageRoot, launch.Get("instanceName").Text() ?? version, version, configuredJava, grouped, shared);
        File.WriteAllText(Path.Combine(stage, ".wonder-complete.json"), JsonSerializer.Serialize(new { fingerprint = File.ReadAllText(Path.Combine(stage, ".wonder-request.json")), version,
            instanceName = launch.Get("instanceName").Text(), javaPath = launch.Get("javaPath").Text() }));
        if (shared) PublishSharedInstance(stage, target, stageRoot, ct);
        else Directory.Move(stage, target);
        return Path.Combine(Path.GetFullPath(target), ".minecraft", "versions", version);
    }

    private static void PublishSharedInstance(string stage, string target, string stageRoot, CancellationToken ct)
    {
        var collection = WonderInstanceConfigurationStore.CollectionRoot(stageRoot)!;
        var source = SafePath(stageRoot, "mods");
        var newFiles = new List<string>();
        try
        {
            if (Directory.Exists(source))
            {
                foreach (var mod in Directory.EnumerateFiles(source, "*.jar"))
                {
                    ct.ThrowIfCancellationRequested();
                    var destination = SafePath(collection, "mods/" + Path.GetFileName(mod));
                    if (File.Exists(destination))
                    {
                        using var staged = File.OpenRead(mod);
                        using var existing = File.OpenRead(destination);
                        if (!SHA256.HashData(staged).AsSpan().SequenceEqual(SHA256.HashData(existing)))
                            throw new IOException("共享模组目录已有同名文件，未覆盖：" + Path.GetFileName(destination));
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.Copy(mod, temporary);
                        File.Move(temporary, destination);
                        newFiles.Add(destination);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
            }
            ct.ThrowIfCancellationRequested();
            Directory.Move(stage, target);
        }
        catch
        {
            foreach (var file in newFiles)
                try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }
    private static async Task WriteJson(string path, string text, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, text, ct); File.Move(temporary, path, true);
    }
    private static async Task<string> Drain(StreamReader reader, CancellationToken ct)
    {
        var result = new StringBuilder(); var buffer = new char[2048]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0) { result.Append(buffer, 0, count); if (result.Length > 4096) result.Remove(0, result.Length - 4096); }
        return result.ToString();
    }
}
