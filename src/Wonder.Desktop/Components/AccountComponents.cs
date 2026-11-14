// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Wonder.Application;
using Wonder.Core;
using Wonder.Extensibility.Contracts;
using Wonder.Workspace;

namespace Wonder.Desktop.Components;

internal static class AccountComponents
{
    public static void Register(ComponentRegistry registry, AccountService service, Action openManager, InstanceService? instances = null,
        Func<string, CancellationToken, Task<byte[]>>? textureDownload = null) =>
        registry.Register(new(ComponentIds.Accounts, "账户管理", "添加、删除和选择微软正版、第三方与离线账户。", 14 * ComponentGrid.Unit, 7 * ComponentGrid.Unit),
            _ => new Runtime(service, openManager, instances), context => Present(context, service, instances, textureDownload), new("manage"));

    private static ComponentContent Present(ComponentPresentationContext context, AccountService service, InstanceService? instances,
        Func<string, CancellationToken, Task<byte[]>>? textureDownload)
    {
        var layout = new Grid { RowDefinitions = new RowDefinitions("26,*,Auto") };
        layout.Children.Add(new TextBlock { Text = context.Definition.Title, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Palette.Accent, Margin = new Thickness(12, 6, 12, 0) });
        var body = new StackPanel { Spacing = 3, Margin = new Thickness(12, 1, 12, 3), VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { FontSize = 14, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Palette.Text };
        var detail = new TextBlock { FontSize = 10, Foreground = Palette.Muted, TextTrimming = TextTrimming.CharacterEllipsis };
        var choices = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 28, FontSize = 11, Padding = new Thickness(8, 4), PlaceholderText = "选择游戏账户" };
        AutomationProperties.SetName(choices, "切换游戏账户");
        var refreshing = false; Choice[] current = [];
        choices.SelectionChanged += (_, _) => { if (!refreshing && choices.SelectedIndex >= 0 && choices.SelectedIndex < current.Length) context.Invoke("select", current[choices.SelectedIndex].Id); };
        const double avatarSize = 32;
        var identity = new Grid { ColumnDefinitions = new ColumnDefinitions("32,8,*") };
        AccountSkinPreview avatar = new(textureDownload) { Width = avatarSize, Height = avatarSize, HeadOnly = true };
        identity.Children.Add(avatar); var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; details.Children.Add(name); details.Children.Add(detail);
        Grid.SetColumn(details, 2); identity.Children.Add(details); body.Children.Add(identity);
        body.Children.Add(choices);
        Grid.SetRow(body, 1); layout.Children.Add(body); string? skinKey = null;
        var button = new Button
        {
            Content = "管理账户", Margin = new Thickness(12, 0, 12, 7), MinWidth = 160, MinHeight = 28, FontSize = 11, Padding = new Thickness(10, 4),
            HorizontalAlignment = HorizontalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center
        };
        AutomationProperties.SetName(button, "打开账户管理"); button.Click += (_, _) => context.Invoke("manage"); Grid.SetRow(button, 2); layout.Children.Add(button);
        void Refresh()
        {
            name.Text = context.Get("name", "尚未选择账户");
            var kind = context.Get("kind");
            var status = context.Get("status");
            detail.Text = status.Length == 0 ? kind : kind.Length == 0 ? status : $"{kind} · {status}";
            ToolTip.SetTip(detail, detail.Text);
            var profile = service.SelectedAccount;
            if (profile?.Id != context.Get("selected")) profile = null;
            var instance = profile is null ? null : instances?.SelectedInstance;
            var alternatives = profile?.Kind == AccountKind.Offline ? instances?.Catalog.Instances : null;
            var key = AccountAvatarLoader.Key(profile, instance, alternatives);
            if (key != skinKey)
            {
                skinKey = key; avatar.Dispose(); identity.Children.Remove(avatar);
                avatar = new(textureDownload) { Width = avatarSize, Height = avatarSize, HeadOnly = true }; identity.Children.Insert(0, avatar);
                AutomationProperties.SetName(avatar, "当前账户皮肤头像");
                var currentAvatar = avatar;
                currentAvatar.LoadedTexture += () => ToolTip.SetTip(currentAvatar, currentAvatar.Description);
                if (profile is not null) UiOperation.Start(() => AccountAvatarLoader.LoadAsync(currentAvatar, profile, instance, alternatives));
                else ToolTip.SetTip(currentAvatar, context.IsPreview ? "显示当前账户的皮肤头像与头部外层" : "添加并选择账户以显示头像");
            }
            ToolTip.SetTip(name, name.Text); refreshing = true;
            try
            {
                current = JsonSerializer.Deserialize<Choice[]>(context.Get("choices", "[]")) ?? [];
                choices.ItemsSource = current.Select(x => x.Label).ToArray(); choices.SelectedIndex = Array.FindIndex(current, x => x.Id == context.Get("selected"));
            }
            finally { refreshing = false; }
            choices.IsEnabled = !context.IsPreview && context.Get("busy") != "true" && current.Length > 0;
            button.IsEnabled = !context.IsPreview;
        }
        Refresh(); return ComponentContent.Card(layout, Refresh, point => point.Y < 30, () => avatar.Dispose());
    }
    private sealed record Choice(string Id, string Label);
    private sealed class Runtime(AccountService service, Action openManager, InstanceService? instances) : IComponentRuntime
    {
        private bool _subscribed;
        public event EventHandler? Changed;
        public IReadOnlyDictionary<string, string> Values
        {
            get
            {
                var account = service.SelectedAccount;
                var busy = service.IsBusy;
                return new Dictionary<string, string>
                {
                    ["name"] = account?.Username ?? "尚未选择账户",
                    ["kind"] = account is null ? "添加账户以选择游戏身份" : AccountService.KindLabel(account.Kind),
                    ["selected"] = account?.Id ?? "", ["busy"] = busy || service.IsReadBlocked ? "true" : "false",
                    ["skin"] = account?.SkinUrl ?? "",
                    ["status"] = busy ? "正在处理账户…" : service.Error is not null ? "账户操作未完成 · 打开账户中心查看" : account?.Kind == AccountKind.Offline ? account.OfflineSkinId is { } skinId ? "默认皮肤 · " + OfflineDefaultSkins.Get(skinId).Name : "自动分配默认皮肤" : "",
                    ["choices"] = JsonSerializer.Serialize(service.Accounts.Select(x => new Choice(x.Id, x.Username + " · " + AccountService.KindLabel(x.Kind))))
                };
            }
        }
        public void Resume() { if (_subscribed) return; _subscribed = true; service.Changed += OnChanged; if (instances is not null) instances.Changed += OnChanged; }
        public void Suspend() { if (!_subscribed) return; _subscribed = false; service.Changed -= OnChanged; if (instances is not null) instances.Changed -= OnChanged; }
        private void OnChanged(object? sender, EventArgs args)
        {
            if (Dispatcher.UIThread.CheckAccess()) Changed?.Invoke(this, EventArgs.Empty);
            else Dispatcher.UIThread.Post(() => { if (_subscribed) Changed?.Invoke(this, EventArgs.Empty); });
        }
        public void Invoke(string actionId, string? value) { if (actionId == "manage") openManager(); else if (actionId == "select" && value is not null) UiOperation.Start(() => service.SelectAsync(value)); }
        public void Dispose() { Suspend(); Changed = null; }
    }
}
