// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Application;
using Wonder.Core;
using Wonder.Infrastructure;

namespace Wonder.Game.Tests;

public sealed class AccountLimitTests
{
    [Theory]
    [InlineData(255, 127, 15)]
    [InlineData(256, 128, 16)]
    public void Supported_boundaries_roundtrip_through_storage_and_application(int count, int idLength, int usernameLength)
    {
        using var fixture = new ImporterFixtures();
        var store = new WindowsAccountStore(Path.Combine(fixture.Root, "accounts.dat"));
        var vault = Vault(count, idLength, usernameLength);
        store.Save(vault);

        var service = Service(store);
        Assert.False(service.IsReadBlocked);
        Assert.Null(service.Error);
        Assert.Equal(count, service.Accounts.Count);
        Assert.Equal(vault.Accounts[0].Profile.Id, service.SelectedAccount!.Id);
        Assert.Equal(vault.Accounts[0].Profile.Username, service.SelectedAccount.Username);
        Assert.Equal(vault.Accounts[0].Profile.Uuid, service.SelectedAccount.Uuid);
        service.Select(vault.Accounts[^1].Profile.Id);

        var loaded = store.Load();
        Assert.Equal(count, loaded.Accounts.Count);
        Assert.Equal(vault.Accounts[^1].Profile.Id, loaded.SelectedAccountId);
        Assert.Equal("synthetic-refresh-secret", loaded.Accounts[0].Credentials.RefreshToken);
    }

    [Theory]
    [InlineData(257, 128, 16, "账户", "256")]
    [InlineData(1, 129, 16, "标识", "128")]
    [InlineData(1, 128, 17, "用户名", "16")]
    public void Storage_rejects_exceeded_limits_with_safe_reason_without_replacing_vault(
        int count, int idLength, int usernameLength, string field, string limit)
    {
        using var fixture = new ImporterFixtures();
        var path = Path.Combine(fixture.Root, "accounts.dat");
        var store = new WindowsAccountStore(path);
        store.Save(Vault());
        var before = File.ReadAllBytes(path);

        var error = Record.Exception(() => store.Save(Vault(count, idLength, usernameLength)));

        Assert.NotNull(error);
        AssertLimitReason(error.Message, field, limit);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Single(store.Load().Accounts);
        Assert.Single(Directory.GetFiles(fixture.Root));
    }

    [Theory]
    [InlineData(257, 128, 16, "账户", "256")]
    [InlineData(1, 129, 16, "标识", "128")]
    [InlineData(1, 128, 17, "用户名", "16")]
    public void Application_blocks_exceeded_limits_on_reload_with_safe_reason(
        int count, int idLength, int usernameLength, string field, string limit)
    {
        var store = new AccountServiceTests.Store { Vault = Vault(count, idLength, usernameLength) };
        var service = Service(store);

        Assert.True(service.IsReadBlocked);
        AssertLimitReason(service.Error, field, limit);
        service.AddOffline("Player");
        Assert.Equal(0, store.Saves);
        Assert.Equal(count, store.Vault.Accounts.Count);
    }

