// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wonder.Application;
using Wonder.Core;

namespace Wonder.Infrastructure;

public sealed partial class YggdrasilAuthenticator : IYggdrasilAuthenticator, IYggdrasilAccountAppearance
{
    private const int MaximumResponseBytes = 1024 * 1024;
    private static readonly HttpClient DefaultClient = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    { Timeout = TimeSpan.FromSeconds(30) };
    private readonly HttpClient _http;

    // An injected client is for controlled hosts/tests; production never follows redirects.
    public YggdrasilAuthenticator(HttpClient? httpClient = null) => _http = httpClient ?? DefaultClient;

    public static string NormalizeServerUrl(string serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl) || serverUrl.Length > 2048 || serverUrl.Any(char.IsControl) ||
            !Uri.TryCreate(serverUrl.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
            throw Error("第三方认证地址必须是没有用户名、密码、查询参数和片段的 HTTPS API 根地址。");
        return uri.AbsoluteUri.TrimEnd('/') + "/";
    }

    public async Task<ThirdPartyLogin> SignInAsync(string serverUrl, string username, string password, CancellationToken ct = default)
    {
        var server = NormalizeServerUrl(serverUrl);
        if (string.IsNullOrWhiteSpace(username) || username.Length > 320 || username.Any(char.IsControl) ||
            string.IsNullOrEmpty(password) || password.Length > 4096)
            throw Error("请填写有效的第三方用户名和密码。");
        var clientToken = Guid.NewGuid().ToString("N");
        using var json = await SendAsync(server, "authenticate", new
        {
            username, password, clientToken, requestUser = true, agent = new { name = "Minecraft", version = 1 }
        }, false, ct).ConfigureAwait(false);
        var root = json!.RootElement;
        var credentials = ReadCredentials(root, clientToken);
        var profiles = new List<PlayerProfile>();
        if (root.TryGetProperty("availableProfiles", out var available))
        {
            if (available.ValueKind != JsonValueKind.Array || available.GetArrayLength() > 256) throw Malformed();
            profiles.AddRange(available.EnumerateArray().Select(ReadProfile));
        }
        var selected = OptionalProfile(root);
        if (selected is not null && profiles.Count == 0) profiles.Add(selected);
        if (profiles.Count == 0) throw Error("第三方账户尚无游戏角色，请先到认证服务创建角色。");
        if (profiles.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != profiles.Count ||
            selected is not null && !profiles.Any(x => x.Id == selected.Id && x.Name == selected.Name)) throw Malformed();
        ct.ThrowIfCancellationRequested();
        return new(server, profiles.AsReadOnly(), selected?.Id, credentials);
    }

    public async Task<AuthenticatedAccount> SelectProfileAsync(ThirdPartyLogin login, string profileId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(login);
        var server = NormalizeServerUrl(login.ServerUrl);
        var selected = login.Profiles.FirstOrDefault(x => x.Id == profileId) ?? throw Error("请选择此账户提供的角色。");
        ValidateProfile(selected); ValidateCredentials(login.Credentials);
        ct.ThrowIfCancellationRequested();
        if (login.SelectedProfileId is not null)
        {
            if (login.SelectedProfileId != profileId) throw Error("本次登录已绑定其他角色，请使用目标角色重新登录。");
            return await ReadAccountAppearanceAsync(Account(server, selected, login.Credentials), ct).ConfigureAwait(false);
        }
        using var json = await SendAsync(server, "refresh", new
        {
            accessToken = login.Credentials.AccessToken, clientToken = login.Credentials.ClientToken,
            requestUser = true, selectedProfile = new { id = selected.Id, name = selected.Name }
        }, false, ct).ConfigureAwait(false);
        var result = ReadBound(json!.RootElement, profileId, login.Credentials.ClientToken);
        ct.ThrowIfCancellationRequested();
        return await ReadAccountAppearanceAsync(Account(server, result.Profile, result.Credentials), ct).ConfigureAwait(false);
    }

    public async Task<AuthenticatedAccount> RefreshAsync(AccountProfile profile, AccountCredentials credentials, CancellationToken ct = default)
    {
        var server = ValidateExisting(profile, credentials);
        using var json = await SendAsync(server, "refresh", new
        {
            accessToken = credentials.AccessToken, clientToken = credentials.ClientToken, requestUser = true
        }, false, ct).ConfigureAwait(false);
        var result = ReadBound(json!.RootElement, profile.Uuid, credentials.ClientToken);
        // The old token may already be revoked. Return the validated rotation without another
        // cancellation point so AccountService can persist it before optional appearance reads.
        return new(profile with { Username = result.Profile.Name }, result.Credentials);
    }

    public async Task InvalidateAsync(AccountProfile profile, AccountCredentials credentials, CancellationToken ct = default)
    {
        var server = ValidateExisting(profile, credentials);
        using var ignored = await SendAsync(server, "invalidate", new
        { accessToken = credentials.AccessToken, clientToken = credentials.ClientToken }, true, ct).ConfigureAwait(false);
    }

