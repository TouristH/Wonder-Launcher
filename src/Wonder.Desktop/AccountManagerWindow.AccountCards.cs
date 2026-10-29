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
        var key = query + "|" + _mode + "|" + _viewId + "|" + _service.SelectedAccount?.Id + "|" + _service.IsReadBlocked;
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
        var children = new List<Control>();
        foreach (var account in visible)
        {
            if (!_accountCards.TryGetValue(account.Id, out var card))
            {
                var id = account.Id;
                card = new AccountCard(() => { _viewId = id; Navigate("details"); });
                _accountCards.Add(id, card);
            }
            card.Update(account, instance, _textureDownload, account.Id == _service.SelectedAccount?.Id,
                account.Id == _viewId && _mode == "details", account.ServerUrl is { } url ? ServerName(url) : null);
            children.Add(card.Button);
        }
        if (children.Count == 0) children.Add(PageStyles.Hint(_service.IsReadBlocked ? "账户库暂不可用。" : profiles.Length == 0 ? "还没有账户，先添加一个吧。" : "没有匹配的账户。"));
        if (_service.IsReadBlocked) children.Add(Button("重新读取账户库", "重新读取账户库", _service.Reload));
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
        private readonly TextBlock _name = new() { FontWeight = FontWeight.SemiBold, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _kind = PageStyles.Hint("");
        private readonly TextBlock _server = PageStyles.Hint("");
        private readonly Border _surface;
        private AccountSkinPreview? _avatar;
        private string? _avatarKey;

        public AccountCard(Action select)
        {
            var labels = new StackPanel { Spacing = 6 };
            labels.Children.Add(_name); labels.Children.Add(_kind); labels.Children.Add(_server);
            _server.MaxLines = 1; _server.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(labels, 2); _content.Children.Add(labels);
            _surface = PageStyles.Card(_content, new Thickness(12));
            Button = AccountManagerWindow.Button("", "", select);
            Button.Content = _surface; Button.HorizontalAlignment = HorizontalAlignment.Stretch;
            Button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            Button.Padding = new Thickness(0); Button.BorderThickness = new Thickness(0); Button.Background = Palette.Surface;
        }

        public void Update(AccountProfile account, GameInstance? instance, Func<string, CancellationToken, Task<byte[]>>? download,
            bool current, bool viewed, string? serverName)
        {
            _name.Text = account.Username;
            _kind.Text = AccountService.KindLabel(account.Kind) + (current ? " · 当前使用" : "");
            _server.Text = serverName; _server.IsVisible = serverName is not null;
            AutomationProperties.SetName(Button, "查看账户 " + account.Username);
            _surface.Background = viewed ? Palette.Active : Palette.Surface;
            _surface.BorderBrush = viewed ? Palette.Accent : Palette.Line;
            var key = AccountAvatarLoader.Key(account, instance);
            if (_avatarKey == key) return;
            _avatarKey = key;
            if (_avatar is not null) { _avatar.Dispose(); _content.Children.Remove(_avatar); }
            var avatar = new AccountSkinPreview(download) { Width = 40, Height = 40, HeadOnly = true, VerticalAlignment = VerticalAlignment.Center };
            _avatar = avatar; _content.Children.Insert(0, avatar);
            avatar.LoadedTexture += () => ToolTip.SetTip(avatar, avatar.Description);
            _ = AccountAvatarLoader.LoadAsync(avatar, account, instance);
        }

        public void Dispose() => _avatar?.Dispose();
    }
}
