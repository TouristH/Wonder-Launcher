// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Wonder.Application;
using Wonder.Core;
using Wonder.Infrastructure;

namespace Wonder.Game.Tests;

public sealed class OfflineDefaultSkinTests
{
    public static IEnumerable<object[]> Presets => OfflineDefaultSkins.Choices.Select(x => new object[] { x.Id });
    [Theory, MemberData(nameof(Presets))]
    public void Every_preset_is_stable_valid_and_matches_the_java_rule(string id)
    {
        var skin = OfflineDefaultSkins.Get(id);
        foreach (var name in new[] { "Player", "Wonder_Test", "Player1234567890", "UPPER", "lower" })
        {
            var uuid = OfflineDefaultSkins.CreateUuid(name, id);
            Assert.Equal(uuid, OfflineDefaultSkins.CreateUuid(name, id)); Assert.True(OfflineDefaultSkins.IsValid(name, uuid, id));
            Assert.Equal('3', uuid[12]); Assert.Contains(uuid[16], "89ab");
            if (skin.IsLegacy) Assert.Equal(skin.TextureName, OfflineDefaultSkins.LegacySkinName(uuid).ToLowerInvariant());
            else Assert.Equal(id, OfflineDefaultSkins.ForUuid(uuid).Id);
        }
    }
    [Fact]
    public void Java_hash_uses_big_endian_uuid_words_and_nonnegative_floor_mod()
    {
        Assert.Equal(int.MinValue, OfflineDefaultSkins.JavaHash("80000000000000000000000000000000"));
        Assert.Equal(1, OfflineDefaultSkins.JavaHash("00000000000000000000000000000001"));
        Assert.Equal(0, OfflineDefaultSkins.JavaHash("ffffffffffffffffffffffffffffffff"));
        Assert.Equal(16, OfflineDefaultSkins.ForUuid("80000000000000000000000000000000").Index);
    }
    [Fact]
    public async Task Selection_persists_renames_and_restores_canonical_identity_without_switching_account()
    {
        var store = new Store(); var service = new AccountService(store, new Microsoft(), new Yggdrasil());
        service.AddOffline("Wonder_Test"); var original = service.SelectedAccount!;
        var previewUuid = service.GetOfflineSkinUuid(original.Id, "steve-classic"); Assert.Equal(original.Uuid, service.SelectedAccount!.Uuid);
        service.AddOffline("OtherPlayer"); var other = service.SelectedAccount!.Id;
        service.SetOfflineSkin(original.Id, "steve-classic"); Assert.Equal(other, service.SelectedAccount!.Id);
        Assert.Equal(previewUuid, service.Accounts.Single(x => x.Id == original.Id).Uuid);
        service.AddOffline("Wonder_Test"); Assert.Equal(2, service.Accounts.Count); Assert.Equal(previewUuid, service.SelectedAccount!.Uuid);
        service.RenameOffline(original.Id, "Renamed"); Assert.Equal("steve-classic", service.SelectedAccount.OfflineSkinId);
        Assert.Equal("steve-classic", OfflineDefaultSkins.ForUuid(service.SelectedAccount.Uuid).Id);
        var session = await service.GetSelectedSessionAsync(); Assert.Equal("steve-classic", session.OfflineSkinId); Assert.Equal(service.SelectedAccount.Uuid, session.Uuid);
        service = new AccountService(store, new Microsoft(), new Yggdrasil()); Assert.False(service.IsReadBlocked);
        service.SetOfflineSkin(original.Id, null); Assert.Equal(AccountService.OfflineUuid("Renamed"), service.SelectedAccount!.Uuid);
        service.SetOfflineSkin(original.Id, "unknown"); Assert.NotNull(service.Error); Assert.Null(service.SelectedAccount.OfflineSkinId);
    }
    [Fact]
    public void Encrypted_vault_roundtrips_selected_skin_and_rejects_unrelated_uuid()
    {
        using var f = new ImporterFixtures(); var store = new WindowsAccountStore(Path.Combine(f.Root, "accounts.dat"));
        var service = new AccountService(store, new Microsoft(), new Yggdrasil()); service.AddOffline("TestPlayer");
        service.SetOfflineSkin(service.SelectedAccount!.Id, "alex-slim"); Assert.Null(service.Error);
        var loaded = store.Load(); Assert.Equal("alex-slim", loaded.Accounts.Single().Profile.OfflineSkinId);
        var current = loaded.Accounts[0]; loaded.Accounts[0] = new(current.Profile with { Uuid = AccountService.OfflineUuid("SomeoneElse") }, current.Credentials);
        Assert.Throws<InvalidDataException>(() => store.Save(loaded));
    }
    [Fact]
    public async Task Launch_uses_chosen_uuid_and_rejects_tampered_or_online_skin_override()
    {
        using var f = new ImporterFixtures(); f.Version(); f.Write(".minecraft/versions/1.20.1/1.20.1.jar", "synthetic-client");
        var instance = (await new InstanceDiscovery().DiscoverAsync(Path.Combine(f.Root, ".minecraft"))).Instances.Single();
        var java = f.Write("java.exe", "synthetic"); instance = instance with { Overrides = new() { JavaPath = java } };
        var launcher = new LocalGameLauncher((path, _) => Task.FromResult(new JavaRuntime(path, 21, "synthetic")));
        var uuid = OfflineDefaultSkins.CreateUuid("TestPlayer", "steve-classic");
        var request = new LaunchRequest(instance, "TestPlayer", f.DirectoryAt("cache")) { Session = new(AccountKind.Offline, "TestPlayer", uuid, "0", offlineSkinId: "steve-classic") };
        var plan = await launcher.BuildPlanAsync(request); Assert.Contains(uuid, plan.Arguments);
        await Assert.ThrowsAsync<InvalidDataException>(() => launcher.BuildPlanAsync(request with { Session = new(AccountKind.Offline, "TestPlayer", uuid, "0", offlineSkinId: "alex-slim") }));
        await Assert.ThrowsAsync<InvalidDataException>(() => launcher.BuildPlanAsync(request with { Session = new(AccountKind.Microsoft, "TestPlayer", uuid, "synthetic", WonderMicrosoftApplication.ClientId, offlineSkinId: "alex-slim") }));
    }
    private sealed class Store : IAccountStore { private AccountVault _vault = new(); public AccountVault Load() => _vault; public void Save(AccountVault vault) => _vault = vault; }
    private sealed class Microsoft : IMicrosoftAuthenticator
    {
        public Task<AuthenticatedAccount> SignInAsync(string id, Action<DeviceCodePrompt> cb, CancellationToken ct = default) => throw new NotSupportedException();
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
