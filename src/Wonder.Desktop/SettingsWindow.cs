// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Wonder.Application;
using Wonder.Workspace;

namespace Wonder.Desktop;

internal sealed class SettingsWindow : Window
{
    private LauncherSettings _settings;
    private bool _updating;
    private readonly Action<LauncherSettings> _change;
    private readonly StackPanel _editor;
    private readonly ComboBox _theme;
    private readonly ComboBox _accent;
    private readonly ToggleSwitch _showAreaNumbers;
    private readonly Slider _padding, _hold, _extent, _closeDelay;
    private readonly TextBlock _status, _error, _resetDescription;
    private readonly Border _errorBanner, _confirmation;
    private readonly List<Button> _categoryButtons = [];
    private readonly StackPanel _launcherPage, _personalizationPage;
    private readonly Control _gamePage;
    private readonly Border _footer;
    private readonly Control _gameFooter;
    private readonly ScrollViewer _scroll;
    private readonly Expander _storage;
    private readonly TextBlock _pageTitle, _pageDescription;
    private string? _storageError;

    public SettingsWindow(LauncherSettings settings, string path, Action<LauncherSettings> change, Action reset, Action retry, InstanceService? instances = null)
    {
        _settings = settings;
        _change = change;
        Title = "设置 · Wonder Launcher";
        Width = 980;
        Height = 760;
        MinWidth = 800;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Palette.Background;
        Foreground = Palette.Text;
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var heading = new StackPanel { Margin = new Thickness(26, 24, 26, 20), Spacing = 6 };
        heading.Children.Add(new TextBlock { Text = "设置", FontSize = 28, FontWeight = FontWeight.SemiBold });
        heading.Children.Add(Hint("按类别调整启动器、游戏和工作区外观。"));
        layout.Children.Add(heading);

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*"), Margin = new Thickness(26, 0, 26, 0) };
        var navigation = new StackPanel { Spacing = 12, Margin = new Thickness(0, 0, 20, 20) };
        var titles = new[] { "启动器设置", "游戏全局设置", "个性化设置" };
        var descriptions = new[] { "拖动与侧边栏交互", "Java、内存与启动参数", "主题、强调色与工作区" };
        for (var index = 0; index < titles.Length; index++)
        {
            var category = index;
            var button = PageStyles.CardButton(titles[index], titles[index], descriptions[index], null, () => SelectCategory(category));
            _categoryButtons.Add(button); navigation.Children.Add(button);
        }
        columns.Children.Add(navigation);
        var body = new StackPanel { Margin = new Thickness(0, 0, 0, 24), Spacing = 18 };
        _pageTitle = new TextBlock { FontSize = 23, FontWeight = FontWeight.SemiBold };
        _pageDescription = Hint("");
        body.Children.Add(_pageTitle); body.Children.Add(_pageDescription);
        _error = Hint("");
        _error.Foreground = Palette.Error;
        _error.Name = "SettingsError";
        var errorStack = new StackPanel { Spacing = 12 };
        errorStack.Children.Add(_error);
        var retryButton = new Button { Content = "重试", HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(retryButton, "重试设置存储");
        retryButton.Click += (_, _) => retry();
        errorStack.Children.Add(retryButton);
        _errorBanner = Card(errorStack);
        _errorBanner.BorderBrush = Palette.Error;
        _errorBanner.IsVisible = false;
        body.Children.Add(_errorBanner);
        _editor = new StackPanel { Spacing = 18 };
        body.Children.Add(_editor);
        _launcherPage = new StackPanel { Spacing = 18 };
        _personalizationPage = new StackPanel { Spacing = 18 };
        _editor.Children.Add(_launcherPage); _editor.Children.Add(_personalizationPage);
        if (instances is not null)
        {
            var gameEditor = new GlobalGameSettingsEditor(instances);
            _gamePage = gameEditor;
            _gameFooter = gameEditor.Footer;
            Closed += (_, _) => gameEditor.Dispose();
        }
        else { _gamePage = Card(Hint("游戏实例服务尚未就绪。")); _gameFooter = new Border(); }
        body.Children.Add(_gamePage);

        var appearance = Section("外观", "整个启动器共用一套外观，包括已打开的组件窗口。");
        _theme = Choice("主题", ["跟随系统", "深色", "浅色"]);
        _theme.SelectionChanged += (_, _) => Change(_settings with { Theme = (LauncherTheme)_theme.SelectedIndex });
        appearance.Children.Add(ChoiceRow("主题", _theme));
        _accent = Choice("强调色", ["薄荷绿", "晴空蓝", "琥珀金", "蔷薇粉"]);
        _accent.SelectionChanged += (_, _) => Change(_settings with { Accent = (AccentColor)_accent.SelectedIndex });
        appearance.Children.Add(ChoiceRow("强调色", _accent));
        _personalizationPage.Children.Add(Card(appearance));

        var workspace = Section("工作区", "保留自由布置，让拖动更顺手。");
        _showAreaNumbers = new ToggleSwitch { OnContent = "开启", OffContent = "关闭" };
        AutomationProperties.SetName(_showAreaNumbers, "显示功能区序号");
        _showAreaNumbers.IsCheckedChanged += (_, _) => Change(_settings with { ShowAreaNumbers = _showAreaNumbers.IsChecked == true });
        workspace.Children.Add(ChoiceRow("显示功能区序号", _showAreaNumbers));
        _padding = Range(workspace, "工作区留白", "功能区与窗口边缘之间的距离", 0, 40, 2, "DIP", value => _settings with { WorkspacePadding = value });
        _personalizationPage.Children.Add(Card(workspace));
        var interaction = Section("工作区交互", "调整组件和功能区的移动手感。");
        _hold = Range(interaction, "长按拖动时间", "短按打开组件，长按后移动组件或功能区", 250, 1000, 50, "ms", value => _settings with { HoldDelayMilliseconds = value });
        _launcherPage.Children.Add(Card(interaction));

        var sidebar = Section("侧边栏", "悬停展开，离开后自动收起。");
        _extent = Range(sidebar, "侧边栏展开尺寸", "左右侧栏为宽度，上下侧栏为高度；空间不足时自动缩小", 200, 600, 20, "DIP", value => _settings with { SidebarExtent = value });
        _closeDelay = Range(sidebar, "侧边栏收起延迟", "移出侧栏后等待多久收起，拖动期间保持展开", 100, 1000, 20, "ms", value => _settings with { SidebarCloseDelayMilliseconds = value });
        _launcherPage.Children.Add(Card(sidebar));

        var storage = new StackPanel { Spacing = 8 };
        storage.Children.Add(Hint("设置仅保存在此设备。恢复默认设置不会清空功能区和快捷方式。"));
        storage.Children.Add(new SelectableTextBlock { Text = path, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Palette.Muted });
        _storage = new Expander { Header = "本地设置文件", Content = storage, HorizontalAlignment = HorizontalAlignment.Stretch };
        body.Children.Add(_storage);
        _scroll = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(_scroll, 1); columns.Children.Add(_scroll);
        Grid.SetRow(columns, 1);
        layout.Children.Add(columns);

        var footer = new StackPanel { Spacing = 12, Margin = new Thickness(26, 14, 26, 18) };
        var confirmation = new StackPanel { Spacing = 10 };
        _resetDescription = Hint("");
        confirmation.Children.Add(_resetDescription);
        var confirmActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var confirm = new Button { Content = "确认恢复" };
        AutomationProperties.SetName(confirm, "确认恢复默认设置");
        var cancel = new Button { Content = "取消" };
        confirmActions.Children.Add(confirm);
        confirmActions.Children.Add(cancel);
        confirmation.Children.Add(confirmActions);
        _confirmation = Card(confirmation);
        _confirmation.IsVisible = false;
        confirm.Click += (_, _) => { _confirmation.IsVisible = false; reset(); };
        cancel.Click += (_, _) => _confirmation.IsVisible = false;
        footer.Children.Add(_confirmation);
        var footerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _status = new TextBlock { Name = "SettingsStatus", FontSize = 12, Foreground = Palette.Muted, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 16, 0) };
        footerRow.Children.Add(_status);
        var restore = new Button { Content = "恢复默认设置" };
        AutomationProperties.SetName(restore, "恢复默认设置");
        restore.Click += (_, _) => _confirmation.IsVisible = !_confirmation.IsVisible;
        Grid.SetColumn(restore, 1);
        footerRow.Children.Add(restore);
        footer.Children.Add(footerRow);
        _footer = new Border { Background = Palette.Header, BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = footer };
        Grid.SetRow(_footer, 2);
        layout.Children.Add(_footer);
        Grid.SetRow(_gameFooter, 2); layout.Children.Add(_gameFooter);
        Content = layout;
        SetValues(settings);
        UpdateStatus(null, false);
        SelectCategory(0);
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            if (_confirmation.IsVisible) _confirmation.IsVisible = false;
            else Close();
            e.Handled = true;
        };
    }

    public void SetValues(LauncherSettings settings)
    {
        _updating = true;
        try
        {
            _settings = settings;
            _theme.SelectedIndex = (int)settings.Theme;
            _accent.SelectedIndex = (int)settings.Accent;
            _showAreaNumbers.IsChecked = settings.ShowAreaNumbers;
            _padding.Value = settings.WorkspacePadding;
            _hold.Value = settings.HoldDelayMilliseconds;
            _extent.Value = settings.SidebarExtent;
            _closeDelay.Value = settings.SidebarCloseDelayMilliseconds;
        }
        finally { _updating = false; }
    }

    public void UpdateStatus(string? error, bool readBlocked)
    {
        _storageError = error;
        _errorBanner.IsVisible = error != null && !_gamePage.IsVisible;
        _error.Text = error;
        _editor.IsEnabled = !readBlocked;
        _status.Text = error == null ? "已应用 · 更改自动保存在此设备" : readBlocked ? "读取失败 · 原文件已保留" : "已应用到当前会话 · 尚未保存";
        _status.Foreground = error == null ? Palette.Muted : Palette.Error;
        _resetDescription.Text = readBlocked
            ? "恢复默认设置将覆盖无法读取的设置文件。功能区和快捷方式保持原样。"
            : "将外观和交互参数恢复为默认值。功能区和快捷方式保持原样。";
    }

    private void SelectCategory(int index)
    {
        _launcherPage.IsVisible = index == 0;
        _gamePage.IsVisible = index == 1;
        _personalizationPage.IsVisible = index == 2;
        _editor.IsVisible = index != 1;
        _footer.IsVisible = index != 1;
        _gameFooter.IsVisible = index == 1;
        _storage.IsVisible = index != 1;
        _errorBanner.IsVisible = index != 1 && _storageError is not null;
        _confirmation.IsVisible = false;
        _pageTitle.Text = index switch { 0 => "启动器设置", 1 => "游戏全局设置", _ => "个性化设置" };
        _pageDescription.Text = index == 1 ? "为所有实例设置共同的启动偏好，保存后在下次启动时生效。" : "更改立即生效，并自动保存在此设备。";
        for (var i = 0; i < _categoryButtons.Count; i++) PageStyles.SetSelected(_categoryButtons[i], i == index);
        _scroll.Offset = default;
    }

    private void Change(LauncherSettings settings)
    {
        if (_updating || settings == _settings) return;
        settings.Validate();
        _settings = settings;
        _change(settings);
    }

    private Slider Range(StackPanel section, string title, string description, int minimum, int maximum, int step, string unit, Func<int, LauncherSettings> update)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 8, 0, 0) };
        row.Children.Add(new TextBlock { Text = title, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        var valueLabel = new TextBlock { Foreground = Palette.Accent, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(valueLabel, 1);
        row.Children.Add(valueLabel);
        section.Children.Add(row);
        section.Children.Add(Hint(description));
        var slider = new Slider { Minimum = minimum, Maximum = maximum, TickFrequency = step, IsSnapToTickEnabled = true, SmallChange = step, LargeChange = step * 2, MinHeight = 30 };
        AutomationProperties.SetName(slider, title);
        slider.ValueChanged += (_, _) =>
        {
            var value = (int)Math.Round(slider.Value);
            valueLabel.Text = $"{value} {unit}";
            Change(update(value));
        };
        valueLabel.Text = $"{minimum} {unit}";
        section.Children.Add(slider);
        return slider;
    }

    private static ComboBox Choice(string title, string[] choices)
    {
        var control = new ComboBox { ItemsSource = choices, MinWidth = 180, HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetName(control, title);
        return control;
    }

    private static Grid ChoiceRow(string title, Control choice)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(new TextBlock { Text = title, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(choice, 1);
        row.Children.Add(choice);
        return row;
    }

    private static StackPanel Section(string title, string subtitle)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold });
        stack.Children.Add(Hint(subtitle));
        return stack;
    }

    private static TextBlock Hint(string text) => new() { Text = text, FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
    private static Border Card(Control content) => PageStyles.Card(content);
}
