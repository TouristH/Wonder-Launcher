// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

namespace Wonder.Core;

public enum AccountKind { Offline, Microsoft, Yggdrasil }
public static class WonderMicrosoftApplication
{
    public const string ClientId = "fab70daf-af0d-423d-9468-6b42edce3c81";
}

public sealed record AccountProfile(string Id, AccountKind Kind, string Username, string Uuid,
    string? ServerUrl = null, string? ClientId = null)
{
    public string? SkinUrl { get; init; }
    public string SkinVariant { get; init; } = "classic";
    public IReadOnlyList<AccountCape> Capes { get; init; } = [];
    public string? ActiveCapeId { get; init; }
    public DateTimeOffset? LastValidatedAt { get; init; }
    public string? OfflineSkinId { get; init; }
}
public sealed record AccountCape(string Id, string Name, string Url);
public sealed record AuthenticationServer(string Id, string Name, string ApiUrl, string? Homepage = null, string? RegistrationUrl = null);
public sealed record AuthenticationAdapter(string Path, string Sha256);
public sealed record PlayerProfile(string Id, string Name);
public sealed record DeviceCodePrompt(string UserCode, Uri VerificationUri, DateTimeOffset ExpiresAt);

// Deliberately not records: generated ToString must never print credentials.
public sealed class AccountCredentials
{
    public string AccessToken { get; init; } = "";
    public string RefreshToken { get; init; } = "";
    public string ClientToken { get; init; } = "";
    public string Xuid { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }
}

public sealed class AuthenticatedAccount(AccountProfile profile, AccountCredentials credentials)
{
    public AccountProfile Profile { get; } = profile;
    public AccountCredentials Credentials { get; } = credentials;
}

public sealed class ThirdPartyLogin(string serverUrl, IReadOnlyList<PlayerProfile> profiles,
    string? selectedProfileId, AccountCredentials credentials)
{
    public string ServerUrl { get; } = serverUrl;
    public IReadOnlyList<PlayerProfile> Profiles { get; } = profiles;
    public string? SelectedProfileId { get; } = selectedProfileId;
    public AccountCredentials Credentials { get; } = credentials;
}

public sealed class AccountVault
{
    public int Version { get; set; } = 1;
    public List<AuthenticatedAccount> Accounts { get; set; } = [];
    public string? SelectedAccountId { get; set; }
    public string MicrosoftClientId { get; set; } = WonderMicrosoftApplication.ClientId;
    public List<AuthenticationServer> Servers { get; set; } = [];
    public string? AuthlibInjectorPath { get; set; }
    public string? AuthlibInjectorSha256 { get; set; }
}

public sealed class GameSession(AccountKind kind, string username, string uuid, string accessToken,
    string clientId = "", string xuid = "", string? serverUrl = null,
    string? authlibInjectorPath = null, string? authlibInjectorSha256 = null, string? offlineSkinId = null)
{
    public AccountKind Kind { get; } = kind;
    public string Username { get; } = username;
    public string Uuid { get; } = uuid;
    public string AccessToken { get; } = accessToken;
    public string ClientId { get; } = clientId;
    public string Xuid { get; } = xuid;
    public string? ServerUrl { get; } = serverUrl;
    public string? AuthlibInjectorPath { get; } = authlibInjectorPath;
    public string? AuthlibInjectorSha256 { get; } = authlibInjectorSha256;
    public string? OfflineSkinId { get; } = offlineSkinId;
}
