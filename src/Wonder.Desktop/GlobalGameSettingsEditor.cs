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

namespace Wonder.Desktop;

internal sealed class GlobalGameSettingsEditor : UserControl, IDisposable
{
    private readonly InstanceService _service;
    private readonly TextBox _username = new() { MaxLength = 16 };
    private readonly TextBlock _usernameStatus = PageStyles.Hint("");
    private readonly Button _saveUsername = new() { Content = "保存离线用户名" };
    private string _loadedUsername = "";
    private bool _usernameDirty;
    private readonly Dictionary<string, TextBox> _fields = [];
    private readonly StackPanel _form = new() { Spacing = 16 };
    private readonly ComboBox _fullscreen = new() { ItemsSource = new[] { "继承实例原配置", "窗口模式", "全屏模式" }, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _clearJvm = new() { Content = "清空额外 JVM 参数" };
    private readonly CheckBox _clearGame = new() { Content = "清空额外游戏参数" };
    private readonly TextBlock _status = PageStyles.Hint("");
    private readonly Button _retry = new() { Content = "重试读取实例设置", HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
    public Control Footer { get; }
    private bool _updating = true, _dirty, _disposed;
    private InstanceSettings _loaded = new();

    public GlobalGameSettingsEditor(InstanceService service)
    {
        _service = service;
        var body = new StackPanel { Spacing = 16 };
        body.Children.Add(PageStyles.Card(PageStyles.Hint("留空表示继承导入的原配置或内置默认值。全局设置覆盖原配置，实例的独立设置优先于全局设置。游戏目录由各实例单独管理。")));
        body.Children.Add(_form);
        var identity = Section("离线身份", "离线用户名用于启动游戏，不提供正版服务器认证。与下方游戏参数分别保存。");
        AutomationProperties.SetName(_username, "离线用户名");
        AutomationProperties.SetName(_saveUsername, "保存离线用户名");
        AutomationProperties.SetName(_usernameStatus, "离线用户名保存状态");
        _username.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty && !_updating) { _usernameDirty = true; _usernameStatus.Text = "用户名尚未保存"; } };
        _saveUsername.Click += (_, _) =>
        {
            _service.SetOfflineUsername(_username.Text ?? "");
            _usernameStatus.Foreground = _service.Error is null ? Palette.Muted : Palette.Error;
            if (_service.Error is { } error) _usernameStatus.Text = error;
            else { _loadedUsername = _service.Catalog.OfflineUsername; _usernameDirty = false; _usernameStatus.Text = "离线用户名已保存"; }
        };
        identity.Children.Add(_username); identity.Children.Add(_saveUsername); identity.Children.Add(_usernameStatus);
        _form.Children.Add(PageStyles.Card(identity));
        var java = Section("Java 与内存", "Java 留空时沿用实例配置；未配置时自动查找。内存单位为 MiB。");
        java.Children.Add(Field("java", "全局 Java 路径", "继承实例原配置 / 自动查找"));
        java.Children.Add(Pair(Field("min", "全局最小内存", "继承 / 默认 512"), Field("max", "全局最大内存", "继承 / 默认 4096")));
        _form.Children.Add(PageStyles.Card(java));
        var window = Section("游戏窗口", "分辨率与全屏模式应用于下次启动。");
        window.Children.Add(Pair(Field("width", "全局窗口宽度", "继承 / 默认 854"), Field("height", "全局窗口高度", "继承 / 默认 480")));
        AutomationProperties.SetName(_fullscreen, "全局窗口模式");
        _fullscreen.SelectionChanged += (_, _) => MarkDirty();
        window.Children.Add(_fullscreen);
        _form.Children.Add(PageStyles.Card(window));
        var arguments = Section("额外启动参数", "留空继续继承；勾选清空可明确移除继承的额外参数。");
        arguments.Children.Add(Field("jvm", "全局额外 JVM 参数", "例如 -XX:+UseG1GC"));
        AutomationProperties.SetName(_clearJvm, "清空全局额外 JVM 参数");
        _clearJvm.IsCheckedChanged += (_, _) => { _fields["jvm"].IsEnabled = _clearJvm.IsChecked != true; MarkDirty(); };
        arguments.Children.Add(_clearJvm);
        arguments.Children.Add(Field("game", "全局额外游戏参数", "例如 --server localhost"));
        AutomationProperties.SetName(_clearGame, "清空全局额外游戏参数");
        _clearGame.IsCheckedChanged += (_, _) => { _fields["game"].IsEnabled = _clearGame.IsChecked != true; MarkDirty(); };
        arguments.Children.Add(_clearGame);
        _form.Children.Add(PageStyles.Card(arguments));
        var save = new Button { Content = "保存全局设置" };
        AutomationProperties.SetName(save, "保存全局设置");
        save.Click += (_, _) => Save();
        var reset = new Button { Content = "恢复继承设置" };
        AutomationProperties.SetName(reset, "恢复全局继承设置");
        reset.Click += (_, _) =>
        {
            _service.ResetGlobalSettings();
            if (_service.Error is { } error) Report(error, true);
            else { LoadValues(); Report("已恢复继承 · 实例的独立设置保持不变", false); }
        };
        _actions.Children.Add(save); _actions.Children.Add(reset);
        _status.Name = "GlobalSettingsStatus";
        AutomationProperties.SetName(_retry, "重试全局设置读取");
        _retry.Click += (_, _) => _service.Reload();
        var footer = new StackPanel { Spacing = 10, Margin = new Thickness(26, 14, 26, 18) };
        footer.Children.Add(_status); footer.Children.Add(_retry); footer.Children.Add(_actions);
        Footer = new Border { Background = Palette.Header, BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = footer };
        Content = body;
        LoadValues();
        service.Changed += ServiceChanged;
        RefreshAvailability();
    }

