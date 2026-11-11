// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Wonder.Core;

namespace Wonder.Infrastructure;

/// <summary>Maps only documented Minecraft endpoints; third-party resource URLs remain untouched.</summary>
public static class DownloadSourceResolver
{
    public const string MirrorRoot = "https://bmclapi2.bangbang93.com";

    public static IReadOnlyList<string> Resolve(string url, DownloadSource source)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("下载地址必须是有效的 HTTP(S) 地址。", nameof(url));
        if (!Enum.IsDefined(source)) throw new ArgumentException("下载源无效。", nameof(source));
        if (source == DownloadSource.Official || !uri.IsDefaultPort) return [url];
        // BMCLAPI's documented directory mappings: https://bmclapidoc.bangbang93.com/.
        var prefix = uri.Host.ToLowerInvariant() switch
        {
            "resources.download.minecraft.net" => "/assets",
            "libraries.minecraft.net" or "maven.minecraftforge.net" => "/maven",
            "launchermeta.mojang.com" or "launcher.mojang.com" or "piston-meta.mojang.com" or "piston-data.mojang.com" => "",
            _ => null
        };
        if (prefix is null) return [url];
        return [MirrorRoot + prefix + uri.PathAndQuery, url];
    }
}
