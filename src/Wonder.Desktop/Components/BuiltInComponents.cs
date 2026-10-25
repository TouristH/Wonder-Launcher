// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Wonder.Extensibility.Contracts;
using Wonder.Workspace;

namespace Wonder.Desktop.Components;

internal static class BuiltInComponents
{
    public static ComponentRegistry CreateRegistry(Action openSettings, string dataDirectory)
    {
        var registry = new ComponentRegistry();
        var settings = new ComponentDefinition(ComponentIds.Settings, "设置",
            "调整启动器外观与工作区交互。修改立即生效，所有入口共享同一份设置。", 168, 164)
        {
            Outline = PolygonGeometry.RegularPolygon(6, 0).Select(point =>
                new ComponentPoint((4 + point.X * 160) / 168, (4 + point.Y * 156) / 164)).ToArray()
        };
        registry.Register(settings, state => new SettingsRuntime(state, openSettings), Settings);
        registry.Register(new(ComponentIds.Notes, "便笺",
            "随手记录想法与待办，文字自动保存；同一应用的所有快捷方式实时同步。", 300, 220),
            state => new NotesRuntime(state), Notes);
        registry.Register(new(ComponentIds.LauncherStatus, "启动器状态",
            "查看版本、运行环境、数据位置和运行时长。组件隐藏时暂停刷新。", 316, 248),
            _ => new StatusRuntime(dataDirectory), Status);
        return registry;
    }

