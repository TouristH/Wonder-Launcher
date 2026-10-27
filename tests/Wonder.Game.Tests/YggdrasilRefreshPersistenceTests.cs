// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Net;
using System.Text;
using System.Text.Json;
using Wonder.Application;
using Wonder.Core;
using Wonder.Infrastructure;

namespace Wonder.Game.Tests;

public sealed class YggdrasilRefreshPersistenceTests
{
    private const string Uuid = "00112233445566778899aabbccddeeff";
    private const string OtherUuid = "ffeeddccbbaa99887766554433221100";
    private const string Server = "https://auth.example.test/api/yggdrasil/";
    private const string Original = "synthetic-original";
    private const string Rotated = "synthetic-rotated";
    private const string Client = "synthetic-client";

    [Fact]
    public async Task Appearance_cancellation_preserves_committed_rotation_for_a_new_service()
    {
        var store = Store();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshes = 0; var savedBeforeAppearance = false;
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                var token = await AccessToken(request);
                if (token != (refreshes == 0 ? Original : Rotated)) return new(HttpStatusCode.Forbidden);
                return Response(++refreshes == 1 ? Rotated : "synthetic-next");
            }
            if (refreshes == 1)
            {
                savedBeforeAppearance = SavedToken(store) == Rotated;
                entered.SetResult(); await Task.Delay(Timeout.Infinite, ct);
            }
            return new(HttpStatusCode.NoContent);
        }));
        var service = Service(store, http);
        using var cancellation = new CancellationTokenSource();
        var operation = service.GetSelectedSessionAsync(cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);

        Assert.Equal(Rotated, SavedToken(store));
        Assert.True(savedBeforeAppearance);
        Assert.Equal("https://cdn.example.test/old.png", Assert.Single(service.Accounts).SkinUrl);
        Assert.False(service.IsBusy);
        var restarted = Service(store, http);
        var session = await restarted.GetSelectedSessionAsync();
        Assert.Equal("synthetic-next", session.AccessToken);
        Assert.Equal(AccountKind.Yggdrasil, session.Kind);
        Assert.Equal(2, refreshes);
    }

    [Fact]
    public async Task Cancellation_after_complete_response_still_commits_validated_credentials()
    {
        var store = Store(); var appearances = 0;
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.Method == HttpMethod.Get) { appearances++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); }
            return Task.FromResult(Response(Rotated, afterBody: cancellation.Cancel));
        }));
        var service = Service(store, http);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetSelectedSessionAsync(cancellation.Token));
        Assert.Equal(Rotated, SavedToken(store));
        Assert.Equal(0, appearances);
    }

    [Fact]
    public async Task Cancellation_before_refresh_response_does_not_replace_credentials()
    {
        var store = Store(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new Handler(async (_, ct) =>
        {
            entered.SetResult(); await Task.Delay(Timeout.Infinite, ct);
            return Response(Rotated);
        }));
        var service = Service(store, http);
        var operation = service.GetSelectedSessionAsync(cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(Original, SavedToken(store)); Assert.Equal(0, store.Saves);
    }

    [Theory]
    [InlineData(OtherUuid, Client)]
    [InlineData(Uuid, "synthetic-other-client")]
    public async Task Cancelled_response_with_changed_identity_or_client_never_commits(string uuid, string client)
    {
        var store = Store();
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Response(Rotated, uuid, client, cancellation.Cancel))));
        var service = Service(store, http);
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => service.GetSelectedSessionAsync(cancellation.Token));
        Assert.Equal(Original, SavedToken(store)); Assert.Equal(0, store.Saves);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Failed_rotation_save_can_be_retried_without_reusing_revoked_credentials(bool reloadFirst)
    {
        var store = Store(); store.FailSave = true;
        var refreshes = 0; var appearances = 0;
        using var http = new HttpClient(new Handler(async (request, _) =>
        {
            if (request.Method == HttpMethod.Get) { appearances++; return new(HttpStatusCode.NoContent); }
            var token = await AccessToken(request);
            if (token != (refreshes == 0 ? Original : Rotated)) return new(HttpStatusCode.Forbidden);
            return Response(++refreshes == 1 ? Rotated : "synthetic-next");
        }));
        var service = Service(store, http);
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => service.GetSelectedSessionAsync());
        Assert.Equal(Original, SavedToken(store));
        Assert.Contains("不要退出", service.Error);
        Assert.Contains("重试", service.Error);
        Assert.DoesNotContain("synthetic", service.Error);
        Assert.Equal(0, appearances);

        service.Reload();
        await service.RefreshAsync("saved-id");
        Assert.NotNull(service.Error); Assert.Equal(1, refreshes);
        store.FailSave = false;
        if (reloadFirst)
        {
            service.Reload();
            Assert.Null(service.Error); Assert.Equal(Rotated, SavedToken(store));
            service = Service(store, http);
        }
        var session = await service.GetSelectedSessionAsync();
        Assert.Equal("synthetic-next", session.AccessToken);
        Assert.Equal("synthetic-next", SavedToken(store));
        Assert.Equal(2, refreshes);
    }

    [Fact]
    public async Task New_login_cancelled_during_appearance_does_not_add_account()
    {
        var store = new AccountServiceTests.Store();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                return Response(Rotated, client: json.RootElement.GetProperty("clientToken").GetString()!);
            }
            entered.SetResult(); await Task.Delay(Timeout.Infinite, ct);
            return new(HttpStatusCode.NoContent);
        }));
        var service = Service(store, http);
        var operation = service.SignInThirdPartyAsync(Server, "synthetic-user", "synthetic-password");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); service.CancelLogin(); await operation;
        Assert.Empty(service.Accounts); Assert.Empty(store.Vault.Accounts); Assert.Equal(0, store.Saves);
    }

    private static AccountServiceTests.Store Store() => new()
    {
        Vault = new()
        {
            Accounts = [new(new("saved-id", AccountKind.Yggdrasil, "WonderTest", Uuid, Server)
                { SkinUrl = "https://cdn.example.test/old.png" },
                new() { AccessToken = Original, ClientToken = Client, ExpiresAt = DateTimeOffset.MaxValue })],
            SelectedAccountId = "saved-id"
        }
    };
    private static AccountService Service(AccountServiceTests.Store store, HttpClient http) => new(store, new AccountServiceTests.Microsoft(), new YggdrasilAuthenticator(http));
    private static string SavedToken(AccountServiceTests.Store store) => Assert.Single(store.Vault.Accounts).Credentials.AccessToken;
    private static async Task<string> AccessToken(HttpRequestMessage request)
    {
        using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        return json.RootElement.GetProperty("accessToken").GetString()!;
    }
    private static HttpResponseMessage Response(string token, string uuid = Uuid, string client = Client, Action? afterBody = null)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { accessToken = token, clientToken = client, selectedProfile = new { id = uuid, name = "WonderTest" } });
        return new(HttpStatusCode.OK) { Content = afterBody is null ? new ByteArrayContent(bytes) : new StreamContent(new CancelAfterBody(bytes, afterBody)) };
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
    private sealed class CancelAfterBody(byte[] bytes, Action completed) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await base.ReadAsync(buffer, cancellationToken);
            if (count == 0) completed();
            return count;
        }
    }
}
