// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Security.Cryptography;
using System.Text;
using Wonder.Core;

namespace Wonder.Application;

public sealed partial class AccountService
{
    private long _discoveryRevision;
    public AuthenticationServer? PendingServer { get; private set; }
    public IReadOnlyList<AuthenticationServer> Servers { get { lock (_gate) return _vault.Servers.ToArray(); } }

    public Task DiscoverServerAsync(string address) => Operate(async ct =>
    {
        if (_directory is null) throw new AccountAuthenticationException("此版本未配置认证服务器目录服务。");
        long revision;
        lock (_gate) { PendingServer = null; revision = ++_discoveryRevision; }
        Status = "正在读取认证服务器信息…"; Notify();
        var server = await _directory.DiscoverAsync(address, ct).ConfigureAwait(false);
        ValidateServers([server]);
        lock (_gate)
        {
            ct.ThrowIfCancellationRequested();
            if (_discoveryRevision != revision) throw new OperationCanceledException();
            PendingServer = server with { ApiUrl = NormalizeServer(server.ApiUrl) };
            Status = "请核对认证服务器名称和实际 API 地址，然后确认添加。";
        }
    });

    public void CancelServerDiscovery()
    {
        lock (_gate) { _discoveryRevision++; PendingServer = null; }
        Notify();
    }

    public void ConfirmServer(string? name = null)
    {
        Mutate(vault =>
        {
            var pending = PendingServer ?? throw new AccountAuthenticationException("请先读取认证服务器信息。");
            var candidate = pending with { Name = ServerName(name ?? pending.Name) };
            var existing = vault.Servers.FindIndex(x => NormalizeServer(x.ApiUrl) == NormalizeServer(candidate.ApiUrl));
            if (existing >= 0) vault.Servers[existing] = candidate with { Id = vault.Servers[existing].Id };
            else vault.Servers.Add(candidate);
        });
        lock (_gate) { if (Error is null && !IsBusy && !IsReadBlocked) PendingServer = null; }
        Notify();
    }

    public void RenameServer(string id, string name) => Mutate(vault =>
    {
        var index = vault.Servers.FindIndex(x => x.Id == id);
        if (index < 0) throw new AccountAuthenticationException("认证服务器不存在。");
        vault.Servers[index] = vault.Servers[index] with { Name = ServerName(name) };
    });

    public void RemoveServer(string id) => Mutate(vault =>
    {
        var server = vault.Servers.FirstOrDefault(x => x.Id == id) ?? throw new AccountAuthenticationException("认证服务器不存在。");
        if (vault.Accounts.Any(x => x.Profile.Kind == AccountKind.Yggdrasil && ServersEqual(x.Profile.ServerUrl, server.ApiUrl)))
            throw new AccountAuthenticationException("仍有账户使用此认证服务器，请先移除这些账户。");
        vault.Servers.Remove(server);
    });

    public void RenameOffline(string id, string username) => Mutate(vault =>
    {
        var index = vault.Accounts.FindIndex(x => x.Profile.Id == id && x.Profile.Kind == AccountKind.Offline);
        if (index < 0) throw new AccountAuthenticationException("只能修改离线账户的玩家名。");
        username = username.Trim(); AccountLimits.ValidateUsernameLength(username); InstanceCatalog.ValidateOfflineUsername(username);
        var current = vault.Accounts[index];
        var uuid = OfflineDefaultSkins.CreateUuid(username, current.Profile.OfflineSkinId);
        if (vault.Accounts.Any(x => x.Profile.Id != id && x.Profile.Kind == AccountKind.Offline && x.Profile.Username == username))
            throw new AccountAuthenticationException("已存在同名离线账户，请直接选择已有账户。");
        vault.Accounts[index] = new(current.Profile with { Username = username, Uuid = uuid }, current.Credentials);
    });

    public string GetOfflineSkinUuid(string id, string? skinId)
    {
        var current = GetAccount(id, AccountKind.Offline);
        return OfflineDefaultSkins.CreateUuid(current.Profile.Username, skinId);
    }

