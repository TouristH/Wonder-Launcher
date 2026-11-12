// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Wonder.Core;
using static Wonder.Infrastructure.LocalFiles;

namespace Wonder.Infrastructure;

public sealed partial class MinecraftDownloadCatalog
{
    private async Task<string> InstallCurseForgeModpack(ResourceInstallRequest request, DownloadOptions options, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var file = request.Version.Files.FirstOrDefault(x => x.Destination.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException(request.Version.DownloadUnavailableReason ?? "所选 CurseForge 版本没有可下载的 ZIP 整合包。");
        ValidateInstallCollection(request.TargetDirectory, request.InstallInCollection);
        var transaction = PrepareInstall(request.TargetDirectory, request);
        if (transaction.Completed is { } completed) return completed;
        var steps = new InstallSteps(progress, 10);
        var archivePath = SafePath(transaction.Stage, "pack.zip");
        await engine.DownloadAsync(file with { Destination = archivePath }, options, steps.Begin("下载并读取 CurseForge 整合包"), ct);

        var root = SafePath(transaction.Stage, ".minecraft");
        string installedVersion;
        using (var archive = ZipFile.OpenRead(archivePath))
        {
            var manifest = await ReadCurseForgeManifest(archive, ct);
            var minecraft = manifest.Get("minecraft");
            var gameVersion = StrictSegment(minecraft.Get("version").Text() ?? throw new InvalidDataException("整合包缺少 Minecraft 版本。"));
            var (loader, loaderVersion) = CurseForgePackLoader(minecraft.Get("modLoaders"));
            var references = ReadCurseForgeReferences(manifest.Get("files"));
            var overrideDirectory = StrictSegment(manifest.Get("overrides").Text() ?? "overrides");
            var overrides = PlanCurseForgeOverrides(archive, root, overrideDirectory, ct);
            var downloads = await ResolveCurseForgePackFiles(references, root, steps.Begin("读取并校验整合包文件清单"), ct);
            var downloadedPaths = downloads.Select(x => x.Destination).ToHashSet(WonderPath.Comparer);
            if (overrides.Any(x => downloadedPaths.Contains(x.Destination)))
                throw new InvalidDataException("整合包覆盖文件与清单中的下载文件路径重复。");

            Directory.CreateDirectory(root);
            var game = new GameInstallRequest(gameVersion, request.InstanceName ?? request.Project.Title, request.TargetDirectory, loader, loaderVersion, request.JavaPath);
            installedVersion = await InstallGameContent(game, root, options, steps, ct);
            await engine.DownloadManyAsync(downloads, options, steps.Begin("下载并校验整合包内容"), ct);
            steps.Begin("解压客户端配置");
            await ExtractCurseForgeOverrides(overrides, ct);
        }
        File.Delete(archivePath);
        steps.Begin("保存并登记实例");
        return CompleteInstall(transaction.Stage, request.TargetDirectory, installedVersion, ct, request.InstallInCollection);
    }

    private static async Task<JsonElement> ReadCurseForgeManifest(ZipArchive archive, CancellationToken ct)
    {
        var entry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("CurseForge 整合包缺少 manifest.json。");
        if (entry.Length is < 1 or > 16 * 1024 * 1024) throw new InvalidDataException("整合包索引大小无效。");
        using var manifest = JsonDocument.Parse(await ArchiveEntryContent.ReadAsync(entry, 16 * 1024 * 1024, ct), new() { MaxDepth = 64 });
        var value = manifest.RootElement;
        if (value.Get("manifestVersion").Number() != 1 || value.Get("manifestType").Text() != "minecraftModpack")
            throw new InvalidDataException("不支持的 CurseForge 整合包格式。");
        return value.Clone();
    }

    private static (string Loader, string? Version) CurseForgePackLoader(JsonElement values)
    {
        var loaders = values.Items().ToArray();
        if (loaders.Length == 0) return ("vanilla", null);
        var primary = loaders.Where(x => x.Get("primary").Boolean() == true).ToArray();
        if (primary.Length > 1 || primary.Length == 0 && loaders.Length > 1)
            throw new InvalidDataException("整合包未声明唯一的主加载器。");
        var id = (primary.Length == 1 ? primary[0] : loaders[0]).Get("id").Text() ?? "";
        var separator = id.IndexOf('-');
        var loader = separator > 0 ? id[..separator].ToLowerInvariant() : "";
        if (loader is not ("fabric" or "quilt" or "forge" or "neoforge"))
            throw new InvalidDataException("CurseForge 整合包使用了不支持的加载器。");
        return (loader, StrictSegment(id[(separator + 1)..]));
    }

    private static IReadOnlyList<(int ProjectId, int FileId)> ReadCurseForgeReferences(JsonElement files)
    {
        if (files.ValueKind != JsonValueKind.Array || files.GetArrayLength() > 50_000)
            throw new InvalidDataException("CurseForge 整合包文件清单无效或过大。");
        var references = new Dictionary<int, int>();
        foreach (var item in files.Items())
        {
            if (item.Get("required").Boolean() == false) continue;
            var projectId = item.Get("projectID").Number().GetValueOrDefault();
            var fileId = item.Get("fileID").Number().GetValueOrDefault();
            if (projectId <= 0 || fileId <= 0)
                throw new InvalidDataException("CurseForge 整合包包含无效的项目或文件 ID。");
            if (!references.TryAdd(projectId, fileId) && references[projectId] != fileId)
                throw new InvalidDataException("CurseForge 整合包包含同一项目的多个文件版本。");
        }
        return references.Select(pair => (pair.Key, pair.Value)).ToArray();
    }

    private async Task<IReadOnlyList<DownloadFile>> ResolveCurseForgePackFiles(IReadOnlyList<(int ProjectId, int FileId)> references,
        string root, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var downloads = new Dictionary<string, DownloadFile>(WonderPath.Comparer);
        foreach (var batch in references.Chunk(50))
        {
            var projectsTask = PostCurseForgeDataAsync("/mods", new { modIds = batch.Select(x => x.ProjectId).ToArray() }, ct);
            var filesTask = PostCurseForgeDataAsync("/mods/files", new { fileIds = batch.Select(x => x.FileId).Distinct().ToArray() }, ct);
            await Task.WhenAll(projectsTask, filesTask);
            var projects = (await projectsTask).Items().ToLookup(x => x.Get("id").Number());
            var files = (await filesTask).Items().ToLookup(x => x.Get("id").Number());
            foreach (var (projectId, fileId) in batch)
            {
                ct.ThrowIfCancellationRequested();
                var project = projects[projectId].FirstOrDefault();
                var file = files[fileId].FirstOrDefault();
                var fileLabel = CurseForgePackFileLabel(project, projectId, fileId);
                if (project.Get("gameId").Number() != CurseForgeMinecraftId || file.Get("gameId").Number() != CurseForgeMinecraftId ||
                    file.Get("modId").Number() != projectId)
                    throw new InvalidDataException("CurseForge 无法提供整合包指定的必需文件：" + fileLabel);
                if (project.Get("allowModDistribution").Boolean() == false)
                    throw new InvalidDataException("作者限制第三方下载，请在 CurseForge 查看必需文件：" + fileLabel);
                var version = ParseCurseForgeVersion(file);
                var descriptor = version.Files.FirstOrDefault();
                if (descriptor is null)
                    throw new InvalidDataException("CurseForge 未提供必需文件的下载地址，请查看文件页面：" + fileLabel);
                if (descriptor.Hash is null)
                    throw new InvalidDataException("CurseForge 未提供必需文件的校验值：" + fileLabel);
                var folder = CurseForgeProjectKind(project.Get("classId").Number()) switch
                {
                    DownloadResourceKind.Mod => "mods",
                    DownloadResourceKind.ResourcePack => "resourcepacks",
                    DownloadResourceKind.Shader => "shaderpacks",
                    _ => throw new InvalidDataException("CurseForge 整合包不能引用另一个整合包：" + fileLabel)
                };
                var destination = SafePackPath(root, folder + "/" + StrictSegment(descriptor.Destination));
                if (!downloads.TryAdd(destination, descriptor with { Destination = destination }))
                    throw new InvalidDataException("整合包包含重复文件路径：" + descriptor.Destination);
            }
            progress?.Report(new("读取 CurseForge 文件清单", CompletedFiles: downloads.Count, TotalFiles: references.Count));
        }
        return downloads.Values.ToArray();
    }

    private static string CurseForgePackFileLabel(JsonElement project, int projectId, int fileId)
    {
        if (OfficialCurseForgePage(project.Get("links").Get("websiteUrl").Text()) is { } page)
            return new Uri(page).GetLeftPart(UriPartial.Path).TrimEnd('/') + "/files/" + fileId.ToString(CultureInfo.InvariantCulture);
        return FormattableString.Invariant($"项目 {projectId}，文件 {fileId}");
    }

    private static IReadOnlyList<(ZipArchiveEntry Entry, string Destination)> PlanCurseForgeOverrides(ZipArchive archive, string root, string directory, CancellationToken ct)
    {
        var prefix = directory + "/";
        var result = new List<(ZipArchiveEntry, string)>();
        var paths = new HashSet<string>(WonderPath.Comparer);
        var remainingBytes = 8L * 1024 * 1024 * 1024;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal) || entry.FullName.EndsWith('/')) continue;
            if (result.Count >= 50_000 || entry.Length > remainingBytes) throw new InvalidDataException("整合包解压大小超出限制。");
            remainingBytes -= entry.Length;
            if (((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) throw new InvalidDataException("整合包不允许符号链接。");
            var destination = SafePackPath(root, entry.FullName[prefix.Length..]);
            if (!paths.Add(destination)) throw new InvalidDataException("整合包包含重复的覆盖文件路径。");
            result.Add((entry, destination));
        }
        return result;
    }

    private static async Task ExtractCurseForgeOverrides(IReadOnlyList<(ZipArchiveEntry Entry, string Destination)> overrides, CancellationToken ct)
    {
        foreach (var (entry, destination) in overrides)
            await ArchiveEntryContent.ExtractAsync(entry, destination, ct);
    }
}
