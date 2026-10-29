// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Core;

namespace Wonder.Application;

public sealed partial class AccountService : ILaunchAccountProvider
{
    private readonly object _gate = new();
    private readonly IAccountStore _store;
    private readonly IMicrosoftAuthenticator _microsoft;
    private readonly IYggdrasilAuthenticator _yggdrasil;
    private readonly IAuthenticationServerDirectory? _directory;
    private readonly IAuthenticationAdapterProvider? _adapter;
    private AccountVault _vault = new();
    private CancellationTokenSource? _operation;
    private bool _operationIsLogin;
    private ThirdPartyLogin? _pendingLogin;
    private string? _pendingReauthenticateId;
    private AuthenticatedAccount? _pendingCredentialSave;
    public event EventHandler? Changed;
    public string? Error { get; private set; }
    public string Status { get; private set; } = "添加账户后即可选择启动身份。";
    public bool IsReadBlocked { get; private set; }
    public bool IsBusy { get { lock (_gate) return _operation is not null; } }
    public bool IsLoggingIn { get { lock (_gate) return _operation is not null && _operationIsLogin; } }
    public DeviceCodePrompt? DevicePrompt { get; private set; }
    public IReadOnlyList<AccountProfile> Accounts { get { lock (_gate) return _vault.Accounts.Select(x => x.Profile).ToArray(); } }
    public AccountProfile? SelectedAccount { get { lock (_gate) return _vault.Accounts.FirstOrDefault(x => x.Profile.Id == _vault.SelectedAccountId)?.Profile; } }
    public IReadOnlyList<PlayerProfile> PendingProfiles { get { lock (_gate) return _pendingLogin?.Profiles.ToArray() ?? []; } }
    public string MicrosoftClientId => WonderMicrosoftApplication.ClientId;
    public string? AuthlibInjectorPath { get { lock (_gate) return _vault.AuthlibInjectorPath; } }

    public AccountService(IAccountStore store, IMicrosoftAuthenticator microsoft, IYggdrasilAuthenticator yggdrasil,
        IAuthenticationServerDirectory? directory = null, IAuthenticationAdapterProvider? adapter = null)
    { _store = store; _microsoft = microsoft; _yggdrasil = yggdrasil; _directory = directory; _adapter = adapter; Reload(); }

    public void Reload()
    {
        lock (_gate)
        {
            if (IsBusy) return;
            try { RetryPendingCredentialSave(); var vault = _store.Load(); Validate(vault); _vault = Copy(vault); ImportAccountServers(_vault); IsReadBlocked = false; Error = null; }
            catch (AccountAuthenticationException ex) when (_pendingCredentialSave is not null) { Error = ex.Message; }
            catch (AccountAuthenticationException ex) { IsReadBlocked = true; Error = ex.Message; }
            catch (AccountLimitException ex) { IsReadBlocked = true; Error = ex.Message + "原账户库已保留，请修复后重新读取。"; }
            catch { IsReadBlocked = true; Error = "账户库读取或解密失败，原文件已保留。请使用原系统用户，并检查系统凭据保护、桌面密钥环或 macOS 钥匙串，或修复文件后重新读取。"; }
        }
        Notify();
    }

    public void AddOffline(string username) => Mutate(vault =>
    {
        username = username.Trim(); AccountLimits.ValidateUsernameLength(username); InstanceCatalog.ValidateOfflineUsername(username);
        var uuid = OfflineUuid(username);
        var existing = vault.Accounts.FirstOrDefault(x => x.Profile.Kind == AccountKind.Offline && x.Profile.Username == username);
        var account = existing ?? new AuthenticatedAccount(new(Guid.NewGuid().ToString("N"), AccountKind.Offline, username, uuid), new());
        if (existing is null) vault.Accounts.Add(account);
        vault.SelectedAccountId = account.Profile.Id;
    });

    public void Select(string id) => Mutate(vault =>
    {
        if (!vault.Accounts.Any(x => x.Profile.Id == id)) throw new AccountAuthenticationException("账户不存在。");
        vault.SelectedAccountId = id;
    });