    private static string ValidateExisting(AccountProfile profile, AccountCredentials credentials)
    {
        if (profile.Kind != AccountKind.Yggdrasil) throw Error("该会话不是第三方认证会话。");
        ValidateProfile(new(profile.Uuid, profile.Username)); ValidateCredentials(credentials);
        return NormalizeServerUrl(profile.ServerUrl!);
    }

    private async Task<JsonDocument?> SendAsync(string server, string action, object payload, bool empty, CancellationToken ct)
    {
        byte[]? bytes = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(server), "authserver/" + action));
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode != (empty ? HttpStatusCode.NoContent : HttpStatusCode.OK))
                throw Error((int)response.StatusCode is >= 300 and < 400
                    ? "第三方认证服务要求重定向，已阻止转发凭据；请核对 HTTPS API 根地址。"
                    : response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                        ? "第三方账户登录或会话验证失败，请检查凭据并重新登录。"
                        : "第三方认证服务请求失败，请检查服务地址和服务器状态。");
            if (empty) return null;
            if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw Malformed();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream(); var chunk = new byte[8192];
            try
            {
                int count;
                while ((count = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) != 0)
                {
                    if (buffer.Length + count > MaximumResponseBytes) throw Malformed();
                    buffer.Write(chunk, 0, count);
                }
                var document = JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length)),
                    new JsonDocumentOptions { MaxDepth = 24, AllowDuplicateProperties = false });
                if (document.RootElement.ValueKind != JsonValueKind.Object) { document.Dispose(); throw Malformed(); }
                // JsonDocument.Parse retains its input; clone via its owned byte-array overload before clearing.
                var owned = JsonDocument.Parse(document.RootElement.GetRawText(), new JsonDocumentOptions { MaxDepth = 24, AllowDuplicateProperties = false });
                document.Dispose(); return owned;
            }
            finally { CryptographicOperations.ZeroMemory(chunk); CryptographicOperations.ZeroMemory(buffer.GetBuffer()); }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw Error("第三方认证服务连接超时，请稍后重试。"); }
        catch (HttpRequestException) { throw Error("无法安全连接第三方认证服务，请检查网络和 HTTPS 证书。"); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or IOException) { throw Malformed(); }
        finally { if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); }
    }

    private static (PlayerProfile Profile, AccountCredentials Credentials) ReadBound(JsonElement root, string expectedProfile, string clientToken)
    {
        var selected = OptionalProfile(root) ?? throw Malformed();
        if (!string.Equals(selected.Id, expectedProfile, StringComparison.OrdinalIgnoreCase)) throw Error("认证服务返回了不同的角色，已停止登录。");
        return (selected, ReadCredentials(root, clientToken));
    }

    private static AccountCredentials ReadCredentials(JsonElement root, string expectedClient)
    {
        var access = String(root, "accessToken"); var client = String(root, "clientToken");
        if (client != expectedClient) throw Error("认证服务返回的客户端凭据不一致，已停止登录。");
        // Yggdrasil does not advertise a token expiry. AccountService still refreshes before every launch.
        var result = new AccountCredentials { AccessToken = access, ClientToken = client, ExpiresAt = DateTimeOffset.MaxValue };
        ValidateCredentials(result); return result;
    }
    private static void ValidateCredentials(AccountCredentials credentials)
    {
        if (!SafeToken(credentials.AccessToken) || !SafeToken(credentials.ClientToken)) throw Malformed();
    }
    private static bool SafeToken(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 65536 && !value.Any(char.IsControl);
    private static PlayerProfile? OptionalProfile(JsonElement root) => root.TryGetProperty("selectedProfile", out var item) && item.ValueKind != JsonValueKind.Null ? ReadProfile(item) : null;
    private static PlayerProfile ReadProfile(JsonElement root)
    {
        var result = new PlayerProfile(String(root, "id").ToLowerInvariant(), String(root, "name")); ValidateProfile(result); return result;
    }
    private static void ValidateProfile(PlayerProfile profile)
    {
        if (profile.Id.Length != 32 || !profile.Id.All(char.IsAsciiHexDigit) || string.IsNullOrWhiteSpace(profile.Name) ||
            profile.Name.Length > 16 || profile.Name.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) throw Malformed();
    }
    private static string String(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.String ? property.GetString()! : throw Malformed();
    private static AuthenticatedAccount Account(string server, PlayerProfile profile, AccountCredentials credentials)
    {
        var id = "yggdrasil-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(server + "\n" + profile.Id))).ToLowerInvariant();
        return new(new(id, AccountKind.Yggdrasil, profile.Name, profile.Id, server), credentials);
    }
    private static AccountAuthenticationException Malformed() => Error("第三方认证服务返回了无效或过大的账户数据。");
    private static AccountAuthenticationException Error(string message) => new(message);
}
