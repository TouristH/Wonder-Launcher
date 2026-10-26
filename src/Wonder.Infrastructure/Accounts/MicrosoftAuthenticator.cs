// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Wonder.Application;
using Wonder.Core;

namespace Wonder.Infrastructure;

/// <summary>Public-client Microsoft device authorization followed by Xbox and Minecraft verification.</summary>
public sealed partial class MicrosoftAuthenticator(HttpClient? httpClient = null) : IMicrosoftAuthenticator, IMicrosoftAccountAppearance
{
    private const string Authority = "https://login.microsoftonline.com/consumers/oauth2/v2.0/";
    private const string Scope = "XboxLive.signin offline_access";
    private const int MaximumResponseBytes = 1024 * 1024;
    private static readonly HttpClient DefaultHttp = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    }) { Timeout = TimeSpan.FromSeconds(30) };
    private readonly HttpClient _http = httpClient ?? DefaultHttp;

    public Task<AuthenticatedAccount> SignInAsync(string clientId, Action<DeviceCodePrompt> prompt, CancellationToken ct = default) =>
        ProtectErrors(async () =>
        {
            clientId = ValidateClientId(clientId);
            ArgumentNullException.ThrowIfNull(prompt);
            ct.ThrowIfCancellationRequested();
            var requestedAt = DateTimeOffset.UtcNow;
            var device = await PostForm(Authority + "devicecode", new() { ["client_id"] = clientId, ["scope"] = Scope }, "Microsoft 设备授权", ct);
            if (device.Status != HttpStatusCode.OK) throw OAuthFailure(device, "Microsoft 设备授权");
            var deviceCode = RequiredText(device.Body, "device_code");
            var userCode = RequiredText(device.Body, "user_code", 64);
            var verification = RequiredText(device.Body, "verification_uri", 2048);
            if (!Uri.TryCreate(verification, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
                uri.Host is not ("microsoft.com" or "www.microsoft.com" or "login.microsoftonline.com" or "login.live.com"))
                throw Failure("Microsoft 返回的验证网址无效，请重试。");
            var expiresAt = requestedAt.AddSeconds(RequiredSeconds(device.Body, "expires_in", 3600));
            var interval = device.Body.TryGetProperty("interval", out _) ? RequiredSeconds(device.Body, "interval", 900) : 5;
            ct.ThrowIfCancellationRequested();
            prompt(new(userCode, uri, expiresAt));
            ct.ThrowIfCancellationRequested();
            using var expiration = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var remaining = expiresAt - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) throw Failure("登录验证码已过期，请重新登录。");
            expiration.CancelAfter(remaining);
            try
            {
                while (true)
                {
                    // The first request is also delayed: never poll faster than the server's interval.
                    await Task.Delay(TimeSpan.FromSeconds(interval), expiration.Token).ConfigureAwait(false);
                    var token = await PostForm(Authority + "token", new()
                    {
                        ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                        ["client_id"] = clientId, ["device_code"] = deviceCode
                    }, "Microsoft 设备授权确认", expiration.Token).ConfigureAwait(false);
                    if (token.Status == HttpStatusCode.OK)
                        return await Finish(clientId, token.Body, null, null, ct).ConfigureAwait(false);
                    var error = KnownOAuthError(token.Body);
                    if (error == "authorization_pending") continue;
                    if (error == "slow_down") { interval = Math.Min(interval + 5, 900); continue; }
                    throw OAuthFailure(token, "Microsoft 设备授权确认");
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && expiration.IsCancellationRequested)
            { throw Failure("登录验证码已过期，请重新登录。"); }
        }, ct);

    public Task<AuthenticatedAccount> RefreshAsync(AccountProfile profile, AccountCredentials credentials, CancellationToken ct = default) =>
        ProtectErrors(async () =>
        {
            if (profile.Kind != AccountKind.Microsoft || profile.ServerUrl is not null)
                throw Failure("此账户不是 Microsoft 账户，请重新选择。");
            var clientId = ValidateClientId(profile.ClientId);
            if (!Guid.TryParseExact(profile.Uuid, "N", out _)) throw Failure("账户资料无效，请重新登录。");
            ValidateSecret(credentials.RefreshToken);
            var token = await PostForm(Authority + "token", new()
            {
                ["grant_type"] = "refresh_token", ["client_id"] = clientId,
                ["scope"] = Scope, ["refresh_token"] = credentials.RefreshToken
            }, "Microsoft 会话刷新", ct).ConfigureAwait(false);
            if (token.Status != HttpStatusCode.OK) throw OAuthFailure(token, "Microsoft 会话刷新");
            return await Finish(clientId, token.Body, profile, credentials.RefreshToken, ct).ConfigureAwait(false);
        }, ct);

    private async Task<AuthenticatedAccount> Finish(string clientId, JsonElement microsoft, AccountProfile? previous, string? previousRefresh, CancellationToken ct)
    {
        if (!string.Equals(RequiredText(microsoft, "token_type", 32), "Bearer", StringComparison.OrdinalIgnoreCase))
            throw Failure("Microsoft 返回的凭据格式无效，请重新登录。");
        var microsoftToken = RequiredText(microsoft, "access_token");
        var refresh = OptionalText(microsoft, "refresh_token") ?? previousRefresh;
        ValidateSecret(refresh);
        var xbox = await PostJson("https://user.auth.xboxlive.com/user/authenticate", new
        {
            Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + microsoftToken },
            RelyingParty = "http://auth.xboxlive.com", TokenType = "JWT"
        }, "Xbox 登录", ct).ConfigureAwait(false);
        var userHash = XboxClaim(xbox, "uhs");
        var xsts = await PostJson("https://xsts.auth.xboxlive.com/xsts/authorize", new
        {
            Properties = new { SandboxId = "RETAIL", UserTokens = new[] { RequiredText(xbox, "Token") } },
            RelyingParty = "rp://api.minecraftservices.com/", TokenType = "JWT"
        }, "Xbox 授权", ct).ConfigureAwait(false);
        if (!string.Equals(userHash, XboxClaim(xsts, "uhs"), StringComparison.Ordinal))
            throw Failure("Xbox 返回的账户身份不一致，请重新登录。");
        var xuid = XboxClaim(xsts, "xid", required: false);
        var minecraft = await PostJson("https://api.minecraftservices.com/launcher/login", new
        {
            xtoken = "XBL3.0 x=" + userHash + ";" + RequiredText(xsts, "Token"), platform = "PC_LAUNCHER"
        }, "Minecraft 登录（请确认应用已获得服务访问权限）", ct).ConfigureAwait(false);
        var access = RequiredText(minecraft, "access_token");
        var expires = DateTimeOffset.UtcNow.AddSeconds(RequiredSeconds(minecraft, "expires_in", 7 * 24 * 3600));
        var entitlements = await GetJson("https://api.minecraftservices.com/entitlements/license?requestId=" + Guid.NewGuid().ToString("D"), access, "Minecraft 所有权校验", ct).ConfigureAwait(false);
        if (!entitlements.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array ||
            !items.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.Object &&
                OptionalText(item, "name", 128) is "game_minecraft" or "product_minecraft"))
            throw Failure("此账户未返回有效的 Minecraft Java Edition 游戏许可，不能以正版账户启动。");
        var data = await GetJson("https://api.minecraftservices.com/minecraft/profile", access, "Minecraft 玩家资料读取", ct).ConfigureAwait(false);
        var uuid = RequiredText(data, "id", 32);
        var name = RequiredText(data, "name", 16);
        if (!Guid.TryParseExact(uuid, "N", out var parsedId) || parsedId == Guid.Empty ||
            name.Length is < 3 or > 16 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw Failure("Minecraft 返回的玩家资料无效，请重新登录。");
        uuid = parsedId.ToString("N");
        if (previous is not null && !string.Equals(previous.Uuid, uuid, StringComparison.OrdinalIgnoreCase))
            throw Failure("刷新后的 Minecraft 身份与保存的账户不符，请重新添加账户。");
        ct.ThrowIfCancellationRequested();
        return new(ReadAppearance(data, new(previous?.Id ?? Guid.NewGuid().ToString("N"), AccountKind.Microsoft, name, uuid, ClientId: clientId)),
            new() { AccessToken = access, RefreshToken = refresh!, Xuid = xuid, ExpiresAt = expires });
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> PostForm(string url, Dictionary<string, string> values, string stage, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(values) };
        return await Send(request, stage, ct).ConfigureAwait(false);
    }

    private async Task<JsonElement> PostJson(string url, object data, string stage, CancellationToken ct)
    {
        // Xbox uses case-sensitive PascalCase names; Minecraft's anonymous payload uses its own lower-case names.
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(data, options: new JsonSerializerOptions { PropertyNamingPolicy = null }) };
        if (request.RequestUri!.Host.EndsWith(".xboxlive.com", StringComparison.Ordinal)) request.Headers.Add("x-xbl-contract-version", "1");
        var response = await Send(request, stage, ct).ConfigureAwait(false);
        RequireSuccess(response, stage);
        return response.Body;
    }

    private async Task<JsonElement> GetJson(string url, string access, string stage, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var response = await Send(request, stage, ct).ConfigureAwait(false);
        RequireSuccess(response, stage);
        return response.Body;
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> Send(HttpRequestMessage request, string stage, CancellationToken ct)
    {
        request.Headers.Accept.Add(new("application/json"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if ((int)response.StatusCode is >= 300 and < 400)
            throw Failure(stage + "失败（HTTP " + (int)response.StatusCode + "）。认证服务返回重定向；为保护凭据已停止请求。");
        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
            throw Failure(stage + "失败（HTTP " + (int)response.StatusCode + "）。认证服务暂时不可用，请稍后重试。");
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            throw Failure("认证服务响应过大，已停止登录。");
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + count > MaximumResponseBytes) throw Failure("认证服务响应过大，已停止登录。");
            buffer.Write(chunk, 0, count);
        }
        using var json = JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, (int)buffer.Length), new() { MaxDepth = 32 });
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw Failure("认证服务响应格式无效，请重试。");
        return (response.StatusCode, json.RootElement.Clone());
    }

    private static string XboxClaim(JsonElement data, string field, bool required = true)
    {
        if (!data.TryGetProperty("DisplayClaims", out var claims) || claims.ValueKind != JsonValueKind.Object ||
            !claims.TryGetProperty("xui", out var xui) || xui.ValueKind != JsonValueKind.Array || xui.GetArrayLength() != 1)
            throw Failure("Xbox 返回的身份资料无效，请重新登录。");
        var value = OptionalText(xui[0], field, 64);
        if (value is null && !required) return "";
        if (value is null || !value.All(char.IsAsciiDigit)) throw Failure("Xbox 返回的身份资料无效，请重新登录。");
        return value;
    }

    private static string ValidateClientId(string? value) => Guid.TryParse(value, out var id) && id != Guid.Empty
        ? id.ToString("D") : throw Failure("Wonder 的登录应用配置无效，请更新启动器或联系维护者。");

    private static string RequiredText(JsonElement data, string field, int limit = 32768) =>
        OptionalText(data, field, limit) ?? throw Failure("认证服务未返回必要的资料，请重新登录。");

    private static string? OptionalText(JsonElement data, string field, int limit = 32768)
    {
        if (data.ValueKind != JsonValueKind.Object) throw Failure("认证服务响应格式无效，请重试。");
        if (!data.TryGetProperty(field, out var item) || item.ValueKind == JsonValueKind.Null) return null;
        if (item.ValueKind != JsonValueKind.String) throw Failure("认证服务响应格式无效，请重试。");
        var value = item.GetString();
        if (string.IsNullOrEmpty(value) || value.Length > limit || value.Any(char.IsControl))
            throw Failure("认证服务返回的资料无效，请重试。");
        return value;
    }

    private static int RequiredSeconds(JsonElement data, string field, int limit) =>
        data.TryGetProperty(field, out var value) && value.TryGetInt32(out var number) && number > 0 && number <= limit
            ? number : throw Failure("认证服务返回的有效期无效，请重试。");

    private static void ValidateSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32768 || value.Any(char.IsControl))
            throw Failure("此账户缺少有效的刷新凭据，请重新登录。");
    }

    private static void RequireSuccess((HttpStatusCode Status, JsonElement Body) response, string stage)
    {
        if (stage.StartsWith("Minecraft 登录", StringComparison.Ordinal) && response.Status == HttpStatusCode.Forbidden)
            throw Failure("Minecraft 登录失败（HTTP 403）。微软与 Xbox 授权已通过，但 Minecraft Services 拒绝访问。请核对 Wonder AppID 的服务访问审批、所获权限和当前客户端 ID；仅凭 403 不能证明审批状态。");
        if (stage.StartsWith("Minecraft 登录", StringComparison.Ordinal) && response.Status == HttpStatusCode.Unauthorized)
            throw Failure("Minecraft 登录失败（HTTP 401）。Minecraft Services 未接受本次 Xbox 会话，请重新登录；若持续失败，请维护者核对服务认证链与应用权限。");
        if (response.Status != HttpStatusCode.OK) throw Failure(stage + "失败（HTTP " + (int)response.Status + "），请检查账户与应用权限后重新登录。");
    }

    // Only protocol constants and known numeric diagnostics may reach the UI. Never echo the
    // server's free-form description, arbitrary error text, request identifiers or response body.
    private static string? KnownOAuthError(JsonElement body)
    {
        if (!body.TryGetProperty("error", out var item) || item.ValueKind != JsonValueKind.String) return null;
        return item.GetString() switch
        {
            "authorization_pending" => "authorization_pending", "slow_down" => "slow_down",
            "authorization_declined" => "authorization_declined", "access_denied" => "access_denied",
            "expired_token" => "expired_token", "bad_verification_code" => "bad_verification_code",
            "invalid_grant" => "invalid_grant", "invalid_client" => "invalid_client",
            "unauthorized_client" => "unauthorized_client", "invalid_scope" => "invalid_scope",
            "invalid_resource" => "invalid_resource", "invalid_request" => "invalid_request",
            "unsupported_grant_type" => "unsupported_grant_type", "interaction_required" => "interaction_required",
            "consent_required" => "consent_required", "temporarily_unavailable" => "temporarily_unavailable",
            "server_error" => "server_error", _ => null
        };
    }

    private static int? KnownMicrosoftDiagnostic(JsonElement body)
    {
        if (!body.TryGetProperty("error_codes", out var codes) || codes.ValueKind != JsonValueKind.Array || codes.GetArrayLength() > 16) return null;
        foreach (var item in codes.EnumerateArray())
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var code) && code is 70002 or 700016 or 7000218 or 70011 or 500011 or 50194)
                return code;
        return null;
    }

    private static AccountAuthenticationException OAuthFailure((HttpStatusCode Status, JsonElement Body) response, string stage)
    {
        var error = KnownOAuthError(response.Body);
        var code = KnownMicrosoftDiagnostic(response.Body);
        // Numeric diagnostics only refine the explanation. They never change protocol flow,
        // authorize a retry, or provide a fallback to another account or application.
        var guidance = code switch
        {
            70002 or 7000218 when error is "invalid_client" or "unauthorized_client" =>
                "Wonder 的应用注册不支持当前公共客户端流程。维护者需在 Microsoft Entra 应用注册的“身份验证”中启用“允许公共客户端流”，并核对个人账户支持。本阶段尚未访问 Minecraft，等待 Minecraft AppID 审核不能解决此错误。",
            700016 => "微软未找到此应用。请维护者核对 Application (client) ID、应用注册所在目录及支持的账户类型。",
            50194 => "应用注册的账户范围不支持当前登录入口，请维护者核对个人 Microsoft 账户支持。",
            _ => error switch
            {
                "authorization_declined" or "access_denied" => "已拒绝 Microsoft 登录授权。",
                "expired_token" or "bad_verification_code" => "登录验证码已失效，请重新发起登录。",
                "invalid_grant" or "interaction_required" or "consent_required" => "Microsoft 登录状态需要重新授权，请重新登录。",
                "invalid_client" or "unauthorized_client" => "Wonder 的应用注册未获准使用此登录方式，请维护者检查公共客户端流程和个人账户支持。本阶段尚未访问 Minecraft。",
                "invalid_scope" or "invalid_resource" => "微软登录权限范围（scope）或资源配置无效，请维护者核对 Xbox 登录权限。",
                "invalid_request" or "unsupported_grant_type" => "微软拒绝了登录协议参数，请更新启动器并联系维护者。",
                "temporarily_unavailable" or "server_error" => "微软认证服务暂时不可用，请稍后重试。",
                _ => "微软授权未完成，请检查网络或联系维护者核对应用注册；此错误尚不能证明 Minecraft AppID 审核状态。"
            }
        };
        return Failure(stage + "失败（HTTP " + (int)response.Status +
            (error is null ? "" : " · " + error) + (code is null ? "" : " · AADSTS" + code) + "）。" + guidance);
    }

    private static AccountAuthenticationException Failure(string message) => new(message);

    private static async Task<AuthenticatedAccount> ProtectErrors(Func<Task<AuthenticatedAccount>> action, CancellationToken ct)
    {
        try { return await action().ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw Failure("认证请求超时，请重试。"); }
        catch (HttpRequestException) { throw Failure("无法安全连接认证服务，请检查网络后重试。"); }
        catch (IOException) { throw Failure("读取认证服务响应失败，请检查网络后重试。"); }
        catch (JsonException) { throw Failure("认证服务响应格式无效，请重试。"); }
        catch (InvalidOperationException) { throw Failure("认证服务返回的资料无效，请重试。"); }
        catch (FormatException) { throw Failure("认证服务返回的资料格式无效，请重试。"); }
    }
}
