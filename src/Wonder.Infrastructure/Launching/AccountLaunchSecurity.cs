// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Security.Cryptography;
using Wonder.Core;

namespace Wonder.Infrastructure;

internal static class AccountLaunchSecurity
{
    internal static void ValidateSession(GameSession session)
    {
        if (!Enum.IsDefined(session.Kind) || string.IsNullOrEmpty(session.Username) || session.Username.Length > 16 ||
            session.Username.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) ||
            session.Kind != AccountKind.Yggdrasil && !session.Username.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ||
            session.Uuid is not { Length: 32 } || !session.Uuid.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("账户角色资料无效，请重新登录。");
        if (session.Kind == AccountKind.Offline)
        {
            InstanceCatalog.ValidateOfflineUsername(session.Username);
            if (session.AccessToken != "0") throw new InvalidDataException("离线账户不能携带在线凭据。");
            if (!OfflineDefaultSkins.IsValid(session.Username, session.Uuid, session.OfflineSkinId)) throw new InvalidDataException("离线账户的默认皮肤与 UUID 不匹配，请重新选择默认皮肤。");
        }
        else if (string.IsNullOrWhiteSpace(session.AccessToken) || session.AccessToken == "0" || session.AccessToken.Length > 16384 || session.AccessToken.Any(char.IsControl))
            throw new InvalidDataException("账户没有有效的游戏会话，请重新登录。");
        if (session.Kind != AccountKind.Offline && session.OfflineSkinId is not null) throw new InvalidDataException("在线账户不能覆盖默认皮肤身份。");
        if (session.Kind == AccountKind.Microsoft && (!Guid.TryParse(session.ClientId, out var clientId) || clientId == Guid.Empty ||
            session.Xuid.Length > 32 || !session.Xuid.All(char.IsAsciiDigit) || session.ServerUrl is not null || session.AuthlibInjectorPath is not null))
            throw new InvalidDataException("正版账户配置无效，不允许使用第三方认证参数。");
        if (session.Kind == AccountKind.Yggdrasil)
        {
            if (!Uri.TryCreate(session.ServerUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
                session.ServerUrl!.Any(char.IsControl))
                throw new InvalidDataException("第三方认证服务器必须为不含凭据、查询或片段的 HTTPS 地址。");
            VerifyAgent(session.AuthlibInjectorPath, session.AuthlibInjectorSha256);
        }
    }

    internal static void VerifyAgent(string? path, string? sha256)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.Contains('=') || path.Any(char.IsControl) ||
            !path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || sha256 is not { Length: 64 } || !sha256.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("第三方启动需要在账户设置中选择可信 authlib-injector JAR 并确认其 SHA-256。");
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length is <= 0 or > 64 * 1024 * 1024 || !Convert.ToHexString(SHA256.HashData(file)).Equals(sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("authlib-injector 文件校验失败，请重新选择可信文件。");
        }
        catch (IOException)
        { throw new InvalidDataException("无法读取 authlib-injector 文件，请重新选择可信文件。"); }
        catch (UnauthorizedAccessException)
        { throw new InvalidDataException("无法读取 authlib-injector 文件，请检查权限。"); }
    }

    internal static void ValidateJvmArguments(IEnumerable<string> arguments, AccountKind kind)
    {
        if (kind == AccountKind.Offline) return;
        foreach (var argument in arguments)
        {
            var lower = argument.ToLowerInvariant();
            if (lower.StartsWith('@') || lower.StartsWith("-javaagent:") || lower.StartsWith("-agentlib:") || lower.StartsWith("-agentpath:") ||
                lower.StartsWith("-xrun") || lower.StartsWith("-xbootclasspath") || lower.StartsWith("--patch-module") ||
                lower.StartsWith("-dauthlib") || lower.StartsWith("-dminecraft.api.") || lower.StartsWith("-dmojang.") ||
                lower.StartsWith("-djavax.net.ssl.") || lower.StartsWith("-djava.security.") || lower.StartsWith("-djava.system.class.loader=") ||
                lower.StartsWith("-dhttps.proxy") || lower.StartsWith("-dhttp.proxy") || lower.StartsWith("-dsocksproxy"))
                throw new InvalidDataException("在线账户不能使用额外认证代理、Java agent 或认证地址/证书覆盖参数。第三方 agent 请通过账户设置配置。");
        }
    }
}