    [Theory]
    [InlineData(129, 16, "标识", "128")]
    [InlineData(128, 17, "用户名", "16")]
    public async Task Application_rejects_oversized_login_profile_before_writing(
        int idLength, int usernameLength, string field, string limit)
    {
        var store = new AccountServiceTests.Store();
        var microsoft = new Microsoft(Vault(1, idLength, usernameLength).Accounts[0]);
        var service = Service(store, microsoft);

        await service.SignInMicrosoftAsync();

        AssertLimitReason(service.Error, field, limit);
        Assert.Empty(service.Accounts);
        Assert.Empty(store.Vault.Accounts);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public void Adding_account_at_capacity_reports_limit_and_preserves_selection()
    {
        var store = new AccountServiceTests.Store { Vault = Vault(256) };
        var service = Service(store);
        var selected = service.SelectedAccount;

        service.AddOffline("ExtraPlayer");

        AssertLimitReason(service.Error, "账户", "256");
        Assert.Equal(256, service.Accounts.Count);
        Assert.Equal(selected, service.SelectedAccount);
        Assert.Equal(0, store.Saves);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Adding_or_renaming_offline_account_reports_username_limit(bool rename)
    {
        var store = new AccountServiceTests.Store();
        var service = Service(store);
        service.AddOffline("OriginalPlayer");
        var original = service.SelectedAccount!;
        var saves = store.Saves;

        if (rename) service.RenameOffline(original.Id, new string('n', 17));
        else service.AddOffline(new string('n', 17));

        AssertLimitReason(service.Error, "用户名", "16");
        Assert.Equal(original, Assert.Single(service.Accounts));
        Assert.Equal(saves, store.Saves);
    }

    [Fact]
    public async Task Failed_rotation_save_at_all_boundaries_recovers_before_next_refresh()
    {
        var store = new AccountServiceTests.Store { Vault = Vault(256, 128, 16) };
        var microsoft = new Microsoft(store.Vault.Accounts[0]);
        var service = Service(store, microsoft);
        store.FailSave = true;

        await service.RefreshAsync(service.SelectedAccount!.Id);
        Assert.Contains("不要退出", service.Error);
        Assert.Equal("synthetic-access-secret", store.Vault.Accounts[0].Credentials.AccessToken);

        store.FailSave = false;
        service.Reload();
        Assert.Null(service.Error);
        Assert.False(service.IsReadBlocked);
        Assert.Equal(256, service.Accounts.Count);
        Assert.Equal("synthetic-rotated", store.Vault.Accounts[0].Credentials.AccessToken);

        var session = await service.GetSelectedSessionAsync();
        Assert.Equal("synthetic-next", session.AccessToken);
        Assert.Equal("synthetic-next", store.Vault.Accounts[0].Credentials.AccessToken);
        Assert.Equal(new[] { "synthetic-access-secret", "synthetic-rotated" }, microsoft.RefreshTokens);
    }

    private static void AssertLimitReason(string? message, string field, string limit)
    {
        Assert.NotNull(message);
        Assert.Contains(field, message);
        Assert.Contains(limit, message);
        Assert.DoesNotContain("synthetic", message);
    }

    private static AccountVault Vault(int count = 1, int idLength = 128, int usernameLength = 16)
    {
        var vault = new AccountVault();
        for (var i = 0; i < count; i++)
        {
            var id = i.ToString("D4").PadRight(idLength, 'i');
            vault.Accounts.Add(new(new(id, AccountKind.Microsoft, new string('n', usernameLength), i.ToString("x32"),
                ClientId: WonderMicrosoftApplication.ClientId), new()
            {
                AccessToken = "synthetic-access-secret", RefreshToken = "synthetic-refresh-secret",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            }));
        }
        vault.SelectedAccountId = vault.Accounts[0].Profile.Id;
        return vault;
    }

    private static AccountService Service(IAccountStore store, IMicrosoftAuthenticator? microsoft = null) =>
        new(store, microsoft ?? new AccountServiceTests.Microsoft(), new AccountServiceTests.Yggdrasil());

    private sealed class Microsoft(AuthenticatedAccount signedIn) : IMicrosoftAuthenticator
    {
        public List<string> RefreshTokens { get; } = [];
        public Task<AuthenticatedAccount> SignInAsync(string clientId, Action<DeviceCodePrompt> prompt, CancellationToken ct = default) =>
            Task.FromResult(signedIn);
        public Task<AuthenticatedAccount> RefreshAsync(AccountProfile profile, AccountCredentials credentials, CancellationToken ct = default)
        {
            RefreshTokens.Add(credentials.AccessToken);
            return Task.FromResult(new AuthenticatedAccount(profile, new()
            {
                AccessToken = RefreshTokens.Count == 1 ? "synthetic-rotated" : "synthetic-next",
                RefreshToken = "synthetic-refresh-secret", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            }));
        }
    }
}
