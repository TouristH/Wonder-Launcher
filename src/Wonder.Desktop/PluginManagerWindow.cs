// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Wonder.Extensibility;

namespace Wonder.Desktop;

internal sealed partial class PluginManagerWindow : Window
{
    internal const string DefaultRepositoryUrl = "https://raw.githubusercontent.com/TouristH/UNuse/dev/public/wonder/v1/index.json";
    private readonly PluginManager _manager;
    private readonly StackPanel _installed = new() { Spacing = 12 };
    private readonly StackPanel _available = new() { Spacing = 12 };
    private readonly TextBox _repository = new() { Text = DefaultRepositoryUrl, MinWidth = 160 };
    private readonly TextBlock _status = Hint("");
    private readonly TextBlock _localError = Hint("");
    private readonly Grid _content;
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Height = 4, IsVisible = false };
    private readonly Button _cancel;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private IReadOnlyList<PluginCatalogEntry> _catalog = [];
    private bool _busy, _closed, _catalogRequested;
    private int _refreshPending;

    public PluginManagerWindow(PluginManager manager)
    {
        _manager = manager;
        Title = "插件 · Wonder Launcher";
        Width = 980;
        Height = 740;
        MinWidth = 700;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Palette.Background;
        Foreground = Palette.Text;
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(24) };
        layout.Children.Add(Heading("插件", 28));
        _content = new Grid { Margin = new Thickness(0, 20, 0, 16) };
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "已安装", Content = BuildInstalledPage() });
        tabs.Items.Add(new TabItem { Header = "在线仓库", Content = BuildOnlinePage() });
        tabs.SelectionChanged += (_, _) =>
        {
            if (tabs.SelectedIndex != 1 || _catalogRequested || _busy) return;
            _catalogRequested = true;
            UiOperation.Start(RefreshCatalogAsync, () => _closed);
        };
        _content.Children.Add(tabs);
        Grid.SetRow(_content, 1);
        layout.Children.Add(_content);
        var footer = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _status.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetName(_status, "插件管理状态");
        footer.Children.Add(_status);
        _cancel = ActionButton("取消操作", () => _operation?.Cancel());
        _cancel.IsVisible = false;
        _cancel.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(_cancel, 1);
        footer.Children.Add(_cancel);
        _progress.Margin = new Thickness(0, 10, 0, 0);
        Grid.SetRow(_progress, 1);
        Grid.SetColumnSpan(_progress, 2);
        footer.Children.Add(_progress);
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);
        Content = layout;
        manager.Changed += OnManagerChanged;
        Closed += (_, _) =>
        {
            _closed = true;
            manager.Changed -= OnManagerChanged;
            _lifetime.Cancel();
            _lifetime.Dispose();
        };
        RefreshInstalled();
        ShowStatus("安装完成后可在“已安装”中启用插件。");
    }

    private Control BuildInstalledPage()
    {
        var page = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), Margin = new Thickness(0, 16, 0, 0) };
        var tools = new WrapPanel { Orientation = Orientation.Horizontal };
        tools.Children.Add(ActionButton("打开本地插件目录", () => OpenDirectory(_manager.RootDirectory)));
        tools.Children.Add(ActionButton("导入 ZIP", () => UiOperation.Start(ImportAsync, () => _closed)));
        tools.Children.Add(ActionButton("刷新本地插件", () => UiOperation.Start(() => RunAsync("正在读取本地插件…", _ =>
        {
            _manager.Refresh();
            ShowStatus("本地插件已刷新。");
            return Task.CompletedTask;
        }), () => _closed)));
        page.Children.Add(tools);
        var notes = new StackPanel { Spacing = 6, Margin = new Thickness(0, 10, 0, 14) };
        notes.Children.Add(Hint("代码插件以当前系统用户的权限运行，请启用你信任的插件。"));
        _localError.Foreground = Palette.Error;
        notes.Children.Add(_localError);
        Grid.SetRow(notes, 1);
        page.Children.Add(notes);
        var scroll = Scroll(_installed);
        Grid.SetRow(scroll, 2);
        page.Children.Add(scroll);
        return page;
    }

    private Control BuildOnlinePage()
    {
        var page = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(0, 16, 0, 0) };
        var source = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 14) };
        AutomationProperties.SetName(_repository, "在线插件仓库地址");
        _repository.PlaceholderText = "HTTPS 插件索引地址";
        source.Children.Add(_repository);
        var refresh = ActionButton("刷新在线仓库", () => UiOperation.Start(RefreshCatalogAsync, () => _closed));
        Grid.SetColumn(refresh, 1);
        source.Children.Add(refresh);
        page.Children.Add(source);
        _available.Children.Add(Hint("正在等待读取在线仓库。"));
        var scroll = Scroll(_available);
        Grid.SetRow(scroll, 1);
        page.Children.Add(scroll);
        return page;
    }

    private void OnManagerChanged(object? sender, EventArgs args)
    {
        if (Interlocked.Exchange(ref _refreshPending, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _refreshPending, 0);
            if (_closed) return;
            RefreshInstalled();
            if (_catalogRequested) RefreshAvailable();
        });
    }

    private void RefreshInstalled()
    {
        _localError.Text = _manager.Error;
        _localError.IsVisible = !string.IsNullOrWhiteSpace(_manager.Error);
        _installed.Children.Clear();
        if (_manager.Plugins.Count == 0)
        {
            _installed.Children.Add(Hint("还没有本地插件。可导入 ZIP，或前往在线仓库下载。"));
            return;
        }
        foreach (var plugin in _manager.Plugins)
        {
            var body = new StackPanel { Spacing = 8 };
            body.Children.Add(Heading(plugin.Manifest.Name));
            body.Children.Add(Hint($"{plugin.Manifest.Version} · {plugin.Manifest.Author} · {plugin.Manifest.Id}"));
            body.Children.Add(Hint(plugin.Manifest.Description));
            var state = Hint(plugin.Enabled ? "已启用" : "已关闭");
            if (plugin.Error is { Length: > 0 } error)
            {
                state.Text = "加载失败 · " + error;
                state.Foreground = Palette.Error;
            }
            body.Children.Add(state);
            var actions = new WrapPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(ActionButton(plugin.Enabled ? "关闭" : "启用", () => UiOperation.Start(() => RunAsync("正在切换插件状态…", _ =>
            {
                _manager.SetEnabled(plugin.Manifest.Id, !plugin.Enabled);
                var current = _manager.Plugins.FirstOrDefault(item => item.Manifest.Id == plugin.Manifest.Id);
                ShowStatus(current?.Error ?? (current?.Enabled == true ? "插件已启用。" : "插件已关闭。"), current?.Error is not null);
                return Task.CompletedTask;
            }), () => _closed), (plugin.Enabled ? "关闭插件 " : "启用插件 ") + plugin.Manifest.Name));
            actions.Children.Add(ActionButton("修改配置", () => UiOperation.Start(() => EditConfigurationAsync(plugin), () => _closed), "修改配置 " + plugin.Manifest.Name));
            actions.Children.Add(ActionButton("打开目录", () => OpenDirectory(plugin.Directory), "打开插件目录 " + plugin.Manifest.Name));
            body.Children.Add(actions);
            if (plugin.Messages.Count > 0)
            {
                var logs = Hint(string.Join(Environment.NewLine, plugin.Messages.TakeLast(8)));
                body.Children.Add(new Expander { Header = "运行日志", Content = logs });
            }
            _installed.Children.Add(Card(body));
        }
    }

    internal Task RefreshCatalogAsync() => RunAsync("正在读取在线仓库…", async token =>
    {
        var catalog = await _manager.GetCatalogAsync(_repository.Text?.Trim() ?? "", token);
        if (_closed) return;
        _catalog = catalog;
        _catalogRequested = true;
        RefreshAvailable();
        ShowStatus($"已读取 {_catalog.Count} 个在线插件。");
    });

    private void RefreshAvailable()
    {
        _available.Children.Clear();
        if (_catalog.Count == 0)
        {
            _available.Children.Add(Hint("这个仓库暂时没有可下载的插件。"));
            return;
        }
        foreach (var entry in _catalog)
        {
            var body = new StackPanel { Spacing = 8 };
            body.Children.Add(Heading(entry.Name));
            body.Children.Add(Hint($"{entry.Version} · {entry.Author} · {entry.Id}"));
            body.Children.Add(Hint(entry.Description));
            var installed = _manager.Plugins.FirstOrDefault(plugin => plugin.Manifest.Id == entry.Id);
            var installedSameVersion = installed?.Manifest.Version == entry.Version;
            var label = installedSameVersion ? "已安装" : installed is null ? "下载安装" : "安装此版本";
            if (installed is not null) body.Children.Add(Hint("本地版本 " + installed.Manifest.Version));
            var download = ActionButton(label, () => UiOperation.Start(() => RunAsync("正在下载并校验 " + entry.Name + "…", async token =>
            {
                await _manager.InstallAsync(entry, token);
                ShowStatus(entry.Name + " 已安装，可在“已安装”中启用。");
            }), () => _closed), "安装在线插件 " + entry.Name);
            download.IsEnabled = !installedSameVersion;
            body.Children.Add(download);
            if (!string.IsNullOrWhiteSpace(entry.SourceUrl)) body.Children.Add(Hint("项目来源 " + entry.SourceUrl));
            _available.Children.Add(Card(body));
        }
    }

    private Task ImportAsync() => RunAsync("请选择本地插件 ZIP。", async token =>
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入 Wonder 插件", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Wonder 插件包") { Patterns = ["*.zip"] }]
        });
        token.ThrowIfCancellationRequested();
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is null) { ShowStatus("已取消选择插件包。"); return; }
        ShowStatus("正在校验并安装插件包…");
        await _manager.InstallLocalAsync(path, token);
        ShowStatus("插件包已安装，可在“已安装”中启用。");
    });

    private async Task RunAsync(string message, Func<CancellationToken, Task> action)
    {
        if (_closed || _busy) return;
        _busy = true;
        _content.IsEnabled = false;
        _progress.IsVisible = true;
        _cancel.IsVisible = true;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = operation;
        ShowStatus(message);
        try { await action(operation.Token); }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { ShowStatus("操作已取消。"); }
        catch (Exception error) { ShowStatus(error.Message, true); }
        finally
        {
            _operation = null;
            _busy = false;
            if (!_closed)
            {
                _content.IsEnabled = true;
                _progress.IsVisible = false;
                _cancel.IsVisible = false;
                RefreshInstalled();
                if (_catalogRequested) RefreshAvailable();
            }
        }
    }

    private void OpenDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception error) { ShowStatus(error.Message, true); }
    }

    private void ShowStatus(string message, bool error = false)
    {
        if (_closed) return;
        _status.Text = message;
        _status.Foreground = error ? Palette.Error : Palette.Muted;
    }

    private static TextBlock Heading(string text, double size = 17) => new()
    {
        Text = text, FontSize = size, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text, TextWrapping = TextWrapping.Wrap
    };
    private static TextBlock Hint(string? text) => new() { Text = text, FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    private static Button ActionButton(string text, Action action, string? accessibleName = null)
    {
        var button = new Button { Content = text, MinHeight = 34, Margin = new Thickness(0, 0, 8, 4) };
        AutomationProperties.SetName(button, accessibleName ?? text);
        button.Click += (_, _) => action();
        return button;
    }
    private static ScrollViewer Scroll(Control content) => new()
    {
        Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Padding = new Thickness(0, 0, 8, 0)
    };
    private static Border Card(Control content) => new()
    {
        Child = content, Padding = new Thickness(18), Background = Palette.Surface, BorderBrush = Palette.Line,
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12)
    };
}
