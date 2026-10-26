// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Wonder.Core;

namespace Wonder.Desktop;

internal sealed partial class AccountManagerWindow
{
    private TabControl? _loginTabs;
    private TextBox? _password;
    private SelectableTextBlock? _deviceCode;
    private StackPanel? _devicePanel, _profiles;
    private TextBlock? _deviceExpiry, _serverAddress;
    private ComboBox? _serverChoices;
    private AuthenticationServer[] _loginServers = [];
    private string? _openedCode;
    private StackPanel? _serverList, _serverPending;

    private Control BuildAddPage()
    {
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var header = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 14) };
        var target = _service.Accounts.FirstOrDefault(x => x.Id == _reauthenticateId);
        header.Children.Add(Heading(target is null ? "添加游戏账户" : "重新登录 · " + target.Username));
        header.Children.Add(PageStyles.Hint(target is null ? "选择一种登录方式，凭据由 Windows 当前用户加密保存。" : "请登录原账户。只有身份验证一致后才会更新本机凭据。"));
        body.Children.Add(header);
        _loginTabs = new TabControl { ItemsSource = new[]
        {
            new TabItem { Header = "微软正版", Content = MicrosoftPage(), IsEnabled = target is null || target.Kind == AccountKind.Microsoft },
            new TabItem { Header = "第三方", Content = ThirdPartyPage(), IsEnabled = target is null || target.Kind == AccountKind.Yggdrasil },
            new TabItem { Header = "离线", Content = OfflinePage(), IsEnabled = target is null }
        }, SelectedIndex = target?.Kind == AccountKind.Yggdrasil ? 1 : 0 };
        Grid.SetRow(_loginTabs, 1); body.Children.Add(_loginTabs); return body;
    }
    private Control MicrosoftPage()
    {
        var body = Page(); body.Margin = new Thickness(2, 18, 8, 12);
        body.Children.Add(Heading("连接 Microsoft 账户"));
        body.Children.Add(PageStyles.Hint("使用拥有 Minecraft Java Edition 的微软账户。点击后会打开官方登录网页，Wonder 不接触你的微软密码。"));
        var steps = new StackPanel { Spacing = 12 };
        steps.Children.Add(new TextBlock { Text = "01   获取设备码并打开微软官网", Foreground = Palette.Accent, FontSize = 14 });
        steps.Children.Add(PageStyles.Hint("02   输入设备码，核对应用名称并授权"));
        steps.Children.Add(PageStyles.Hint("03   自动验证游戏所有权并读取角色"));
        body.Children.Add(PageStyles.Card(steps));
        body.Children.Add(IdleButton("登录 Microsoft 账户", "开始微软登录", async () =>
        {
            var targetId = _reauthenticateId; var revision = _pageRevision;
            await Run(() => _service.SignInMicrosoftAsync(targetId));
            if (revision == _pageRevision && !_closed && _service.Error is null && _service.SelectedAccount is not null && _service.Status != "操作已取消。")
            { _viewId = targetId ?? _service.SelectedAccount.Id; Navigate("details"); }
        }));
        _devicePanel = new StackPanel { Spacing = 12, IsVisible = false };
        _devicePanel.Children.Add(PageStyles.Hint("在微软官方网页输入以下设备码。只授权你主动发起的登录，不要向他人透露设备码。"));
        _deviceCode = new SelectableTextBlock { FontSize = 32, FontWeight = FontWeight.SemiBold, Foreground = Palette.Accent };
        AutomationProperties.SetName(_deviceCode, "微软登录设备码"); _devicePanel.Children.Add(_deviceCode);
        _deviceExpiry = PageStyles.Hint(""); _devicePanel.Children.Add(_deviceExpiry);
        var actions = new WrapPanel();
        actions.Children.Add(Button("复制设备码", "复制设备码", async () => { if (_service.DevicePrompt is { } prompt) await CopyText(prompt.UserCode); }));
        var reopen = Button("重新打开登录页", "打开微软官方登录页", OpenMicrosoft); reopen.Margin = new Thickness(8, 0, 0, 0); actions.Children.Add(reopen);
        _devicePanel.Children.Add(actions); body.Children.Add(_devicePanel);
        body.Children.Add(PageStyles.Hint("登录完成后自动返回账户详情。令牌仅供认证和游戏启动使用，不会写入桌面布局；删除账户会清除本机保存的会话。"));
        return Scroll(body);
    }
    private void OpenMicrosoft()
    {
        if (_service.DevicePrompt is not { } prompt || prompt.VerificationUri.Scheme != "https" ||
            prompt.VerificationUri.Host is not ("microsoft.com" or "www.microsoft.com" or "login.microsoftonline.com" or "login.live.com")) return;
        OpenWebsite(prompt.VerificationUri.AbsoluteUri);
    }
    private Control ThirdPartyPage()
    {
        var body = Page(); body.Margin = new Thickness(2, 18, 8, 12);
        body.Children.Add(Heading("自定义认证服务器"));
        body.Children.Add(PageStyles.Hint("选择你信任的 Yggdrasil 服务器，使用该服务自己的账号密码。不要在这里输入微软密码。"));
        _loginServers = _service.Servers.ToArray();
        body.Children.Add(PageStyles.Hint("认证服务器"));
        _serverChoices = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = _loginServers.Select(x => x.Name).ToArray() };
        AutomationProperties.SetName(_serverChoices, "选择认证服务器");
        var target = _service.Accounts.FirstOrDefault(x => x.Id == _reauthenticateId);
        _serverChoices.SelectedIndex = target is null ? (_loginServers.Length > 0 ? 0 : -1) : Array.FindIndex(_loginServers, x => x.ApiUrl == target.ServerUrl);
        _serverAddress = PageStyles.Hint("");
        _serverChoices.SelectionChanged += (_, _) => UpdateServerAddress(); body.Children.Add(_serverChoices);
        if (target is null) _idle.Add(_serverChoices); else _serverChoices.IsEnabled = false;
        body.Children.Add(_serverAddress); UpdateServerAddress();
        body.Children.Add(Button("添加或管理服务器", "添加认证服务器", () => Navigate("servers")));
        var username = Field(body, "服务器账号 / 邮箱", "第三方账号", "", 256);
        body.Children.Add(PageStyles.Hint("该服务器的密码"));
        _password = new TextBox { PasswordChar = '●', MaxLength = 1024 };
        AutomationProperties.SetName(_password, "第三方密码"); body.Children.Add(_password); _idle.Add(_password);
        body.Children.Add(IdleButton("登录并读取角色", "登录第三方服务器", async () =>
        {
            if (_serverChoices.SelectedIndex < 0 || _serverChoices.SelectedIndex >= _loginServers.Length) { _status.Text = "请先添加并确认认证服务器。"; return; }
            var password = _password.Text ?? ""; _password.Text = "";
            var targetId = _reauthenticateId; var revision = _pageRevision;
            var serverUrl = _loginServers[_serverChoices.SelectedIndex].ApiUrl;
            await Run(() => _service.SignInThirdPartyAsync(serverUrl, username.Text ?? "", password, targetId));
            FinishThirdParty(revision, targetId);
        }));
        _profiles = new StackPanel { Spacing = 8 }; body.Children.Add(_profiles);
        body.Children.Add(PageStyles.Hint("拥有多个角色时可在登录后选择。密码不保存；首次启动会自动下载官方认证组件并校验文件，无需手动选择 JAR。"));
        return Scroll(body);
    }
    private void UpdateServerAddress()
    {
        if (_serverAddress is null || _serverChoices is null) return;
        _serverAddress.Text = _serverChoices.SelectedIndex >= 0 && _serverChoices.SelectedIndex < _loginServers.Length
            ? "凭据仅发送到：\n" + _loginServers[_serverChoices.SelectedIndex].ApiUrl : "还没有服务器，请先添加并核对其认证地址。";
    }
    private void FinishThirdParty(int revision, string? targetId)
    {
        if (revision == _pageRevision && !_closed && _service.Error is null && _service.PendingProfiles.Count == 0 && _service.SelectedAccount is not null && _service.Status != "操作已取消。")
        { _viewId = targetId ?? _service.SelectedAccount.Id; Navigate("details"); }
    }
    private Control OfflinePage()
    {
        var body = Page(); body.Margin = new Thickness(2, 18, 8, 12);
        body.Children.Add(Heading("创建离线身份"));
        body.Children.Add(PageStyles.Hint("离线账户不验证游戏所有权，不能用于需要微软正版认证的服务。它与同名正版账户是不同身份。"));
        var name = Field(body, "玩家名", "离线玩家名", _offlineUsername, 16);
        body.Children.Add(PageStyles.Hint("3–16 位英文字母、数字或下划线。名字决定离线 UUID。"));
        body.Children.Add(IdleButton("添加并使用", "添加离线账户", () =>
        {
            _service.AddOffline(name.Text ?? "");
            if (_service.Error is null) { _viewId = _service.SelectedAccount?.Id; Navigate("details"); }
        })); return Scroll(body);
    }
    private void RefreshLogin()
    {
        if (_devicePanel is not null && _deviceCode is not null)
        {
            _devicePanel.IsVisible = _service.DevicePrompt is not null; _deviceCode.Text = _service.DevicePrompt?.UserCode ?? "";
            if (_service.DevicePrompt is { } prompt)
            {
                if (_deviceExpiry is not null) _deviceExpiry.Text = "有效至 " + prompt.ExpiresAt.ToLocalTime().ToString("HH:mm:ss") + " · 正在等待网页授权…";
                if (_openedCode != prompt.UserCode) { _openedCode = prompt.UserCode; OpenMicrosoft(); }
            }
        }
        if (_profiles is null) return;
        _profiles.Children.Clear();
        foreach (var p in _service.PendingProfiles)
        {
            var choose = Button("使用角色 " + p.Name, "选择第三方角色 " + p.Name, async () =>
            {
                var revision = _pageRevision; var targetId = _reauthenticateId;
                await Run(() => _service.CompleteThirdPartyAsync(p.Id)); FinishThirdParty(revision, targetId);
            });
            choose.IsEnabled = !_service.IsBusy; _profiles.Children.Add(choose);
        }
    }
    private Control BuildServersPage()
    {
        var body = Page(); body.Children.Add(Heading("认证服务器"));
        body.Children.Add(PageStyles.Hint("先读取公开服务信息，再核对实际认证地址并保存。服务器发现过程不会发送账号或密码。"));
        var address = Field(body, "服务器地址或 authlib-injector 链接", "发现认证服务器地址", "", 2048);
        address.PlaceholderText = "https://skin.example.com/";
        body.Children.Add(IdleButton("读取服务器信息", "发现认证服务器", async () => await Run(() => _service.DiscoverServerAsync(address.Text ?? ""))));
        _serverPending = new StackPanel { Spacing = 10 }; body.Children.Add(_serverPending);
        body.Children.Add(Heading("已保存的服务器")); _serverList = new StackPanel { Spacing = 12 }; body.Children.Add(_serverList);
        body.Children.Add(Button("返回添加账户", "返回添加账户", () => Navigate("add"))); return Scroll(body);
    }
    private void RefreshServers()
    {
        if (_serverList is null || _serverPending is null) return;
        _serverPending.Children.Clear();
        if (_service.PendingServer is { } pending)
        {
            var box = new StackPanel { Spacing = 10 };
            box.Children.Add(Heading("确认服务器")); box.Children.Add(PageStyles.Hint("实际认证 API：\n" + pending.ApiUrl));
            var name = new TextBox { Text = pending.Name, MaxLength = 120 }; AutomationProperties.SetName(name, "待保存服务器名称"); box.Children.Add(name);
            box.Children.Add(Button("信任此地址并保存", "确认添加认证服务器", () => _service.ConfirmServer(name.Text)));
            box.Children.Add(Button("取消", "取消添加认证服务器", _service.CancelServerDiscovery));
            _serverPending.Children.Add(PageStyles.Card(box));
        }
        _serverList.Children.Clear();
        foreach (var server in _service.Servers)
        {
            var box = new StackPanel { Spacing = 8 };
            var name = new TextBox { Text = server.Name, MaxLength = 120 }; AutomationProperties.SetName(name, "服务器名称 " + server.Id); box.Children.Add(name);
            box.Children.Add(PageStyles.Hint(server.ApiUrl));
            box.Children.Add(PageStyles.Hint("皮肤管理网站（留空时打开认证域名首页）"));
            var website = new TextBox { Text = server.Homepage ?? "", MaxLength = 2048, PlaceholderText = "https://skin.example.com/user/skin" };
            AutomationProperties.SetName(website, "服务器皮肤网站 " + server.Id); box.Children.Add(website);
            var count = _service.Accounts.Count(x => x.ServerUrl == server.ApiUrl);
            box.Children.Add(PageStyles.Hint(count + " 个角色正在使用"));
            var actions = new WrapPanel();
            var rename = Button("保存名称", "重命名服务器 " + server.Id, () => _service.RenameServer(server.Id, name.Text ?? "")); rename.IsEnabled = !_service.IsBusy; actions.Children.Add(rename);
            var saveWebsite = Button("保存网站", "保存服务器皮肤网站 " + server.Id, () => _service.SetServerWebsite(server.Id, website.Text ?? ""));
            saveWebsite.IsEnabled = !_service.IsBusy; saveWebsite.Margin = new Thickness(8, 0, 0, 0); actions.Children.Add(saveWebsite);
            var remove = Button("移除服务器", "移除服务器 " + server.Id, () => _service.RemoveServer(server.Id)); remove.Margin = new Thickness(8, 0, 0, 0);
            remove.IsEnabled = !_service.IsBusy && count == 0; ToolTip.SetTip(remove, count > 0 ? "请先移除使用此服务器的账户。" : "只移除本地服务器记录。"); actions.Children.Add(remove);
            if (server.RegistrationUrl is { } register)
            { var link = Button("注册账号 ↗", "注册服务器账号 " + server.Id, () => OpenWebsite(register)); link.Margin = new Thickness(8, 0, 0, 0); actions.Children.Add(link); }
            box.Children.Add(actions); _serverList.Children.Add(PageStyles.Card(box));
        }
        if (_service.Servers.Count == 0) _serverList.Children.Add(PageStyles.Hint("尚未添加认证服务器。"));
    }
}