    public void ConfigureInjector(string path, string sha256) => Mutate(vault =>
    {
        if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
            sha256.Length != 64 || !sha256.All(char.IsAsciiHexDigit))
            throw new AccountAuthenticationException("请选择可信来源的 authlib-injector JAR 文件。");
        vault.AuthlibInjectorPath = path; vault.AuthlibInjectorSha256 = sha256.ToUpperInvariant();
    });

    public Task SignInMicrosoftAsync(string? reauthenticateId = null) => Operate(async ct =>
    {
        ClearPending();
        var expected = reauthenticateId is null ? null : GetAccount(reauthenticateId, AccountKind.Microsoft);
        var account = await _microsoft.SignInAsync(MicrosoftClientId, prompt =>
        {
            lock (_gate)
            {
                if (ct.IsCancellationRequested) return;
                DevicePrompt = prompt; Status = "在微软官方页面输入设备码，确认应用名称为 Wonder Launcher。";
            }
            Notify();
        }, ct).ConfigureAwait(false);
        if (account.Profile.Kind != AccountKind.Microsoft || account.Profile.ClientId != MicrosoftClientId || account.Profile.ServerUrl is not null)
            throw new AccountAuthenticationException("微软登录返回的应用或账户类型不匹配。");
        if (expected is not null) VerifyIdentity(expected.Profile, account.Profile, allowNewMicrosoftApplication: true);
        SaveAccount(account, ct, select: expected is null);
    }, login: true);

    public Task SignInThirdPartyAsync(string serverUrl, string username, string password, string? reauthenticateId = null) => Operate(async ct =>
    {
        ClearPending();
        var expected = reauthenticateId is null ? null : GetAccount(reauthenticateId, AccountKind.Yggdrasil);
        if (expected is not null && NormalizeServer(serverUrl) != NormalizeServer(expected.Profile.ServerUrl!))
            throw new AccountAuthenticationException("重新登录必须使用原账户的认证服务器。");
        var login = await _yggdrasil.SignInAsync(serverUrl, username, password, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (NormalizeServer(login.ServerUrl) != NormalizeServer(serverUrl))
            throw new AccountAuthenticationException("认证服务器返回的地址不匹配。");
        if (login.Profiles.Count == 1)
        {
            var account = await _yggdrasil.SelectProfileAsync(login, login.Profiles[0].Id, ct).ConfigureAwait(false);
            VerifyThirdPartyLogin(login, account);
            if (expected is not null) VerifyIdentity(expected.Profile, account.Profile);
            SaveAccount(account, ct, select: expected is null);
        }
        else
        {
            lock (_gate)
            {
                ct.ThrowIfCancellationRequested(); _pendingLogin = login; _pendingReauthenticateId = reauthenticateId;
                Status = "请选择此第三方账户的游戏角色。";
            }
        }
    }, login: true);

    public Task CompleteThirdPartyAsync(string profileId) => Operate(async ct =>
    {
        ThirdPartyLogin login; AuthenticatedAccount? expected;
        lock (_gate)
        {
            login = _pendingLogin ?? throw new AccountAuthenticationException("请先登录第三方账户。");
            expected = _pendingReauthenticateId is null ? null : GetAccount(_pendingReauthenticateId, AccountKind.Yggdrasil);
        }
        if (expected is not null && !string.Equals(expected.Profile.Uuid, profileId, StringComparison.OrdinalIgnoreCase))
            throw new AccountAuthenticationException("请选择正在重新登录的原游戏角色。");
        var account = await _yggdrasil.SelectProfileAsync(login, profileId, ct).ConfigureAwait(false);
        VerifyThirdPartyLogin(login, account);
        if (expected is not null) VerifyIdentity(expected.Profile, account.Profile);
        SaveAccount(account, ct, select: expected is null); ClearPending();
    }, login: true);

    public Task RemoveAsync(string id) => Operate(async ct =>
    {
        AuthenticatedAccount account;
        lock (_gate)
        {
            account = _vault.Accounts.FirstOrDefault(x => x.Profile.Id == id) ?? throw new AccountAuthenticationException("账户不存在。");
            var next = Copy(_vault); next.Accounts.RemoveAll(x => x.Profile.Id == id);
            if (next.SelectedAccountId == id) next.SelectedAccountId = next.Accounts.FirstOrDefault()?.Profile.Id;
            Persist(next); Status = "账户和本机保存的凭据已移除。";
        }
        if (account.Profile.Kind == AccountKind.Yggdrasil)
        {
            try { await _yggdrasil.InvalidateAsync(account.Profile, account.Credentials, ct).ConfigureAwait(false); }
            catch { Status = "本机账户已移除；服务器会话注销未确认，可在第三方网站撤销会话。"; }
        }
    });

    public async Task<GameSession> GetSelectedSessionAsync(CancellationToken ct = default)
    {
        var operation = Begin(ct);
        try
        {
            AuthenticatedAccount current;
            lock (_gate) current = _vault.Accounts.FirstOrDefault(x => x.Profile.Id == _vault.SelectedAccountId)
                ?? throw new AccountAuthenticationException("请先在账户管理中添加并选择账户。");
            if (current.Profile.Kind != AccountKind.Offline)
            {
                Status = "正在验证所选账户…"; Notify();
                current = await RefreshAccount(current, operation.Token).ConfigureAwait(false);
            }
            if (current.Profile.Kind == AccountKind.Yggdrasil && _adapter is not null)
            {
                Status = "正在准备第三方认证适配器…"; Notify();
                var adapter = await _adapter.EnsureAsync(operation.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    operation.Token.ThrowIfCancellationRequested();
                    var next = Copy(_vault); next.AuthlibInjectorPath = adapter.Path; next.AuthlibInjectorSha256 = adapter.Sha256; Persist(next);
                }
            }
            operation.Token.ThrowIfCancellationRequested();
            var p = current.Profile; var c = current.Credentials;
            if (p.Kind != AccountKind.Offline && (string.IsNullOrEmpty(c.AccessToken) || c.ExpiresAt <= DateTimeOffset.UtcNow))
                throw new AccountAuthenticationException("账户会话已过期，请重新登录。");
            lock (_gate) return new(p.Kind, p.Username, p.Uuid, p.Kind == AccountKind.Offline ? "0" : c.AccessToken,
                p.ClientId ?? "", c.Xuid, p.ServerUrl,
                p.Kind == AccountKind.Yggdrasil ? _vault.AuthlibInjectorPath : null,
                p.Kind == AccountKind.Yggdrasil ? _vault.AuthlibInjectorSha256 : null, p.OfflineSkinId);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Error = SafeError(ex); throw new AccountAuthenticationException(Error); }
        finally { End(operation); }
    }

    public void Cancel()
    {
        lock (_gate) { _operation?.Cancel(); _pendingLogin = null; _pendingReauthenticateId = null; DevicePrompt = null; PendingServer = null; }
        Notify();
    }

    public void CancelLogin()
    {
        lock (_gate)
        {
            if (_operationIsLogin) _operation?.Cancel();
            _pendingLogin = null; _pendingReauthenticateId = null; DevicePrompt = null;
        }
        Notify();
    }

    private async Task Operate(Func<CancellationToken, Task> action, bool login = false)
    {
        CancellationTokenSource operation;
        try { operation = Begin(login: login); }
        catch (AccountAuthenticationException ex) { Error = ex.Message; Notify(); return; }
        Notify();
        try { await action(operation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { Status = "操作已取消。"; }
        catch (Exception ex) { Error = SafeError(ex); }
        finally { End(operation); }
    }
    private CancellationTokenSource Begin(CancellationToken ct = default, bool login = false)
    {
        lock (_gate)
        {
            if (IsReadBlocked) throw new AccountAuthenticationException("账户库不可读取，已阻止修改与启动。");
            if (_operation is not null) throw new AccountAuthenticationException("账户操作正在进行，请稍后重试。");
            RetryPendingCredentialSave();
            Error = null; _operationIsLogin = login; return _operation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        }
    }
    private void End(CancellationTokenSource operation)
    {
        lock (_gate) { if (_operation == operation) { _operation = null; _operationIsLogin = false; } DevicePrompt = null; }
        operation.Dispose(); Notify();
    }
    private void ClearPending() { lock (_gate) { _pendingLogin = null; _pendingReauthenticateId = null; DevicePrompt = null; } }
    private void SaveAccount(AuthenticatedAccount account, CancellationToken ct, bool select = true, bool preserveCredentialsOnFailure = false)
    {
        lock (_gate)
        {
            ct.ThrowIfCancellationRequested();
            if (account.Profile.Kind != AccountKind.Offline)
                account = new(account.Profile with { LastValidatedAt = DateTimeOffset.UtcNow }, account.Credentials);
            var next = Copy(_vault);
            var index = next.Accounts.FindIndex(x => x.Profile.Kind == account.Profile.Kind &&
                string.Equals(x.Profile.Uuid, account.Profile.Uuid, StringComparison.OrdinalIgnoreCase) && ServersEqual(x.Profile.ServerUrl, account.Profile.ServerUrl));
            // Permissions belong to this local identity; authentication and appearance results cannot grant them.
            if (index >= 0)
            {
                var local = next.Accounts[index].Profile;
                account = new(account.Profile with { Id = local.Id, AllowedPrivateTextureOrigins = local.AllowedPrivateTextureOrigins }, account.Credentials);
                next.Accounts[index] = account;
            }
            else
            {
                account = new(account.Profile with { AllowedPrivateTextureOrigins = Array.AsReadOnly(Array.Empty<string>()) }, account.Credentials);
                next.Accounts.Add(account);
            }
            if (select) next.SelectedAccountId = account.Profile.Id;
            ImportAccountServers(next);
            Persist(next, preserveCredentialsOnFailure ? account : null);
            Status = (select ? "已选择 " : "已更新 ") + account.Profile.Username + " · " + KindLabel(account.Profile.Kind);
        }
    }
    private void Mutate(Action<AccountVault> change)
    {
        lock (_gate)
        {
            if (IsReadBlocked || IsBusy) return;
            try { RetryPendingCredentialSave(); var next = Copy(_vault); change(next); Persist(next); Error = null; Status = "账户设置已保存。"; }
            catch (Exception ex) { Error = SafeError(ex); }
        }
        Notify();
    }
    private void RetryPendingCredentialSave()
    {
        if (_pendingCredentialSave is { } account)
            SaveAccount(account, CancellationToken.None, select: false, preserveCredentialsOnFailure: true);
    }
    private void Persist(AccountVault vault, AuthenticatedAccount? rotatedAccount = null)
    {
        Validate(vault);
        try { _store.Save(Copy(vault)); }
        catch when (rotatedAccount is not null)
        {
            _pendingCredentialSave = rotatedAccount;
            throw new AccountAuthenticationException("新账户凭据已在服务器生效，但本机保存失败。请不要退出启动器，修复数据目录权限或磁盘空间后刷新账户或重试启动；将先重试保存。若已退出，请重新登录。");
        }
        _vault = Copy(vault);
        if (rotatedAccount is not null) _pendingCredentialSave = null;
    }
    private static string SafeError(Exception ex) => ex is AccountAuthenticationException or AccountLimitException ? ex.Message :
        ex is ArgumentException ? "输入无效，请检查用户名或服务器地址。" :
        "账户操作未完成，请检查网络、数据目录权限或重新登录。如已提交外观修改，请刷新账户核对。";
    private static AccountVault Copy(AccountVault value) => new()
    {
        Version = value.Version, Accounts = value.Accounts.Select(x => new AuthenticatedAccount(x.Profile with
        {
            AllowedPrivateTextureOrigins = Array.AsReadOnly(x.Profile.AllowedPrivateTextureOrigins.ToArray()),
            Capes = Array.AsReadOnly(x.Profile.Capes.ToArray())
        }, x.Credentials)).ToList(), SelectedAccountId = value.SelectedAccountId,
        MicrosoftClientId = WonderMicrosoftApplication.ClientId, Servers = [.. value.Servers],
        AuthlibInjectorPath = value.AuthlibInjectorPath, AuthlibInjectorSha256 = value.AuthlibInjectorSha256
    };
    private static void Validate(AccountVault value)
    {
        if (value.Version != 1 || value.Accounts is null || !Guid.TryParse(value.MicrosoftClientId, out var appId) || appId == Guid.Empty)
            throw new InvalidDataException();
        AccountLimits.ValidateAccountCount(value.Accounts.Count);
        ValidateServers(value.Servers);
        if ((value.AuthlibInjectorPath is null) != (value.AuthlibInjectorSha256 is null) ||
            (value.AuthlibInjectorPath is { } path && (!Path.IsPathFullyQualified(path) || !path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))) ||
            (value.AuthlibInjectorSha256 is { } hash && (hash.Length != 64 || !hash.All(char.IsAsciiHexDigit)))) throw new InvalidDataException();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var account in value.Accounts)
        {
            var p = account.Profile;
            if (p is null || account.Credentials is null) throw new InvalidDataException();
            AccountLimits.ValidateProfileIdLength(p.Id);
            AccountLimits.ValidateUsernameLength(p.Username);
            if (string.IsNullOrWhiteSpace(p.Id) || !ids.Add(p.Id) ||
                !Enum.IsDefined(p.Kind) || p.Uuid.Length != 32 || !p.Uuid.All(char.IsAsciiHexDigit) ||
                string.IsNullOrWhiteSpace(p.Username) || p.Username.Any(char.IsControl)) throw new InvalidDataException();
            if (p.Kind == AccountKind.Offline) { InstanceCatalog.ValidateOfflineUsername(p.Username); if (!OfflineDefaultSkins.IsValid(p.Username, p.Uuid, p.OfflineSkinId)) throw new InvalidDataException(); }
            else if (p.OfflineSkinId is not null) throw new InvalidDataException();
            if (p.Kind == AccountKind.Microsoft && (!Guid.TryParse(p.ClientId, out _) || p.ServerUrl is not null)) throw new InvalidDataException();
            if (p.Kind == AccountKind.Yggdrasil && (!Uri.TryCreate(p.ServerUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)) throw new InvalidDataException();
            ValidateAppearance(p);
            TextureOrigin.Validate(p);
        }
        if (value.SelectedAccountId is not null && !ids.Contains(value.SelectedAccountId)) throw new InvalidDataException();
    }
    public static string KindLabel(AccountKind kind) => kind switch { AccountKind.Microsoft => "微软正版", AccountKind.Yggdrasil => "第三方", _ => "离线" };
    public static string OfflineUuid(string username) => OfflineIdentity.CreateUuid(username);
    private void Notify()
    {
        if (Changed is not { } handlers) return;
        foreach (EventHandler handler in handlers.GetInvocationList()) try { handler(this, EventArgs.Empty); } catch { }
    }
}
