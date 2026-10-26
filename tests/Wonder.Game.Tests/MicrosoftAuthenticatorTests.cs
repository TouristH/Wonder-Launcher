// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Net;
using System.Text;
using System.Text.Json;
using Wonder.Application;
using Wonder.Core;
using Wonder.Infrastructure;

namespace Wonder.Game.Tests;

public sealed class MicrosoftAuthenticatorTests
{
    private const string ClientId = "11111111-2222-4333-8444-555555555555";
    private const string Uuid = "00112233445546778899aabbccddeeff";
    private static AccountProfile Profile => new("account-one", AccountKind.Microsoft, "PreviousName", Uuid, ClientId: ClientId);
    private static AccountCredentials Credentials => new() { RefreshToken = "synthetic-old-refresh" };

    [Fact]
    public async Task Refresh_rotates_credentials_and_binds_verified_identity_using_only_official_endpoints()
    {
        using var handler = new ScriptedHandler(Success());
        using var http = new HttpClient(handler);
        var result = await new MicrosoftAuthenticator(http).RefreshAsync(Profile, Credentials);
        Assert.Equal("account-one", result.Profile.Id);
        Assert.Equal(AccountKind.Microsoft, result.Profile.Kind);
        Assert.Equal(Uuid, result.Profile.Uuid);
        Assert.Equal("VerifiedPlayer", result.Profile.Username);
        Assert.Equal("synthetic-new-refresh", result.Credentials.RefreshToken);
        Assert.Equal("synthetic-minecraft-token", result.Credentials.AccessToken);
        Assert.Equal("123456", result.Credentials.Xuid);
        Assert.True(result.Credentials.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(50));
        Assert.Collection(handler.Requests,
            x => { Assert.Equal("https://login.microsoftonline.com/consumers/oauth2/v2.0/token", x.Url); Assert.Contains("grant_type=refresh_token", x.Body); Assert.Contains("refresh_token=synthetic-old-refresh", x.Body); Assert.DoesNotContain("client_secret", x.Body); Assert.Null(x.Authorization); },
            x => { Assert.Equal("https://user.auth.xboxlive.com/user/authenticate", x.Url); using var j = JsonDocument.Parse(x.Body); Assert.Equal("d=synthetic-microsoft-token", j.RootElement.GetProperty("Properties").GetProperty("RpsTicket").GetString()); Assert.Null(x.Authorization); },
            x => { Assert.Equal("https://xsts.auth.xboxlive.com/xsts/authorize", x.Url); Assert.Contains("rp://api.minecraftservices.com/", x.Body); Assert.DoesNotContain("synthetic-microsoft-token", x.Body); },
            x => { Assert.Equal("https://api.minecraftservices.com/launcher/login", x.Url); Assert.Contains("XBL3.0 x=123;synthetic-xsts-token", x.Body); Assert.Contains("PC_LAUNCHER", x.Body); },
            x => { Assert.StartsWith("https://api.minecraftservices.com/entitlements/license?requestId=", x.Url); Assert.Equal("Bearer synthetic-minecraft-token", x.Authorization); },
            x => { Assert.Equal("https://api.minecraftservices.com/minecraft/profile", x.Url); Assert.Equal("Bearer synthetic-minecraft-token", x.Authorization); });
    }

    [Fact]
    public async Task Device_signin_prompts_without_password_then_finishes_the_entitlement_chain()
    {
        using var handler = new ScriptedHandler([Device(), .. Success()]);
        using var http = new HttpClient(handler);
        DeviceCodePrompt? shown = null;
        var result = await new MicrosoftAuthenticator(http).SignInAsync(ClientId, value => shown = value);
        Assert.Equal("TEST-CODE", shown!.UserCode);
        Assert.Equal("https://microsoft.com/devicelogin", shown.VerificationUri.AbsoluteUri);
        Assert.Equal(Uuid, result.Profile.Uuid);
        Assert.True(Guid.TryParseExact(result.Profile.Id, "N", out _));
        Assert.Contains("scope=XboxLive.signin+offline_access", handler.Requests[0].Body);
        Assert.Contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code", handler.Requests[1].Body);
        Assert.Contains("device_code=synthetic-device-secret", handler.Requests[1].Body);
        Assert.All(handler.Requests, x => Assert.DoesNotContain("password", x.Body));
    }

