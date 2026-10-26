// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Wonder.Core;

namespace Wonder.Application;

public sealed partial class AccountService
{
    public void SetServerWebsite(string id, string address) => Mutate(vault =>
    {
        var index = vault.Servers.FindIndex(x => x.Id == id);
        if (index < 0) throw new AccountAuthenticationException("认证服务器不存在。");
        address = address.Trim();
        if (address.Length > 2048 || address.Any(char.IsControl) || (address.Length > 0 &&
            (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0)))
            throw new AccountAuthenticationException("皮肤管理网站须为不含用户名和密码的 HTTPS 地址。");
        vault.Servers[index] = vault.Servers[index] with { Homepage = address.Length == 0 ? null : new Uri(address).AbsoluteUri };
    });

    public string? GetSkinManagementWebsite(string accountId)
    {
        lock (_gate)
        {
            var profile = _vault.Accounts.FirstOrDefault(x => x.Profile.Id == accountId)?.Profile;
            if (profile?.Kind != AccountKind.Yggdrasil || profile.ServerUrl is null) return null;
            var server = _vault.Servers.FirstOrDefault(x => ServersEqual(x.ApiUrl, profile.ServerUrl));
            // Yggdrasil doesn't standardize an upload endpoint. Open the declared site in the browser.
            return server?.Homepage ?? new Uri(profile.ServerUrl).GetLeftPart(UriPartial.Authority) + "/";
        }
    }
}