    private static ComponentContent Settings(ComponentPresentationContext context)
    {
        var stack = new StackPanel { Spacing = 7, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = "⚙", FontSize = 34, Foreground = Palette.Accent, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = "设置", FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = "外观与交互", FontSize = 10, Foreground = Palette.Muted, HorizontalAlignment = HorizontalAlignment.Center });
        var size = new Size(context.Definition.Width, context.Definition.Height);
        var action = new PolygonButton(new Points(context.Definition.Outline.Select(point =>
            new Point(point.X * size.Width, point.Y * size.Height))), size, stack);
        action.SetRestingFill(Palette.Active);
        AutomationProperties.SetName(action, "打开设置");
        action.Click += (_, _) => context.Invoke("open");
        return new ComponentContent(action, select: action.SetSelected);
    }

    private static ComponentContent Notes(ComponentPresentationContext context)
    {
        var panel = new Grid { RowDefinitions = new RowDefinitions("38,*,25") };
        panel.Children.Add(Header("便笺", "长按标题移动"));
        var input = new TextBox
        {
            Text = context.Get("text"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            PlaceholderText = "写下想法、待办或下次游玩的计划…", MaxLength = 65_536,
            Margin = new Thickness(12, 2, 12, 2), FontSize = 13,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = Palette.Text, IsReadOnly = context.IsPreview
        };
        AutomationProperties.SetName(input, "便笺内容");
        ScrollViewer.SetHorizontalScrollBarVisibility(input, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(input, ScrollBarVisibility.Auto);
        Grid.SetRow(input, 1);
        panel.Children.Add(input);
        var footer = new TextBlock { Text = "自动保存 · 与所有快捷方式同步", FontSize = 10,
            Foreground = Palette.Muted, Margin = new Thickness(15, 2, 12, 0) };
        Grid.SetRow(footer, 2);
        panel.Children.Add(footer);
        var refreshing = false;
        // TextChanged is queued by Avalonia; observe the property so closing a view cannot drop its last edit.
        input.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty && !refreshing) context.Invoke("set-text", input.Text ?? "");
        };
        return ComponentContent.Card(panel, () =>
        {
            var text = context.Get("text");
            if (input.Text == text) return;
            refreshing = true;
            try { input.Text = text; }
            finally { refreshing = false; }
        }, point => point.Y < 38);
    }

    private static ComponentContent Status(ComponentPresentationContext context)
    {
        var panel = new Grid { RowDefinitions = new RowDefinitions("38,*") };
        panel.Children.Add(Header("启动器状态", "实时"));
        var body = new StackPanel { Spacing = 9, Margin = new Thickness(15, 5, 15, 10) };
        var version = StatusLine();
        var runtime = StatusLine();
        var location = StatusLine();
        var uptime = StatusLine();
        location.FontSize = 11;
        location.MaxHeight = 60;
        body.Children.Add(version);
        body.Children.Add(runtime);
        body.Children.Add(new TextBlock { Text = "数据位置", FontSize = 10, Foreground = Palette.Muted });
        body.Children.Add(location);
        body.Children.Add(uptime);
        Grid.SetRow(body, 1);
        panel.Children.Add(body);
        void Refresh()
        {
            version.Text = "Wonder Launcher  " + context.Get("version", "—");
            runtime.Text = context.Get("runtime", "运行环境");
            location.Text = context.Get("data-directory", "显示时读取数据位置");
            ToolTip.SetTip(location, location.Text);
            uptime.Text = "已运行  " + context.Get("uptime", "—");
        }
        Refresh();
        return ComponentContent.Card(panel, Refresh);
    }

    private static TextBlock StatusLine() => new()
    {
        FontSize = 12, Foreground = Palette.Text, TextWrapping = TextWrapping.Wrap
    };

    private static Control Header(string title, string hint)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(15, 10, 15, 0) };
        header.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Palette.Accent });
        var label = new TextBlock { Text = hint, FontSize = 10, Foreground = Palette.Muted, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 1);
        header.Children.Add(label);
        return header;
    }

    private sealed class SettingsRuntime(IComponentState state, Action openSettings) : IComponentRuntime
    {
        public IReadOnlyDictionary<string, string> Values => state.Snapshot;
        public event EventHandler? Changed { add { } remove { } }
        public void Resume() { }
        public void Suspend() { }
        public void Invoke(string actionId, string? value) { if (actionId == "open") openSettings(); }
        public void Dispose() { }
    }

    private sealed class NotesRuntime(IComponentState state) : IComponentRuntime
    {
        public IReadOnlyDictionary<string, string> Values => state.Snapshot;
        public event EventHandler? Changed { add { } remove { } }
        public void Resume() { }
        public void Suspend() { }
        public void Invoke(string actionId, string? value) { if (actionId == "set-text") state.Set("text", value ?? ""); }
        public void Dispose() { }
    }

    private sealed class StatusRuntime : IComponentRuntime
    {
        private readonly Dictionary<string, string> _values;
        private readonly DispatcherTimer _timer;
        private static readonly long ProcessStart = GetProcessStart();
        public IReadOnlyDictionary<string, string> Values => _values;
        public event EventHandler? Changed;

        public StatusRuntime(string dataDirectory)
        {
            _values = new()
            {
                ["version"] = typeof(BuiltInComponents).Assembly.GetName().Version?.ToString(3) ?? "未知",
                ["runtime"] = RuntimeInformation.FrameworkDescription + " · " + RuntimeInformation.ProcessArchitecture,
                ["data-directory"] = dataDirectory
            };
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Tick;
            UpdateUptime();
        }
        private static long GetProcessStart()
        {
            using var process = Process.GetCurrentProcess();
            return Stopwatch.GetTimestamp() - (long)((DateTime.Now - process.StartTime).TotalSeconds * Stopwatch.Frequency);
        }
        private void UpdateUptime()
        {
            var elapsed = Stopwatch.GetElapsedTime(ProcessStart);
            _values["uptime"] = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        }
        private void Tick(object? sender, EventArgs args) { UpdateUptime(); Changed?.Invoke(this, EventArgs.Empty); }
        public void Resume() { UpdateUptime(); _timer.Start(); }
        public void Suspend() => _timer.Stop();
        public void Invoke(string actionId, string? value) { }
        public void Dispose()
        {
            _timer.Stop();
            _timer.Tick -= Tick;
            Changed = null;
        }
    }
}