    [Fact]
    public async Task Cancellation_from_prompt_does_not_poll_or_issue_credentials()
    {
        using var handler = new ScriptedHandler([Device()]);
        using var http = new HttpClient(handler);
        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new MicrosoftAuthenticator(http).SignInAsync(ClientId, _ => cts.Cancel(), cts.Token));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("http://microsoft.com/devicelogin")]
    [InlineData("https://microsoft.com.evil.example/devicelogin")]
    [InlineData("https://evil.example/devicelogin")]
    [InlineData("https://user@microsoft.com/devicelogin")]
    public async Task Device_prompt_rejects_non_official_or_unsafe_verification_urls(string url)
    {
        using var handler = new ScriptedHandler([Json(new { device_code = "synthetic-device-secret", user_code = "TEST-CODE", verification_uri = url, expires_in = 900, interval = 1 })]);
        using var http = new HttpClient(handler);
        var prompted = false;
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => new MicrosoftAuthenticator(http).SignInAsync(ClientId, _ => prompted = true));
        Assert.False(prompted);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("{\"items\":[]}")]
    [InlineData("{\"items\":[{\"name\":\"unrelated_product\"}]}")]
    [InlineData("{}")]
    public async Task Missing_minecraft_entitlement_never_returns_a_game_session(string body)
    {
        var replies = Success(); replies[4] = Raw(body);
        using var handler = new ScriptedHandler(replies);
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => new MicrosoftAuthenticator(http).RefreshAsync(Profile, Credentials));
        Assert.Equal(5, handler.Requests.Count);
    }

    [Theory]
    [InlineData("ffeeddccbbaa49888776655443322110", "OtherPlayer")]
    [InlineData("not-a-uuid", "VerifiedPlayer")]
    [InlineData(Uuid, "bad player name")]
    public async Task Changed_or_malformed_profile_is_rejected(string id, string name)
    {
        var replies = Success(); replies[5] = Json(new { id, name });
        using var handler = new ScriptedHandler(replies);
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => new MicrosoftAuthenticator(http).RefreshAsync(Profile, Credentials));
    }

    [Fact]
    public async Task Xbox_user_hash_must_match_the_xsts_identity()
    {
        var replies = Success(); replies[2] = Json(new { Token = "synthetic-xsts-token", DisplayClaims = new { xui = new[] { new { uhs = "different-user" } } } });
        using var handler = new ScriptedHandler(replies);
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => new MicrosoftAuthenticator(http).RefreshAsync(Profile, Credentials));
        Assert.Equal(3, handler.Requests.Count);
    }

    [Theory]
    [InlineData(400, "{\"error\":\"invalid_grant\",\"error_description\":\"synthetic-secret-should-never-appear\"}")]
    [InlineData(302, "synthetic-secret-should-never-appear")]
    [InlineData(500, "synthetic-secret-should-never-appear")]
    [InlineData(200, "synthetic-secret-should-never-appear")]
    public async Task Service_errors_and_malformed_json_do_not_echo_response_bodies(int status, string body)
    {
        using var handler = new ScriptedHandler([Raw(body, (HttpStatusCode)status)]);
        using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<AccountAuthenticationException>(() => new MicrosoftAuthenticator(http).RefreshAsync(Profile, Credentials));
        Assert.DoesNotContain("synthetic-secret-should-never-appear", error.ToString());
        Assert.Null(error.InnerException);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Oversized_response_is_rejected_without_exposing_its_content()
    {
        using var handler = new ScriptedHandler([Raw("{\"token\":\"" + new string('x', 1_048_577) + "\"}")]);
        using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<AccountAuthenticationException>(() => new MicrosoftAuthenticator(http).RefreshAsync(Profile, Credentials));
        Assert.True(error.Message.Length < 200);
    }

    [Fact]
    public async Task Missing_rotated_refresh_token_retains_previous_refresh_token()
    {
        var replies = Success(); replies[0] = Json(new { token_type = "Bearer", access_token = "synthetic-microsoft-token", expires_in = 3600 });
        using var handler = new ScriptedHandler(replies);
        using var http = new HttpClient(handler);
        var result = await new MicrosoftAuthenticator(http).RefreshAsync(Profile, Credentials);
        Assert.Equal("synthetic-old-refresh", result.Credentials.RefreshToken);
    }

    [Fact]
    public async Task Expired_device_session_stops_before_polling_again()
    {
        using var handler = new ScriptedHandler([Device(expires: 1, interval: 2)]);
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => new MicrosoftAuthenticator(http).SignInAsync(ClientId, _ => { }));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Pending_and_slow_down_responses_are_polled_at_the_requested_minimum_intervals()
    {
        using var handler = new ScriptedHandler([Device(), Json(new { error = "authorization_pending" }, HttpStatusCode.BadRequest), Json(new { error = "slow_down" }, HttpStatusCode.BadRequest), .. Success()]);
        using var http = new HttpClient(handler);
        var result = await new MicrosoftAuthenticator(http).SignInAsync(ClientId, _ => { });
        Assert.Equal(Uuid, result.Profile.Uuid);
        Assert.True(handler.Requests[2].At - handler.Requests[1].At >= TimeSpan.FromSeconds(0.9));
        Assert.True(handler.Requests[3].At - handler.Requests[2].At >= TimeSpan.FromSeconds(5.9));
    }

    [Theory]
    [InlineData("invalid_grant")]
    [InlineData("authorization_declined")]
    [InlineData("access_denied")]
    [InlineData("expired_token")]
    [InlineData("bad_verification_code")]
    public async Task Terminal_device_errors_stop_polling(string code)
    {
        using var handler = new ScriptedHandler([Device(), Json(new { error = code }, HttpStatusCode.BadRequest)]);
        using var http = new HttpClient(handler);
        var failure = await Assert.ThrowsAsync<AccountAuthenticationException>(() => new MicrosoftAuthenticator(http).SignInAsync(ClientId, _ => { }));
        Assert.StartsWith("Microsoft 设备授权确认失败（HTTP 400", failure.Message);
        Assert.Contains(code, failure.Message);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Invalid_client_id_is_rejected_before_network_access()
    {
        using var handler = new ScriptedHandler([]);
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => new MicrosoftAuthenticator(http).SignInAsync("not-an-app-id", _ => { }));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Device_registration_failure_explains_public_client_setting_and_not_minecraft_review()
    {
        using var handler = new ScriptedHandler([Json(new
        {
            error = "invalid_client", error_codes = new[] { 70002 },
            error_description = "synthetic-private-server-text", device_code = "synthetic-device-secret",
            access_token = "synthetic-access-secret", trace_id = "synthetic-trace-id"
        }, HttpStatusCode.Unauthorized)]);
        using var http = new HttpClient(handler); var prompted = false;
        var failure = await Assert.ThrowsAsync<AccountAuthenticationException>(() => new MicrosoftAuthenticator(http).SignInAsync(ClientId, _ => prompted = true));
        Assert.StartsWith("Microsoft 设备授权失败（HTTP 401 · invalid_client · AADSTS70002）", failure.Message);
        Assert.Contains("允许公共客户端流", failure.Message);
        Assert.Contains("等待 Minecraft AppID 审核不能解决", failure.Message);
        Assert.DoesNotContain("synthetic", failure.ToString()); Assert.Null(failure.InnerException);
        Assert.False(prompted); Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("unauthorized_client", 700016, "微软未找到此应用")]
    [InlineData("invalid_client", 7000218, "允许公共客户端流")]
    [InlineData("invalid_scope", 70011, "scope")]
    [InlineData("invalid_resource", 500011, "资源配置无效")]
    [InlineData("unauthorized_client", 50194, "个人 Microsoft 账户支持")]
    [InlineData("unsupported_grant_type", 0, "协议参数")]
    [InlineData("invalid_request", 0, "协议参数")]
    [InlineData("temporarily_unavailable", 0, "稍后重试")]
    public async Task Known_device_oauth_failures_keep_actionable_safe_diagnostics(string oauth, int code, string expected)
    {
        using var handler = new ScriptedHandler([Json(new { error = oauth, error_codes = new[] { code }, error_description = "synthetic-secret-server-body" }, HttpStatusCode.BadRequest)]);
        using var http = new HttpClient(handler);
        var failure = await Assert.ThrowsAsync<AccountAuthenticationException>(() => new MicrosoftAuthenticator(http).SignInAsync(ClientId, _ => Assert.Fail("Rejected registration must not show a device code.")));
        Assert.Contains("HTTP 400", failure.Message); Assert.Contains(expected, failure.Message);
        Assert.DoesNotContain("synthetic-secret", failure.ToString()); Assert.Single(handler.Requests);
        if (code == 0) Assert.DoesNotContain("AADSTS0", failure.Message);
    }

    [Theory]
    [InlineData("{\"error\":\"synthetic-access-secret\",\"error_codes\":[123456789],\"error_description\":\"AADSTS70002 synthetic-secret\"}")]
    [InlineData("{\"error\":{\"value\":\"synthetic-secret\"},\"error_codes\":[\"70002 synthetic-secret\"]}")]
    [InlineData("{\"error\":\"invalid_client\",\"error_codes\":\"synthetic-secret\",\"trace_id\":\"synthetic-secret\"}")]
    public async Task Unexpected_oauth_diagnostics_are_never_echoed_or_parsed_from_descriptions(string body)
    {
        using var handler = new ScriptedHandler([Raw(body, HttpStatusCode.BadRequest)]);
        using var http = new HttpClient(handler);
        var failure = await Assert.ThrowsAsync<AccountAuthenticationException>(() => new MicrosoftAuthenticator(http).SignInAsync(ClientId, _ => { }));
        Assert.Contains("Microsoft 设备授权失败（HTTP 400", failure.Message);
        Assert.DoesNotContain("synthetic", failure.ToString()); Assert.DoesNotContain("123456789", failure.Message);
        Assert.DoesNotContain("AADSTS70002", failure.Message); Assert.Null(failure.InnerException);
    }

    [Fact]
    public async Task Refresh_registration_failure_reports_its_own_stage_without_leaking_the_refresh_token()
    {
        using var handler = new ScriptedHandler([Json(new { error = "invalid_client", error_codes = new[] { 7000218 }, error_description = "synthetic-old-refresh" }, HttpStatusCode.Unauthorized)]);
        using var http = new HttpClient(handler);
        var failure = await Assert.ThrowsAsync<AccountAuthenticationException>(() => new MicrosoftAuthenticator(http).RefreshAsync(Profile, Credentials));
        Assert.StartsWith("Microsoft 会话刷新失败（HTTP 401", failure.Message);
        Assert.Contains("AADSTS7000218", failure.Message); Assert.DoesNotContain("synthetic-old-refresh", failure.ToString());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Minecraft_service_rejection_is_reported_as_minecraft_stage_not_public_client_registration()
    {
        var replies = Success(); replies[3] = Json(new { error = "FORBIDDEN", error_description = "synthetic-minecraft-token" }, HttpStatusCode.Forbidden);
        using var handler = new ScriptedHandler(replies); using var http = new HttpClient(handler);
        var failure = await Assert.ThrowsAsync<AccountAuthenticationException>(() => new MicrosoftAuthenticator(http).RefreshAsync(Profile, Credentials));
        Assert.StartsWith("Minecraft 登录", failure.Message); Assert.Contains("HTTP 403", failure.Message);
        Assert.DoesNotContain("公共客户端", failure.Message); Assert.DoesNotContain("synthetic", failure.ToString());
        Assert.Equal(4, handler.Requests.Count);
    }

    private static HttpResponseMessage Device(int expires = 900, int interval = 1) => Json(new { device_code = "synthetic-device-secret", user_code = "TEST-CODE", verification_uri = "https://microsoft.com/devicelogin", expires_in = expires, interval });
    private static HttpResponseMessage[] Success() =>
    [
        Json(new { token_type = "Bearer", access_token = "synthetic-microsoft-token", refresh_token = "synthetic-new-refresh", expires_in = 3600, scope = "XboxLive.signin offline_access" }),
        Json(new { Token = "synthetic-xbox-token", DisplayClaims = new { xui = new[] { new { uhs = "123" } } } }),
        Json(new { Token = "synthetic-xsts-token", DisplayClaims = new { xui = new[] { new { uhs = "123", xid = "123456" } } } }),
        Json(new { username = "synthetic-service-user", token_type = "Bearer", access_token = "synthetic-minecraft-token", expires_in = 3600 }),
        Json(new { items = new[] { new { name = "game_minecraft" }, new { name = "product_minecraft" } } }),
        Json(new { id = Uuid, name = "VerifiedPlayer", skins = Array.Empty<object>(), capes = Array.Empty<object>() })
    ];
    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => Raw(JsonSerializer.Serialize(value), status);
    private static HttpResponseMessage Raw(string value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    private sealed record Request(string Url, string Body, string? Authorization, DateTimeOffset At);
    private sealed class ScriptedHandler(IEnumerable<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<Request> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(new(request.RequestUri!.AbsoluteUri, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct), request.Headers.Authorization?.ToString(), DateTimeOffset.UtcNow));
            return _responses.Count > 0 ? _responses.Dequeue() : throw new InvalidOperationException("Unexpected authentication request.");
        }
        protected override void Dispose(bool disposing) { if (disposing) foreach (var response in _responses) response.Dispose(); base.Dispose(disposing); }
    }
}
