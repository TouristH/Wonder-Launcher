// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
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

internal static class DownloadComponents
{
    public static void Register(ComponentRegistry registry, DownloadService service, Action openCenter) =>
        registry.Register(new(ComponentIds.Downloads, "下载", "安装游戏、加载器、模组、整合包、资源包与光影，管理后台下载任务。", 340, 240),
            _ => new Runtime(service, openCenter), Present);

    private static ComponentContent Present(ComponentPresentationContext context)
    {
        var layout = new Grid { RowDefinitions = new RowDefinitions("38,*,Auto") };
        layout.Children.Add(new TextBlock { Text = "下载", FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Palette.Accent, Margin = new Thickness(15, 10, 15, 0) });
        var body = new StackPanel { Spacing = 9, Margin = new Thickness(15, 6, 15, 10) };
        var title = new TextBlock { FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text, TextTrimming = TextTrimming.CharacterEllipsis };
        var status = PageStyles.Hint(""); status.MaxLines = 2; status.TextTrimming = TextTrimming.CharacterEllipsis;
        var source = PageStyles.Hint("");
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 6 };
        body.Children.Add(title); body.Children.Add(status); body.Children.Add(progress); body.Children.Add(source);
        Grid.SetRow(body, 1); layout.Children.Add(body);
        var open = new Button { Content = "打开下载中心", MinHeight = 34, Margin = new Thickness(15, 0, 15, 14), HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = !context.IsPreview };
        AutomationProperties.SetName(open, "打开下载中心"); open.Click += (_, _) => context.Invoke("open");
        Grid.SetRow(open, 2); layout.Children.Add(open);
        void Refresh()
        {
            title.Text = context.Get("title", "发现下一个世界"); status.Text = context.Get("status", "游戏、模组、整合包、资源包与光影");
            source.Text = context.Get("source", "下载源可在启动器设置中切换");
            progress.IsVisible = context.Get("active") == "true";
            progress.IsIndeterminate = context.Get("indeterminate") == "true";
            progress.Value = double.TryParse(context.Get("percent"), System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;
        }
        Refresh(); return ComponentContent.Card(layout, Refresh, point => point.Y < 38);
    }
    private sealed class Runtime(DownloadService service, Action openCenter) : IComponentRuntime
    {
        private bool _subscribed; private int _pending;
        public event EventHandler? Changed;
        public IReadOnlyDictionary<string, string> Values
        {
            get
            {
                var tasks = service.Tasks;
                var active = tasks.FirstOrDefault(x => x.State == DownloadTaskState.Running) ?? tasks.FirstOrDefault(x => x.State == DownloadTaskState.Queued);
                var p = active?.Progress;
                var percent = p?.TotalBytes is > 0 ? p.BytesReceived * 100d / p.TotalBytes.Value : p?.TotalFiles > 0 ? p.CompletedFiles * 100d / p.TotalFiles : 0;
                return new Dictionary<string, string>
                {
                    ["title"] = active?.Title ?? "发现下一个世界",
                    ["status"] = active is null ? $"{tasks.Count(x => x.State == DownloadTaskState.Completed)} 项完成 · {tasks.Count(x => x.State == DownloadTaskState.Paused)} 项暂停 · {tasks.Count(x => x.State == DownloadTaskState.Failed)} 项失败" :
                        active.Progress.Stage + " · " + DownloadCenterWindow.Bytes((long)Math.Max(0, active.Progress.BytesPerSecond)) + "/s",
                    ["source"] = "下载源：" + DownloadCenterWindow.SourceLabel(service.Options.Source),
                    ["active"] = active is null ? "false" : "true",
                    ["indeterminate"] = active is not null && p?.TotalBytes is not > 0 && p?.TotalFiles is not > 0 ? "true" : "false",
                    ["percent"] = Math.Clamp(percent, 0, 100).ToString(System.Globalization.CultureInfo.InvariantCulture)
                };
            }
        }
        public void Resume() { if (_subscribed) return; _subscribed = true; service.Changed += OnChanged; }
        public void Suspend() { if (!_subscribed) return; _subscribed = false; service.Changed -= OnChanged; }
        private void OnChanged(object? sender, EventArgs args)
        {
            if (Interlocked.Exchange(ref _pending, 1) != 0) return;
            Dispatcher.UIThread.Post(() => { Interlocked.Exchange(ref _pending, 0); if (_subscribed) Changed?.Invoke(this, EventArgs.Empty); });
        }
        public void Invoke(string actionId, string? value) { if (actionId == "open") openCenter(); }
        public void Dispose() { Suspend(); Changed = null; }
    }
}
