// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Wonder.Application;
using Wonder.Core;

namespace Wonder.Desktop;

internal sealed partial class AccountManagerWindow : Window
{
    private readonly AccountService _service;
    private readonly Action<Uri> _openBrowser;
    private readonly Func<string, CancellationToken, Task<byte[]>>? _textureDownload;
    private readonly Func<GameInstance?>? _selectedInstance;
    private readonly InstanceService? _instances;
    private readonly string _offlineUsername;
    private readonly StackPanel _accounts = new() { Spacing = 9 };
    private readonly ContentControl _page = new();
    private readonly TextBox _search = new() { PlaceholderText = "搜索玩家名或服务器", MaxLength = 128 };
    private readonly TextBlock _count = PageStyles.Hint("");
    private readonly TextBlock _status = PageStyles.Hint("");
    private readonly List<Control> _idle = [];
    private readonly List<AccountSkinPreview> _cardPreviews = [];
    private AccountProfile[] _cardProfiles = [];
    private string? _cardKey;
    private string? _instancePreviewKey;
    private readonly Button _cancel;
    private readonly Button _copyError;
    private string _mode = "details";
    private string? _viewId;
    private AccountProfile? _renderedProfile;
    private string? _renderedSelectedId;
    private int _pageRevision;
    private AccountSkinPreview? _preview;
    private AccountSkinPreview? _capePreview;
    private bool _closed;
    private string? _reauthenticateId;

