// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Wonder.Application;
using Wonder.Core;

namespace Wonder.Game.Tests;

public sealed class AccountCenterServiceTests
{
    [Theory]
    [InlineData(AccountKind.Yggdrasil, false)]
    [InlineData(AccountKind.Microsoft, true)]
    public void Custom_texture_hosts_are_accepted_only_for_third_party_accounts(AccountKind kind, bool blocked)
    {
        var capeId = "ba3a48ec-2681-441d-a11c-e402b85d3944";
        var profile = new AccountProfile("texture-test", kind, "TexturePlayer", Uuid, kind == AccountKind.Yggdrasil ? ServerUrl : null, kind == AccountKind.Microsoft ? WonderMicrosoftApplication.ClientId : null)
        {
            SkinUrl = "https://cdn.example.test/skin.png", SkinVariant = "slim",
            Capes = [new(capeId, "Active cape", "https://cdn.example.test/cape.png")], ActiveCapeId = capeId
        };
        var store = new AccountServiceTests.Store { Vault = new() { Accounts = [new(profile, new() { AccessToken = "synthetic" })], SelectedAccountId = profile.Id } };
        var service = new AccountService(store, new Microsoft(), new Yggdrasil());
        Assert.Equal(blocked, service.IsReadBlocked);
        if (!blocked) Assert.Equal(profile.Capes, service.SelectedAccount!.Capes);
    }

    private const string Uuid = "0123456789abcdef0123456789abcdef";
    private const string OtherUuid = "abcdef0123456789abcdef0123456789";
    private const string ServerUrl = "https://skin.example/api/yggdrasil/";

    [Fact]
    public async Task Microsoft_login_uses_embedded_application_even_with_legacy_configuration()
    {
        var store = new AccountServiceTests.Store { Vault = new() { MicrosoftClientId = Guid.NewGuid().ToString() } };
        var microsoft = new Microsoft(); var service = new AccountService(store, microsoft, new Yggdrasil());
        await service.SignInMicrosoftAsync();
        Assert.Equal(WonderMicrosoftApplication.ClientId, microsoft.LastClientId);
        Assert.Equal(WonderMicrosoftApplication.ClientId, service.MicrosoftClientId);
        Assert.Equal(WonderMicrosoftApplication.ClientId, store.Vault.MicrosoftClientId);
    }

    [Fact]
    public async Task Refresh_updates_credentials_and_metadata_without_switching_launch_account()
    {
        var store = new AccountServiceTests.Store(); var microsoft = new Microsoft(); var service = new AccountService(store, microsoft, new Yggdrasil());
        await service.SignInMicrosoftAsync(); var online = service.SelectedAccount!;
        service.AddOffline("SelectedPlayer"); var selected = service.SelectedAccount!;
        await service.RefreshAsync(online.Id);
        Assert.Null(service.Error); Assert.Equal(selected.Id, service.SelectedAccount!.Id);
        var saved = store.Vault.Accounts.Single(x => x.Profile.Id == online.Id);
        Assert.Equal("rotated-refresh", saved.Credentials.RefreshToken); Assert.NotNull(saved.Profile.LastValidatedAt);
    }

    [Fact]
    public async Task Reauthentication_rejects_different_microsoft_identity_without_adding_or_switching()
    {
        var store = new AccountServiceTests.Store(); var microsoft = new Microsoft(); var service = new AccountService(store, microsoft, new Yggdrasil());
        await service.SignInMicrosoftAsync(); var original = service.SelectedAccount!;
        service.AddOffline("SelectedPlayer"); var selected = service.SelectedAccount!;
        microsoft.LoginUuid = OtherUuid;
        await service.SignInMicrosoftAsync(original.Id);
        Assert.NotNull(service.Error); Assert.Equal(2, service.Accounts.Count); Assert.Equal(selected.Id, service.SelectedAccount!.Id);
        Assert.Equal(Uuid, service.Accounts.Single(x => x.Id == original.Id).Uuid);
        microsoft.LoginUuid = Uuid;
        await service.SignInMicrosoftAsync(original.Id);
        Assert.Null(service.Error); Assert.Equal(selected.Id, service.SelectedAccount!.Id); Assert.Equal(2, service.Accounts.Count);
    }

    [Fact]
    public async Task Reauthentication_migrates_legacy_application_only_after_same_identity_login()
    {
        var oldClient = Guid.NewGuid().ToString();
        var store = new AccountServiceTests.Store { Vault = new() { Accounts = [Online(oldClient)], SelectedAccountId = "ms" } };
        var service = new AccountService(store, new Microsoft(), new Yggdrasil());
        await service.SignInMicrosoftAsync("ms");
        Assert.Null(service.Error); Assert.Single(service.Accounts); Assert.Equal("ms", service.SelectedAccount!.Id);
        Assert.Equal(WonderMicrosoftApplication.ClientId, service.SelectedAccount.ClientId);
    }

