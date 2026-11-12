// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Wonder.Core;
using static Wonder.Infrastructure.LocalFiles;

namespace Wonder.Infrastructure;

public sealed partial class MinecraftDownloadCatalog
{
    private const string CurseForgeApi = "https://api.curseforge.com/v1";
    private const int CurseForgeMinecraftId = 432;
    private const int CurseForgeSearchPageSize = 20;
    private const int CurseForgeFilePageSize = 50;
    private const int CurseForgePaginationLimit = 10_000;
    private const string CurseForgeRestrictedDownload = "CurseForge 未向第三方启动器提供该文件的下载地址，请打开官网查看。";

    private static readonly HttpClient CurseForgeHttp = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromSeconds(30),
        MaxResponseContentBufferSize = 16 * 1024 * 1024
    };

    private async Task<ResourceSearchResult> SearchCurseForgeAsync(DownloadResourceKind kind, string query,
        string? minecraftVersion, string? loader, int offset, CancellationToken ct)
    {
        var (classId, _) = CurseForgeClass(kind);
        if (offset >= CurseForgePaginationLimit) return new([], CurseForgePaginationLimit, offset);
        var size = Math.Min(CurseForgeSearchPageSize, CurseForgePaginationLimit - offset);
        var path = FormattableString.Invariant($"/mods/search?gameId={CurseForgeMinecraftId}&classId={classId}&index={offset}&pageSize={size}");
        if (!string.IsNullOrWhiteSpace(query)) path += "&searchFilter=" + Escape(query);
        else path += "&sortField=2&sortOrder=desc";
        var hasGameVersion = !string.IsNullOrWhiteSpace(minecraftVersion);
        if (hasGameVersion) path += "&gameVersion=" + Escape(minecraftVersion!);
        if (kind is DownloadResourceKind.Mod or DownloadResourceKind.Modpack && CurseForgeLoaderId(loader) is { } loaderId)
        {
            // The single-loader parameter requires a game version. The multi-loader filter accepts an array.
            path += hasGameVersion
                ? FormattableString.Invariant($"&modLoaderType={loaderId}")
                : "&modLoaderTypes=" + Escape(FormattableString.Invariant($"[{loaderId}]"));
        }
        var result = await RequestCurseForgeAsync(path, null, ct).ConfigureAwait(false);
        var projects = result.Get("data").Items().Select(item => ParseCurseForgeProject(item, kind)).ToArray();
        var total = Math.Clamp(result.Get("pagination").Get("totalCount").Number() ?? offset + projects.Length, 0, CurseForgePaginationLimit);
        return new(projects, total, offset);
    }

    private async Task<ResourceProject> GetCurseForgeProjectAsync(ResourceProject project, CancellationToken ct)
    {
        var data = await GetCurseForgeProjectDataAsync(project.Id, ct).ConfigureAwait(false);
        return ParseCurseForgeProject(data, project.Kind);
    }

    private static ResourceProject ParseCurseForgeProject(JsonElement project, DownloadResourceKind kind)
    {
        var id = CurseForgeId(project.Get("id"));
        var (classId, segment) = CurseForgeClass(kind);
        if (project.Get("classId").Number() is { } actualClass && actualClass != classId)
            throw new InvalidDataException("CurseForge 项目类型与请求不匹配。");

        var authors = project.Get("authors").Items().Select(author => author.Get("name").Text())
            .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
        var slug = NonEmpty(project.Get("slug").Text());
        var logo = project.Get("logo");
        var page = OfficialCurseForgePage(project.Get("links").Get("websiteUrl").Text()) ??
            (slug is null ? null : $"https://www.curseforge.com/minecraft/{segment}/{Escape(slug)}");
        return new ResourceProject(id, project.Get("name").Text() ?? id, project.Get("summary").Text() ?? "", kind,
            CurseForgeCdnUrl(logo.Get("thumbnailUrl").Text()) ?? CurseForgeCdnUrl(logo.Get("url").Text()), Long(project.Get("downloadCount")) ?? 0)
        {
            Provider = ResourceProvider.CurseForge,
            Author = authors.Length == 0 ? null : string.Join("、", authors),
            PageUrl = page
        };
    }

    private async Task<IReadOnlyList<ResourceVersion>> GetCurseForgeVersionsAsync(string projectId, string? minecraftVersion,
        string? loader, CancellationToken ct)
    {
        var project = await GetCurseForgeProjectDataAsync(projectId, ct).ConfigureAwait(false);
        return await GetCurseForgeVersionsAsync(project, minecraftVersion, loader, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ResourceVersion>> GetCurseForgeVersionsAsync(JsonElement project, string? minecraftVersion,
        string? loader, CancellationToken ct)
    {
        var projectId = CurseForgeId(project.Get("id"));
        var path = $"/mods/{projectId}/files?pageSize={CurseForgeFilePageSize}";
        if (!string.IsNullOrWhiteSpace(minecraftVersion)) path += "&gameVersion=" + Escape(minecraftVersion);
        var loaderId = CurseForgeLoaderId(loader);
        if (loaderId is { } value) path += FormattableString.Invariant($"&modLoaderType={value}");

        var indexedLoaders = project.Get("latestFilesIndexes").Items().Select(item =>
                (FileId: Positive(item.Get("fileId").Number()), Loader: CurseForgeLoaderTag(item.Get("modLoader").Number())))
            .Where(item => item.FileId is not null && item.Loader is not null)
            .ToLookup(item => item.FileId!.Value.ToString(CultureInfo.InvariantCulture), item => item.Loader!);
        var allowDistribution = project.Get("allowModDistribution").Boolean() != false;
        var versions = new List<ResourceVersion>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var offset = 0; offset < CurseForgePaginationLimit; offset += CurseForgeFilePageSize)
        {
            var page = await RequestCurseForgeAsync(path + FormattableString.Invariant($"&index={offset}"), null, ct).ConfigureAwait(false);
            var files = page.Get("data").Items().ToArray();
            foreach (var file in files)
            {
                if (file.Get("isServerPack").Boolean() == true) continue;
                var version = ParseCurseForgeVersion(file);
                if (version.ProjectId != projectId) throw new InvalidDataException("CurseForge 文件不属于请求的项目。");
                if (!seen.Add(version.Id)) continue;
                var loaders = version.Loaders.Concat(indexedLoaders[version.Id]).Distinct(StringComparer.Ordinal).ToArray();
                if (loaders.Length == 0 && loaderId is { } knownLoader)
                    loaders = [CurseForgeLoaderTag(knownLoader)!];
                versions.Add(ApplyCurseForgeDistribution(version with { Loaders = loaders }, allowDistribution));
            }
            var total = page.Get("pagination").Get("totalCount").Number();
            if (files.Length < CurseForgeFilePageSize || offset + files.Length >= total) break;
        }
        return versions.OrderByDescending(version => version.PublishedAt).ToArray();
    }

    private async Task<ResourceVersion> GetCurseForgeVersionAsync(JsonElement project, string versionId, CancellationToken ct)
    {
        var projectId = CurseForgeId(project.Get("id"));
        versionId = CurseForgeId(versionId);
        var file = await GetCurseForgeDataAsync($"/mods/{projectId}/files/{versionId}", ct).ConfigureAwait(false);
        var version = ParseCurseForgeVersion(file);
        if (version.ProjectId != projectId || version.Id != versionId)
            throw new InvalidDataException("CurseForge 文件与请求的项目或版本不匹配。");
        return ApplyCurseForgeDistribution(version, project.Get("allowModDistribution").Boolean() != false);
    }

    private async Task<JsonElement> GetCurseForgeProjectDataAsync(string projectId, CancellationToken ct)
    {
        projectId = CurseForgeId(projectId);
        var project = await GetCurseForgeDataAsync($"/mods/{projectId}", ct).ConfigureAwait(false);
        if (CurseForgeId(project.Get("id")) != projectId)
            throw new InvalidDataException("CurseForge 返回了不同的项目。");
        return project;
    }

    private static ResourceVersion ParseCurseForgeVersion(JsonElement file)
    {
        var projectId = CurseForgeId(file.Get("modId"));
        var fileId = CurseForgeId(file.Get("id"));
        var name = NonEmpty(file.Get("fileName").Text());
        var labels = file.Get("gameVersions").Items().Select(item => item.Text()).OfType<string>().ToArray();
        var gameVersions = labels.Concat(file.Get("sortableGameVersions").Items().Select(item => item.Get("gameVersion").Text()).OfType<string>())
            .Where(value => CurseForgeGameVersion().IsMatch(value)).Distinct(StringComparer.Ordinal).ToArray();
        var loaders = labels.Select(label => label.Trim().ToLowerInvariant() switch
        {
            "iris" => "iris", "optifine" => "optifine", "canvas" => "canvas",
            _ => CurseForgeLoaderTag(CurseForgeLoaderId(label))
        }).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        var dependencies = file.Get("dependencies").Items()
            .Where(item => item.Get("relationType").Number() is >= 1 and <= 6)
            .Select(item => new ResourceDependency(CurseForgeId(item.Get("modId")), null, item.Get("relationType").Number() switch
            {
                1 => "embedded", 2 => "optional", 3 => "required", 4 => "tool", 5 => "incompatible", _ => "include"
            })).ToArray();
        var date = CurseForgeDate(file.Get("fileDate").Text());
        var url = CurseForgeCdnUrl(file.Get("downloadUrl").Text());
        var earlyAccess = file.Get("isEarlyAccessContent").Boolean() == true &&
            (CurseForgeDate(file.Get("earlyAccessEndDate").Text()) ?? DateTimeOffset.MaxValue) > DateTimeOffset.UtcNow;
        var downloadable = !earlyAccess && file.Get("isAvailable").Boolean() != false &&
            file.Get("isServerPack").Boolean() != true && url is not null && name is not null;
        DownloadFile[] files = [];
        if (downloadable)
        {
            var hashes = file.Get("hashes");
            var sha1 = CurseForgeHash(hashes, 1, 40);
            var md5 = sha1 is null ? CurseForgeHash(hashes, 2, 32) : null;
            files = [new DownloadFile(url!, StrictSegment(name!), sha1 ?? md5, md5 is null ? "SHA1" : "MD5", Long(file.Get("fileLength")))];
        }
        return new ResourceVersion(fileId, projectId, NonEmpty(file.Get("displayName").Text()) ?? name ?? fileId, name ?? fileId,
            gameVersions, loaders, files, dependencies)
        {
            PublishedAt = date,
            ReleaseType = file.Get("releaseType").Number() switch { 1 => "release", 2 => "beta", 3 => "alpha", _ => null },
            DownloadUnavailableReason = downloadable ? null : CurseForgeRestrictedDownload
        };
    }

    private static ResourceVersion ApplyCurseForgeDistribution(ResourceVersion version, bool allowDistribution) =>
        allowDistribution ? version : version with { Files = [], DownloadUnavailableReason = CurseForgeRestrictedDownload };

    private static string? CurseForgeHash(JsonElement hashes, int algorithm, int length) =>
        hashes.Items().Where(item => item.Get("algo").Number() == algorithm).Select(item => item.Get("value").Text())
            .FirstOrDefault(value => value?.Length == length && value.All(Uri.IsHexDigit));

    private static DateTimeOffset? CurseForgeDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result) ? result : null;

    private async Task<JsonElement> GetCurseForgeDataAsync(string relativePath, CancellationToken ct) =>
        (await RequestCurseForgeAsync(relativePath, null, ct).ConfigureAwait(false)).Get("data");

    private async Task<JsonElement> PostCurseForgeDataAsync(string relativePath, object body, CancellationToken ct) =>
        (await RequestCurseForgeAsync(relativePath, body, ct).ConfigureAwait(false)).Get("data");

    private async Task<JsonElement> RequestCurseForgeAsync(string relativePath, object? body, CancellationToken ct)
    {
        var key = _curseForgeApiKey()?.Trim();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("请在设置 → 启动器设置填写 CurseForge 官方 API 密钥，或设置 WONDER_CURSEFORGE_API_KEY。");
        if (key.Length > 4096 || key.Any(character => character is < '!' or > '~'))
            throw new InvalidOperationException("CurseForge API 密钥格式无效，请在启动器设置中检查。");

        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, CurseForgeApi + relativePath);
        request.Headers.Add("x-api-key", key);
        request.Headers.Accept.ParseAdd("application/json");
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await _curseForgeHttp.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new HttpRequestException("CurseForge 拒绝访问，请检查 API 密钥及其权限。", null, response.StatusCode);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("CurseForge 请求过于频繁，请稍后再试。", null, response.StatusCode);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, new() { MaxDepth = 64 }, ct).ConfigureAwait(false);
        if (document.RootElement.Get("data").ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new InvalidDataException("CurseForge 响应缺少数据。");
        return document.RootElement.Clone();
    }

    private static string CurseForgeId(JsonElement value) => Positive(value.Number()) is { } id
        ? id.ToString(CultureInfo.InvariantCulture) : throw new InvalidDataException("CurseForge 项目或文件缺少有效 ID。");

    private static string CurseForgeId(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
        ? id.ToString(CultureInfo.InvariantCulture) : throw new InvalidDataException("CurseForge 项目或文件 ID 无效。");

    private static (int Id, string Segment) CurseForgeClass(DownloadResourceKind kind) => kind switch
    {
        DownloadResourceKind.Mod => (6, "mc-mods"),
        DownloadResourceKind.Modpack => (4471, "modpacks"),
        DownloadResourceKind.ResourcePack => (12, "texture-packs"),
        DownloadResourceKind.Shader => (6552, "shaders"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static DownloadResourceKind CurseForgeProjectKind(int? classId) => classId switch
    {
        6 => DownloadResourceKind.Mod,
        4471 => DownloadResourceKind.Modpack,
        12 => DownloadResourceKind.ResourcePack,
        6552 => DownloadResourceKind.Shader,
        _ => throw new InvalidDataException("CurseForge 项目不属于支持的 Minecraft 资源类型。")
    };

    private static int? CurseForgeLoaderId(string? loader) => loader?.Trim().ToLowerInvariant() switch
    {
        "forge" => 1, "cauldron" => 2, "liteloader" => 3, "fabric" => 4, "quilt" => 5, "neoforge" or "neo forge" or "neo-forge" => 6, _ => null
    };

    private static string? CurseForgeLoaderTag(int? loader) => loader switch
    {
        1 => "forge", 2 => "cauldron", 3 => "liteloader", 4 => "fabric", 5 => "quilt", 6 => "neoforge", _ => null
    };

    private static string? CurseForgeCdnUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps &&
        url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0 &&
        url.Host.EndsWith(".forgecdn.net", StringComparison.OrdinalIgnoreCase) ? url.AbsoluteUri : null;

    private static string? OfficialCurseForgePage(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps &&
        url.IsDefaultPort && url.UserInfo.Length == 0 && url.Host is "www.curseforge.com" or "curseforge.com" &&
        url.AbsolutePath.StartsWith("/minecraft/", StringComparison.Ordinal) ? url.AbsoluteUri : null;

    [GeneratedRegex(@"^(?:(?:[ab]|c|inf-)?[0-9]+\.[0-9]+|[0-9]{2}w[0-9]{2}[a-z]|rd-[0-9]+)(?:[.\s-][a-z0-9]+)*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CurseForgeGameVersion();
}