    public void SetOfflineSkin(string id, string? skinId) => Mutate(vault =>
    {
        var index = vault.Accounts.FindIndex(x => x.Profile.Id == id && x.Profile.Kind == AccountKind.Offline);
        if (index < 0) throw new AccountAuthenticationException("只能为离线账户选择默认皮肤。");
        var current = vault.Accounts[index];
        var uuid = OfflineDefaultSkins.CreateUuid(current.Profile.Username, skinId);
        vault.Accounts[index] = new(current.Profile with { Uuid = uuid, OfflineSkinId = skinId,
            SkinVariant = skinId is null ? OfflineDefaultSkins.ForUuid(uuid).Variant : OfflineDefaultSkins.Get(skinId).Variant }, current.Credentials);
    });

    public Task RefreshAsync(string id) => Operate(async ct =>
    {
        var current = GetAccount(id);
        if (current.Profile.Kind == AccountKind.Offline) { Status = "离线账户无需网络验证。"; return; }
        Status = "正在刷新账户资料…"; Notify();
        await RefreshAccount(current, ct).ConfigureAwait(false);
    });

    public Task UploadSkinAsync(string id, byte[] png, string variant) => ChangeAppearance(id,
        (appearance, account, ct) => appearance.UploadSkinAsync(account.Profile, account.Credentials, png, variant, ct));

    public Task ResetSkinAsync(string id) => ChangeAppearance(id,
        (appearance, account, ct) => appearance.ResetSkinAsync(account.Profile, account.Credentials, ct));

    public Task SetCapeAsync(string id, string? capeId) => ChangeAppearance(id,
        (appearance, account, ct) => appearance.SetCapeAsync(account.Profile, account.Credentials, capeId, ct));

