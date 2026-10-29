// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text.Json;
using Wonder.Application;
using Wonder.Core;

namespace Wonder.Infrastructure;

internal static class AccountVaultSerializer
{
    internal const int MaximumBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowDuplicateProperties = false, MaxDepth = 24
    };

    internal static AccountVault Deserialize(byte[] plaintext)
    {
        var vault = JsonSerializer.Deserialize<AccountVault>(plaintext, Options) ?? throw InvalidVault();
        Validate(vault);
        return vault;
    }

    internal static byte[] Serialize(AccountVault vault)
    {
        Validate(vault);
        return JsonSerializer.SerializeToUtf8Bytes(vault, Options);
    }

    internal static void Validate(AccountVault vault)
    {
        if (vault is null || vault.Version != 1 || vault.Accounts is null ||
            !Guid.TryParse(vault.MicrosoftClientId, out _) || vault.Servers is null || vault.Servers.Count > 256) throw InvalidVault();
        AccountLimits.ValidateAccountCount(vault.Accounts.Count);
        var serverIds = new HashSet<string>(StringComparer.Ordinal);
        var serverUrls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var server in vault.Servers)
        {
            if (server is null || !SafeText(server.Id, 128) || !serverIds.Add(server.Id) || !SafeText(server.Name, 120)) throw InvalidVault();
            try
            {
                if (server.ApiUrl != YggdrasilAuthenticator.NormalizeServerUrl(server.ApiUrl) || !serverUrls.Add(server.ApiUrl)) throw InvalidVault();
            }
            catch (AccountAuthenticationException) { throw InvalidVault(); }
            foreach (var link in new[] { server.Homepage, server.RegistrationUrl })
                if (link is not null && (!SafeText(link, 2048) || !Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0)) throw InvalidVault();
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in vault.Accounts)
        {
            if (entry?.Profile is not { } profile || entry.Credentials is not { } credentials) throw InvalidVault();
            AccountLimits.ValidateProfileIdLength(profile.Id);
            AccountLimits.ValidateUsernameLength(profile.Username);
            TextureOrigin.Validate(profile);
            if (!SafeText(profile.Id, AccountLimits.MaximumProfileIdLength) || !ids.Add(profile.Id) || !Enum.IsDefined(profile.Kind) ||
                !SafeText(profile.Username, AccountLimits.MaximumUsernameLength) || !ValidUuid(profile.Uuid)) throw InvalidVault();
            if (profile.Kind == AccountKind.Microsoft && (!Guid.TryParse(profile.ClientId, out _) || profile.ServerUrl is not null)) throw InvalidVault();
            if (profile.Kind == AccountKind.Yggdrasil)
            {
                try { if (profile.ServerUrl != YggdrasilAuthenticator.NormalizeServerUrl(profile.ServerUrl!)) throw InvalidVault(); }
                catch (AccountAuthenticationException) { throw InvalidVault(); }
            }
            if (profile.Kind == AccountKind.Offline && (!string.IsNullOrEmpty(credentials.AccessToken) || !string.IsNullOrEmpty(credentials.RefreshToken))) throw InvalidVault();
            if (profile.Kind == AccountKind.Offline && !OfflineDefaultSkins.IsValid(profile.Username, profile.Uuid, profile.OfflineSkinId)) throw InvalidVault();
            if (profile.Kind != AccountKind.Offline && profile.OfflineSkinId is not null) throw InvalidVault();
            if (!Bounded(credentials.AccessToken, 65536) || !Bounded(credentials.RefreshToken, 65536) ||
                !Bounded(credentials.ClientToken, 65536) || !Bounded(credentials.Xuid, 128)) throw InvalidVault();
            if (profile.SkinVariant is not ("classic" or "slim") || profile.Capes is null || profile.Capes.Count > 128) throw InvalidVault();
            try
            {
                string? TextureUrl(string? url) => profile.Kind == AccountKind.Yggdrasil
                    ? AccountTextureClient.ThirdPartyTextureUrl(url) : MicrosoftAuthenticator.OfficialTextureUrl(url);
                if (profile.SkinUrl is { } skin && (skin.Length > 2048 || TextureUrl(skin) != skin)) throw InvalidVault();
                var capeIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var cape in profile.Capes)
                    if (cape is null || !Guid.TryParse(cape.Id, out _) || !capeIds.Add(cape.Id) || !SafeText(cape.Name, 80) ||
                        !SafeText(cape.Url, 2048) || TextureUrl(cape.Url) != cape.Url) throw InvalidVault();
                if (profile.ActiveCapeId is not null && !capeIds.Contains(profile.ActiveCapeId)) throw InvalidVault();
            }
            catch (AccountAuthenticationException) { throw InvalidVault(); }
        }
        if (vault.SelectedAccountId is not null && !ids.Contains(vault.SelectedAccountId)) throw InvalidVault();
        if (vault.AuthlibInjectorPath is { } agent && (!SafeText(agent, 32767) || !Path.IsPathFullyQualified(agent))) throw InvalidVault();
        if (vault.AuthlibInjectorSha256 is { } hash && (hash.Length != 64 || !hash.All(char.IsAsciiHexDigit))) throw InvalidVault();
        if ((vault.AuthlibInjectorPath is null) != (vault.AuthlibInjectorSha256 is null)) throw InvalidVault();
    }

    private static bool Bounded(string? value, int max) => value is not null && value.Length <= max && !value.Any(char.IsControl);
    private static bool SafeText(string? value, int max) => !string.IsNullOrWhiteSpace(value) && Bounded(value, max);
    private static bool ValidUuid(string? value) => value is { Length: 32 } && value.All(char.IsAsciiHexDigit);
    private static InvalidDataException InvalidVault() => new("账户库无法读取或格式无效，原文件已保留。请先备份文件并检查当前用户的凭据保护服务。");

}
