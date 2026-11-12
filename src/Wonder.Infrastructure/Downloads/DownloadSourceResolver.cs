// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Wonder.Core;

namespace Wonder.Infrastructure;

/// <summary>Maps only documented Minecraft endpoints; third-party resource URLs remain untouched.</summary>
public static class DownloadSourceResolver
{
    public const string MirrorRoot = "https://bmclapi2.bangbang93.com";
    internal const string MirrorHost = "bmclapi2.bangbang93.com";

    public static IReadOnlyList<string> Resolve(string url, DownloadSource source)
    {
        var uri = RequireUrl(url);
        if (!Enum.IsDefined(source)) throw new ArgumentException("下载源无效。", nameof(source));
        if (source == DownloadSource.Official) return [uri.AbsoluteUri];
        // BMCLAPI's documented directory mappings: https://bmclapidoc.bangbang93.com/.
        var prefix = uri.Host.ToLowerInvariant() switch
        {
            "resources.download.minecraft.net" => "/assets",
            "libraries.minecraft.net" or "maven.minecraftforge.net" => "/maven",
            "launcher.mojang.com" or "piston-data.mojang.com" => "",
            _ => null
        };
        if (prefix is null) return [uri.AbsoluteUri];
        return [MirrorRoot + prefix + uri.PathAndQuery, uri.AbsoluteUri];
    }

    internal static Uri RequireUrl(string? value, params string[] hosts)
    {
        if (value is null || value.Any(char.IsControl) || value.Contains('\\') ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            uri.HostNameType != UriHostNameType.Dns || !KnownHost(uri.IdnHost) ||
            (hosts.Length != 0 && !hosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("下载地址必须使用受支持主机的默认 HTTPS 端口，且不能包含凭据或片段。");
        var authority = value.AsSpan(value.IndexOf("://", StringComparison.Ordinal) + 3);
        var end = authority.IndexOfAny('/', '?', '#');
        if ((end < 0 ? authority : authority[..end]).Contains('@'))
            throw new InvalidDataException("下载地址不能包含凭据。");
        return uri;
    }

    private static bool KnownHost(string host) => host is
        "piston-meta.mojang.com" or "launchermeta.mojang.com" or "piston-data.mojang.com" or "launcher.mojang.com" or
        "libraries.minecraft.net" or "resources.download.minecraft.net" or
        "meta.fabricmc.net" or "meta.quiltmc.org" or "maven.fabricmc.net" or "maven.quiltmc.org" or
        "maven.minecraftforge.net" or "files.minecraftforge.net" or "maven.neoforged.net" or
        "repo.maven.apache.org" or "repo1.maven.org" or "optifine.net" or "api.modrinth.com" or "cdn.modrinth.com" or
        "github.com" or "raw.githubusercontent.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com" or
        "gitlab.com" or MirrorHost || IsCurseForgeCdn(host);

    internal static bool IsCurseForgeCdn(string host) => host is "edge.forgecdn.net" or "media.forgecdn.net" or "mediafilez.forgecdn.net" or "mediafiles.forgecdn.net";

    internal static bool IsAllowedRedirect(Uri origin, Uri destination, bool metadata)
    {
        var from = origin.IdnHost;
        var to = destination.IdnHost;
        if (from == to) return true;
        if (from is "launchermeta.mojang.com" && to is "piston-meta.mojang.com") return true;
        if (from is "files.minecraftforge.net" && to is "maven.minecraftforge.net") return true;
        if (metadata) return false;
        return (from, to) switch
        {
            ("github.com", "release-assets.githubusercontent.com" or "objects.githubusercontent.com" or "raw.githubusercontent.com") => true,
            ("launcher.mojang.com" or "piston-data.mojang.com", "launcher.mojang.com" or "piston-data.mojang.com") => true,
            _ => IsCurseForgeCdn(from) && IsCurseForgeCdn(to)
        };
    }
}