    private Task ChangeAppearance(string id, Func<IMicrosoftAccountAppearance, AuthenticatedAccount, CancellationToken, Task<AccountProfile>> change) => Operate(async ct =>
    {
        var current = GetAccount(id, AccountKind.Microsoft);
        if (_microsoft is not IMicrosoftAccountAppearance appearance) throw new AccountAuthenticationException("此版本未提供正版外观服务。");
        Status = "正在验证账户并提交外观修改…"; Notify();
        // Persist rotated refresh credentials even when the following remote appearance request fails.
        current = await RefreshAccount(current, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var profile = await change(appearance, current, ct).ConfigureAwait(false);
        VerifyIdentity(current.Profile, profile);
        SaveAccount(new(profile with { Id = current.Profile.Id, LastValidatedAt = DateTimeOffset.UtcNow }, current.Credentials), ct, select: false);
        Status = "外观已更新。正在运行的游戏可能需要重新登录才能显示。";
    });

    private async Task<AuthenticatedAccount> RefreshAccount(AuthenticatedAccount current, CancellationToken ct)
    {
        var refreshed = current.Profile.Kind == AccountKind.Microsoft
            ? await _microsoft.RefreshAsync(current.Profile, current.Credentials, ct).ConfigureAwait(false)
            : await _yggdrasil.RefreshAsync(current.Profile, current.Credentials, ct).ConfigureAwait(false);
        VerifyIdentity(current.Profile, refreshed.Profile);
        if (string.IsNullOrEmpty(refreshed.Credentials.AccessToken) || refreshed.Credentials.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new AccountAuthenticationException("账户会话已过期，请重新登录。");
        var result = new AuthenticatedAccount(refreshed.Profile with { Id = current.Profile.Id, LastValidatedAt = DateTimeOffset.UtcNow }, refreshed.Credentials);
        // A successful rotation may revoke the previous token. Commit only after identity and
        // session validation, but finish this commit even when the caller cancels preparation.
        SaveAccount(result, CancellationToken.None, select: false, preserveCredentialsOnFailure: true);
        ct.ThrowIfCancellationRequested();
        if (current.Profile.Kind == AccountKind.Yggdrasil && _yggdrasil is IYggdrasilAccountAppearance appearance)
        {
            var profile = await appearance.ReadAppearanceAsync(result.Profile, ct).ConfigureAwait(false);
            VerifyIdentity(result.Profile, profile);
            if (profile != result.Profile)
            {
                result = new(profile with { Id = current.Profile.Id }, result.Credentials);
                SaveAccount(result, ct, select: false);
            }
            ct.ThrowIfCancellationRequested();
        }
        return result;
    }

    private AuthenticatedAccount GetAccount(string id, AccountKind? kind = null)
    {
        lock (_gate)
        {
            var account = _vault.Accounts.FirstOrDefault(x => x.Profile.Id == id) ?? throw new AccountAuthenticationException("账户不存在。");
            if (kind is not null && account.Profile.Kind != kind) throw new AccountAuthenticationException("此操作不适用于该账户类型。");
            return account;
        }
    }

    private static void VerifyIdentity(AccountProfile expected, AccountProfile received, bool allowNewMicrosoftApplication = false)
    {
        if (received.Kind != expected.Kind || !string.Equals(received.Uuid, expected.Uuid, StringComparison.OrdinalIgnoreCase) ||
            !ServersEqual(received.ServerUrl, expected.ServerUrl) ||
            (!allowNewMicrosoftApplication && received.ClientId != expected.ClientId))
            throw new AccountAuthenticationException("认证返回的身份与原账户不一致，请使用原账户重新登录。");
    }

    private static void VerifyThirdPartyLogin(ThirdPartyLogin login, AuthenticatedAccount account)
    {
        if (account.Profile.Kind != AccountKind.Yggdrasil || !ServersEqual(login.ServerUrl, account.Profile.ServerUrl) ||
            !login.Profiles.Any(x => string.Equals(x.Id, account.Profile.Uuid, StringComparison.OrdinalIgnoreCase)))
            throw new AccountAuthenticationException("认证服务器返回的角色不匹配。");
    }

    private static bool ServersEqual(string? first, string? second) => first is null || second is null
        ? first == second : NormalizeServer(first) == NormalizeServer(second);

    private static string NormalizeServer(string value)
    {
        if (value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new AccountAuthenticationException("认证 API 地址必须是有效的 HTTPS 根地址。");
        return uri.AbsoluteUri.TrimEnd('/') + "/";
    }

    private static string ServerName(string value)
    {
        value = value.Trim();
        if (value.Length is < 1 or > 120 || value.Any(char.IsControl)) throw new AccountAuthenticationException("服务器名称须为 1–120 个可显示字符。");
        return value;
    }

    private static void ImportAccountServers(AccountVault vault)
    {
        foreach (var account in vault.Accounts.Where(x => x.Profile.Kind == AccountKind.Yggdrasil))
        {
            var url = NormalizeServer(account.Profile.ServerUrl!);
            if (vault.Servers.Any(x => NormalizeServer(x.ApiUrl) == url)) continue;
            var id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..32];
            vault.Servers.Add(new(id, new Uri(url).Host, url));
        }
    }

    private static void ValidateServers(List<AuthenticationServer>? servers)
    {
        if (servers is null || servers.Count > 256) throw new InvalidDataException();
        var ids = new HashSet<string>(StringComparer.Ordinal); var urls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var server in servers)
        {
            if (server is null || string.IsNullOrWhiteSpace(server.Id) || server.Id.Length > 128 || server.Id.Any(char.IsControl) ||
                !ids.Add(server.Id) || !urls.Add(NormalizeServer(server.ApiUrl))) throw new InvalidDataException();
            ServerName(server.Name);
            foreach (var link in new[] { server.Homepage, server.RegistrationUrl })
                if (link is not null && (link.Length > 2048 || !Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0))
                    throw new InvalidDataException();
        }
    }

    private static void ValidateAppearance(AccountProfile profile)
    {
        if (profile.SkinVariant is not ("classic" or "slim") || profile.Capes is null || profile.Capes.Count > 128) throw new InvalidDataException();
        void Texture(string? url)
        {
            if (url is null) return;
            if (url.Length > 2048 || url.Any(char.IsControl) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                uri.Host.Length == 0 || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0) throw new InvalidDataException();
            if (profile.Kind != AccountKind.Yggdrasil && (uri.Host != "textures.minecraft.net" || !uri.IsDefaultPort ||
                !uri.AbsolutePath.StartsWith("/texture/", StringComparison.Ordinal))) throw new InvalidDataException();
        }
        Texture(profile.SkinUrl);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cape in profile.Capes)
        {
            if (cape is null || string.IsNullOrWhiteSpace(cape.Url) || !Guid.TryParse(cape.Id, out _) || !ids.Add(cape.Id) || string.IsNullOrWhiteSpace(cape.Name) ||
                cape.Name.Length > 80 || cape.Name.Any(char.IsControl)) throw new InvalidDataException();
            Texture(cape.Url);
        }
        if (profile.ActiveCapeId is not null && !ids.Contains(profile.ActiveCapeId)) throw new InvalidDataException();
    }
}
