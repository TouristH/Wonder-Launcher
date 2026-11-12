// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Wonder.Application;
using Wonder.Core;
using ResourceProvider = Wonder.Core.ResourceProvider;

namespace Wonder.Desktop;

internal sealed partial class DownloadCenterWindow : Window
{
    internal const bool ShowCurseForgeByDefault = false;

    private readonly DownloadService _downloads;
    private readonly InstanceService _instances;
    private readonly bool _showCurseForge;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<Button> _navigation = [];
    private readonly Control _gamePage, _resourcesPage, _tasksPage;
    private readonly TextBlock _title, _subtitle, _source, _status;
    private readonly ScrollViewer _scroll;
    private int _page, _refreshPending;
    private bool _closed;

    public DownloadCenterWindow(DownloadService downloads, InstanceService instances, Action openSettings,
        ResourceProvider resourceProvider = ResourceProvider.Modrinth, bool showCurseForge = ShowCurseForgeByDefault)
    {
        _downloads = downloads; _instances = instances;
        _showCurseForge = showCurseForge;
        Title = "下载 · Wonder Launcher"; Width = 1120; Height = 810; MinWidth = 860; MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Palette.Background; Foreground = Palette.Text;
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(24) };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 22) };
        var brand = new StackPanel { Spacing = 5 };
        brand.Children.Add(Heading("下载", 29));
        _source = PageStyles.Hint(""); brand.Children.Add(_source); heading.Children.Add(brand);
        var settings = ActionButton("下载设置", openSettings); settings.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(settings, 1); heading.Children.Add(settings); layout.Children.Add(heading);
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("190,22,*") };
        var nav = new StackPanel { Spacing = 8 };
        string[] titles = ["游戏", "模组", "整合包", "资源包", "光影", "任务管理"];
        string[] captions = ["Minecraft 与加载器", "拓展游戏玩法", "一键安装新实例", "方块、音效与界面", "光照与渲染效果", "进度、暂停与重试"];
        for (var i = 0; i < titles.Length; i++)
        {
            var index = i;
            var button = PageStyles.CardButton("下载分类·" + titles[i], titles[i], captions[i], null, () => SelectPage(index));
            button.MinHeight = 68; PageStyles.SetCompact(button, true); _navigation.Add(button); nav.Children.Add(button);
        }
        content.Children.Add(new ScrollViewer { Content = nav, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var body = new StackPanel { Spacing = 15, Margin = new Thickness(0, 0, 6, 18) };
        _title = Heading("", 23); _subtitle = PageStyles.Hint(""); body.Children.Add(_title); body.Children.Add(_subtitle);
        _gamePage = BuildGamePage(); _resourcesPage = BuildResourcesPage(resourceProvider); _tasksPage = BuildTasksPage();
        body.Children.Add(_gamePage); body.Children.Add(_resourcesPage); body.Children.Add(_tasksPage);
        _scroll = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(_scroll, 2); content.Children.Add(_scroll); Grid.SetRow(content, 1); layout.Children.Add(content);
        _status = PageStyles.Hint("关闭此窗口后，下载任务继续运行。");
        _status.Margin = new Thickness(0, 15, 0, 0); AutomationProperties.SetName(_status, "下载中心状态");
        Grid.SetRow(_status, 2); layout.Children.Add(_status); Content = layout;
        downloads.Changed += OnDownloadsChanged; instances.Changed += OnInstancesChanged;
        Opened += (_, _) => UiOperation.Start(() => { RefreshInstances(); return LoadGameVersionsAsync(); }, () => _closed);
        Closed += (_, _) =>
        {
            _closed = true; downloads.Changed -= OnDownloadsChanged; instances.Changed -= OnInstancesChanged;
            _lifetime.Cancel(); _lifetime.Dispose();
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        SelectPage(0); RefreshDownloads();
    }

    private void SelectPage(int page)
    {
        var changed = page != _page; _page = page;
        for (var i = 0; i < _navigation.Count; i++) PageStyles.SetSelected(_navigation[i], i == page);
        _gamePage.IsVisible = page == 0; _resourcesPage.IsVisible = page is >= 1 and <= 4; _tasksPage.IsVisible = page == 5;
        _title.Text = page switch { 0 => "安装 Minecraft", 1 => "发现模组", 2 => "发现整合包", 3 => "发现资源包", 4 => "发现光影", _ => "下载任务" };
        _subtitle.Text = page switch
        {
            0 => "选择游戏版本和加载器，创建独立实例；安装完成后自动加入实例管理。",
            2 => "选择整合包，在新目录中安装游戏、加载器和资源。",
            5 => "查看每一步的进度。暂停和继续复用已下载文件，失败后可以重试。",
            _ => "按来源、Minecraft 版本与加载器筛选资源，可安装到实例或单独下载。"
        };
        _scroll.Offset = default;
        if (changed)
        {
            _resourceRequest?.Cancel(); _versionRequest?.Cancel();
            if (page is >= 1 and <= 4)
            {
                _targetInstanceRow.IsVisible = page != 2;
                UiOperation.Start(() => SearchResourcesAsync(0), () => _closed);
            }
        }
        if (page == 5) RefreshTasks();
    }

    private void OnDownloadsChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _refreshPending, 1) != 0) return;
        Dispatcher.UIThread.Post(() => { Interlocked.Exchange(ref _refreshPending, 0); if (!_closed) RefreshDownloads(); });
    }
    private void OnInstancesChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (_closed) return;
        RefreshInstances();
        RefreshGameInstallDirectory();
    });
    private void RefreshDownloads()
    {
        _source.Text = "下载源：" + SourceLabel(_downloads.Options.Source) + $" · {_downloads.Options.ConcurrentFiles} 文件并发 · {_downloads.Options.ConnectionsPerFile} 连接/文件";
        if (_page == 5) RefreshTasks();
        if (_downloads.StorageError is { } error) ShowStatus(error, true);
    }
    internal static string SourceLabel(DownloadSource source) => source switch { DownloadSource.Official => "官方", DownloadSource.BmclApi => "BMCLAPI", _ => "自动（镜像优先）" };
    private void ShowStatus(string message, bool error = false) { if (_closed) return; _status.Text = message; _status.Foreground = error ? Palette.Error : Palette.Muted; }
    private void Queued(string title) { ShowStatus(title + " 已加入下载队列。关闭窗口不影响下载。"); SelectPage(5); }
    private static TextBlock Heading(string text, double size = 17) => new() { Text = text, FontSize = size, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private static Button ActionButton(string title, Action action)
    {
        var button = new Button { Content = title, MinHeight = 34 }; AutomationProperties.SetName(button, title);
        button.Click += (_, _) => action(); return button;
    }
    private static TextBox Input(string name, string placeholder = "", string? value = null)
    {
        var box = new TextBox { PlaceholderText = placeholder, Text = value, MinHeight = 36 }; AutomationProperties.SetName(box, name); return box;
    }
    private static ComboBox Choice(string name, string[]? values = null)
    {
        var choice = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 36, ItemsSource = values };
        AutomationProperties.SetName(choice, name); return choice;
    }
    private static StackPanel Field(string label, Control control)
    {
        var field = new StackPanel { Spacing = 6 }; field.Children.Add(PageStyles.Hint(label)); field.Children.Add(control); return field;
    }
    private static StackPanel Stack(params Control[] children)
    {
        var stack = new StackPanel { Spacing = 12 }; foreach (var child in children) stack.Children.Add(child); return stack;
    }
    private Control DirectoryField(string label, TextBox field)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,Auto") }; row.Children.Add(field);
        var choose = ActionButton("选择" + label, () => UiOperation.Start(() => PickDirectoryAsync(field), () => _closed)); Grid.SetColumn(choose, 2); row.Children.Add(choose);
        return Field(label, row);
    }
    private async Task PickDirectoryAsync(TextBox input)
    {
        try
        {
            var folder = (await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择实例保存位置", AllowMultiple = false })).FirstOrDefault()?.TryGetLocalPath();
            if (!_closed && folder is not null) input.Text = folder;
        }
        catch (Exception ex) { ShowStatus("无法选择目录：" + ex.Message, true); }
    }
    internal static string NewInstancePath(string? parent, string? name)
    {
        name = name?.Trim(); parent = parent?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Length > 100 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\') || name.EndsWith('.') || name.EndsWith(' '))
            throw new ArgumentException("请输入有效的实例名称，不包含路径或特殊文件名字符。");
        if (string.IsNullOrWhiteSpace(parent) || !Path.IsPathFullyQualified(parent)) throw new ArgumentException("请选择完整的实例保存目录。");
        var path = Path.Combine(Path.GetFullPath(parent), name);
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("同名目录已存在，请换一个实例名称；现有内容不会被覆盖。");
        return path;
    }

    private string DefaultGameInstallDirectory() => _instances.SelectedGameDirectory ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "Wonder");

    private static string NewGameInstallPath(string? parent, string? name)
    {
        if (string.IsNullOrWhiteSpace(parent) || !Path.IsPathFullyQualified(parent.Trim())) throw new ArgumentException("请选择完整的游戏目录。");
        return NewInstancePath(Path.Combine(parent.Trim(), "instances"), name);
    }
}
