// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Wonder.Application;
using Wonder.Extensibility.Contracts;
using Wonder.Workspace;

namespace Wonder.Desktop.Components;

internal static class GameComponents
{
    public static void Register(ComponentRegistry registry, InstanceService service, Action openManager, AccountService? accounts = null)
    {
        LaunchStatusWindow? statusWindow = null;
        void OpenStatus(Window? owner)
        {
            if (statusWindow is not null) { if (statusWindow.WindowState == WindowState.Minimized) statusWindow.WindowState = WindowState.Normal; statusWindow.Activate(); return; }
            statusWindow = new LaunchStatusWindow(service);
            if (owner is not null) statusWindow.FontFamily = owner.FontFamily;
            statusWindow.Closed += (_, _) => statusWindow = null;
            if (owner is null) statusWindow.Show(); else statusWindow.Show(owner);
        }
        registry.Register(new(ComponentIds.Instances, "实例管理", "读取已有游戏目录与启动器配置，管理实例并细调启动设置。", 320, 240),
            _ => new Runtime(service, openManager, accounts), context => Present(context, false, OpenStatus, accounts is not null));
        registry.Register(new(ComponentIds.Launch, "启动游戏", "使用账户管理中选定的身份启动当前实例，启动状态在所有入口同步。", 300, 240),
            _ => new Runtime(service, openManager, accounts), context => Present(context, true, OpenStatus, accounts is not null));
    }

    private static ComponentContent Present(ComponentPresentationContext context, bool launch, Action<Window?> openStatus, bool accountAware)
    {
        var layout = new Grid { RowDefinitions = new RowDefinitions("38,*,Auto") };
        layout.Children.Add(new TextBlock { Text = context.Definition.Title, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Palette.Accent, Margin = new Thickness(15, 10, 15, 0) });
        var body = new StackPanel { Spacing = 7, Margin = new Thickness(15, 5, 15, 8) };
        var name = new TextBlock { FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text, TextTrimming = TextTrimming.CharacterEllipsis };
        var detail = Label(); var status = Label(); status.MaxHeight = 36;
        body.Children.Add(name); body.Children.Add(detail); body.Children.Add(status); Grid.SetRow(body, 1); layout.Children.Add(body);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(15, 0, 15, 14) };
        var primary = new Button { Content = launch ? accountAware ? "启动游戏" : "离线启动" : "管理实例", MinHeight = 34 };
        AutomationProperties.SetName(primary, launch ? accountAware ? "启动所选账户游戏" : "离线启动游戏" : "打开实例管理");
        primary.Click += (_, _) => context.Invoke(launch ? "launch" : "manage"); actions.Children.Add(primary);
        var secondary = new Button { Content = "启动状态", MinHeight = 34, IsEnabled = !context.IsPreview }; AutomationProperties.SetName(secondary, "查看启动状态");
        secondary.Click += (_, _) => { if (!context.IsPreview) openStatus(TopLevel.GetTopLevel(layout) as Window); };
        if (launch) actions.Children.Add(secondary);
        Grid.SetRow(actions, 2); layout.Children.Add(actions);
        void Refresh()
        {
            name.Text = context.Get("name", "尚未选择实例"); ToolTip.SetTip(name, name.Text);
            detail.Text = launch ? context.Get("version", "选择已有游戏目录") + "\n" + context.Get("account-kind", "离线模式") + " · " + context.Get("username", "Player") : context.Get("count", "0") + " 个实例 · " + context.Get("version", "选择已有游戏目录");
            detail.MaxLines = 3; ToolTip.SetTip(detail, detail.Text);
            status.Text = launch ? context.Get("error") is { Length: > 0 } error ? error : context.Get("status", "选择实例后即可开始") : "在实例列表中选择启动目标或打开设置。";
            ToolTip.SetTip(status, status.Text); status.Foreground = launch && context.Get("error").Length > 0 ? Palette.Error : Palette.Muted;
            primary.IsEnabled = !context.IsPreview && (!launch || context.Get("can-launch") == "true");
            if (launch) primary.Content = context.Get("phase") switch { "Preparing" => "检查中…", "Running" => "游戏运行中", _ => accountAware ? "启动游戏" : "离线启动" };
        }
        Refresh(); return ComponentContent.Card(layout, Refresh, point => point.Y < 38);
    }
    private static TextBlock Label() => new() { FontSize = 11, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis };

    private sealed class Runtime(InstanceService service, Action openManager, AccountService? accounts) : IComponentRuntime
    {
        private bool _subscribed;
        private int _pending;
        public event EventHandler? Changed;
        public IReadOnlyDictionary<string, string> Values
        {
            get
            {
                var catalog = service.Catalog; var selected = catalog.Instances.FirstOrDefault(x => x.Id == catalog.SelectedInstanceId);
                return new Dictionary<string, string>
                {
                    ["name"] = selected?.Name ?? "尚未选择实例", ["version"] = selected?.VersionLabel ?? "选择已有游戏目录", ["count"] = catalog.Instances.Count.ToString(),
                    ["username"] = accounts is null ? catalog.OfflineUsername : accounts.SelectedAccount?.Username ?? "请先添加账户",
                    ["account-kind"] = accounts is null ? "离线模式" : accounts.SelectedAccount is { } account ? AccountService.KindLabel(account.Kind) : "未选择账户",
                    ["status"] = service.StatusMessage, ["error"] = service.Error ?? "",
                    ["phase"] = service.LaunchPhase.ToString(), ["can-launch"] = (!service.IsBusy && selected is not null && (accounts is null || !accounts.IsBusy && !accounts.IsReadBlocked && accounts.SelectedAccount is not null)).ToString().ToLowerInvariant()
                };
            }
        }
        private void OnChanged(object? sender, EventArgs args)
        {
            if (Dispatcher.UIThread.CheckAccess()) Changed?.Invoke(this, EventArgs.Empty);
            else if (Interlocked.Exchange(ref _pending, 1) == 0) Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref _pending, 0);
                if (_subscribed) Changed?.Invoke(this, EventArgs.Empty);
            });
        }
        public void Resume() { if (!_subscribed) { service.Changed += OnChanged; if (accounts is not null) accounts.Changed += OnChanged; _subscribed = true; OnChanged(this, EventArgs.Empty); } }
        public void Suspend() { if (_subscribed) { service.Changed -= OnChanged; if (accounts is not null) accounts.Changed -= OnChanged; _subscribed = false; } }
        public void Invoke(string actionId, string? value)
        {
            if (actionId == "manage") openManager();
            else if (actionId == "launch") _ = service.LaunchSelectedAsync();
        }
        public void Dispose() { Suspend(); Changed = null; }
    }
}
