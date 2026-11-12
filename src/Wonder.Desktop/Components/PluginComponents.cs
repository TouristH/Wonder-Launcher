// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Wonder.Extensibility;
using Wonder.Extensibility.Contracts;
using Wonder.Workspace;

namespace Wonder.Desktop.Components;

internal static class PluginComponents
{
    public static void Register(ComponentRegistry registry, PluginManager manager, Action openManager) =>
        registry.Register(new(ComponentIds.Plugins, "插件", "管理本地插件、修改配置，并从在线仓库下载安装。", 300, 144),
            _ => new Runtime(manager, openManager), Present);

    private static ComponentContent Present(ComponentPresentationContext context)
    {
        var body = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(new TextBlock { Text = "插件", FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Palette.Accent });
        var count = new TextBlock { Foreground = Palette.Text, FontSize = 13 };
        var status = new TextBlock { Foreground = Palette.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxLines = 2 };
        body.Children.Add(count);
        body.Children.Add(status);
        return ComponentContent.ActionCard(context, body, "打开插件管理", "open", () =>
        {
            count.Text = context.Get("count", "0 个本地插件 · 0 个已启用");
            status.Text = context.Get("status", "本地管理与在线仓库");
        });
    }

    private sealed class Runtime(PluginManager manager, Action openManager) : IComponentRuntime
    {
        private bool _subscribed;
        private int _refreshPending;
        public event EventHandler? Changed;
        public IReadOnlyDictionary<string, string> Values => new Dictionary<string, string>
        {
            ["count"] = $"{manager.Plugins.Count} 个本地插件 · {manager.Plugins.Count(plugin => plugin.Enabled)} 个已启用",
            ["status"] = manager.Error ?? "本地管理与在线仓库"
        };
        public void Resume()
        {
            if (_subscribed) return;
            _subscribed = true;
            manager.Changed += OnChanged;
        }
        public void Suspend()
        {
            if (!_subscribed) return;
            _subscribed = false;
            manager.Changed -= OnChanged;
        }
        private void OnChanged(object? sender, EventArgs args)
        {
            if (Interlocked.Exchange(ref _refreshPending, 1) != 0) return;
            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref _refreshPending, 0);
                if (_subscribed) Changed?.Invoke(this, EventArgs.Empty);
            });
        }
        public void Invoke(string actionId, string? value) { if (actionId == "open") openManager(); }
        public void Dispose() { Suspend(); Changed = null; }
    }
}
