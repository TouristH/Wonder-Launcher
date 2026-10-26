// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Application;
using Wonder.Core;

namespace Wonder.Game.Tests;

public sealed class AccountServiceTests
{
    [Fact]
    public async Task Offline_selection_survives_reload_and_removing_last_requires_an_account()
    {
        var store = new Store(); var service = new AccountService(store, new Microsoft(), new Yggdrasil());
        service.AddOffline("Player"); var first = Assert.Single(service.Accounts);
        service.AddOffline("Wonder_Player"); service.Select(first.Id);
        var loaded = new AccountService(store, new Microsoft(), new Yggdrasil());
        var session = await loaded.GetSelectedSessionAsync();
        Assert.Equal("Player", session.Username); Assert.Equal("a01e3843e5213998958af459800e4d11", session.Uuid);
        await loaded.RemoveAsync(first.Id);
        Assert.Equal("Wonder_Player", loaded.SelectedAccount!.Username);
        await loaded.RemoveAsync(loaded.SelectedAccount.Id);
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => loaded.GetSelectedSessionAsync());
    }

    [Fact]
    public void Invalid_usernames_duplicates_and_failed_writes_preserve_current_state()
    {
        var store = new Store(); var service = new AccountService(store, new Microsoft(), new Yggdrasil());
        service.AddOffline("Player"); service.AddOffline("Player"); Assert.Single(service.Accounts);
        service.AddOffline("a\nsecret"); Assert.Single(service.Accounts); Assert.NotNull(service.Error);
        store.FailSave = true; service.AddOffline("Different");
        Assert.Single(service.Accounts); Assert.Equal("Player", service.SelectedAccount!.Username);
    }

    [Fact]
    public void Corrupt_store_does_not_allow_writes()
    {
        var store = new Store { FailLoad = true }; var service = new AccountService(store, new Microsoft(), new Yggdrasil());
        service.AddOffline("Player"); Assert.True(service.IsReadBlocked); Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task Online_refresh_failure_never_returns_offline_identity()
    {
        var ms = new Microsoft(); var service = new AccountService(new Store(), ms, new Yggdrasil());
        await service.SignInMicrosoftAsync();
        ms.FailRefresh = true;
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => service.GetSelectedSessionAsync());
        Assert.Equal(AccountKind.Microsoft, service.SelectedAccount!.Kind);
    }

    [Fact]
    public async Task Cancelled_login_does_not_save_even_if_provider_returns_success()
    {
        var ms = new Microsoft { Wait = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var store = new Store(); var service = new AccountService(store, ms, new Yggdrasil());
        var task = service.SignInMicrosoftAsync();
        service.Cancel(); ms.Wait.SetResult(); await task;
        Assert.Empty(service.Accounts); Assert.Equal(0, store.Saves); Assert.False(service.IsBusy);
    }

    [Fact]
    public async Task Cancel_between_response_and_profile_publication_does_not_restore_pending_credentials()
    {
        using var profiles = new GatedProfiles();
        var provider = new Yggdrasil { Login = new("https://skin.example/", profiles, null,
            new() { AccessToken = "synthetic-access", ClientToken = "synthetic-client" }) };
        var service = new AccountService(new Store(), new Microsoft(), provider);
        var login = Task.Run(() => service.SignInThirdPartyAsync("https://skin.example/", "user", "synthetic-password"));
        try { await profiles.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); service.CancelLogin(); }
        finally { profiles.Release.Set(); }
        await login;
        Assert.Empty(service.PendingProfiles); Assert.Empty(service.Accounts);
    }

    [Fact]
    public async Task Third_party_injector_configuration_never_enters_microsoft_session()
    {
        var service = new AccountService(new Store(), new Microsoft(), new Yggdrasil());
        service.ConfigureInjector(Path.Combine(Path.GetTempPath(), "synthetic.jar"), new string('A', 64));
        await service.SignInMicrosoftAsync();
        var session = await service.GetSelectedSessionAsync();
        Assert.Null(session.AuthlibInjectorPath); Assert.Null(session.AuthlibInjectorSha256); Assert.Null(session.ServerUrl);
    }

    [Fact]
    public async Task Refresh_identity_change_is_rejected_without_overwriting_account()
    {
        var ms = new Microsoft(); var service = new AccountService(new Store(), ms, new Yggdrasil());
        await service.SignInMicrosoftAsync(); ms.ChangeIdentity = true;
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => service.GetSelectedSessionAsync());
        Assert.Equal("0123456789abcdef0123456789abcdef", service.SelectedAccount!.Uuid);
    }

    internal sealed class Store : IAccountStore
    {
        public AccountVault Vault = new(); public bool FailSave, FailLoad; public int Saves;
        public AccountVault Load() => FailLoad ? throw new InvalidDataException("synthetic") : Vault;
        public void Save(AccountVault vault) { if (FailSave) throw new IOException("synthetic"); Vault = vault; Saves++; }
    }
    internal sealed class Microsoft : IMicrosoftAuthenticator
    {
        public bool FailRefresh, ChangeIdentity; public TaskCompletionSource? Wait;
        public async Task<AuthenticatedAccount> SignInAsync(string clientId, Action<DeviceCodePrompt> prompt, CancellationToken ct = default)
        {
            if (Wait is not null) await Wait.Task;
            return new(new("test-ms", AccountKind.Microsoft, "OnlinePlayer", "0123456789abcdef0123456789abcdef", ClientId: clientId),
                new() { AccessToken = "synthetic-access", RefreshToken = "synthetic-refresh", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });
        }
        public Task<AuthenticatedAccount> RefreshAsync(AccountProfile profile, AccountCredentials credentials, CancellationToken ct = default)
        {
            if (FailRefresh) throw new AccountAuthenticationException("需要重新登录。");
            return Task.FromResult(new AuthenticatedAccount(ChangeIdentity ? profile with { Uuid = "ffffffffffffffffffffffffffffffff" } : profile, credentials));
        }
    }
    internal sealed class Yggdrasil : IYggdrasilAuthenticator
    {
        public ThirdPartyLogin? Login;
        public Task<ThirdPartyLogin> SignInAsync(string serverUrl, string username, string password, CancellationToken ct = default) =>
            Login is null ? throw new NotSupportedException() : Task.FromResult(Login);
        public Task<AuthenticatedAccount> SelectProfileAsync(ThirdPartyLogin login, string profileId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthenticatedAccount> RefreshAsync(AccountProfile profile, AccountCredentials credentials, CancellationToken ct = default) => throw new NotSupportedException();
        public Task InvalidateAsync(AccountProfile profile, AccountCredentials credentials, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class GatedProfiles : IReadOnlyList<PlayerProfile>, IDisposable
    {
        private readonly PlayerProfile[] _values = [new("0123456789abcdef0123456789abcdef", "PlayerOne"), new("abcdef0123456789abcdef0123456789", "PlayerTwo")];
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release = new(false);
        public int Count { get { Entered.TrySetResult(); Release.Wait(TimeSpan.FromSeconds(10)); return _values.Length; } }
        public PlayerProfile this[int index] => _values[index];
        public IEnumerator<PlayerProfile> GetEnumerator() => ((IEnumerable<PlayerProfile>)_values).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public void Dispose() => Release.Dispose();
    }
}
