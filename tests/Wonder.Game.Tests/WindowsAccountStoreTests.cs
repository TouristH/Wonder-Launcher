// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text;
using Wonder.Core;
using Wonder.Infrastructure;

namespace Wonder.Game.Tests;

public sealed class WindowsAccountStoreTests
{
    [Fact]
    public void Protected_vault_roundtrip_has_no_plaintext_credentials_or_profile()
    {
        using var f = new ImporterFixtures(); var path = Path.Combine(f.Root, "accounts.dat");
        var store = new WindowsAccountStore(path);
        var vault = Sample(); store.Save(vault);
        var disk = Encoding.UTF8.GetString(File.ReadAllBytes(path));
        Assert.DoesNotContain("synthetic-access-secret", disk);
        Assert.DoesNotContain("synthetic-refresh-secret", disk);
        Assert.DoesNotContain("WonderTest", disk);
        var loaded = store.Load();
        Assert.Equal(vault.SelectedAccountId, loaded.SelectedAccountId);
        Assert.Equal("synthetic-refresh-secret", Assert.Single(loaded.Accounts).Credentials.RefreshToken);
        Assert.Single(Directory.GetFiles(f.Root));
    }

    [Fact]
    public void Corrupt_vault_cannot_be_loaded_or_overwritten_even_by_new_store()
    {
        using var f = new ImporterFixtures(); var path = f.Write("accounts.dat", "not encrypted synthetic file");
        var before = File.ReadAllBytes(path);
        Assert.Throws<InvalidDataException>(() => new WindowsAccountStore(path).Load());
        Assert.Throws<InvalidDataException>(() => new WindowsAccountStore(path).Save(Sample()));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Invalid_changes_do_not_replace_previous_protected_vault()
    {
        using var f = new ImporterFixtures(); var path = Path.Combine(f.Root, "accounts.dat");
        var store = new WindowsAccountStore(path); store.Save(Sample()); var before = File.ReadAllBytes(path);
        var invalid = Sample(); invalid.SelectedAccountId = "missing";
        Assert.Throws<InvalidDataException>(() => store.Save(invalid));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Server_directory_and_appearance_roundtrip_inside_encrypted_vault()
    {
        using var f = new ImporterFixtures(); var store = new WindowsAccountStore(Path.Combine(f.Root, "accounts.dat"));
        var vault = Sample(); var original = vault.Accounts[0]; var capeId = Guid.NewGuid().ToString();
        vault.Accounts[0] = new(original.Profile with
        {
            SkinUrl = "https://textures.minecraft.net/texture/synthetic", SkinVariant = "slim",
            Capes = [new(capeId, "Synthetic cape", "https://textures.minecraft.net/texture/synthetic-cape")], ActiveCapeId = capeId
        }, original.Credentials);
        vault.Servers.Add(new("synthetic-server", "Test server", "https://skin.example.test/api/", "https://skin.example.test/"));
        store.Save(vault); var loaded = store.Load();
        Assert.Equal("slim", loaded.Accounts[0].Profile.SkinVariant);
        Assert.Equal(capeId, loaded.Accounts[0].Profile.ActiveCapeId);
        Assert.Equal(vault.Servers, loaded.Servers);
        var before = File.ReadAllBytes(Path.Combine(f.Root, "accounts.dat"));
        vault.Accounts[0] = new(original.Profile with { SkinUrl = "https://untrusted.example.test/pixel.png" }, original.Credentials);
        Assert.Throws<InvalidDataException>(() => store.Save(vault));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(f.Root, "accounts.dat")));
    }

    [Fact]
    public void Third_party_custom_skin_and_active_cape_roundtrip_in_encrypted_vault()
    {
        using var f = new ImporterFixtures(); var store = new WindowsAccountStore(Path.Combine(f.Root, "accounts.dat"));
        const string capeId = "1d4918b0-6a74-42b6-95bd-b4149b567b0c";
        var vault = new AccountVault
        {
            Accounts = [new(new("synthetic-yggdrasil", AccountKind.Yggdrasil, "WonderTest", "00112233445566778899aabbccddeeff", "https://auth.example.test/api/")
            {
                SkinUrl = "https://cdn.example.test/skins/test.png", SkinVariant = "slim",
                Capes = [new(capeId, "当前披风", "https://cdn.example.test/capes/test.png")], ActiveCapeId = capeId
            }, new() { AccessToken = "synthetic-token", ClientToken = "synthetic-client" })]
        };
        store.Save(vault);
        var profile = Assert.Single(store.Load().Accounts).Profile;
        Assert.Equal("https://cdn.example.test/skins/test.png", profile.SkinUrl); Assert.Equal("slim", profile.SkinVariant);
        Assert.Equal("https://cdn.example.test/capes/test.png", Assert.Single(profile.Capes).Url); Assert.Equal(capeId, profile.ActiveCapeId);
    }

    [Fact]
    public void Missing_vault_is_empty_and_uses_project_client_id()
    {
        using var f = new ImporterFixtures();
        var vault = new WindowsAccountStore(Path.Combine(f.Root, "accounts.dat")).Load();
        Assert.Empty(vault.Accounts);
        Assert.Equal("fab70daf-af0d-423d-9468-6b42edce3c81", vault.MicrosoftClientId);
    }

    private static AccountVault Sample() => new()
    {
        SelectedAccountId = "test-account",
        Accounts = [new(new("test-account", AccountKind.Microsoft, "WonderTest", "00112233445566778899aabbccddeeff", ClientId: "fab70daf-af0d-423d-9468-6b42edce3c81"),
            new() { AccessToken = "synthetic-access-secret", RefreshToken = "synthetic-refresh-secret", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) })]
    };
}