    [Fact]
    public async Task Third_party_reauthentication_requires_original_server_and_role()
    {
        var provider = new Yggdrasil { Multiple = true };
        var store = new AccountServiceTests.Store { Vault = new() { Accounts = [ThirdParty()], SelectedAccountId = "ygg" } };
        var service = new AccountService(store, new Microsoft(), provider);
        service.AddOffline("SelectedPlayer"); var selected = service.SelectedAccount!.Id;
        await service.SignInThirdPartyAsync("https://other.example/", "user", "synthetic", "ygg");
        Assert.NotNull(service.Error); Assert.Equal(0, provider.SignIns);
        await service.SignInThirdPartyAsync(ServerUrl, "user", "synthetic", "ygg");
        Assert.Equal(2, service.PendingProfiles.Count);
        await service.CompleteThirdPartyAsync(OtherUuid);
        Assert.NotNull(service.Error); Assert.Equal(0, provider.Selections);
        await service.CompleteThirdPartyAsync(Uuid);
        Assert.Null(service.Error); Assert.Equal(1, provider.Selections); Assert.Empty(service.PendingProfiles);
        Assert.Equal(selected, service.SelectedAccount!.Id); Assert.Equal(2, service.Accounts.Count);
    }

    [Fact]
    public void Offline_rename_preserves_account_selection_but_changes_identity_and_rejects_duplicates()
    {
        var service = new AccountService(new AccountServiceTests.Store(), new Microsoft(), new Yggdrasil());
        service.AddOffline("Original"); var original = service.SelectedAccount!;
        service.RenameOffline(original.Id, "Renamed");
        Assert.Equal(original.Id, service.SelectedAccount!.Id); Assert.Equal("Renamed", service.SelectedAccount.Username);
        Assert.Equal(AccountService.OfflineUuid("Renamed"), service.SelectedAccount.Uuid); Assert.NotEqual(original.Uuid, service.SelectedAccount.Uuid);
        service.AddOffline("OtherPlayer"); service.RenameOffline(original.Id, "OtherPlayer");
        Assert.NotNull(service.Error); Assert.Equal("Renamed", service.Accounts.Single(x => x.Id == original.Id).Username);
    }

    [Fact]
    public async Task Server_discovery_requires_confirmation_and_deduplicates_actual_api()
    {
        var store = new AccountServiceTests.Store(); var directory = new Directory();
        var service = new AccountService(store, new Microsoft(), new Yggdrasil(), directory);
        await service.DiscoverServerAsync("skin.example");
        Assert.Empty(service.Servers); Assert.NotNull(service.PendingServer); Assert.Equal(0, store.Saves);
        service.ConfirmServer("My Skin Server");
        Assert.Null(service.PendingServer); var server = Assert.Single(service.Servers); Assert.Equal("My Skin Server", server.Name);
        await service.DiscoverServerAsync("skin.example"); service.ConfirmServer();
        Assert.Single(service.Servers); service.RenameServer(server.Id, "Renamed Server");
        Assert.Equal("Renamed Server", service.Servers[0].Name);
        service.RemoveServer(server.Id); Assert.Empty(service.Servers);
    }

    [Fact]
    public async Task Cancelled_discovery_does_not_publish_stale_result()
    {
        var directory = new Directory { Wait = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var service = new AccountService(new AccountServiceTests.Store(), new Microsoft(), new Yggdrasil(), directory);
        var request = service.DiscoverServerAsync("skin.example"); service.CancelServerDiscovery(); directory.Wait.SetResult(); await request;
        Assert.Null(service.PendingServer); Assert.Empty(service.Servers);
    }

    [Fact]
    public async Task Existing_third_party_accounts_migrate_to_server_directory_and_block_removal_while_used()
    {
        var store = new AccountServiceTests.Store { Vault = new() { Accounts = [ThirdParty()], SelectedAccountId = "ygg" } };
        var service = new AccountService(store, new Microsoft(), new Yggdrasil());
        var server = Assert.Single(service.Servers); Assert.Equal(ServerUrl, server.ApiUrl);
        Assert.Equal(0, store.Saves);
        service.RemoveServer(server.Id); Assert.NotNull(service.Error); Assert.Single(service.Servers);
        await service.RemoveAsync("ygg"); service.RemoveServer(server.Id); Assert.Null(service.Error); Assert.Empty(service.Servers);
    }

    [Fact]
    public async Task Third_party_launch_automatically_prepares_adapter_and_does_not_expose_it_to_microsoft()
    {
        var store = new AccountServiceTests.Store { Vault = new() { Accounts = [ThirdParty()], SelectedAccountId = "ygg" } };
        var adapter = new Adapter(); var service = new AccountService(store, new Microsoft(), new Yggdrasil(), adapter: adapter);
        var session = await service.GetSelectedSessionAsync();
        Assert.Equal(1, adapter.Calls); Assert.Equal(adapter.Result.Path, session.AuthlibInjectorPath); Assert.Equal(adapter.Result.Sha256, session.AuthlibInjectorSha256);
        await service.SignInMicrosoftAsync(); var microsoftSession = await service.GetSelectedSessionAsync();
        Assert.Equal(1, adapter.Calls); Assert.Null(microsoftSession.AuthlibInjectorPath); Assert.Null(microsoftSession.ServerUrl);
    }

    [Fact]
    public async Task Adapter_failure_stops_third_party_launch_without_falling_back()
    {
        var store = new AccountServiceTests.Store { Vault = new() { Accounts = [ThirdParty()], SelectedAccountId = "ygg" } };
        var service = new AccountService(store, new Microsoft(), new Yggdrasil(), adapter: new Adapter { Fail = true });
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => service.GetSelectedSessionAsync());
        Assert.Equal(AccountKind.Yggdrasil, service.SelectedAccount!.Kind); Assert.Null(store.Vault.AuthlibInjectorPath);
    }

