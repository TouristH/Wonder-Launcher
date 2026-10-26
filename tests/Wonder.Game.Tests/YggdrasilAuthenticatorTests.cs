// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Net;
using System.Text;
using System.Text.Json;
using Wonder.Application;
using Wonder.Core;
using Wonder.Infrastructure;

namespace Wonder.Game.Tests;

public sealed class YggdrasilAuthenticatorTests
{
    private const string Uuid = "00112233445566778899aabbccddeeff";

    [Theory]
    [InlineData("http://example.test/api/")]
    [InlineData("https://name:password@example.test/")]
    [InlineData("https://example.test/?key=private")]
    [InlineData("https://example.test/#fragment")]
    [InlineData("file:///c:/secret")]
    public void Insecure_or_credential_bearing_server_is_rejected_before_request(string server)
    {
        Assert.Throws<AccountAuthenticationException>(() => YggdrasilAuthenticator.NormalizeServerUrl(server));
    }

    [Fact]
    public async Task Multi_profile_selection_binds_requested_profile_and_refresh_preserves_identity()
    {
        var step = 0; string? clientToken = null;
        using var http = new HttpClient(new Handler(async request =>
        {
            if (request.Method == HttpMethod.Get) return new(HttpStatusCode.NoContent);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()); var root = json.RootElement;
            Assert.Equal("skin.example.test", request.RequestUri!.Host);
            if (step++ == 0)
            {
                Assert.Equal("/api/yggdrasil/authserver/authenticate", request.RequestUri.AbsolutePath);
                Assert.Equal("synthetic-password", root.GetProperty("password").GetString());
                clientToken = root.GetProperty("clientToken").GetString();
                return Json(new { accessToken = "synthetic-access", clientToken, availableProfiles = new[] { new { id = Uuid, name = "WonderTest" }, new { id = "ffeeddccbbaa99887766554433221100", name = "Other" } } });
            }
            Assert.Equal("/api/yggdrasil/authserver/refresh", request.RequestUri.AbsolutePath);
            Assert.False(root.TryGetProperty("password", out _));
            if (step == 2) Assert.Equal(Uuid, root.GetProperty("selectedProfile").GetProperty("id").GetString());
            else Assert.False(root.TryGetProperty("selectedProfile", out _));
            return Json(new { accessToken = "synthetic-rotated-" + step, clientToken, selectedProfile = new { id = Uuid, name = "RenamedTest" } });
        }));
        var auth = new YggdrasilAuthenticator(http);
        var login = await auth.SignInAsync("https://skin.example.test/api/yggdrasil", "user@example.test", "synthetic-password");
        Assert.Equal(2, login.Profiles.Count);
        var account = await auth.SelectProfileAsync(login, Uuid);
        Assert.Equal(AccountKind.Yggdrasil, account.Profile.Kind);
        Assert.Equal("https://skin.example.test/api/yggdrasil/", account.Profile.ServerUrl);
        var refreshed = await auth.RefreshAsync(account.Profile, account.Credentials);
        Assert.Equal(account.Profile.Id, refreshed.Profile.Id);
        Assert.Equal("RenamedTest", refreshed.Profile.Username);
        Assert.Equal("synthetic-rotated-3", refreshed.Credentials.AccessToken);
        Assert.True(refreshed.Credentials.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Refresh_rejects_profile_substitution()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Json(new { accessToken = "synthetic-new", clientToken = "synthetic-client", selectedProfile = new { id = "ffeeddccbbaa99887766554433221100", name = "Other" } }))));
        var auth = new YggdrasilAuthenticator(http);
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => auth.RefreshAsync(Profile(), Credentials()));
    }

    [Theory]
    [InlineData(302)]
    [InlineData(403)]
    [InlineData(500)]
    public async Task Redirect_and_service_errors_do_not_echo_upstream_secrets(int status)
    {
        var count = 0;
        using var http = new HttpClient(new Handler(_ => { count++; return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("synthetic-password synthetic-access") }); }));
        var error = await Assert.ThrowsAsync<AccountAuthenticationException>(() => new YggdrasilAuthenticator(http).SignInAsync("https://skin.example.test/", "test", "synthetic-password"));
        Assert.DoesNotContain("synthetic", error.ToString()); Assert.Equal(1, count);
    }

    [Fact]
    public async Task Invalidating_only_sends_the_given_third_party_session()
    {
        using var http = new HttpClient(new Handler(async request =>
        {
            Assert.Equal("/authserver/invalidate", request.RequestUri!.AbsolutePath);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("synthetic-access", json.RootElement.GetProperty("accessToken").GetString());
            Assert.Equal(2, json.RootElement.EnumerateObject().Count());
            return new(HttpStatusCode.NoContent);
        }));
        await new YggdrasilAuthenticator(http).InvalidateAsync(Profile(), Credentials());
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => new YggdrasilAuthenticator(http).InvalidateAsync(Profile() with { Kind = AccountKind.Microsoft }, Credentials()));
    }

    private static AccountProfile Profile() => new("synthetic-id", AccountKind.Yggdrasil, "WonderTest", Uuid, "https://skin.example.test/");
    private static AccountCredentials Credentials() => new() { AccessToken = "synthetic-access", ClientToken = "synthetic-client" };
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
