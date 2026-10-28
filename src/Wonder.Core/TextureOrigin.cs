// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
namespace Wonder.Core;

/// <summary>Exact HTTPS origin permissions. Parsing does not resolve or classify network addresses.</summary>
public static class TextureOrigin
{
    public const int MaximumAllowedOrigins = 32;

    public static string FromUrl(string value)
    {
        var uri = Parse(value);
        var host = uri.IdnHost.ToLowerInvariant();
        if (uri.HostNameType == UriHostNameType.IPv6 && !host.StartsWith('[')) host = "[" + host + "]";
        return "https://" + host + ":" + uri.Port;
    }

    public static string Normalize(string value)
    {
        var uri = Parse(value);
        // Uri.UserInfo is empty for an explicit empty user-info delimiter (https://@host).
        if (value.Contains('@')) throw InvalidOrigin();
        // Checking the original suffix also rejects normalized-away paths, queries and fragments.
        var authorityEnd = value.IndexOfAny(['/', '?', '#'], value.IndexOf("://", StringComparison.Ordinal) + 3);
        if (authorityEnd >= 0 && value[authorityEnd..] != "/") throw InvalidOrigin();
        if (uri.AbsolutePath != "/") throw InvalidOrigin();
        return FromUrl(value);
    }

    public static void Validate(AccountProfile profile)
    {
        if (profile.AllowedPrivateTextureOrigins is not { } origins || origins.Count > MaximumAllowedOrigins ||
            (profile.Kind != AccountKind.Yggdrasil && origins.Count > 0)) throw new InvalidDataException("纹理来源许可无效。");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var origin in origins)
        {
            try
            {
                if (origin != Normalize(origin) || !seen.Add(origin)) throw new InvalidDataException("纹理来源许可必须唯一且规范化。");
            }
            catch (ArgumentException ex) { throw new InvalidDataException("纹理来源许可无效。", ex); }
        }
    }

    private static Uri Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl) ||
            value.Contains('\\') || value.Contains('*') || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Host.Length == 0 || uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || value.Contains('?') || value.Contains('#') ||
            uri.HostNameType == UriHostNameType.Unknown || uri.HostNameType == UriHostNameType.Basic || uri.IdnHost.Contains('%'))
            throw InvalidOrigin();
        return uri;
    }

    private static ArgumentException InvalidOrigin() => new("纹理来源必须是明确的 HTTPS 主机和端口，不得包含路径、通配符、凭据或查询片段。");
}