    [Fact]
    public async Task Skin_upload_refreshes_and_persists_rotation_without_switching_account()
    {
        var store = new AccountServiceTests.Store(); var microsoft = new Microsoft(); var service = new AccountService(store, microsoft, new Yggdrasil());
        await service.SignInMicrosoftAsync(); service.AddOffline("SelectedPlayer"); var selected = service.SelectedAccount!.Id;
        await service.UploadSkinAsync("ms", [1, 2, 3], "slim");
        Assert.Null(service.Error); Assert.Equal("rotated-access", microsoft.AppearanceAccessToken); Assert.Equal(selected, service.SelectedAccount!.Id);
        Assert.Equal("slim", service.Accounts.Single(x => x.Id == "ms").SkinVariant);
        Assert.Equal("rotated-refresh", store.Vault.Accounts.Single(x => x.Profile.Id == "ms").Credentials.RefreshToken);
    }

    [Fact]
    public async Task Appearance_failure_keeps_rotated_credentials_and_does_not_claim_new_appearance()
    {
        var store = new AccountServiceTests.Store(); var microsoft = new Microsoft { FailAppearance = true }; var service = new AccountService(store, microsoft, new Yggdrasil());
        await service.SignInMicrosoftAsync(); await service.UploadSkinAsync("ms", [1, 2, 3], "slim");
        Assert.NotNull(service.Error); var saved = Assert.Single(store.Vault.Accounts);
        Assert.Equal("classic", saved.Profile.SkinVariant); Assert.Equal("rotated-refresh", saved.Credentials.RefreshToken);
    }

    [Fact]
    public async Task Failed_refresh_or_credential_save_prevents_remote_appearance_change()
    {
        var store = new AccountServiceTests.Store(); var microsoft = new Microsoft { FailRefresh = true }; var service = new AccountService(store, microsoft, new Yggdrasil());
        await service.SignInMicrosoftAsync(); await service.ResetSkinAsync("ms");
        Assert.NotNull(service.Error); Assert.Equal(0, microsoft.AppearanceCalls);
        microsoft.FailRefresh = false; store.FailSave = true; await service.SetCapeAsync("ms", null);
        Assert.NotNull(service.Error); Assert.Equal(0, microsoft.AppearanceCalls);
    }

    [Fact]
    public async Task Appearance_identity_change_is_rejected_after_request()
    {
        var store = new AccountServiceTests.Store(); var microsoft = new Microsoft { WrongAppearanceIdentity = true }; var service = new AccountService(store, microsoft, new Yggdrasil());
        await service.SignInMicrosoftAsync(); await service.ResetSkinAsync("ms");
        Assert.NotNull(service.Error); Assert.Equal(Uuid, Assert.Single(service.Accounts).Uuid);
    }

