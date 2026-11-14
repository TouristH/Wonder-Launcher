// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Globalization;
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
        registry.Register(new(ComponentIds.Downloads, "下载", "安装游戏、加载器、模组、整合包、资源包与光影，管理后台下载任务。", 14 * ComponentGrid.Unit, 7 * ComponentGrid.Unit),
            _ => new Runtime(service, openCenter), Present, new("open"));

    private static ComponentContent Present(ComponentPresentationContext context)
    {
        var body = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock { Text = "↓  下载", FontSize = 13, LineHeight = 17, FontWeight = FontWeight.SemiBold, Foreground = Palette.Accent });
        var source = Label(); source.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(source, 1); header.Children.Add(source);
        var title = new TextBlock { FontSize = 13, LineHeight = 17, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text, TextTrimming = TextTrimming.CharacterEllipsis };
        var status = Label(); status.TextWrapping = TextWrapping.Wrap; status.MaxLines = 2;
        var metrics = Label();
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 6 };
        AutomationProperties.SetName(progress, "当前下载步骤进度");
        body.Children.Add(header); body.Children.Add(title); body.Children.Add(status); body.Children.Add(metrics); body.Children.Add(progress);
        void Refresh()
        {
            var values = context.Values;
            title.Text = values.GetValueOrDefault("title", "没有进行中的下载");
            status.Text = values.GetValueOrDefault("status", ""); status.IsVisible = status.Text.Length > 0;
            metrics.Text = values.GetValueOrDefault("metrics", ""); metrics.IsVisible = metrics.Text.Length > 0;
            source.Text = values.GetValueOrDefault("source", "");
            progress.IsVisible = values.GetValueOrDefault("active") == "true";
            progress.IsIndeterminate = values.GetValueOrDefault("indeterminate") == "true";
            progress.Value = double.TryParse(values.GetValueOrDefault("percent"), CultureInfo.InvariantCulture, out var value) ? value : 0;
        }
        return ComponentContent.ActionCard(context, body, "打开下载中心", "open", Refresh, description: () =>
            string.Join("\n", new[] { title.Text, status.Text, metrics.Text, source.Text }.Where(text => !string.IsNullOrEmpty(text))));
    }

    private static TextBlock Label() => new() { FontSize = 10, LineHeight = 14, Foreground = Palette.Muted, TextTrimming = TextTrimming.CharacterEllipsis };

    private sealed class Runtime(DownloadService service, Action openCenter) : IComponentRuntime
    {
        private bool _subscribed; private int _pending;
        public event EventHandler? Changed;
        public IReadOnlyDictionary<string, string> Values
        {
            get
            {
                var tasks = service.Tasks;
                var running = tasks.FirstOrDefault(x => x.State == DownloadTaskState.Running);
                var active = running ?? tasks.FirstOrDefault(x => x.State == DownloadTaskState.Queued);
                var p = active?.Progress;
                var knownTotal = p?.TotalBytes is > 0 || p?.TotalFiles > 0;
                var percent = Math.Clamp(p?.TotalBytes is > 0 ? p.BytesReceived * 100d / p.TotalBytes.Value : p?.TotalFiles > 0 ? p.CompletedFiles * 100d / p.TotalFiles : 0, 0, 100);
                var queued = tasks.Count(x => x.State == DownloadTaskState.Queued) - (active?.State == DownloadTaskState.Queued ? 1 : 0);
                var status = p?.Stage ?? "";
                if (p is { TotalSteps: > 0 })
                {
                    var step = string.IsNullOrWhiteSpace(p.StepName) ? p.Stage : p.StepName;
                    status = $"第 {p.StepNumber}/{p.TotalSteps} 步 · {step}";
                    if (p.Stage != step) status += "\n" + p.Stage;
                }
                var metrics = new List<string>();
                if (knownTotal) metrics.Add($"{percent:F0}%");
                if (running is not null) metrics.Add(DownloadCenterWindow.Bytes((long)Math.Max(0, running.Progress.BytesPerSecond)) + "/s");
                if (queued > 0) metrics.Add($"{queued} 项排队");
                if (active is null && tasks.Count > 0)
                    status = $"{tasks.Count(x => x.State == DownloadTaskState.Completed)} 项完成 · {tasks.Count(x => x.State == DownloadTaskState.Paused)} 项暂停 · {tasks.Count(x => x.State == DownloadTaskState.Failed)} 项失败";
                return new Dictionary<string, string>
                {
                    ["title"] = active?.Title ?? "没有进行中的下载",
                    ["status"] = status,
                    ["metrics"] = string.Join(" · ", metrics),
                    ["source"] = "下载源：" + DownloadCenterWindow.SourceLabel(service.Options.Source),
                    ["active"] = active is null ? "false" : "true",
                    ["indeterminate"] = running is not null && !knownTotal ? "true" : "false",
                    ["percent"] = percent.ToString(CultureInfo.InvariantCulture)
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
