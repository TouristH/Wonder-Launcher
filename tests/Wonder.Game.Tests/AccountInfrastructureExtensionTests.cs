// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wonder.Application;
using Wonder.Core;
using Wonder.Infrastructure;

namespace Wonder.Game.Tests;

public sealed class AccountInfrastructureExtensionTests
{
    private const string Uuid = "0123456789abcdef0123456789abcdef";
    private const string CapeId = "aabbccdd-1234-4567-8901-aabbccddeeff";
    private static AccountProfile Profile => new("account", AccountKind.Microsoft, "PlayerOne", Uuid, ClientId: WonderMicrosoftApplication.ClientId)
    { Capes = [new(CapeId, "Test cape", "https://textures.minecraft.net/texture/cape")] };
    private static AccountCredentials Credentials => new() { AccessToken = "synthetic-game-token", RefreshToken = "synthetic-refresh-never-send", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };

    [Fact]
    public async Task Discovery_follows_https_locator_and_returns_actual_api_without_credentials()
    {
        var redirect = new HttpResponseMessage(HttpStatusCode.OK);
        redirect.Headers.Add("X-Authlib-Injector-API-Location", "https://auth.example.test/api/yggdrasil/");
        using var handler = new Handler([redirect, Json(new { meta = new { serverName = "Example skin site", links = new { homepage = "https://example.test/", register = "https://example.test/register" } } })]);
        using var http = new HttpClient(handler);
        var server = await new AuthenticationServerDirectory(http).DiscoverAsync("authlib-injector:yggdrasil-server:https%3A%2F%2Fexample.test");
        Assert.Equal("https://auth.example.test/api/yggdrasil/", server.ApiUrl);
        Assert.Equal("Example skin site", server.Name); Assert.Equal(32, server.Id.Length);
        Assert.Equal("https://example.test/register", server.RegistrationUrl);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => { Assert.Null(r.Authorization); Assert.Equal("GET", r.Method); Assert.Empty(r.Body); });
    }

    [Theory]
    [InlineData("http://evil.example/api/")]
    [InlineData("https://user:password@evil.example/api/")]
    [InlineData("https://evil.example/api/?secret=bad")]
    public async Task Discovery_rejects_unsafe_locator_without_following_it(string location)
    {
        var redirect = new HttpResponseMessage(HttpStatusCode.Found); redirect.Headers.Location = new(location);
        using var handler = new Handler([redirect]); using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => new AuthenticationServerDirectory(http).DiscoverAsync("https://skin.example.test/"));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("{\"meta\":{\"serverName\":\"A\",\"serverName\":\"B\"}}")]
    [InlineData("{\"meta\":{\"serverName\":\"bad\\nname\"}}")]
    [InlineData("{\"meta\":{\"serverName\":null}}")]
    [InlineData("not-json-secret")]
    public async Task Discovery_rejects_malformed_data_without_echoing_response(string content)
    {
        using var handler = new Handler([Raw(content)]); using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<AccountAuthenticationException>(() => new AuthenticationServerDirectory(http).DiscoverAsync("skin.example.test"));
        Assert.DoesNotContain(content, error.ToString());
    }

    [Fact]
    public async Task Discovery_bounds_body_and_ignores_insecure_links()
    {
        using var handler = new Handler([Json(new { meta = new { serverName = "Example", links = new { homepage = "http://example.test", register = "https://user:pass@example.test" } } }), Raw(new string('x', 262145))]);
        using var http = new HttpClient(handler); var provider = new AuthenticationServerDirectory(http);
        var server = await provider.DiscoverAsync("skin.example.test"); Assert.Null(server.Homepage); Assert.Null(server.RegistrationUrl);
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => provider.DiscoverAsync("skin.example.test"));
    }

    [Fact]
    public async Task Adapter_verifies_download_reuses_cache_and_repairs_tampering()
    {
        using var directory = new TemporaryDirectory(); var jar = Encoding.UTF8.GetBytes("synthetic-pinned-jar"); var hash = Convert.ToHexString(SHA256.HashData(jar));
        using var handler = new Handler([Bytes(jar), Bytes(jar)]); using var http = new HttpClient(handler);
        var provider = new AuthenticationAdapterProvider(directory.Path, http, hash);
        var first = await provider.EnsureAsync(); Assert.Equal(jar, File.ReadAllBytes(first.Path)); Assert.Equal(hash, first.Sha256);
        Assert.Equal(first, await provider.EnsureAsync()); Assert.Single(handler.Requests);
        File.WriteAllText(first.Path, "tampered"); Assert.Equal(first, await provider.EnsureAsync());
        Assert.Equal(jar, File.ReadAllBytes(first.Path)); Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => { Assert.Equal("https://authlib-injector.yushi.moe/artifact/56/authlib-injector-1.2.8.jar", r.Url); Assert.Null(r.Authorization); });
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(302)]
    public async Task Adapter_hash_mismatch_and_redirect_leave_previous_file_intact(int status)
    {
        using var directory = new TemporaryDirectory(); var cache = System.IO.Path.Combine(directory.Path, "authlib-injector-1.2.8.jar");
        File.WriteAllText(cache, "existing-invalid-file");
        using var handler = new Handler([new((HttpStatusCode)status) { Content = new StringContent("untrusted-download") }]); using var http = new HttpClient(handler);
        var provider = new AuthenticationAdapterProvider(directory.Path, http);
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => provider.EnsureAsync());
        Assert.Equal("existing-invalid-file", File.ReadAllText(cache)); Assert.Single(handler.Requests); Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task Adapter_cancelled_body_does_not_write_cache_and_gate_is_released()
    {
        using var directory = new TemporaryDirectory(); var data = Encoding.UTF8.GetBytes("synthetic-pinned-jar");
        using var waiting = new BlockingStream(); using var handler = new Handler([new(HttpStatusCode.OK) { Content = new StreamContent(waiting) }, Bytes(data)]);
        using var http = new HttpClient(handler); var provider = new AuthenticationAdapterProvider(directory.Path, http, Convert.ToHexString(SHA256.HashData(data)));
        using var cts = new CancellationTokenSource(); var task = provider.EnsureAsync(cts.Token); await waiting.Started.Task; cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task); Assert.Empty(Directory.GetFiles(directory.Path));
        Assert.True(File.Exists((await provider.EnsureAsync()).Path));
    }

    [Fact]
    public async Task Skin_upload_uses_multipart_then_verifies_profile_and_only_sends_game_token_to_official_host()
    {
        using var handler = new Handler([new(HttpStatusCode.NoContent), Appearance()]); using var http = new HttpClient(handler);
        var result = await new MicrosoftAuthenticator(http).UploadSkinAsync(Profile, Credentials, SkinPng(), "slim");
        Assert.Equal("slim", result.SkinVariant); Assert.Equal("https://textures.minecraft.net/texture/skin", result.SkinUrl);
        Assert.Equal(CapeId, result.ActiveCapeId); Assert.Single(result.Capes); Assert.NotNull(result.LastValidatedAt);
        Assert.Equal("POST", handler.Requests[0].Method); Assert.EndsWith("/minecraft/profile/skins", handler.Requests[0].Url);
        Assert.Contains("name=variant", handler.Requests[0].Body); Assert.Contains("slim", handler.Requests[0].Body); Assert.Contains("filename=skin.png", handler.Requests[0].Body);
        Assert.Equal("GET", handler.Requests[1].Method); Assert.EndsWith("/minecraft/profile", handler.Requests[1].Url);
        Assert.All(handler.Requests, r => { Assert.StartsWith("https://api.minecraftservices.com/", r.Url); Assert.Equal("Bearer synthetic-game-token", r.Authorization); Assert.DoesNotContain(Credentials.RefreshToken, r.Body); });
    }

    [Theory]
    [InlineData("reset", "DELETE", "skins/active")]
    [InlineData("cape", "PUT", "capes/active")]
    [InlineData("hide", "DELETE", "capes/active")]
    public async Task Appearance_actions_use_expected_methods_and_payloads(string action, string method, string path)
    {
        using var handler = new Handler([new(HttpStatusCode.NoContent), Appearance()]); using var http = new HttpClient(handler); var client = new MicrosoftAuthenticator(http);
        _ = action == "reset" ? await client.ResetSkinAsync(Profile, Credentials) : await client.SetCapeAsync(Profile, Credentials, action == "cape" ? CapeId : null);
        Assert.Equal(method, handler.Requests[0].Method); Assert.Equal("https://api.minecraftservices.com/minecraft/profile/" + path, handler.Requests[0].Url);
        if (action == "cape") Assert.Equal(CapeId, JsonDocument.Parse(handler.Requests[0].Body).RootElement.GetProperty("capeId").GetString());
        else Assert.Empty(handler.Requests[0].Body);
    }

    [Fact]
    public async Task Appearance_refuses_unowned_capes_expired_sessions_and_non_microsoft_accounts_before_network()
    {
        using var handler = new Handler([]); using var http = new HttpClient(handler); var client = new MicrosoftAuthenticator(http);
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => client.SetCapeAsync(Profile, Credentials, Guid.NewGuid().ToString()));
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => client.ResetSkinAsync(Profile, new() { AccessToken = "expired" }));
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => client.ResetSkinAsync(Profile with { Kind = AccountKind.Yggdrasil, ServerUrl = "https://skin.example.test/" }, Credentials));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(302)]
    [InlineData(401)]
    [InlineData(500)]
    public async Task Appearance_failure_does_not_follow_redirect_or_echo_upstream_data(int status)
    {
        using var handler = new Handler([Raw("synthetic-upstream-secret", (HttpStatusCode)status)]); using var http = new HttpClient(handler);
        var ex = await Assert.ThrowsAsync<AccountAuthenticationException>(() => new MicrosoftAuthenticator(http).ResetSkinAsync(Profile, Credentials));
        Assert.DoesNotContain("synthetic-upstream-secret", ex.ToString()); Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Appearance_readback_rejects_changed_identity()
    {
        using var handler = new Handler([new(HttpStatusCode.NoContent), Json(new { id = "ffeeddccbbaa00998877665544332211", name = "AnotherUser" })]); using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => new MicrosoftAuthenticator(http).ResetSkinAsync(Profile, Credentials));
    }

    [Theory]
    [InlineData("https://evil.example/texture/skin")]
    [InlineData("https://textures.minecraft.net.evil.example/texture/skin")]
    [InlineData("https://user:secret@textures.minecraft.net/texture/skin")]
    [InlineData("https://textures.minecraft.net:8443/texture/skin")]
    [InlineData("https://textures.minecraft.net/texture/skin?token=bad")]
    public async Task Texture_urls_are_restricted_before_request(string url)
    {
        using var handler = new Handler([]); using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => new AccountTextureClient(http).DownloadAsync(url)); Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Texture_download_upgrades_legacy_http_and_never_attaches_account_credentials()
    {
        var png = SkinPng(); using var handler = new Handler([Bytes(png)]); using var http = new HttpClient(handler);
        Assert.Equal(png, await new AccountTextureClient(http).DownloadAsync("http://textures.minecraft.net/texture/skin"));
        Assert.Equal("https://textures.minecraft.net/texture/skin", handler.Requests.Single().Url); Assert.Null(handler.Requests.Single().Authorization);
    }

    [Fact]
    public async Task Texture_cancel_applies_while_reading_body()
    {
        using var waiting = new BlockingStream(); using var handler = new Handler([new(HttpStatusCode.OK) { Content = new StreamContent(waiting) }]); using var http = new HttpClient(handler);
        using var cts = new CancellationTokenSource(); var task = new AccountTextureClient(http).DownloadAsync("https://textures.minecraft.net/texture/skin", cts.Token);
        await waiting.Started.Task; cts.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public void Skin_validation_accepts_both_minecraft_sizes_and_rejects_corruption_truncation_and_wrong_dimensions()
    {
        MinecraftSkinImage.Validate(SkinPng()); MinecraftSkinImage.Validate(SkinPng(height: 32));
        var corrupted = SkinPng(); corrupted[50] ^= 42;
        Assert.Throws<AccountAuthenticationException>(() => MinecraftSkinImage.Validate(corrupted));
        Assert.Throws<AccountAuthenticationException>(() => MinecraftSkinImage.Validate(SkinPng()[..^1]));
        Assert.Throws<AccountAuthenticationException>(() => MinecraftSkinImage.Validate(SkinPng(width: 128)));
        Assert.Throws<AccountAuthenticationException>(() => MinecraftSkinImage.Validate(new byte[1024 * 1024 + 1]));
    }

    private static HttpResponseMessage Appearance() => Json(new { id = Uuid, name = "PlayerOne", skins = new[] { new { state = "ACTIVE", variant = "SLIM", url = "http://textures.minecraft.net/texture/skin" } }, capes = new[] { new { id = CapeId, state = "ACTIVE", alias = "Test cape", url = "https://textures.minecraft.net/texture/cape" } } });
    private static HttpResponseMessage Json(object value) => Raw(JsonSerializer.Serialize(value));
    private static HttpResponseMessage Raw(string text, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private sealed record Request(string Method, string Url, string Body, string? Authorization);
    private sealed class Handler(IEnumerable<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<Request> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(new(request.Method.Method, request.RequestUri!.AbsoluteUri, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct), request.Headers.Authorization?.ToString()));
            return _responses.Count > 0 ? _responses.Dequeue() : throw new InvalidOperationException("Unexpected network request.");
        }
        protected override void Dispose(bool disposing) { if (disposing) foreach (var response in _responses) response.Dispose(); base.Dispose(disposing); }
    }
    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Wonder-account-extension-" + Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
    private sealed class BlockingStream : Stream
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) { Started.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0; }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException(); public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private static byte[] SkinPng(int width = 64, int height = 64)
    {
        using var png = new MemoryStream(); png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height); header[8] = 8; header[9] = 6;
        Chunk("IHDR", header); using var compressed = new MemoryStream(); using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true)) zlib.Write(new byte[(width * 4 + 1) * height]);
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", []); return png.ToArray();
        void Chunk(string type, byte[] data)
        {
            var name = Encoding.ASCII.GetBytes(type); Span<byte> value = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(value, data.Length); png.Write(value); png.Write(name); png.Write(data);
            uint crc = uint.MaxValue; foreach (var octet in name.Concat(data)) { crc ^= octet; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0); }
            BinaryPrimitives.WriteUInt32BigEndian(value, ~crc); png.Write(value);
        }
    }
}