    public AccountManagerWindow(AccountService service, string offlineUsername = "Player", Action<Uri>? openBrowser = null,
        Func<string, CancellationToken, Task<byte[]>>? textureDownload = null, Func<GameInstance?>? selectedInstance = null, InstanceService? instances = null)
    {
        _service = service; _offlineUsername = offlineUsername;
        _textureDownload = textureDownload;
        _instances = instances;
        _selectedInstance = selectedInstance ?? (() => instances?.SelectedInstance);
        _instancePreviewKey = AccountAvatarLoader.InstanceKey(_selectedInstance());
        _openBrowser = openBrowser ?? (uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }));
        Title = "账户中心 · Wonder Launcher"; Width = 1100; Height = 800; MinWidth = 880; MinHeight = 660;
        Background = Palette.Background; Foreground = Palette.Text; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(28, 24) };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 22) };
        var title = new StackPanel { Spacing = 5 };
        title.Children.Add(new TextBlock { Text = "账户中心", FontSize = 28, FontWeight = FontWeight.SemiBold });
        title.Children.Add(PageStyles.Hint("你的游戏身份、角色外观与认证服务器")); header.Children.Add(title);
        var servers = Button("认证服务器", "管理认证服务器", () => Navigate("servers"));
        servers.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(servers, 1); header.Children.Add(servers); layout.Children.Add(header);
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("280,24,*") };
        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        _count.Margin = new Thickness(0, 0, 0, 10); left.Children.Add(_count);
        AutomationProperties.SetName(_search, "搜索账户"); _search.TextChanged += (_, _) => RefreshAccounts();
        _search.Margin = new Thickness(0, 0, 0, 12); Grid.SetRow(_search, 1); left.Children.Add(_search);
        var accountScroll = Scroll(_accounts); Grid.SetRow(accountScroll, 2); left.Children.Add(accountScroll);
        var add = Button("＋ 添加账户", "添加账户", () => { _reauthenticateId = null; Navigate("add"); });
        add.HorizontalAlignment = HorizontalAlignment.Stretch; add.HorizontalContentAlignment = HorizontalAlignment.Center; add.Margin = new Thickness(0, 12, 0, 0);
        Grid.SetRow(add, 3); left.Children.Add(add); columns.Children.Add(left);
        Grid.SetColumn(_page, 2); columns.Children.Add(_page); Grid.SetRow(columns, 1); layout.Children.Add(columns);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 18, 0, 0) };
        _status.MaxLines = 3; footer.Children.Add(_status);
        _cancel = Button("取消登录", "取消账户操作", () => { _service.CancelLogin(); if (_password is not null) _password.Text = ""; Refresh(); });
        _copyError = Button("复制诊断", "复制账户诊断", async () => { if (_service.Error is { } error) await CopyText(error); });
        var footerActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; footerActions.Children.Add(_copyError); footerActions.Children.Add(_cancel);
        Grid.SetColumn(footerActions, 1); footer.Children.Add(footerActions); Grid.SetRow(footer, 2); layout.Children.Add(footer);
        Content = layout; service.Changed += OnChanged;
        if (_instances is not null) _instances.Changed += OnInstanceChanged;
        Closed += (_, _) =>
        {
            _closed = true; service.Changed -= OnChanged; service.CancelLogin(); service.CancelServerDiscovery(); DisposeAppearance();
            if (_instances is not null) _instances.Changed -= OnInstanceChanged;
            foreach (var avatar in _cardPreviews) avatar.Dispose();
            if (_password is not null) _password.Text = ""; if (_deviceCode is not null) _deviceCode.Text = "";
        };
        _viewId = service.SelectedAccount?.Id; Navigate(service.Accounts.Count == 0 ? "add" : "details");
    }

    private void Navigate(string mode)
    {
        _pageRevision++;
        _mode = mode; _idle.Clear(); DisposeAppearance(); _renderedProfile = null;
        if (_password is not null) _password.Text = "";
        _deviceCode = null; _devicePanel = null; _profiles = null; _serverChoices = null;
        _page.Content = mode switch { "add" => BuildAddPage(), "servers" => BuildServersPage(), _ => BuildDetails() };
        Refresh();
    }
    private Control BuildDetails()
    {
        var profile = _service.Accounts.FirstOrDefault(x => x.Id == _viewId) ?? _service.SelectedAccount;
        _renderedProfile = profile; _viewId = profile?.Id;
        _renderedSelectedId = _service.SelectedAccount?.Id;
        if (profile is null)
        {
            var empty = Page(); empty.Children.Add(Heading("开始你的旅程"));
            empty.Children.Add(PageStyles.Hint("添加微软正版、自定义认证服务器或离线账户。添加后可在桌面组件中快速切换。"));
            empty.Children.Add(Button("添加第一个账户", "添加第一个账户", () => Navigate("add"))); return PageStyles.Card(empty);
        }
        var body = Page();
        var identity = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var labels = new StackPanel { Spacing = 7 };
        labels.Children.Add(Heading(profile.Username));
        labels.Children.Add(PageStyles.Hint(AccountService.KindLabel(profile.Kind) + (profile.Id == _service.SelectedAccount?.Id ? "  ·  当前启动账户" : "  ·  可切换使用")));
        labels.Children.Add(PageStyles.Hint(profile.Kind == AccountKind.Offline ? "本地身份 · 不进行正版认证" : profile.LastValidatedAt is { } time ? "最近验证 " + time.ToLocalTime().ToString("MM-dd HH:mm") : "登录资料已保存 · 启动前自动验证"));
        identity.Children.Add(labels);
        var use = IdleButton("使用此账户", "选择账户 " + profile.Username, () => { _service.Select(profile.Id); _renderedProfile = null; Refresh(); });
        use.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(use, 1); identity.Children.Add(use); body.Children.Add(identity);
        var uuid = new StackPanel { Spacing = 5 };
        uuid.Children.Add(PageStyles.Hint("UUID")); uuid.Children.Add(new SelectableTextBlock { Text = profile.Uuid, FontSize = 12, Foreground = Palette.Muted });
        uuid.Children.Add(Button("复制 UUID", "复制账户 UUID", async () => await CopyText(profile.Uuid)));
        if (profile.ServerUrl is { } server) uuid.Children.Add(PageStyles.Hint("认证服务器  " + ServerName(server) + "\n" + server));
        body.Children.Add(PageStyles.Card(uuid, new Thickness(16, 12)));
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        if (profile.Kind != AccountKind.Offline)
        {
            actions.Children.Add(IdleButton("刷新资料", "刷新账户", async () => await Run(() => _service.RefreshAsync(profile.Id))));
            actions.Children.Add(IdleButton("重新登录", "重新登录账户", () =>
            {
                _reauthenticateId = profile.Id; Navigate("add");
                if (_loginTabs is not null) _loginTabs.SelectedIndex = profile.Kind == AccountKind.Microsoft ? 0 : 1;
                Refresh();
            }));
        }
        actions.Children.Add(IdleButton("移除账户", "删除账户 " + profile.Username, () => ShowConfirmation(body, profile)));
        foreach (Control c in actions.Children) c.Margin = new Thickness(0, 0, 8, 6); body.Children.Add(actions);
        if (profile.Kind == AccountKind.Offline)
        {
            body.Children.Add(Heading("离线身份"));
            body.Children.Add(PageStyles.Hint("修改玩家名会生成新的离线 UUID，游戏可能把它视为另一位玩家，原存档中的背包和权限不会自动迁移。"));
            var rename = Field(body, "玩家名", "修改离线玩家名", profile.Username, 16);
            body.Children.Add(IdleButton("确认修改名字与 UUID", "重命名离线账户", () => _service.RenameOffline(profile.Id, rename.Text ?? "")));
            BuildOfflineSkinPicker(body, profile);
        }
        else if (profile.Kind == AccountKind.Microsoft) BuildAppearance(body, profile);
        else
        {
            body.Children.Add(Heading("第三方角色"));
            body.Children.Add(PageStyles.Hint("皮肤、披风和角色资料由认证服务器管理。添加账户可选择该账号拥有的其他角色。"));
            body.Children.Add(BuildCharacterPreview(profile));
            BuildActiveCape(body, profile);
            body.Children.Add(PageStyles.Hint("网站修改外观后点击“刷新资料”，同步当前皮肤和已选披风。"));
            if (_service.GetSkinManagementWebsite(profile.Id) is { } home)
            {
                body.Children.Add(IdleButton("选择皮肤 · 打开认证网站 ↗", "选择第三方皮肤", () => OpenWebsite(home)));
                body.Children.Add(PageStyles.Hint("将在默认浏览器中打开：\n" + home + "\n请在该网站登录并选择皮肤。Wonder 不向浏览器传递密码或令牌。"));
                body.Children.Add(Button("设置皮肤管理网站", "设置第三方皮肤网站", () => Navigate("servers")));
            }
        }
        return Scroll(body);
    }
    private void ShowConfirmation(StackPanel body, AccountProfile profile)
    {
        if (body.Children.OfType<Border>().Any(x => x.Name == "DeleteConfirmation")) return;
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(PageStyles.Hint("移除 “" + profile.Username + "” 和本机保存的凭据？游戏存档保留。正在运行的游戏不会退出；微软应用授权可在 Microsoft 账户网站撤销。"));
        content.Children.Add(IdleButton("确认删除", "确认删除账户", async () =>
        {
            await Run(() => _service.RemoveAsync(profile.Id)); if (!_closed) Navigate("details");
        }));
        Border? card = null;
        content.Children.Add(Button("保留账户", "取消删除账户", () => body.Children.Remove(card!)));
        card = PageStyles.Card(content); card.Name = "DeleteConfirmation"; body.Children.Insert(3, card);
    }
    private void RefreshAccounts()
    {
        _count.Text = "我的账户  /  " + _service.Accounts.Count;
        var query = _search.Text?.Trim() ?? ""; var profiles = _service.Accounts.ToArray();
        var key = query + "|" + _mode + "|" + _viewId + "|" + _service.SelectedAccount?.Id + "|" + _service.IsReadBlocked;
        if (_cardKey == key && profiles.SequenceEqual(_cardProfiles)) return;
        _cardKey = key; _cardProfiles = profiles;
        foreach (var avatar in _cardPreviews) avatar.Dispose(); _cardPreviews.Clear(); _accounts.Children.Clear();
        foreach (var account in profiles.Where(x => (x.Username + " " + x.ServerUrl + " " + AccountService.KindLabel(x.Kind)).Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            var current = account.Id == _service.SelectedAccount?.Id;
            var avatar = new AccountSkinPreview(_textureDownload) { Width = 40, Height = 40, HeadOnly = true, VerticalAlignment = VerticalAlignment.Center };
            _cardPreviews.Add(avatar);
            avatar.LoadedTexture += () => ToolTip.SetTip(avatar, avatar.Description);
            _ = AccountAvatarLoader.LoadAsync(avatar, account, _selectedInstance?.Invoke());
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("40,12,*") }; content.Children.Add(avatar);
            var labels = new StackPanel { Spacing = 6 };
            labels.Children.Add(new TextBlock { Text = account.Username, FontWeight = FontWeight.SemiBold, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis });
            labels.Children.Add(PageStyles.Hint(AccountService.KindLabel(account.Kind) + (current ? " · 当前使用" : "")));
            if (account.ServerUrl is { } url) { var site = PageStyles.Hint(ServerName(url)); site.MaxLines = 1; site.TextTrimming = TextTrimming.CharacterEllipsis; labels.Children.Add(site); }
            Grid.SetColumn(labels, 2); content.Children.Add(labels);
            var surface = PageStyles.Card(content, new Thickness(12));
            if (account.Id == _viewId && _mode == "details") { surface.Background = Palette.Active; surface.BorderBrush = Palette.Accent; }
            var card = Button("", "查看账户 " + account.Username, () => { _viewId = account.Id; Navigate("details"); });
            card.Content = surface; card.HorizontalAlignment = HorizontalAlignment.Stretch; card.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            card.Padding = new Thickness(0); card.BorderThickness = new Thickness(0); card.Background = Palette.Surface;
            _accounts.Children.Add(card);
        }
        if (_accounts.Children.Count == 0) _accounts.Children.Add(PageStyles.Hint(_service.IsReadBlocked ? "账户库暂不可用。" : _service.Accounts.Count == 0 ? "还没有账户，先添加一个吧。" : "没有匹配的账户。"));
        if (_service.IsReadBlocked) _accounts.Children.Add(Button("重新读取账户库", "重新读取账户库", _service.Reload));
    }
    private void Refresh()
    {
        if (_closed) return;
        var busy = _service.IsBusy;
        _cancel.IsVisible = _service.IsLoggingIn || _service.PendingProfiles.Count > 0;
        _copyError.IsVisible = _service.Error is not null;
        _status.Text = _service.Error ?? _service.Status; _status.Foreground = _service.Error is null ? Palette.Muted : Palette.Error;
        ToolTip.SetTip(_status, _service.Error);
        RefreshAccounts();
        if (_mode == "details")
        {
            var profile = _service.Accounts.FirstOrDefault(x => x.Id == _viewId);
            if (profile != _renderedProfile || _renderedSelectedId != _service.SelectedAccount?.Id || (_renderedProfile is null && _service.Accounts.Count > 0))
            {
                _idle.Clear(); DisposeAppearance(); _page.Content = BuildDetails();
            }
        }
        else if (_mode == "add") RefreshLogin();
        else if (_mode == "servers") RefreshServers();
        foreach (var control in _idle) control.IsEnabled = !busy && !_service.IsReadBlocked;
    }
    private void OnChanged(object? sender, EventArgs args)
    {
        if (Dispatcher.UIThread.CheckAccess()) Refresh(); else Dispatcher.UIThread.Post(Refresh);
    }
    private void OnInstanceChanged(object? sender, EventArgs args)
    {
        void Update()
        {
            if (_closed) return;
            var key = AccountAvatarLoader.InstanceKey(_selectedInstance?.Invoke());
            if (key == _instancePreviewKey) return;
            _instancePreviewKey = key; _cardKey = null;
            if (_renderedProfile?.Kind == AccountKind.Offline) _renderedProfile = null;
            Refresh();
        }
        if (Dispatcher.UIThread.CheckAccess()) Update(); else Dispatcher.UIThread.Post(Update);
    }
    private void DisposeAppearance()
    {
        _preview?.Dispose(); _preview = null;
        _capePreview?.Dispose(); _capePreview = null;
    }
    private async Task Run(Func<Task> action)
    {
        try { var task = action(); Refresh(); await task; }
        catch { if (!_closed) _status.Text = "操作未完成，请刷新账户后重试。"; }
        if (!_closed) Refresh();
    }
    private async Task CopyText(string value)
    {
        try { if (Clipboard is { } clipboard) { await clipboard.SetTextAsync(value); _status.Text = "已复制。"; } }
        catch { _status.Text = "剪贴板暂不可用，可以选中文字手动复制。"; }
    }
    private void OpenWebsite(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0) return;
        try { _openBrowser(uri); } catch { _status.Text = "浏览器未能打开，请手动访问：" + uri.GetLeftPart(UriPartial.Path); }
    }
    private string ServerName(string url) => _service.Servers.FirstOrDefault(x => x.ApiUrl == url)?.Name ?? new Uri(url).Host;
    private Button IdleButton(string text, string automation, Action action)
    { var button = Button(text, automation, action); _idle.Add(button); return button; }
    private TextBox Field(StackPanel parent, string label, string automation, string text, int length)
    {
        parent.Children.Add(PageStyles.Hint(label)); var box = new TextBox { Text = text, MaxLength = length };
        AutomationProperties.SetName(box, automation); parent.Children.Add(box); _idle.Add(box); return box;
    }
    private static Button Button(string text, string automation, Action action)
    {
        var button = new Button { Content = text, MinHeight = 36 }; AutomationProperties.SetName(button, automation);
        button.Click += (_, _) => action(); return button;
    }
    private static StackPanel Page() => new() { Spacing = 16, Margin = new Thickness(2, 0, 8, 14) };
    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 21, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private static ScrollViewer Scroll(Control content) => new() { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
}
