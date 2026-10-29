// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Wonder.Workspace;
using Wonder.Application;
using Wonder.Core;
using Wonder.Infrastructure;
using Wonder.Desktop.Components;

namespace Wonder.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly WorkspaceView _workspace;
    private readonly WorkspaceStore _store;
    private readonly string _workspacePath;
    private readonly Button _saveStatus;
    private ComponentLibraryWindow? _library;
    private string? _error;
    private bool _loadFailed;
    private readonly InstanceService _instances;
    private readonly AccountService _accounts;
    private AccountManagerWindow? _accountManager;
    private InstanceManagerWindow? _instanceManager;

    public MainWindow(string? workspacePath = null, string? settingsPath = null) : this(workspacePath, settingsPath, null) { }

    internal MainWindow(string? workspacePath, string? settingsPath, Action<WorkspaceDocument>? saveWorkspace)
    {
        Title = "奇想我的世界启动器 · Wonder Minecraft Launcher";
        Width = 1180;
        Height = 780;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowDecorations = WindowDecorations.BorderOnly;
        Background = Palette.Background;
        Foreground = Palette.Text;
        FontFamily = new FontFamily(OperatingSystem.IsLinux()
            ? "Noto Sans CJK SC,Noto Sans CJK TC,WenQuanYi Micro Hei,DejaVu Sans,sans-serif"
            : "Microsoft YaHei UI,Microsoft YaHei,Segoe UI,sans-serif");
        _workspacePath = workspacePath ?? LauncherDataPaths.WorkspacePath;
        _store = new WorkspaceStore(_workspacePath);
        _settingsPath = settingsPath ?? (workspacePath is null ? LauncherDataPaths.SettingsPath : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_workspacePath))!, "settings.json"));
        _settingsStore = new LauncherSettingsStore(_settingsPath);
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_workspacePath))!);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_settingsPath))!);
            if (workspacePath is null) System.IO.Directory.CreateDirectory(LauncherDataPaths.PluginsDirectory);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
        {
            _error = $"数据目录创建失败。请检查启动器所在目录和写入权限后重启。\n{ex.Message}";
            _loadFailed = true;
        }
        WorkspaceDocument document;
        try { document = _store.Load(); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException or ArgumentException)
        {
            document = WorkspaceDocument.CreateEmpty();
            _error = $"布局读取失败，原文件保留。\n{ex.Message}";
            _loadFailed = true;
        }
        document.EnsureLibrary();
        var dataDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_workspacePath))!;
        var accountStore = AccountStoreFactory.Create(dataDirectory);
        _accounts = new AccountService(accountStore, new MicrosoftAuthenticator(), new YggdrasilAuthenticator(),
            new AuthenticationServerDirectory(), new AuthenticationAdapterProvider(System.IO.Path.Combine(dataDirectory, "cache", "authentication")));
        _instances = new InstanceService(new InstanceStore(System.IO.Path.Combine(dataDirectory, "instances.json")), new InstanceDiscovery(), new LocalGameLauncher(launcherVersion: LauncherBuildInfo.Version), System.IO.Path.Combine(dataDirectory, "cache"), new InstanceFileOperations(), _accounts);
        var registry = BuiltInComponents.CreateRegistry(() => _workspace!.OpenSettings(), dataDirectory);
        GameComponents.Register(registry, _instances, OpenInstanceManager, _accounts);
        AccountComponents.Register(registry, _accounts, OpenAccountManager, _instances);
        _workspace = new WorkspaceView(document, dataDirectory, registry) { Margin = new Thickness(22, 20, 22, 22) };
        InitializeWorkspaceStorage(saveWorkspace ?? _store.Save);
        _workspace.Changed += WorkspaceChanged;
        _workspace.HelpRequested += (_, _) => ShowHelp();
        _workspace.ComponentLibraryRequested += OpenComponentLibrary;
        _workspace.SettingsRequested += OpenSettings;
        Deactivated += (_, _) => _workspace.CancelGesture();
        Closing += CloseWorkspace;
        Closed += (_, _) => { DisposeWorkspaceStorage(); _accounts.Cancel(); _workspace.DisposeComponents(); };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty || e.Property == IsVisibleProperty)
                _workspace.SetRuntimeVisible(IsVisible && WindowState != WindowState.Minimized);
        };

        var layout = new Grid { RowDefinitions = new RowDefinitions("64,*") };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Background = Palette.Header };
        var brand = new Button { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(22, 10), HorizontalContentAlignment = HorizontalAlignment.Left };
        var brandContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        var logo = new Polygon { Points = new Points { new(0, 0), new(8, 5), new(16, 0), new(12, 22), new(4, 22) }, Fill = Palette.Accent, Width = 18, Height = 24, VerticalAlignment = VerticalAlignment.Center };
        var brandText = new StackPanel { Spacing = 1 };
        brandText.Children.Add(new TextBlock { Text = "W O N D E R  ⌄", FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text });
        brandText.Children.Add(new TextBlock { Text = "奇想我的世界启动器", FontSize = 10, Foreground = Palette.Muted });
        brandContent.Children.Add(logo);
        brandContent.Children.Add(brandText);
        brand.Content = brandContent;
        ToolTip.SetTip(brand, "工作区菜单 · 添加功能区与组件");
        brand.Click += (_, _) => { brand.ContextMenu = _workspace.CreateWorkspaceMenu(); brand.ContextMenu.Open(brand); };
        top.Children.Add(brand);
        var dragSpace = new Border { Background = Brushes.Transparent };
        Grid.SetColumn(dragSpace, 1);
        dragSpace.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) ToggleMaximize();
            else BeginMoveDrag(e);
        };
        top.Children.Add(dragSpace);
        _saveStatus = new Button { Foreground = Palette.Muted, Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 18, 0) };
        _saveStatus.Click += (_, _) => { if (_settingsError != null) OpenSettings(); else if (_error != null) ShowStorageError(); else ShowHelp(); };
        ToolTip.SetTip(_saveStatus, "点击查看操作说明或存储状态");
        Grid.SetColumn(_saveStatus, 2);
        top.Children.Add(_saveStatus);
        var windowButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        windowButtons.Children.Add(WindowButton("—", "最小化", () => WindowState = WindowState.Minimized));
        windowButtons.Children.Add(WindowButton("□", "最大化 / 还原", ToggleMaximize));
        windowButtons.Children.Add(WindowButton("×", "关闭", Close));
        Grid.SetColumn(windowButtons, 3);
        top.Children.Add(windowButtons);
        layout.Children.Add(new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = top });
        Grid.SetRow(_workspace, 1);
        layout.Children.Add(_workspace);
        Content = new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(1), Child = layout };
        InitializeSettings();
        UpdateStorageStatus();
    }

    private Button WindowButton(string glyph, string tip, Action action)
    {
        var button = new Button { Content = glyph, Width = 44, Height = 42, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Palette.Muted, FontSize = 18 };
        ToolTip.SetTip(button, tip);
        Avalonia.Automation.AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => action();
        return button;
    }

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OpenComponentLibrary()
    {
        _workspace.CancelGesture();
        if (_library != null)
        {
            if (_library.WindowState == WindowState.Minimized) _library.WindowState = WindowState.Normal;
            _library.Activate();
            return;
        }
        _library = new ComponentLibraryWindow(_workspace, _workspace.Document.Library.ToArray()) { FontFamily = FontFamily };
        _library.Closed += (_, _) => _library = null;
        _library.Show(this);
        // Leave part of the workspace exposed so the source and destination can
        // be used together immediately, including on a single monitor.
        var screen = Screens.ScreenFromWindow(this)?.WorkingArea;
        var position = this.PointToScreen(new Point(Math.Max(0, Bounds.Width - _library.Width), 86));
        if (screen is { } available)
            position = new PixelPoint(Math.Clamp(position.X, available.X, Math.Max(available.X, available.Right - (int)(_library.Width * RenderScaling))),
                Math.Clamp(position.Y, available.Y, Math.Max(available.Y, available.Bottom - (int)(_library.Height * RenderScaling))));
        _library.Position = position;
    }

    private void OpenInstanceManager()
    {
        _workspace.CancelGesture();
        if (_instanceManager is not null)
        {
            if (_instanceManager.WindowState == WindowState.Minimized) _instanceManager.WindowState = WindowState.Normal;
            _instanceManager.Activate(); return;
        }
        _instanceManager = new InstanceManagerWindow(_instances, initialLayout: _settings.InstanceLayout,
            layoutChanged: layout => ChangeSettings(_settings with { InstanceLayout = layout })) { FontFamily = FontFamily };
        _instanceManager.Closed += (_, _) => _instanceManager = null;
        _instanceManager.Show(this);
    }

    private void OpenAccountManager()
    {
        _workspace.CancelGesture();
        if (_accountManager is not null)
        {
            if (_accountManager.WindowState == WindowState.Minimized) _accountManager.WindowState = WindowState.Normal;
            _accountManager.Activate(); return;
        }
        _accountManager = new AccountManagerWindow(_accounts, _instances.Catalog.OfflineUsername, instances: _instances) { FontFamily = FontFamily };
        _accountManager.Closed += (_, _) => _accountManager = null;
        _accountManager.Show(this);
    }

    private void UpdateStorageStatus()
    {
        _saveStatus.Content = _settingsError != null ? "设置存储异常 · 点击查看" : _error != null ? "工作区未保存 · 点击查看" : "WONDER  /  " + LauncherBuildInfo.Version;
        _saveStatus.Foreground = _error == null && _settingsError == null ? Palette.Muted : Palette.Error;
    }

    private async void ShowHelp()
    {
        var text = "框架只保留顶栏，其余内容都可自由布置。\n\n" +
            $"功能区：仅长按顶边栏 {_settings.HoldDelayMilliseconds} 毫秒，拖到其他区后松开交换。\n" +
            "改名：右键功能区或侧边栏，选择“重命名功能区…”，Enter 保存、Esc 取消，名称随布局保存。\n" +
            "序号：默认隐藏，可在设置的“工作区”中开启“显示功能区序号”。\n" +
            "大小：整条分隔边均可选中，按住鼠标直接拖动，无需长按。\n" +
            "侧边栏：把框架边缘的功能区拖小即可收起，悬停按固定厚度展开并压缩邻区。\n" +
            "恢复：展开后拖动朝向工作区的边向内放大，松开恢复普通功能区。\n" +
            "组件：短按按钮操作，长按后可在本区或跨功能区移动。\n" +
            "定位：组件中心相对功能区中心的偏移固定，缩放不改变位置；右键可整理到可见范围。\n" +
            "取消：按 Esc 或切换窗口；移动组件/换区时在工作区外松开也会取消。\n\n" +
            "提示：鼠标按住或跨窗口拖放时，悬浮提示暂停显示；操作提示继续保留。\n\n" +
            "组件库：独立窗口，可直接操作应用；拖到功能区创建快捷方式，所有入口共享状态，移除快捷方式不会删除应用。\n" +
            "移除：把功能区组件长按拖回组件库页面并松开，或使用组件右键菜单。\n" +
            "Tab 切换按钮，Enter / 空格操作，F1 打开说明，Shift+F10 打开菜单。\n\n" +
            "设置：通过桌面快捷方式、组件库或工作区菜单打开；外观和交互修改即时生效、自动保存。\n\n" +
            "布局文件：\n" + _workspacePath + "\n\n设置文件：\n" + _settingsPath;
        await ShowMessage("工作区操作", text);
    }

    private async void ShowStorageError()
    {
        await ShowMessage("布局存储", (_error ?? "布局已保存。") + "\n\n" + _workspacePath + (_loadFailed ? "\n\n当前会话允许体验，但不会写回原文件。请备份并移走该文件后重启，重新创建布局。" : "\n\n检查目录写入权限后点击重试保存，或继续编辑后自动重试。"), _loadFailed ? null : RetryWorkspaceSave);
    }

    private async Task ShowMessage(string title, string message, Action? retry = null)
    {
        var dialog = new Window { Title = title, Width = 570, Height = 470, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Palette.Background, Foreground = Palette.Text, FontFamily = FontFamily };
        var stack = new StackPanel { Margin = new Thickness(28), Spacing = 18 };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 21, FontWeight = FontWeight.SemiBold });
        stack.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 23, Foreground = Palette.Muted });
        if (retry is not null)
        {
            var retryButton = new Button { Content = "重试保存", HorizontalAlignment = HorizontalAlignment.Right };
            retryButton.Click += (_, _) => { retry(); dialog.Close(); };
            stack.Children.Add(retryButton);
        }
        var close = new Button { Content = "知道了", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => dialog.Close();
        stack.Children.Add(close);
        dialog.Content = new ScrollViewer { Content = stack };
        await dialog.ShowDialog(this);
    }
}
