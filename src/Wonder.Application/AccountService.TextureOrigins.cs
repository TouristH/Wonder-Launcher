// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Wonder.Core;

namespace Wonder.Application;

public sealed partial class AccountService
{
    /// <summary>Only an explicit local user action may change an account's private-network texture origins.</summary>
    public void SetPrivateTextureOrigin(string accountId, string origin, bool allowed) => Mutate(vault =>
    {
        var index = vault.Accounts.FindIndex(x => x.Profile.Id == accountId && x.Profile.Kind == AccountKind.Yggdrasil);
        if (index < 0) throw new AccountAuthenticationException("只能为当前第三方账户设置纹理来源许可。");
        string normalized;
        try { normalized = TextureOrigin.Normalize(origin); }
        catch (ArgumentException) { throw new AccountAuthenticationException("请输入明确的 HTTPS 来源，例如 https://textures.example:443；不得包含路径、通配符或凭据。"); }
        var account = vault.Accounts[index];
        var origins = account.Profile.AllowedPrivateTextureOrigins.ToList();
        if (allowed && !origins.Contains(normalized, StringComparer.Ordinal)) origins.Add(normalized);
        if (!allowed) origins.Remove(normalized);
        if (origins.Count > TextureOrigin.MaximumAllowedOrigins) throw new AccountAuthenticationException("每个账户最多允许 32 个纹理来源。");
        vault.Accounts[index] = new(account.Profile with { AllowedPrivateTextureOrigins = Array.AsReadOnly(origins.ToArray()) }, account.Credentials);
    });
}
