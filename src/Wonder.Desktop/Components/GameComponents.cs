// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
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
            _ => new Runtime(service, openManager, accounts), PresentInstances);
        registry.Register(new(ComponentIds.Launch, "启动游戏", "点击整个组件启动当前选中的实例；长按可移动，启动详情见启动日志组件。", 300, 160),
            _ => new Runtime(service, openManager, accounts), context => PresentLaunch(context, accounts is not null));
        registry.Register(new(ComponentIds.LaunchLog, "启动日志", "查看实时启动状态和最近 200 行日志，取消启动准备或打开完整状态窗口。", 480, 300),
            _ => new Runtime(service, openManager, accounts, includeLogs: true), context => PresentLog(context, OpenStatus));
    }

    private static ComponentContent PresentLaunch(ComponentPresentationContext context, bool accountAware)
    {
        var body = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        var action = new TextBlock { FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Palette.Accent };
        var name = new TextBlock { FontSize = 22, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text, TextTrimming = TextTrimming.CharacterEllipsis };
        var detail = Label(); detail.MaxLines = 2;
        body.Children.Add(action); body.Children.Add(name); body.Children.Add(detail);
        var surface = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(22, 18), Child = body };
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            Template = new FuncControlTemplate<Button>((_, _) => surface)
        };
        AutomationProperties.SetName(button, accountAware ? "启动所选账户游戏" : "离线启动游戏");
        button.Click += (_, _) => context.Invoke("launch");
        var selected = false;
        void Paint()
        {
            surface.Background = button.IsPressed ? Palette.Pressed : button.IsPointerOver ? Palette.Hover : Palette.Active;
            surface.BorderBrush = selected || button.IsFocused ? Palette.Emphasis : Palette.Accent;
            surface.BorderThickness = new Thickness(selected || button.IsFocused ? 2.6 : 1);
            body.Opacity = button.IsEnabled || context.IsPreview ? 1 : .55;
        }
        button.PropertyChanged += (_, args) =>
        {
            if (args.Property == Button.IsPressedProperty || args.Property == Button.IsPointerOverProperty ||
                args.Property == Button.IsFocusedProperty || args.Property == Button.IsEnabledProperty) Paint();
        };
        void Refresh()
        {
            name.Text = context.Get("name", "尚未选择实例");
            detail.Text = context.Get("version", "请先在实例管理中选择实例") + "\n" +
                context.Get("account-kind", "离线模式") + " · " + context.Get("username", "Player");
            action.Text = context.Get("phase") switch { "Preparing" => "正在准备…", "Running" => "游戏运行中", _ => "▶  启动游戏" };
            button.IsEnabled = !context.IsPreview && context.Get("can-launch") == "true";
            ToolTip.SetTip(button, name.Text + "\n" + detail.Text);
            AutomationProperties.SetHelpText(button, name.Text + " · " + action.Text);
            Paint();
        }
        Refresh();
        return new ComponentContent(button, Refresh, value => { selected = value; Paint(); });
    }

    private static ComponentContent PresentLog(ComponentPresentationContext context, Action<Window?> openStatus)
    {
        var layout = new Grid { RowDefinitions = new RowDefinitions("38,Auto,*,Auto") };
        layout.Children.Add(new TextBlock { Text = "启动日志", FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Palette.Accent, Margin = new Thickness(15, 10, 15, 0) });
        var status = Label(); status.Margin = new Thickness(15, 0, 15, 8); status.MaxLines = 3;
        AutomationProperties.SetName(status, "当前启动状态"); Grid.SetRow(status, 1); layout.Children.Add(status);
        var logs = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 11,
            Margin = new Thickness(15, 0, 15, 10), PlaceholderText = "启动后在这里查看日志…"
        };
        AutomationProperties.SetName(logs, "最近启动日志");
        ScrollViewer.SetHorizontalScrollBarVisibility(logs, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(logs, ScrollBarVisibility.Auto);
        Grid.SetRow(logs, 2); layout.Children.Add(logs);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(15, 0, 15, 14) };
        var cancel = new Button { Content = "取消准备", MinHeight = 32 };
        AutomationProperties.SetName(cancel, "取消启动准备"); cancel.Click += (_, _) => context.Invoke("cancel"); actions.Children.Add(cancel);
        var expand = new Button { Content = "查看启动状态", MinHeight = 32, IsEnabled = !context.IsPreview };
        AutomationProperties.SetName(expand, "查看启动状态");
        expand.Click += (_, _) => { if (!context.IsPreview) openStatus(TopLevel.GetTopLevel(layout) as Window); }; actions.Children.Add(expand);
        Grid.SetRow(actions, 3); layout.Children.Add(actions);
        void Refresh()
        {
            var error = context.Get("error");
            status.Text = context.Get("status", "尚未启动游戏") + (error.Length > 0 ? "\n" + error : "");
            status.Foreground = error.Length > 0 ? Palette.Error : Palette.Muted; ToolTip.SetTip(status, status.Text);
            var text = context.Get("logs");
            if (logs.Text != text) { logs.Text = text; logs.CaretIndex = text.Length; }
            cancel.IsVisible = context.Get("phase") == "Preparing";
            cancel.IsEnabled = !context.IsPreview && cancel.IsVisible;
        }
        Refresh(); return ComponentContent.Card(layout, Refresh, point => point.Y < 38);
    }

    private static ComponentContent PresentInstances(ComponentPresentationContext context)
    {
        var layout = new Grid { RowDefinitions = new RowDefinitions("38,*,Auto") };
        layout.Children.Add(new TextBlock { Text = context.Definition.Title, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Palette.Accent, Margin = new Thickness(15, 10, 15, 0) });
        var body = new StackPanel { Spacing = 7, Margin = new Thickness(15, 5, 15, 8) };
        var name = new TextBlock { FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text, TextTrimming = TextTrimming.CharacterEllipsis };
        var detail = Label(); var status = Label(); status.MaxHeight = 36;
        body.Children.Add(name); body.Children.Add(detail); body.Children.Add(status); Grid.SetRow(body, 1); layout.Children.Add(body);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(15, 0, 15, 14) };
        var primary = new Button { Content = "管理实例", MinHeight = 34, IsEnabled = !context.IsPreview };
        AutomationProperties.SetName(primary, "打开实例管理");
        primary.Click += (_, _) => context.Invoke("manage"); actions.Children.Add(primary);
        Grid.SetRow(actions, 2); layout.Children.Add(actions);
        void Refresh()
        {
            name.Text = context.Get("name", "尚未选择实例"); ToolTip.SetTip(name, name.Text);
            detail.Text = context.Get("count", "0") + " 个实例 · " + context.Get("version", "选择已有游戏目录");
            detail.MaxLines = 3; ToolTip.SetTip(detail, detail.Text);
            status.Text = "在实例列表中选择启动目标或打开设置。";
        }
        Refresh(); return ComponentContent.Card(layout, Refresh, point => point.Y < 38);
    }
    private static TextBlock Label() => new() { FontSize = 11, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis };

    private sealed class Runtime(InstanceService service, Action openManager, AccountService? accounts, bool includeLogs = false) : IComponentRuntime
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
                    ["logs"] = includeLogs ? string.Join(Environment.NewLine, service.LogLines) : "",
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
            else if (actionId == "cancel") service.CancelPreparation();
        }
        public void Dispose() { Suspend(); Changed = null; }
    }
}
