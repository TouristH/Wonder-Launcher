// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Wonder.Application;
using Wonder.Core;
using static Wonder.Infrastructure.LocalFiles;

namespace Wonder.Infrastructure;

public sealed partial class MinecraftDownloadCatalog(IDownloadEngine engine, Func<string?>? curseForgeApiKey = null,
    HttpClient? curseForgeHttp = null) : IDownloadCatalog
{
    private const string Manifest = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private const string Modrinth = "https://api.modrinth.com/v2";
    private readonly HttpClient _curseForgeHttp = curseForgeHttp ?? CurseForgeHttp;
    private readonly Func<string?> _curseForgeApiKey = curseForgeApiKey ??
        (() => Environment.GetEnvironmentVariable("WONDER_CURSEFORGE_API_KEY"));
    private static string Escape(string value) => Uri.EscapeDataString(value);
    private async Task<JsonElement> GetJson(string url, DownloadOptions options, CancellationToken ct, string? sha1 = null)
    {
        var text = await engine.GetStringAsync(url, options with { Source = DownloadSource.Official }, ct).ConfigureAwait(false);
        if (sha1 is not null) VerifyMetadata(text, sha1);
        using var document = JsonDocument.Parse(text, new() { MaxDepth = 64 });
        return document.RootElement.Clone();
    }

    public async Task<IReadOnlyList<MinecraftVersion>> GetMinecraftVersionsAsync(DownloadOptions options, CancellationToken ct = default)
    {
        options.Validate();
        var manifest = await GetJson(Manifest, options, ct);
        return manifest.Get("versions").Items().Select(x => new MinecraftVersion(Segment(x.Get("id").Text() ?? ""), x.Get("type").Text() ?? "release",
            RequiredUrl(x.Get("url").Text(), "piston-meta.mojang.com", "launchermeta.mojang.com"),
            DateTimeOffset.TryParse(x.Get("releaseTime").Text(), out var time) ? time : default)
            { Sha1 = x.Get("sha1").Text() }).ToArray();
    }

    public async Task<IReadOnlyList<LoaderVersion>> GetLoaderVersionsAsync(string loader, string minecraftVersion, DownloadOptions options, CancellationToken ct = default)
    {
        options.Validate(); Segment(minecraftVersion); loader = loader.ToLowerInvariant();
        if (loader is "fabric" or "quilt")
        {
            var json = await GetJson($"{LoaderHost(loader)}/v{(loader == "fabric" ? 2 : 3)}/versions/loader/{Escape(minecraftVersion)}", options, ct);
            return json.Items().Select(x => x.Get("loader")).Where(x => x.Get("version").Text() is not null)
                .Select(x => new LoaderVersion(loader, x.Get("version").Text()!, minecraftVersion, x.Get("stable").Boolean() ?? !x.Get("version").Text()!.Contains("beta", StringComparison.OrdinalIgnoreCase))).ToArray();
        }
        if (loader is "forge" or "neoforge")
        {
            var legacyNeo = loader == "neoforge" && minecraftVersion == "1.20.1";
            var xml = await engine.GetStringAsync(loader == "forge" ? "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml" :
                $"https://maven.neoforged.net/releases/net/neoforged/{(legacyNeo ? "forge" : "neoforge")}/maven-metadata.xml",
                options with { Source = DownloadSource.Official }, ct);
            var prefix = loader == "forge" || legacyNeo ? minecraftVersion + "-" : NeoPrefix(minecraftVersion);
            return XDocument.Parse(xml).Descendants("version").Select(x => x.Value).Where(x => x.StartsWith(prefix, StringComparison.Ordinal))
                .Reverse().Select(x => new LoaderVersion(loader, x, minecraftVersion, !x.Contains("beta", StringComparison.OrdinalIgnoreCase))).ToArray();
        }
        if (loader == "vanilla") return [];
        throw new ArgumentException("未知的模组加载器。");
    }

    private static string NeoPrefix(string minecraft)
    {
        var parts = minecraft.Split('.');
        if (parts.Length < 2 || parts.Any(x => !int.TryParse(x, out _))) return "__unsupported__";
        if (parts[0] != "1") return string.Join('.', parts) + (parts.Length == 2 ? ".0." : ".");
        return parts[1] + "." + (parts.Length > 2 ? parts[2] : "0") + ".";
    }
    private static string LoaderHost(string loader) => loader == "fabric" ? "https://meta.fabricmc.net" : "https://meta.quiltmc.org";
    private static string RequiredUrl(string? url, params string[] hosts) => DownloadSourceResolver.RequireUrl(url, hosts).AbsoluteUri;

    internal static void VerifyMetadata(string text, string? sha1, long? size = null)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        if (size is { } expectedSize && bytes.LongLength != expectedSize || sha1 is not { Length: 40 } ||
            !Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes)).Equals(sha1, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("官方元数据的长度或 SHA-1 与上级清单不符。");
    }
    private static string StrictSegment(string value)
    {
        Segment(value);
        if (value.Any(char.IsControl) || value.Contains(':') || value.EndsWith('.') || value.EndsWith(' ') || value.Contains('\\') || value.Contains('/'))
            throw new InvalidDataException("下载文件名无效。");
        var stem = value.Split('.')[0];
        if (OperatingSystem.IsWindows() && (new[] { "CON", "PRN", "AUX", "NUL" }.Contains(stem, StringComparer.OrdinalIgnoreCase) ||
            stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9'))
            throw new InvalidDataException("下载文件名是系统保留名称。");
        return value;
    }
    private static string SafePath(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Contains(':') || relative.Split('/').Any(x => x is "." or ".." || x.Length == 0 || x.Any(char.IsControl)))
            throw new InvalidDataException("下载路径越出所属目录。");
        foreach (var part in relative.Split('/')) StrictSegment(part);
        var destination = Under(root, relative);
        var current = Path.GetFullPath(root);
        if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("下载目标目录不能是符号链接。");
        foreach (var part in relative.Split('/'))
        {
            current = Path.Combine(current, part);
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("下载目标包含符号链接，请选择普通目录。");
        }
        return destination;
    }
    private static DownloadFile Descriptor(JsonElement value, string path, params string[] hosts)
    {
        var url = RequiredUrl(value.Get("url").Text(), hosts);
        return HashedFile(value, url, path) ?? throw new InvalidDataException("下载清单缺少完整性校验值：" + Path.GetFileName(path));
    }

    private static DownloadFile? HashedFile(JsonElement value, string url, string path)
    {
        foreach (var (key, algorithm) in new[] { ("sha512", "SHA512"), ("sha256", "SHA256"), ("sha1", "SHA1") })
            if (value.Get(key).Text() is { Length: > 0 } hash)
            {
                var file = new DownloadFile(url, path, hash, algorithm, Long(value.Get("size")));
                DownloadEngine.ValidateHash(file);
                return file;
            }
        return null;
    }
    private static long? Long(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= 0 ? number : null;
}