    private void Save()
    {
        try
        {
            var settings = new InstanceSettings
            {
                JavaPath = Optional("java"), MinimumMemoryMb = Number("min"), MaximumMemoryMb = Number("max"),
                WindowWidth = Number("width"), WindowHeight = Number("height"),
                FullScreen = _fullscreen.SelectedIndex == 0 ? null : _fullscreen.SelectedIndex == 2,
                JvmArguments = _clearJvm.IsChecked == true ? "" : Optional("jvm"),
                GameArguments = _clearGame.IsChecked == true ? "" : Optional("game")
            };
            settings.Validate();
            _service.UpdateGlobalSettings(settings);
            if (_service.Error is { } error) Report(error, true);
            else { LoadValues(); Report("已保存 · 下次启动游戏时生效", false); }
        }
        catch (ArgumentException ex) { Report(ex.Message, true); }
    }

    private void LoadValues()
    {
        _updating = true;
        _loaded = _service.Catalog.GlobalSettings;
        if (!_usernameDirty) { _loadedUsername = _service.Catalog.OfflineUsername; _username.Text = _loadedUsername; }
        _fields["java"].Text = _loaded.JavaPath ?? "";
        _fields["min"].Text = _loaded.MinimumMemoryMb?.ToString() ?? "";
        _fields["max"].Text = _loaded.MaximumMemoryMb?.ToString() ?? "";
        _fields["width"].Text = _loaded.WindowWidth?.ToString() ?? "";
        _fields["height"].Text = _loaded.WindowHeight?.ToString() ?? "";
        _fields["jvm"].Text = _loaded.JvmArguments ?? "";
        _fields["game"].Text = _loaded.GameArguments ?? "";
        _fullscreen.SelectedIndex = _loaded.FullScreen is null ? 0 : _loaded.FullScreen == true ? 2 : 1;
        _clearJvm.IsChecked = _loaded.JvmArguments == "";
        _clearGame.IsChecked = _loaded.GameArguments == "";
        _dirty = false; _updating = false;
        Report("已载入 · 编辑后点击保存全局设置", false);
    }

    private void ServiceChanged(object? sender, EventArgs args)
    {
        if (Dispatcher.UIThread.CheckAccess()) RefreshAvailability();
        else Dispatcher.UIThread.Post(RefreshAvailability);
    }

    private void RefreshAvailability()
    {
        if (_disposed) return;
        var wasBlocked = !_form.IsEnabled;
        _form.IsEnabled = !_service.IsReadBlocked;
        _actions.IsEnabled = !_service.IsReadBlocked;
        _saveUsername.IsEnabled = !_service.IsReadBlocked;
        if (!_usernameDirty && _loadedUsername != _service.Catalog.OfflineUsername)
        {
            var updating = _updating; _updating = true;
            _loadedUsername = _service.Catalog.OfflineUsername; _username.Text = _loadedUsername; _updating = updating;
        }
        _retry.IsVisible = _service.IsReadBlocked;
        if (_service.IsReadBlocked) Report(_service.Error ?? "实例设置读取失败。原文件已保留。", true);
        else if (!_dirty && (wasBlocked || _loaded != _service.Catalog.GlobalSettings)) LoadValues();
    }

    private void MarkDirty() { if (!_updating) { _dirty = true; Report("尚未保存 · 点击保存后应用于下次启动", false); } }
    private void Report(string message, bool error) { _status.Text = message; _status.Foreground = error ? Palette.Error : Palette.Muted; }
    private string? Optional(string key) => string.IsNullOrWhiteSpace(_fields[key].Text) ? null : _fields[key].Text!.Trim();
    private int? Number(string key) => Optional(key) is not { } value ? null : int.TryParse(value, out var number) ? number : throw new ArgumentException("内存和窗口尺寸必须填写整数，或留空继承。");
    private Control Field(string key, string label, string placeholder)
    {
        var box = new TextBox { PlaceholderText = placeholder, MaxLength = 32768, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(box, label);
        box.PropertyChanged += (_, args) => { if (args.Property == TextBox.TextProperty) MarkDirty(); };
        _fields.Add(key, box);
        var field = new StackPanel { Spacing = 6 };
        field.Children.Add(new TextBlock { Text = label.Replace("全局", ""), FontSize = 13 }); field.Children.Add(box);
        return field;
    }
    private static Grid Pair(Control left, Control right)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,14,*") };
        grid.Children.Add(left); Grid.SetColumn(right, 2); grid.Children.Add(right); return grid;
    }
    private static StackPanel Section(string title, string subtitle)
    {
        var section = new StackPanel { Spacing = 12 };
        section.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold }); section.Children.Add(PageStyles.Hint(subtitle)); return section;
    }
    public void Dispose() { _disposed = true; _service.Changed -= ServiceChanged; }
}
