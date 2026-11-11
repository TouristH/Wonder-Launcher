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
    public async Task<string> InstallGameAsync(GameInstallRequest request, DownloadOptions options, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        options.Validate(); StrictSegment(request.InstanceName); StrictSegment(request.MinecraftVersion);
        var transaction = PrepareInstall(request.TargetDirectory, request);
        if (transaction.Completed is { } completed) return completed;
        var root = SafePath(transaction.Stage, ".minecraft"); Directory.CreateDirectory(root);
        var version = await InstallGameContent(request, root, options, progress, ct);
        return CompleteInstall(transaction.Stage, request.TargetDirectory, version, ct);
    }

    private async Task<string> InstallGameContent(GameInstallRequest request, string root, DownloadOptions options, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new("读取 Minecraft 版本描述"));
        var versions = await GetMinecraftVersionsAsync(options, ct);
        var selected = versions.FirstOrDefault(x => x.Id == request.MinecraftVersion) ?? throw new InvalidDataException("Minecraft 版本不存在，请刷新版本列表。");
        var metadata = await GetJson(selected.Url, options, ct);
        if (metadata.Get("id").Text() != request.MinecraftVersion || metadata.Get("mainClass").Text() is null) throw new InvalidDataException("Minecraft 版本描述不匹配。");
        var javaPath = string.IsNullOrWhiteSpace(request.JavaPath)
            ? await MojangJavaInstaller.InstallAsync(engine, metadata, root, options, progress, ct)
            : request.JavaPath;
        var savedJava = Path.IsPathFullyQualified(javaPath) && javaPath.StartsWith(root + Path.DirectorySeparatorChar, WonderPath.Comparison)
            ? Path.Combine(Path.GetFullPath(request.TargetDirectory), ".minecraft", Path.GetRelativePath(root, javaPath)) : javaPath;
        await WriteJson(Path.Combine(Path.GetDirectoryName(root)!, ".wonder-launch.json"), JsonSerializer.Serialize(new { instanceName = request.InstanceName, javaPath = savedJava }), ct);
        var versionDirectory = SafePath(root, "versions/" + StrictSegment(selected.Id)); Directory.CreateDirectory(versionDirectory);
        await WriteJson(Path.Combine(versionDirectory, selected.Id + ".json"), metadata.GetRawText(), ct);
        await engine.DownloadAsync(Descriptor(metadata.Get("downloads").Get("client"), Path.Combine(versionDirectory, selected.Id + ".jar")), options, progress, ct);
        await InstallLibraries(metadata, root, options, progress, ct);
        var assetIndex = metadata.Get("assetIndex");
        if (assetIndex.ValueKind == JsonValueKind.Object)
        {
            var assetId = StrictSegment(assetIndex.Get("id").Text() ?? "");
            var indexPath = Path.Combine(root, "assets", "indexes", assetId + ".json");
            await engine.DownloadAsync(Descriptor(assetIndex, indexPath), options, progress, ct);
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
            await engine.DownloadAsync(Descriptor(log, Path.Combine(root, "assets", "log_configs", StrictSegment(log.Get("id").Text() ?? ""))), options, progress, ct);
        var loader = request.Loader.ToLowerInvariant();
        if (loader == "vanilla") return selected.Id;
        var loaderVersion = StrictSegment(request.LoaderVersion ?? throw new InvalidDataException("请选择模组加载器版本。"));
        if (loader is "fabric" or "quilt")
        {
            var profile = await GetJson($"{LoaderHost(loader)}/v{(loader == "fabric" ? 2 : 3)}/versions/loader/{Escape(selected.Id)}/{Escape(loaderVersion)}/profile/json", options, ct);
            var id = StrictSegment(profile.Get("id").Text() ?? "");
            if (profile.Get("inheritsFrom").Text() != selected.Id || profile.Get("mainClass").Text() is null) throw new InvalidDataException("加载器版本描述与 Minecraft 版本不匹配。");
            await InstallLibraries(profile, root, options, progress, ct);
            await WriteJson(Path.Combine(root, "versions", id, id + ".json"), profile.GetRawText(), ct);
            return id;
        }
        if (loader is not ("forge" or "neoforge")) throw new InvalidDataException("不支持的模组加载器。");
        var artifact = loader == "forge" || selected.Id == "1.20.1" ? "forge" : "neoforge";
        var fullVersion = loaderVersion.StartsWith(selected.Id + "-", StringComparison.Ordinal) || artifact != "forge" ? loaderVersion : selected.Id + "-" + loaderVersion;
        var origin = loader == "forge" ? "https://maven.minecraftforge.net/net/minecraftforge" : "https://maven.neoforged.net/releases/net/neoforged";
        var installerUrl = $"{origin}/{artifact}/{Escape(fullVersion)}/{artifact}-{Escape(fullVersion)}-installer.jar";
        var installer = Path.Combine(root, "installer", loader + "-installer.jar");
        var hashText = await engine.GetStringAsync(installerUrl + ".sha1", options, ct);
        var installerHash = hashText.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (installerHash.Length != 40 || !installerHash.All(char.IsAsciiHexDigit)) throw new InvalidDataException("加载器安装器校验信息无效。");
        await engine.DownloadAsync(new(installerUrl, installer, installerHash), options, progress, ct);
        return await InstallForgeArchive(installer, root, javaPath, metadata, options, progress, ct);
    }

    private async Task InstallLibraries(JsonElement metadata, string root, DownloadOptions options, IProgress<DownloadProgress>? progress, CancellationToken ct, bool deferGenerated = false)
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
                if (!string.IsNullOrWhiteSpace(artifact.Get("url").Text())) files.Add(Descriptor(artifact, path));
                else if (!deferGenerated && !File.Exists(path)) throw new InvalidDataException("安装器没有生成依赖库：" + Path.GetFileName(path));
            }
            else if (downloads.ValueKind == JsonValueKind.Undefined)
            {
                var relative = LaunchArguments.Maven(library.Get("name").Text() ?? "");
                var destination = SafePath(Path.Combine(root, "libraries"), relative);
                if (!File.Exists(destination)) files.Add(new(RequiredUrl((library.Get("url").Text() ?? "https://libraries.minecraft.net/").TrimEnd('/') + "/" + relative), destination));
            }
            var native = library.Get("natives").Get(os).Text()?.Replace("${arch}", arch.Contains("64", StringComparison.Ordinal) ? "64" : "32");
            if (native is not null)
            {
                var classifier = downloads.Get("classifiers").Get(native);
                files.Add(Descriptor(classifier, SafePath(Path.Combine(root, "libraries"), classifier.Get("path").Text() ?? LaunchArguments.Maven(library.Get("name").Text() ?? "", native))));
            }
        }
        await engine.DownloadManyAsync(files.DistinctBy(x => x.Destination).ToArray(), options, progress, ct);
    }

    private static (string Stage, string? Completed) PrepareInstall(string target, object request)
    {
        if (!Path.IsPathFullyQualified(target)) throw new ArgumentException("安装目标必须是完整路径。");
        target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        var stage = target + ".wonder-install";
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
    private static string CompleteInstall(string stage, string target, string version, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var launch = Json(Path.Combine(stage, ".wonder-launch.json"));
        var configuredJava = launch.Get("javaPath").Text();
        if (configuredJava is not null && Path.IsPathFullyQualified(configuredJava) &&
            configuredJava.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)) + Path.DirectorySeparatorChar, WonderPath.Comparison))
            configuredJava = Path.Combine(stage, Path.GetRelativePath(target, configuredJava));
        WonderInstanceConfigurationStore.Create(Path.Combine(stage, ".minecraft"), launch.Get("instanceName").Text() ?? version, version, configuredJava);
        File.WriteAllText(Path.Combine(stage, ".wonder-complete.json"), JsonSerializer.Serialize(new { fingerprint = File.ReadAllText(Path.Combine(stage, ".wonder-request.json")), version,
            instanceName = launch.Get("instanceName").Text(), javaPath = launch.Get("javaPath").Text() }));
        Directory.Move(stage, target);
        return Path.Combine(Path.GetFullPath(target), ".minecraft", "versions", version);
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
