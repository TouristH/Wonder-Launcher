// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.IO.Compression;
using System.Text.Json;
using Wonder.Core;
using static Wonder.Infrastructure.LocalFiles;

namespace Wonder.Infrastructure;

public sealed partial class MinecraftDownloadCatalog
{
    public async Task<ResourceSearchResult> SearchResourcesAsync(DownloadResourceKind kind, string query, string? minecraftVersion, string? loader, int offset, DownloadOptions options, CancellationToken ct = default)
    {
        options.Validate();
        if (!Enum.IsDefined(kind) || offset < 0 || offset > 1_000_000) throw new ArgumentException("搜索类型或页码无效。");
        var type = kind switch { DownloadResourceKind.Mod => "mod", DownloadResourceKind.Modpack => "modpack", DownloadResourceKind.ResourcePack => "resourcepack", _ => "shader" };
        var facets = new List<string[]> { new[] { "project_type:" + type } };
        if (!string.IsNullOrWhiteSpace(minecraftVersion)) facets.Add(["versions:" + minecraftVersion]);
        if (!string.IsNullOrWhiteSpace(loader) && loader != "vanilla" && kind is DownloadResourceKind.Mod or DownloadResourceKind.Modpack) facets.Add(["categories:" + loader]);
        var json = await GetJson($"{Modrinth}/search?query={Escape(query)}&facets={Escape(JsonSerializer.Serialize(facets))}&limit=20&offset={offset}&index=relevance", options, ct);
        return new(json.Get("hits").Items().Select(x => new ResourceProject(x.Get("project_id").Text() ?? throw new InvalidDataException("资源缺少 ID。"),
            x.Get("title").Text() ?? "", x.Get("description").Text() ?? "", kind, x.Get("icon_url").Text(), Long(x.Get("downloads")) ?? 0)).ToArray(), json.Get("total_hits").Number() ?? 0, offset);
    }