    [Fact]
    public async Task Cancelled_appearance_response_does_not_replace_local_profile()
    {
        var store = new AccountServiceTests.Store();
        var microsoft = new Microsoft { AppearanceWait = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var service = new AccountService(store, microsoft, new Yggdrasil());
        await service.SignInMicrosoftAsync();
        var operation = service.UploadSkinAsync("ms", [1, 2, 3], "slim");
        Assert.Equal(1, microsoft.AppearanceCalls);
        service.Cancel(); microsoft.AppearanceWait.SetResult(); await operation;
        Assert.Equal("classic", Assert.Single(service.Accounts).SkinVariant);
        Assert.Equal("rotated-refresh", Assert.Single(store.Vault.Accounts).Credentials.RefreshToken);
        Assert.False(service.IsBusy);
    }

    private static AccountCredentials Credentials(string prefix = "synthetic") => new()
    { AccessToken = prefix + "-access", RefreshToken = prefix + "-refresh", ClientToken = "synthetic-client", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
    private static AuthenticatedAccount Online(string clientId = WonderMicrosoftApplication.ClientId) => new(new("ms", AccountKind.Microsoft, "OnlinePlayer", Uuid, ClientId: clientId), Credentials());
    private static AuthenticatedAccount ThirdParty() => new(new("ygg", AccountKind.Yggdrasil, "SkinPlayer", Uuid, ServerUrl), Credentials());
    private sealed class Microsoft : IMicrosoftAuthenticator, IMicrosoftAccountAppearance
    {
        public string LoginUuid = Uuid; public string? LastClientId, AppearanceAccessToken;
        public bool FailRefresh, FailAppearance, WrongAppearanceIdentity; public int AppearanceCalls;
        public TaskCompletionSource? AppearanceWait;
        public Task<AuthenticatedAccount> SignInAsync(string clientId, Action<DeviceCodePrompt> prompt, CancellationToken ct = default)
        { LastClientId = clientId; var original = Online(clientId); return Task.FromResult(new AuthenticatedAccount(original.Profile with { Uuid = LoginUuid }, original.Credentials)); }
        public Task<AuthenticatedAccount> RefreshAsync(AccountProfile profile, AccountCredentials credentials, CancellationToken ct = default)
        { if (FailRefresh) throw new AccountAuthenticationException("Synthetic refresh failed."); return Task.FromResult(new AuthenticatedAccount(profile, Credentials("rotated"))); }
        public Task<AccountProfile> UploadSkinAsync(AccountProfile profile, AccountCredentials credentials, byte[] png, string variant, CancellationToken ct = default) => Change(profile with { SkinVariant = variant }, credentials);
        public Task<AccountProfile> ResetSkinAsync(AccountProfile profile, AccountCredentials credentials, CancellationToken ct = default) => Change(profile, credentials);
        public Task<AccountProfile> SetCapeAsync(AccountProfile profile, AccountCredentials credentials, string? capeId, CancellationToken ct = default) => Change(profile, credentials);
        private async Task<AccountProfile> Change(AccountProfile profile, AccountCredentials credentials)
        {
            AppearanceCalls++; AppearanceAccessToken = credentials.AccessToken;
            if (FailAppearance) throw new AccountAuthenticationException("Synthetic remote appearance failed.");
            if (AppearanceWait is not null) await AppearanceWait.Task;
            return WrongAppearanceIdentity ? profile with { Uuid = OtherUuid } : profile;
        }
    }
    private sealed class Yggdrasil : IYggdrasilAuthenticator
    {
        public bool Multiple; public int SignIns, Selections;
        public Task<ThirdPartyLogin> SignInAsync(string serverUrl, string username, string password, CancellationToken ct = default)
        { SignIns++; return Task.FromResult(new ThirdPartyLogin(ServerUrl, Multiple ? [new(Uuid, "SkinPlayer"), new(OtherUuid, "OtherSkin")] : [new(Uuid, "SkinPlayer")], null, Credentials())); }
        public Task<AuthenticatedAccount> SelectProfileAsync(ThirdPartyLogin login, string profileId, CancellationToken ct = default)
        { Selections++; return Task.FromResult(new AuthenticatedAccount(new("temporary", AccountKind.Yggdrasil, "SkinPlayer", profileId, login.ServerUrl), login.Credentials)); }
        public Task<AuthenticatedAccount> RefreshAsync(AccountProfile profile, AccountCredentials credentials, CancellationToken ct = default) => Task.FromResult(new AuthenticatedAccount(profile, credentials));
        public Task InvalidateAsync(AccountProfile profile, AccountCredentials credentials, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class Directory : IAuthenticationServerDirectory
    {
        public TaskCompletionSource? Wait;
        public async Task<AuthenticationServer> DiscoverAsync(string address, CancellationToken ct = default)
        { if (Wait is not null) await Wait.Task; return new("server", "Skin Server", ServerUrl); }
    }
    private sealed class Adapter : IAuthenticationAdapterProvider
    {
        public bool Fail; public int Calls;
        public AuthenticationAdapter Result = new(Path.Combine(Path.GetTempPath(), "synthetic-authlib.jar"), new string('A', 64));
        public Task<AuthenticationAdapter> EnsureAsync(CancellationToken ct = default)
        { Calls++; if (Fail) throw new AccountAuthenticationException("Synthetic adapter download failed."); return Task.FromResult(Result); }
    }
}
