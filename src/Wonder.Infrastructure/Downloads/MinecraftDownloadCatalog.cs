// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Wonder.Application;
using Wonder.Core;
using static Wonder.Infrastructure.LocalFiles;

namespace Wonder.Infrastructure;

public sealed partial class MinecraftDownloadCatalog(IDownloadEngine engine) : IDownloadCatalog
{
    private const string Manifest = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private const string Modrinth = "https://api.modrinth.com/v2";
    private static string Escape(string value) => Uri.EscapeDataString(value);
    private async Task<JsonElement> GetJson(string url, DownloadOptions options, CancellationToken ct)
    {
        var text = await engine.GetStringAsync(url, options, ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(text, new() { MaxDepth = 64 });
        return document.RootElement.Clone();
    }

    public async Task<IReadOnlyList<MinecraftVersion>> GetMinecraftVersionsAsync(DownloadOptions options, CancellationToken ct = default)
    {
        options.Validate();
        var manifest = await GetJson(Manifest, options, ct);
        return manifest.Get("versions").Items().Select(x => new MinecraftVersion(Segment(x.Get("id").Text() ?? ""), x.Get("type").Text() ?? "release",
            RequiredUrl(x.Get("url").Text()), DateTimeOffset.TryParse(x.Get("releaseTime").Text(), out var time) ? time : default)).ToArray();
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
                $"https://maven.neoforged.net/releases/net/neoforged/{(legacyNeo ? "forge" : "neoforge")}/maven-metadata.xml", options, ct);
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
    private static string RequiredUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException("下载元数据中的地址必须使用 HTTPS。");
        return uri.AbsoluteUri;
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
    private static DownloadFile Descriptor(JsonElement value, string path) => new(RequiredUrl(value.Get("url").Text()), path, value.Get("sha1").Text(), "SHA1", Long(value.Get("size")));
    private static long? Long(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= 0 ? number : null;
}
