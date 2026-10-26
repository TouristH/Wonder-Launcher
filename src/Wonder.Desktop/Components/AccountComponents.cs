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
        registry.Register(new(ComponentIds.Accounts, "账户管理", "添加、删除和选择微软正版、第三方与离线账户。", 320, 258),
            _ => new Runtime(service, openManager, instances), context => Present(context, service, instances, textureDownload));

    private static ComponentContent Present(ComponentPresentationContext context, AccountService service, InstanceService? instances,
        Func<string, CancellationToken, Task<byte[]>>? textureDownload)
    {
        var layout = new Grid { RowDefinitions = new RowDefinitions("38,*,Auto") };
        layout.Children.Add(new TextBlock { Text = "账户管理", FontSize = 14, Foreground = Palette.Accent, Margin = new Thickness(15, 10, 15, 0) });
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(15, 6, 15, 12) };
        var name = new TextBlock { FontSize = 20, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Palette.Text };
        var kind = PageStyles.Hint("");
        var choices = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 32 };
        AutomationProperties.SetName(choices, "切换游戏账户");
        var refreshing = false; Choice[] current = [];
        choices.SelectionChanged += (_, _) => { if (!refreshing && choices.SelectedIndex >= 0 && choices.SelectedIndex < current.Length) context.Invoke("select", current[choices.SelectedIndex].Id); };
        var identity = new Grid { ColumnDefinitions = new ColumnDefinitions("42,12,*") };
        AccountSkinPreview avatar = new(textureDownload) { Width = 42, Height = 42, HeadOnly = true };
        identity.Children.Add(avatar); var details = new StackPanel { Spacing = 5 }; details.Children.Add(name); details.Children.Add(kind);
        Grid.SetColumn(details, 2); identity.Children.Add(details); body.Children.Add(identity); body.Children.Add(choices);
        var status = PageStyles.Hint(""); status.MaxLines = 1; status.TextTrimming = TextTrimming.CharacterEllipsis; body.Children.Add(status);
        Grid.SetRow(body, 1); layout.Children.Add(body); string? skinKey = null;
        var button = new Button { Content = "管理账户", Margin = new Thickness(15, 0, 15, 14), HorizontalAlignment = HorizontalAlignment.Left, MinHeight = 34 };
        AutomationProperties.SetName(button, "打开账户管理"); button.Click += (_, _) => context.Invoke("manage"); Grid.SetRow(button, 2); layout.Children.Add(button);
        void Refresh()
        {
            name.Text = context.Get("name", "尚未选择账户"); kind.Text = context.Get("kind", "添加账户以选择游戏身份");
            status.Text = context.Get("status", "在账户中心管理登录与角色外观");
            var profile = context.IsPreview ? null : service.SelectedAccount;
            var instance = context.IsPreview ? null : instances?.SelectedInstance;
            var key = AccountAvatarLoader.Key(profile, instance);
            if (key != skinKey)
            {
                skinKey = key; avatar.Dispose(); identity.Children.Remove(avatar);
                avatar = new(textureDownload) { Width = 42, Height = 42, HeadOnly = true }; identity.Children.Insert(0, avatar);
                AutomationProperties.SetName(avatar, "当前账户皮肤头像");
                var currentAvatar = avatar;
                currentAvatar.LoadedTexture += () => ToolTip.SetTip(currentAvatar, currentAvatar.Description);
                if (profile is not null) _ = AccountAvatarLoader.LoadAsync(currentAvatar, profile, instance);
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
        Refresh(); return ComponentContent.Card(layout, Refresh, point => point.Y < 38, () => avatar.Dispose());
    }
    private sealed record Choice(string Id, string Label);
    private sealed class Runtime(AccountService service, Action openManager, InstanceService? instances) : IComponentRuntime
    {
        private bool _subscribed;
        public event EventHandler? Changed;
        public IReadOnlyDictionary<string, string> Values => new Dictionary<string, string>
        {
            ["name"] = service.SelectedAccount?.Username ?? "尚未选择账户",
            ["kind"] = service.SelectedAccount is { } account ? AccountService.KindLabel(account.Kind) : "添加账户以选择游戏身份",
            ["selected"] = service.SelectedAccount?.Id ?? "", ["busy"] = service.IsBusy ? "true" : "false",
            ["skin"] = service.SelectedAccount?.SkinUrl ?? "",
            ["status"] = service.IsBusy ? "正在处理账户…" : service.Error is not null ? "账户操作未完成 · 打开账户中心查看" : service.SelectedAccount?.Kind == AccountKind.Offline ? service.SelectedAccount.OfflineSkinId is { } skinId ? "默认皮肤 · " + OfflineDefaultSkins.Get(skinId).Name : "离线身份 · 自动分配默认皮肤" : "启动前自动验证 · 可管理皮肤与披风",
            ["choices"] = JsonSerializer.Serialize(service.Accounts.Select(x => new Choice(x.Id, x.Username + " · " + AccountService.KindLabel(x.Kind))))
        };
        public void Resume() { if (_subscribed) return; _subscribed = true; service.Changed += OnChanged; if (instances is not null) instances.Changed += OnChanged; }
        public void Suspend() { if (!_subscribed) return; _subscribed = false; service.Changed -= OnChanged; if (instances is not null) instances.Changed -= OnChanged; }
        private void OnChanged(object? sender, EventArgs args)
        {
            if (Dispatcher.UIThread.CheckAccess()) Changed?.Invoke(this, EventArgs.Empty);
            else Dispatcher.UIThread.Post(() => { if (_subscribed) Changed?.Invoke(this, EventArgs.Empty); });
        }
        public void Invoke(string actionId, string? value) { if (actionId == "manage") openManager(); else if (actionId == "select" && value is not null) service.Select(value); }
        public void Dispose() { Suspend(); Changed = null; }
    }
}
