// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Core;

namespace Wonder.Application;

public interface IAccountStore
{
    AccountVault Load();
    void Save(AccountVault vault);
}

public interface IMicrosoftAuthenticator
{
    Task<AuthenticatedAccount> SignInAsync(string clientId, Action<DeviceCodePrompt> prompt, CancellationToken ct = default);
    Task<AuthenticatedAccount> RefreshAsync(AccountProfile profile, AccountCredentials credentials, CancellationToken ct = default);
}

public interface IYggdrasilAuthenticator
{
    Task<ThirdPartyLogin> SignInAsync(string serverUrl, string username, string password, CancellationToken ct = default);
    Task<AuthenticatedAccount> SelectProfileAsync(ThirdPartyLogin login, string profileId, CancellationToken ct = default);
    // A validated rotation must reach the caller even if cancellation arrives after the response.
    // The caller commits these credentials before starting cancellable, optional appearance work.
    Task<AuthenticatedAccount> RefreshAsync(AccountProfile profile, AccountCredentials credentials, CancellationToken ct = default);
    Task InvalidateAsync(AccountProfile profile, AccountCredentials credentials, CancellationToken ct = default);
}

public interface IYggdrasilAccountAppearance
{
    Task<AccountProfile> ReadAppearanceAsync(AccountProfile profile, CancellationToken ct = default);
}

public interface ILaunchAccountProvider
{
    Task<GameSession> GetSelectedSessionAsync(CancellationToken ct = default);
}

public interface IMicrosoftAccountAppearance
{
    Task<AccountProfile> UploadSkinAsync(AccountProfile profile, AccountCredentials credentials, byte[] png, string variant, CancellationToken ct = default);
    Task<AccountProfile> ResetSkinAsync(AccountProfile profile, AccountCredentials credentials, CancellationToken ct = default);
    Task<AccountProfile> SetCapeAsync(AccountProfile profile, AccountCredentials credentials, string? capeId, CancellationToken ct = default);
}

public interface IAuthenticationServerDirectory
{
    Task<AuthenticationServer> DiscoverAsync(string address, CancellationToken ct = default);
}

public interface IAuthenticationAdapterProvider
{
    Task<AuthenticationAdapter> EnsureAsync(CancellationToken ct = default);
}

public sealed class AccountAuthenticationException(string message) : Exception(message);