    public async Task<IReadOnlyList<ResourceVersion>> GetResourceVersionsAsync(string projectId, string? minecraftVersion, string? loader, DownloadOptions options, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(minecraftVersion)) query.Add("game_versions=" + Escape(JsonSerializer.Serialize(new[] { minecraftVersion })));
        if (!string.IsNullOrWhiteSpace(loader) && loader != "vanilla") query.Add("loaders=" + Escape(JsonSerializer.Serialize(new[] { loader })));
        var json = await GetJson($"{Modrinth}/project/{Escape(projectId)}/version" + (query.Count > 0 ? "?" + string.Join('&', query) : ""), options, ct);
        return json.Items().Select(ParseResourceVersion).ToArray();
    }

    private static ResourceVersion ParseResourceVersion(JsonElement value)
    {
        var allFiles = value.Get("files").Items().ToArray();
        var selected = allFiles.Where(x => x.Get("primary").Boolean() == true).ToArray();
        if (selected.Length == 0) selected = allFiles.Take(1).ToArray();
        return new(value.Get("id").Text() ?? "", value.Get("project_id").Text() ?? "", value.Get("name").Text() ?? "", value.Get("version_number").Text() ?? "",
            value.Get("game_versions").Items().Select(x => x.Text() ?? "").ToArray(), value.Get("loaders").Items().Select(x => x.Text() ?? "").ToArray(),
            selected.Select(x => new DownloadFile(RequiredUrl(x.Get("url").Text()), StrictSegment(x.Get("filename").Text() ?? ""),
                x.Get("hashes").Get("sha512").Text() ?? x.Get("hashes").Get("sha1").Text(), x.Get("hashes").Get("sha512").Text() is not null ? "SHA512" : "SHA1", Long(x.Get("size")))).ToArray(),
            value.Get("dependencies").Items().Select(x => new ResourceDependency(x.Get("project_id").Text(), x.Get("version_id").Text(), x.Get("dependency_type").Text() ?? "")).ToArray());
    }

    public async Task<string> InstallResourceAsync(ResourceInstallRequest request, DownloadOptions options, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        options.Validate();
        if (request.Project.Id != request.Version.ProjectId) throw new InvalidDataException("资源版本不属于所选项目。");
        if (request.Project.Kind == DownloadResourceKind.Modpack) return await InstallModpack(request, options, progress, ct);
        if (!Path.IsPathFullyQualified(request.TargetDirectory) || !Directory.Exists(request.TargetDirectory)) throw new DirectoryNotFoundException("请先选择已存在的实例游戏目录。");
        var folder = request.Project.Kind switch { DownloadResourceKind.Mod => "mods", DownloadResourceKind.ResourcePack => "resourcepacks", DownloadResourceKind.Shader => "shaderpacks", _ => throw new ArgumentException("资源类型无效。") };
        var files = new Dictionary<string, DownloadFile>(WonderPath.Comparer); var visited = new HashSet<string>(); var projects = new Dictionary<string, string>();
        async Task Resolve(ResourceVersion version)
        {
            ct.ThrowIfCancellationRequested();
            if (!visited.Add(version.Id)) return;
            if (visited.Count > 128) throw new InvalidDataException("资源依赖层级过多。");
            if (projects.TryGetValue(version.ProjectId, out var prior) && prior != version.Id) throw new InvalidDataException("所选资源需要互相冲突的依赖版本。");
            projects[version.ProjectId] = version.Id;
            if (!string.IsNullOrWhiteSpace(request.MinecraftVersion) && !version.GameVersions.Contains(request.MinecraftVersion)) throw new InvalidDataException("资源版本不兼容所选 Minecraft 版本。");
            if (request.Project.Kind == DownloadResourceKind.Mod && (string.IsNullOrWhiteSpace(request.Loader) || request.Loader == "vanilla" || !version.Loaders.Contains(request.Loader)))
                throw new InvalidDataException("模组需要兼容的已安装加载器，请选择 Fabric、Quilt、Forge 或 NeoForge 实例。");
            foreach (var dependency in version.Dependencies.Where(x => x.Type == "required"))
            {
                ResourceVersion resolved;
                if (dependency.VersionId is { } id) resolved = ParseResourceVersion(await GetJson($"{Modrinth}/version/{Escape(id)}", options, ct));
                else if (dependency.ProjectId is { } project) resolved = (await GetResourceVersionsAsync(project, request.MinecraftVersion, request.Project.Kind == DownloadResourceKind.Mod ? request.Loader : null, options, ct)).FirstOrDefault() ?? throw new InvalidDataException("必需依赖没有兼容版本：" + project);
                else throw new InvalidDataException("必需依赖缺少项目与版本 ID。");
                await Resolve(resolved);
            }
            if (version.Files.Count == 0) throw new InvalidDataException("资源版本没有可下载的文件。");
            foreach (var file in version.Files)
            {
                var destination = SafePath(request.TargetDirectory, folder + "/" + StrictSegment(file.Destination));
                var download = file with { Destination = destination };
                if (files.TryGetValue(destination, out var old) && old.Url != file.Url) throw new InvalidDataException("依赖文件名冲突：" + file.Destination);
                files[destination] = download;
            }
        }
        await Resolve(request.Version);
        // Existing resource files belong to the player. Only an identical hash permits reuse.
        foreach (var file in files.Values)
            if (File.Exists(file.Destination) && !await MatchesHash(file, ct)) throw new IOException("目标已存在不同文件，未覆盖：" + Path.GetFileName(file.Destination));
        await engine.DownloadManyAsync(files.Values.ToArray(), options, progress, ct);
        return Path.Combine(request.TargetDirectory, folder);
    }

    private async Task<string> InstallModpack(ResourceInstallRequest request, DownloadOptions options, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var transaction = PrepareInstall(request.TargetDirectory, request);
        if (transaction.Completed is { } complete) return complete;
        var file = request.Version.Files.FirstOrDefault(x => x.Destination.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("所选版本没有 Modrinth .mrpack 整合包。");
        var archivePath = Path.Combine(transaction.Stage, "pack.mrpack");
        await engine.DownloadAsync(file with { Destination = archivePath }, options, progress, ct);
        using var archive = ZipFile.OpenRead(archivePath);
        var indexEntry = archive.GetEntry("modrinth.index.json") ?? throw new InvalidDataException("整合包缺少 modrinth.index.json。");
        if (indexEntry.Length > 16 * 1024 * 1024) throw new InvalidDataException("整合包索引过大。");
        using var stream = indexEntry.Open(); using var document = await JsonDocument.ParseAsync(stream, new() { MaxDepth = 64 }, ct);
        var index = document.RootElement;
        if (index.Get("formatVersion").Number() != 1 || index.Get("game").Text() != "minecraft") throw new InvalidDataException("不支持的整合包格式。");
        var dependencies = index.Get("dependencies"); var minecraft = dependencies.Get("minecraft").Text() ?? throw new InvalidDataException("整合包缺少 Minecraft 版本。");
        var loaders = new[] { ("fabric-loader", "fabric"), ("quilt-loader", "quilt"), ("forge", "forge"), ("neoforge", "neoforge") }.Where(x => dependencies.Get(x.Item1).Text() is not null).ToArray();
        if (loaders.Length > 1) throw new InvalidDataException("整合包声明多个不兼容加载器。");
        var loader = loaders.FirstOrDefault();
        var root = SafePath(transaction.Stage, ".minecraft"); Directory.CreateDirectory(root);
        var downloads = new List<DownloadFile>();
        foreach (var item in index.Get("files").Items())
        {
            if (item.Get("env").Get("client").Text() == "unsupported") continue;
            var path = SafePackPath(root, item.Get("path").Text() ?? "");
            var hash = item.Get("hashes").Get("sha512").Text() ?? item.Get("hashes").Get("sha1").Text() ?? throw new InvalidDataException("整合包文件缺少哈希。");
            downloads.Add(new(RequiredUrl(item.Get("downloads").Items().FirstOrDefault().Text()), path, hash, item.Get("hashes").Get("sha512").Text() is null ? "SHA1" : "SHA512", Long(item.Get("fileSize"))));
        }
        if (downloads.Select(x => x.Destination).Distinct(WonderPath.Comparer).Count() != downloads.Count) throw new InvalidDataException("整合包包含重复文件路径。");
        var version = await InstallGameContent(new(minecraft, request.InstanceName ?? request.Project.Title, request.TargetDirectory, loader.Item2 ?? "vanilla", loader.Item1 is null ? null : dependencies.Get(loader.Item1).Text(), request.JavaPath), root, options, progress, ct);
        await engine.DownloadManyAsync(downloads, options, progress, ct);
        long total = 0; int count = 0;
        foreach (var prefix in new[] { "overrides/", "client-overrides/" })
            foreach (var entry in archive.Entries.Where(x => x.FullName.StartsWith(prefix, StringComparison.Ordinal)))
            {
                ct.ThrowIfCancellationRequested();
                if (entry.FullName.EndsWith('/')) continue;
                if (++count > 50_000 || (total += entry.Length) > 8L * 1024 * 1024 * 1024) throw new InvalidDataException("整合包解压大小超出限制。");
                if (((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) throw new InvalidDataException("整合包不允许符号链接。");
                var target = SafePackPath(root, entry.FullName[prefix.Length..]);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var output = new FileStream(target, FileMode.Create, FileAccess.Write); using var input = entry.Open(); await input.CopyToAsync(output, ct);
            }
        stream.Dispose(); archive.Dispose(); File.Delete(archivePath);
        return CompleteInstall(transaction.Stage, request.TargetDirectory, version, ct);
    }

    private static string SafePackPath(string root, string relative)
    {
        var path = SafePath(root, relative);
        if (new[] { "versions", "libraries", "assets", "runtime", "installer", "wonder.instance.json" }.Contains(relative.Split('/')[0], StringComparer.OrdinalIgnoreCase) || relative.StartsWith("launcher_profiles", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("整合包不能替换启动器管理的游戏文件。");
        return path;
    }
    private static async Task<bool> MatchesHash(DownloadFile file, CancellationToken ct)
    {
        if (file.Hash is null) return false;
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(new(file.HashAlgorithm));
        await using var input = File.OpenRead(file.Destination); var buffer = new byte[65536]; int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0) hash.AppendData(buffer, 0, count);
        return Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Hash, StringComparison.OrdinalIgnoreCase);
    }
}
