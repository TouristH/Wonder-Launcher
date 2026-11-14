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
using Wonder.Core;
using Wonder.Infrastructure;
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
        registry.Register(new(ComponentIds.Instances, "实例管理", "切换当前游戏目录内的实例，管理目录与启动设置。", 14 * ComponentGrid.Unit, 7 * ComponentGrid.Unit),
            _ => new Runtime(service, openManager, accounts), context => PresentInstances(context, service), new("manage"));
        registry.Register(new(ComponentIds.Launch, "启动游戏", "点击整个组件启动当前选中的实例；长按可移动，启动详情见启动日志组件。", 14 * ComponentGrid.Unit, 7 * ComponentGrid.Unit),
            _ => new Runtime(service, openManager, accounts), context => PresentLaunch(context, accounts is not null), new("manage", Title: "游戏实例"));
        registry.Register(new(ComponentIds.LaunchLog, "启动日志", "查看实时启动状态和最近 200 行日志，取消启动准备或打开完整状态窗口。", 20 * ComponentGrid.Unit, 13 * ComponentGrid.Unit),
            _ => new Runtime(service, openManager, accounts, includeLogs: true), context => PresentLog(context, OpenStatus), new(OpenPage: OpenStatus));
    }

    private static ComponentContent PresentLaunch(ComponentPresentationContext context, bool accountAware)
    {
        var body = new StackPanel { Spacing = 7, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        var action = new TextBlock { FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Palette.Accent };
        var name = new TextBlock { FontSize = 19, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text, TextTrimming = TextTrimming.CharacterEllipsis };
        var detail = Label(); detail.MaxLines = 2;
        var icon = new InstanceIconView(30);
        var launchHeading = new Grid { ColumnDefinitions = new ColumnDefinitions("30,8,*") };
        launchHeading.Children.Add(icon); Grid.SetColumn(action, 2); action.VerticalAlignment = VerticalAlignment.Center; launchHeading.Children.Add(action);
        body.Children.Add(launchHeading); body.Children.Add(name); body.Children.Add(detail);
        void Refresh()
        {
            name.Text = context.Get("name", "尚未选择实例");
            icon.SetIcon(context.Get("icon-path"), context.Get("icon-key", "minecraft"));
            detail.Text = context.Get("version", "请先在实例管理中选择实例") + "\n" +
                context.Get("account-kind", "离线模式") + " · " + context.Get("username", "Player");
            action.Text = context.Get("phase") switch { "Preparing" => "正在准备…", "Running" => "游戏运行中", _ => "▶  启动游戏" };
        }
        return ComponentContent.ActionCard(context, body, accountAware ? "启动所选账户游戏" : "离线启动游戏", "launch",
            Refresh, () => context.Get("can-launch") == "true", () => name.Text + "\n" + detail.Text);
    }

    private static ComponentContent PresentLog(ComponentPresentationContext context, Action<Window?> openStatus)
    {
        var layout = new Grid { RowDefinitions = new RowDefinitions("38,Auto,*,Auto") };
        layout.Children.Add(new TextBlock { Text = "启动日志", FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Palette.Accent, Margin = new Thickness(15, 10, 15, 0) });
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
        var cancel = new Button { Content = "取消准备", MinHeight = 28, FontSize = 11, Padding = new Thickness(10, 4) };
        AutomationProperties.SetName(cancel, "取消启动准备"); cancel.Click += (_, _) => context.Invoke("cancel"); actions.Children.Add(cancel);
        var expand = new Button { Content = "查看启动状态", MinHeight = 28, FontSize = 11, Padding = new Thickness(10, 4), IsEnabled = !context.IsPreview };
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

    private static ComponentContent PresentInstances(ComponentPresentationContext context, InstanceService service)
    {
        var layout = new Grid { RowDefinitions = new RowDefinitions("30,*,Auto") };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(14, 7, 14, 0) };
        heading.Children.Add(new TextBlock { Text = context.Definition.Title, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Palette.Accent });
        var count = Label();
        Grid.SetColumn(count, 1); heading.Children.Add(count); layout.Children.Add(heading);
        var choices = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0), MinHeight = 34,
            ItemTemplate = new FuncDataTemplate<GameInstance>((item, _) => InstanceChoice(item))
        };
        AutomationProperties.SetName(choices, "切换当前目录实例");
        var refreshing = false;
        GameInstance[] current = [];
        choices.SelectionChanged += (_, _) =>
        {
            if (!refreshing && choices.SelectedItem is GameInstance item) context.Invoke("select", item.Id);
        };
        Grid.SetRow(choices, 1); layout.Children.Add(choices);
        var primary = new Button
        {
            Content = "管理实例", Margin = new Thickness(12, 0, 12, 7), MinWidth = 160, MinHeight = 28, FontSize = 11, Padding = new Thickness(10, 4),
            HorizontalAlignment = HorizontalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center, IsEnabled = !context.IsPreview
        };
        AutomationProperties.SetName(primary, "打开实例管理");
        primary.Click += (_, _) => context.Invoke("manage");
        Grid.SetRow(primary, 2); layout.Children.Add(primary);
        void Refresh()
        {
            var catalog = service.Catalog;
            var directory = service.SelectedGameDirectory;
            var available = catalog.Instances.Where(item => WonderPath.Comparer.Equals(item.GameDirectoryGroup, directory)).ToArray();
            count.Text = directory is null ? "" : $"{available.Length} 个实例";
            ToolTip.SetTip(count, directory);
            choices.PlaceholderText = directory is null ? "尚未选择游戏目录" : available.Length == 0 ? "此目录没有实例" : "选择此目录中的实例";
            refreshing = true;
            try
            {
                if (!current.Select(ChoiceKey).SequenceEqual(available.Select(ChoiceKey)))
                {
                    current = available;
                    choices.ItemsSource = current;
                }
                choices.SelectedIndex = Array.FindIndex(current, item => item.Id == catalog.SelectedInstanceId);
                ToolTip.SetTip(choices, choices.SelectedItem is GameInstance item ? item.Name + " · " + item.VersionLabel : choices.PlaceholderText);
                choices.IsEnabled = !context.IsPreview && !service.IsBusy && !service.IsReadBlocked && current.Length > 0;
            }
            finally { refreshing = false; }
        }
        Refresh(); return ComponentContent.Card(layout, Refresh, point => point.Y < 30);
    }

    private static (string Id, string Name, string Version, string Root, string? Icon) ChoiceKey(GameInstance item) =>
        (item.Id, item.Name, item.VersionLabel, item.RootDirectory, item.PresetIconPath);

    private static Control InstanceChoice(GameInstance? item)
    {
        if (item is null) return new TextBlock();
        var icon = new InstanceIconView(23);
        icon.SetIcon(InstanceIcons.FindPreset(item), InstanceIcon.DefaultKey(item));
        icon.VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("23,8,*") };
        row.Children.Add(icon);
        var caption = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = item.Name, FontSize = 12, LineHeight = 16, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = item.VersionLabel, FontSize = 11, LineHeight = 14, Foreground = Palette.Muted, TextTrimming = TextTrimming.CharacterEllipsis }
            }
        };
        ToolTip.SetTip(row, item.Name + " · " + item.VersionLabel);
        Grid.SetColumn(caption, 2); row.Children.Add(caption);
        return row;
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
                    ["icon-path"] = selected is null ? "" : InstanceIcons.FindPreset(selected) ?? "",
                    ["icon-key"] = selected is null ? "minecraft" : InstanceIcon.DefaultKey(selected),
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
            else if (actionId == "select" && value is not null) UiOperation.Start(() => service.SelectAsync(value));
            else if (actionId == "launch") UiOperation.Start(() => service.LaunchSelectedAsync());
            else if (actionId == "cancel") service.CancelPreparation();
        }
        public void Dispose() { Suspend(); Changed = null; }
    }
}
