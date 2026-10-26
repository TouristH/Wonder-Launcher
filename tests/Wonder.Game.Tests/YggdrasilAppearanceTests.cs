// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Net;
using System.Text;
using System.Text.Json;
using Wonder.Core;
using Wonder.Infrastructure;

namespace Wonder.Game.Tests;

public sealed class YggdrasilAppearanceTests
{
    private const string Uuid = "00112233445566778899aabbccddeeff";
    private const string OtherUuid = "ffeeddccbbaa99887766554433221100";
    private const string Server = "https://auth.example.test/api/yggdrasil/";
    private const string Skin = "https://cdn.example.test/skins/test-skin.png";
    private const string Cape = "https://cdn.example.test/capes/test-cape.png";

    [Fact]
    public async Task Selected_profile_reads_skin_model_and_active_cape_without_sending_credentials()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(Server + "sessionserver/session/minecraft/profile/" + Uuid, request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Content); Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("Cookie"));
            return Task.FromResult(Appearance());
        }));
        var account = await new YggdrasilAuthenticator(http).SelectProfileAsync(Login(), Uuid);
        Assert.Equal(Skin, account.Profile.SkinUrl); Assert.Equal("slim", account.Profile.SkinVariant);
        var cape = Assert.Single(account.Profile.Capes);
        Assert.Equal(Cape, cape.Url); Assert.Equal(cape.Id, account.Profile.ActiveCapeId); Assert.True(Guid.TryParse(cape.Id, out _));
        Assert.Equal("synthetic-access", account.Credentials.AccessToken);
    }

    [Fact]
    public async Task Refresh_updates_saved_appearance_preserving_account_identity_and_rotated_credentials()
    {
        using var http = new HttpClient(new Handler(request => Task.FromResult(request.Method == HttpMethod.Post ? Refreshed() : Appearance())));
        var refreshed = await new YggdrasilAuthenticator(http).RefreshAsync(SavedProfile(), Credentials());
        Assert.Equal("saved-id", refreshed.Profile.Id); Assert.Equal(Uuid, refreshed.Profile.Uuid);
        Assert.Equal("RenamedTest", refreshed.Profile.Username); Assert.Equal("synthetic-rotated", refreshed.Credentials.AccessToken);
        Assert.Equal(Skin, refreshed.Profile.SkinUrl); Assert.Equal("slim", refreshed.Profile.SkinVariant);
        Assert.Equal(Cape, Assert.Single(refreshed.Profile.Capes).Url);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Successful_profile_without_textures_clears_stale_saved_appearance(bool emptyProperty)
    {
        using var http = new HttpClient(new Handler(request => Task.FromResult(request.Method == HttpMethod.Post ? Refreshed() :
            emptyProperty ? WithTextures(new { profileId = Uuid, profileName = "RenamedTest", textures = new { } }) : Json(new { id = Uuid, name = "RenamedTest" }))));
        var result = await new YggdrasilAuthenticator(http).RefreshAsync(SavedProfile(), Credentials());
        Assert.Null(result.Profile.SkinUrl); Assert.Equal("classic", result.Profile.SkinVariant);
        Assert.Empty(result.Profile.Capes); Assert.Null(result.Profile.ActiveCapeId);
    }

    [Theory]
    [InlineData(204)]
    [InlineData(302)]
    [InlineData(403)]
    [InlineData(500)]
    public async Task Unavailable_appearance_preserves_successful_refresh_and_does_not_follow_redirects(int status)
    {
        var reads = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.Method == HttpMethod.Post) return Task.FromResult(Refreshed());
            reads++;
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("synthetic-secret-never-display") };
            response.Headers.Location = new("https://elsewhere.example.test/private"); return Task.FromResult(response);
        }));
        var result = await new YggdrasilAuthenticator(http).RefreshAsync(SavedProfile(), Credentials());
        Assert.Equal(1, reads); Assert.Equal("synthetic-rotated", result.Credentials.AccessToken);
        Assert.Equal(SavedProfile().SkinUrl, result.Profile.SkinUrl); Assert.Equal(SavedProfile().ActiveCapeId, result.Profile.ActiveCapeId);
    }

    [Theory]
    [InlineData("outer-id")]
    [InlineData("inner-id")]
    [InlineData("invalid-base64")]
    [InlineData("duplicate-textures")]
    [InlineData("invalid-json")]
    [InlineData("oversized-property")]
    [InlineData("oversized-body")]
    [InlineData("oversized-stream")]
    public async Task Invalid_or_substituted_texture_data_does_not_change_identity_or_saved_appearance(string scenario)
    {
        var reads = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.Method == HttpMethod.Post) return Task.FromResult(Refreshed());
            reads++;
            var value = Encoded(new { profileId = Uuid, profileName = "RenamedTest", textures = new { SKIN = new { url = Skin } } });
            return Task.FromResult(scenario switch
            {
                "outer-id" => WithTextures(new { profileId = Uuid, textures = new { SKIN = new { url = Skin } } }, OtherUuid),
                "inner-id" => WithTextures(new { profileId = OtherUuid, textures = new { SKIN = new { url = Skin } } }),
                "invalid-base64" => WithProperty("not-base64!"),
                "duplicate-textures" => Json(new { id = Uuid, name = "RenamedTest", properties = new[] { new { name = "textures", value }, new { name = "textures", value } } }),
                "invalid-json" => WithProperty(Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"textures\":{},\"textures\":{}}"))),
                "oversized-property" => WithProperty(new string('A', 100_000)),
                "oversized-body" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string(' ', 1024 * 1024 + 1)) },
                "oversized-stream" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnseekableStream(new byte[1024 * 1024 + 1])) },
                _ => throw new InvalidOperationException()
            });
        }));
        var result = await new YggdrasilAuthenticator(http).RefreshAsync(SavedProfile(), Credentials());
        Assert.Equal(1, reads); Assert.Equal(Uuid, result.Profile.Uuid); Assert.Equal("synthetic-rotated", result.Credentials.AccessToken);
        Assert.Equal(SavedProfile().SkinUrl, result.Profile.SkinUrl); Assert.Equal(SavedProfile().Capes, result.Profile.Capes);
    }

    [Theory]
    [InlineData("http://cdn.example.test/skin.png")]
    [InlineData("https://user:password@cdn.example.test/skin.png")]
    [InlineData("https://cdn.example.test/skin.png?token=secret")]
    [InlineData("https://cdn.example.test/skin.png#secret")]
    [InlineData("file:///c:/skin.png")]
    [InlineData("https://cdn.example.test/skin\n.png")]
    public async Task Unsafe_texture_url_does_not_fail_successful_login_or_publish_untrusted_appearance(string url)
    {
        var reads = 0;
        using var http = new HttpClient(new Handler(_ => { reads++; return Task.FromResult(WithTextures(new { profileId = Uuid, textures = new { SKIN = new { url } } })); }));
        var result = await new YggdrasilAuthenticator(http).SelectProfileAsync(Login(), Uuid);
        Assert.Equal(1, reads); Assert.Null(result.Profile.SkinUrl); Assert.Empty(result.Profile.Capes);
        Assert.Equal("synthetic-access", result.Credentials.AccessToken);
    }

    [Fact]
    public async Task Cancellation_during_profile_read_is_not_converted_to_success()
    {
        using var cts = new CancellationTokenSource();
        using var http = new HttpClient(new Handler(_ => { cts.Cancel(); return Task.FromCanceled<HttpResponseMessage>(cts.Token); }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new YggdrasilAuthenticator(http).SelectProfileAsync(Login(), Uuid, cts.Token));
    }

    private static ThirdPartyLogin Login() => new(Server, [new(Uuid, "WonderTest")], Uuid, Credentials());
    private static AccountCredentials Credentials() => new() { AccessToken = "synthetic-access", ClientToken = "synthetic-client" };
    private static AccountProfile SavedProfile() => new("saved-id", AccountKind.Yggdrasil, "WonderTest", Uuid, Server)
    {
        SkinUrl = "https://cdn.example.test/skins/old.png", SkinVariant = "slim",
        Capes = [new("7d8a6247-cf51-4d93-b4a8-bddac9356362", "Old cape", "https://cdn.example.test/capes/old.png")], ActiveCapeId = "7d8a6247-cf51-4d93-b4a8-bddac9356362"
    };
    private static HttpResponseMessage Refreshed() => Json(new { accessToken = "synthetic-rotated", clientToken = "synthetic-client", selectedProfile = new { id = Uuid, name = "RenamedTest" } });
    private static HttpResponseMessage Appearance() => WithTextures(new { timestamp = 1720000000000L, profileId = Uuid, profileName = "RenamedTest", textures = new { SKIN = new { url = Skin, metadata = new { model = "slim" } }, CAPE = new { url = Cape } } });
    private static HttpResponseMessage WithTextures(object textures, string id = Uuid) => WithProperty(Encoded(textures), id);
    private static string Encoded(object value) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value));
    private static HttpResponseMessage WithProperty(string value, string id = Uuid) => Json(new { id, name = "RenamedTest", properties = new[] { new { name = "textures", value } } });
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
    private sealed class UnseekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
