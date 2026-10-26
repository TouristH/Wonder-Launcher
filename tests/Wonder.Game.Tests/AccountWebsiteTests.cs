// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Wonder.Application;
using Wonder.Core;

namespace Wonder.Game.Tests;

public sealed class AccountWebsiteTests
{
    [Fact]
    public void Website_uses_saved_metadata_and_supports_custom_skin_page_without_credentials()
    {
        var store = new Store(); var service = new AccountService(store, new Microsoft(), new Yggdrasil());
        Assert.Equal("https://login.example.test/", service.GetSkinManagementWebsite("third"));
        service.SetServerWebsite("site", "https://skin.example.test/user/skin");
        Assert.Null(service.Error); Assert.Equal("https://skin.example.test/user/skin", service.GetSkinManagementWebsite("third"));
        Assert.DoesNotContain("synthetic", service.GetSkinManagementWebsite("third"));
        service.SetServerWebsite("site", ""); Assert.Equal("https://login.example.test/", service.GetSkinManagementWebsite("third"));
        service.AddOffline("LocalPlayer"); Assert.Null(service.GetSkinManagementWebsite(service.SelectedAccount!.Id));
    }
    [Theory]
    [InlineData("http://skin.example.test/")]
    [InlineData("https://user:secret@skin.example.test/")]
    [InlineData("file:///C:/skin")]
    [InlineData("javascript:alert(1)")]
    public void Invalid_websites_are_rejected_without_overwriting_previous_value(string address)
    {
        var store = new Store(); var service = new AccountService(store, new Microsoft(), new Yggdrasil());
        service.SetServerWebsite("site", "https://skin.example.test/"); service.SetServerWebsite("site", address);
        Assert.NotNull(service.Error); Assert.Equal("https://skin.example.test/", service.GetSkinManagementWebsite("third"));
    }
    private sealed class Store : IAccountStore
    {
        private AccountVault _vault = new()
        {
            Accounts = [new(new("third", AccountKind.Yggdrasil, "TestPlayer", "0123456789abcdef0123456789abcdef", "https://login.example.test/api/yggdrasil/"), new() { AccessToken = "synthetic-secret" })],
            Servers = [new("site", "Test site", "https://login.example.test/api/yggdrasil/")], SelectedAccountId = "third"
        };
        public AccountVault Load() => _vault;
        public void Save(AccountVault vault) => _vault = vault;
    }
    private sealed class Microsoft : IMicrosoftAuthenticator
    {
        public Task<AuthenticatedAccount> SignInAsync(string id, Action<DeviceCodePrompt> callback, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthenticatedAccount> RefreshAsync(AccountProfile p, AccountCredentials c, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class Yggdrasil : IYggdrasilAuthenticator
    {
        public Task<ThirdPartyLogin> SignInAsync(string s, string u, string p, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthenticatedAccount> SelectProfileAsync(ThirdPartyLogin l, string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthenticatedAccount> RefreshAsync(AccountProfile p, AccountCredentials c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task InvalidateAsync(AccountProfile p, AccountCredentials c, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
