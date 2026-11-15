// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Wonder.Application;
using Wonder.Core;

namespace Wonder.Desktop;

internal sealed partial class AccountManagerWindow
{
    private void RefreshAccounts()
    {
        if (_closed) return;
        _count.Text = "我的账户  /  " + _service.Accounts.Count;
        var query = _search.Text?.Trim() ?? "";
        var profiles = _service.Accounts.ToArray();
        var key = query + "|" + _mode + "|" + _viewId + "|" + _service.SelectedAccount?.Id + "|" + _service.IsReadBlocked + "|" + _service.IsBusy;
        if (_cardKey == key && profiles.SequenceEqual(_cardProfiles)) return;
        _cardKey = key; _cardProfiles = profiles;
        var visible = profiles.Where(x => (x.Username + " " + x.ServerUrl + " " + AccountService.KindLabel(x.Kind)).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        var ids = visible.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        // Keep only visible cards: filtering out an account releases its texture and cancels its request.
        foreach (var id in _accountCards.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            var card = _accountCards[id];
            _accounts.Children.Remove(card.Button); card.Dispose(); _accountCards.Remove(id);
        }
        var instance = _selectedInstance?.Invoke();
        var alternatives = _instances?.Catalog.Instances;
        var children = new List<Control>();
        foreach (var account in visible)
        {
            if (!_accountCards.TryGetValue(account.Id, out var card))
            {
                var id = account.Id;
                card = new AccountCard(() => { _viewId = id; Navigate("details"); });
                _accountCards.Add(id, card);
            }
            card.Update(account, instance, alternatives, _textureDownload, AccountAvatarLoader.For(_service), account.Id == _service.SelectedAccount?.Id,
                account.Id == _viewId && _mode == "details", account.ServerUrl is { } url ? ServerName(url) : null);
            children.Add(card.Button);
        }
        if (children.Count == 0) children.Add(PageStyles.Hint(_service.IsReadBlocked ? _service.IsBusy ? "正在读取账户库…" : "账户库暂不可用。" : profiles.Length == 0 ? "还没有账户，先添加一个吧。" : "没有匹配的账户。"));
        if (_service.IsReadBlocked && !_service.IsBusy)
            children.Add(PageStyles.Button("重新读取账户库", "重新读取账户库", () => UiOperation.Start(async () => await Run(_service.ReloadAsync), () => _closed)));
        foreach (var child in _accounts.Children.Where(x => !children.Contains(x)).ToArray()) _accounts.Children.Remove(child);
        for (var i = 0; i < children.Count; i++)
        {
            if (i < _accounts.Children.Count && ReferenceEquals(_accounts.Children[i], children[i])) continue;
            _accounts.Children.Remove(children[i]); _accounts.Children.Insert(i, children[i]);
        }
    }

    private sealed class AccountCard : IDisposable
    {
        public Button Button { get; }
        private readonly Grid _content = new() { ColumnDefinitions = new ColumnDefinitions("40,12,*") };
        private readonly TextBlock _name = PageStyles.SectionTitle("");
        private readonly TextBlock _kind = PageStyles.Hint("");
        private readonly TextBlock _server = PageStyles.Hint("");
        private readonly Border _surface;
        private AccountSkinPreview? _avatar;
        private string? _avatarKey;
        private string? _avatarRetentionKey;

        public AccountCard(Action select)
        {
            var labels = new StackPanel { Spacing = 6 };
            labels.Children.Add(_name); labels.Children.Add(_kind); labels.Children.Add(_server);
            _name.TextWrapping = TextWrapping.NoWrap; _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _server.MaxLines = 1; _server.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(labels, 2); _content.Children.Add(labels);
            _surface = PageStyles.Card(_content, new Thickness(11));
            Button = PageStyles.Button("", "", select);
            Button.Content = _surface; Button.HorizontalAlignment = HorizontalAlignment.Stretch;
            Button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            Button.Padding = new Thickness(0); Button.BorderThickness = new Thickness(0); Button.Background = Palette.Surface;
            Button.Margin = new Thickness(0);
        }

        public void Update(AccountProfile account, GameInstance? instance, IReadOnlyList<GameInstance>? alternatives, Func<string, CancellationToken, Task<byte[]>>? download,
            AccountAvatarLoader.Cache cache, bool current, bool viewed, string? serverName)
        {
            _name.Text = account.Username;
            _kind.Text = AccountService.KindLabel(account.Kind) + (current ? " · 当前使用" : "");
            _server.Text = serverName; _server.IsVisible = serverName is not null;
            AutomationProperties.SetName(Button, "查看账户 " + account.Username);
            _surface.Background = viewed ? Palette.Active : Palette.Surface;
            _surface.BorderBrush = viewed ? Palette.Accent : Palette.Line;
            _surface.BorderThickness = new Thickness(viewed ? 2 : 1);
            var key = AccountAvatarLoader.Key(account, instance, alternatives);
            if (_avatarKey == key) return;
            _avatarKey = key;
            var retentionKey = AccountAvatarLoader.RetentionKey(account);
            var retained = _avatarRetentionKey == retentionKey ? _avatar?.Avatar : null;
            _avatarRetentionKey = retentionKey;
            if (_avatar is not null) { _avatar.Dispose(); _content.Children.Remove(_avatar); }
            var avatar = new AccountSkinPreview(download) { Width = 40, Height = 40, HeadOnly = true, VerticalAlignment = VerticalAlignment.Center };
            if (retained is not null) avatar.RestoreAvatar(retained);
            _avatar = avatar; _content.Children.Insert(0, avatar);
            avatar.LoadedTexture += () => ToolTip.SetTip(avatar, avatar.Description);
            UiOperation.Start(() => AccountAvatarLoader.LoadAsync(avatar, account, instance, alternatives, cache));
        }

        public void Dispose() => _avatar?.Dispose();
    }
}
