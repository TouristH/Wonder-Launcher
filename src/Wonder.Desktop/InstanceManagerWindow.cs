// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Wonder.Application;
using Wonder.Core;
using Wonder.Workspace;

namespace Wonder.Desktop;

internal sealed class InstanceManagerWindow : Window
{
    private readonly InstanceService _service;
    private readonly Func<Task<string?>>? _pickDirectory;
    private readonly StackPanel _roots = new() { Spacing = 12 };
    private readonly WrapPanel _list = new();
    private readonly ScrollViewer _instanceScroll;
    private readonly ToggleButton _listView = new() { Content = "列表", MinHeight = 34 }, _gridView = new() { Content = "网格", MinHeight = 34 };
    private readonly Action<InstanceLayout>? _layoutChanged;
    private InstanceLayout _layout;
    private RootRow[] _rootRows = [];
    private InstanceRow[] _instanceRows = [];
    private readonly Grid _browser = new(), _detailPage = new();
    private readonly TextBlock _rootHeading = Text(), _empty = Text(), _detailTitle = Text(), _detailVersion = Text();
    private readonly Dictionary<string, TextBox> _fields = new();
    private readonly Dictionary<string, Draft> _drafts = new();
    private readonly ComboBox _fullscreen = new() { ItemsSource = new[] { "继承来源", "窗口", "全屏" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _clearJvm = new() { Content = "清空来源的额外 JVM 参数" }, _clearGame = new() { Content = "清空来源的额外游戏参数" };
    private readonly TextBlock _details = Text(), _effective = Text(), _status = Text(), _error = Text(), _draftStatus = Text();
    private readonly StackPanel _editor = new() { Spacing = 13 };
    private readonly Button _import, _refreshRoot, _refresh, _retry;
    private bool _updating, _closed, _showDetails;
    private int _pendingRefresh;
    private string? _currentId, _rootPath, _localError;

    public InstanceManagerWindow(InstanceService service, Func<Task<string?>>? pickDirectory = null, InstanceLayout initialLayout = InstanceLayout.Grid, Action<InstanceLayout>? layoutChanged = null)
    {
        _service = service; _pickDirectory = pickDirectory; _layoutChanged = layoutChanged;
        Title = "实例管理 · Wonder Launcher"; Width = 1050; Height = 820; MinWidth = 800; MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Palette.Background; Foreground = Palette.Text;
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(24) };
        var heading = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 20) };
        heading.Children.Add(new TextBlock { Text = "实例管理", FontSize = 28, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text });
        heading.Children.Add(Text("点击实例选择启动目标；通过卡片右侧按钮打开设置或删除。")); layout.Children.Add(heading);
        var body = new Grid();
        _browser.ColumnDefinitions = new ColumnDefinitions("280,22,*");
        var sidebar = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var rootHeader = new StackPanel { Spacing = 12 };
        rootHeader.Children.Add(new TextBlock { Text = "游戏目录", FontSize = 17, Foreground = Palette.Text });
        _import = Button("选择实例目录", "添加游戏目录…", async () => await Import()); rootHeader.Children.Add(_import); sidebar.Children.Add(rootHeader);
        AutomationProperties.SetName(_roots, "游戏根目录列表");
        var rootScroll = new ScrollViewer { Content = _roots, Margin = new Thickness(0, 12, 0, 12), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(rootScroll, 1); sidebar.Children.Add(rootScroll);
        var rootActions = new StackPanel { Spacing = 10 };
        _refreshRoot = Button("刷新游戏目录", "重新扫描此目录", async () =>
        {
            if (_rootPath is not { } path) return;
            _localError = null; await _service.ImportAsync(path); _localError = _service.Error; Refresh();
        });
        rootActions.Children.Add(_refreshRoot);
        rootActions.Children.Add(Text("可添加 .minecraft、versions 或单个实例目录，也支持 Prism / MultiMC。"));
        Grid.SetRow(rootActions, 2); sidebar.Children.Add(rootActions); _browser.Children.Add(sidebar);
        var children = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        var instanceHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _rootHeading.FontSize = 17; _rootHeading.Foreground = Palette.Text; _rootHeading.VerticalAlignment = VerticalAlignment.Center;
        _rootHeading.TextWrapping = TextWrapping.NoWrap; _rootHeading.TextTrimming = TextTrimming.CharacterEllipsis;
        _rootHeading.Margin = new Thickness(0, 0, 12, 0); instanceHeader.Children.Add(_rootHeading);
        var viewModes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        AutomationProperties.SetName(_listView, "实例列表视图"); AutomationProperties.SetName(_gridView, "实例网格视图");
        ToolTip.SetTip(_listView, "长条卡片，纵向排列"); ToolTip.SetTip(_gridView, "紧凑卡片，网格排列");
        _listView.Click += (_, _) => ChangeLayout(InstanceLayout.List); _gridView.Click += (_, _) => ChangeLayout(InstanceLayout.Grid);
        viewModes.Children.Add(_listView); viewModes.Children.Add(_gridView); Grid.SetColumn(viewModes, 1); instanceHeader.Children.Add(viewModes);
        children.Children.Add(instanceHeader);
        var childHint = Text("高亮实例是启动游戏组件的当前目标。"); childHint.Margin = new Thickness(0, 6, 0, 12); Grid.SetRow(childHint, 1); children.Children.Add(childHint);
        AutomationProperties.SetName(_list, "目录中的实例");
        _list.SizeChanged += (_, _) => SizeInstanceCards();
        _instanceScroll = new ScrollViewer { Content = _list, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(_instanceScroll, 2); children.Children.Add(_instanceScroll);
        _empty.Margin = new Thickness(16); Grid.SetRow(_empty, 2); children.Children.Add(_empty);
        Grid.SetColumn(children, 2); _browser.Children.Add(children); body.Children.Add(_browser);

        _detailPage.RowDefinitions = new RowDefinitions("Auto,*,Auto");
        var detailHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,18,*,Auto"), Margin = new Thickness(0, 0, 0, 16) };
        detailHeader.Children.Add(Button("返回游戏目录", "← 游戏目录", () => { RememberDraft(); _showDetails = false; Refresh(); }));
        var identity = new StackPanel { Spacing = 4 };
        _detailTitle.FontSize = 20; _detailTitle.Foreground = Palette.Text; _detailTitle.FontWeight = FontWeight.SemiBold;
        _detailTitle.TextTrimming = TextTrimming.CharacterEllipsis; _detailTitle.TextWrapping = TextWrapping.NoWrap;
        _detailVersion.Foreground = Palette.Accent;
        identity.Children.Add(_detailTitle); identity.Children.Add(_detailVersion); Grid.SetColumn(identity, 2); detailHeader.Children.Add(identity);
        var sideActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _refresh = Button("刷新实例来源", "重新读取来源", async () => { if (_currentId is { } id) { _localError = null; await _service.RefreshAsync(id); _localError = _service.Error; Refresh(); } });
        sideActions.Children.Add(_refresh);
        Grid.SetColumn(sideActions, 3); detailHeader.Children.Add(sideActions); _detailPage.Children.Add(detailHeader);
        var form = new StackPanel { Spacing = 14 };
        _details.TextWrapping = TextWrapping.Wrap; form.Children.Add(PageStyles.Card(_details)); form.Children.Add(_editor);
        var basic = SettingSection("实例信息", "自定义名称只保存在 Wonder，实例路径保持原样。");
        AddField(basic, "name", "实例名称");
        AddField(basic, "directory", "游戏目录", "继承来源游戏目录");
        _editor.Children.Add(PageStyles.Card(basic));
        var runtime = SettingSection("Java 与内存", "留空使用全局偏好或来源配置，单实例设置优先。");
        AddField(runtime, "java", "Java 路径", "继承全局或来源；未指定时自动查找 Java");
        var memory = Pair(); AddField(memory.Left, "min", "最小内存 MiB"); AddField(memory.Right, "max", "最大内存 MiB"); runtime.Children.Add(memory.Grid);
        _editor.Children.Add(PageStyles.Card(runtime));
        var display = SettingSection("游戏窗口", "为此实例指定窗口尺寸和显示模式。");
        var dimensions = Pair(); AddField(dimensions.Left, "width", "窗口宽度"); AddField(dimensions.Right, "height", "窗口高度"); display.Children.Add(dimensions.Grid);
        var fullscreen = new StackPanel { Spacing = 5 }; fullscreen.Children.Add(Text("窗口模式")); fullscreen.Children.Add(_fullscreen); display.Children.Add(fullscreen);
        _editor.Children.Add(PageStyles.Card(display));
        AutomationProperties.SetName(_fullscreen, "窗口模式");
        var advanced = SettingSection("高级启动参数", "留空继承全局或来源，勾选清空可显式禁用额外参数。");
        AddField(advanced, "jvm", "额外 JVM 参数", "留空继承；含空格的值请加双引号"); advanced.Children.Add(_clearJvm);
        AddField(advanced, "game", "额外游戏参数", "留空继承；身份、目录和窗口使用上方设置项"); advanced.Children.Add(_clearGame);
        _editor.Children.Add(PageStyles.Card(advanced));
        _clearJvm.IsCheckedChanged += (_, _) => { _fields["jvm"].IsEnabled = _clearJvm.IsChecked != true; MarkDraft(); };
        _clearGame.IsCheckedChanged += (_, _) => { _fields["game"].IsEnabled = _clearGame.IsChecked != true; MarkDraft(); };
        _fullscreen.SelectionChanged += (_, _) => MarkDraft();
        _editor.Children.Add(Text("数值和路径留空表示继承。保存后用于下次启动，不改写来源启动器的配置。"));
        var saveActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        saveActions.Children.Add(Button("保存实例设置", "保存设置", Save));
        saveActions.Children.Add(Button("恢复来源设置", "清除实例覆盖", () =>
        {
            if (_currentId is not { } id) return; _localError = null; _service.UpdateSettings(id, new());
            _localError = _service.Error;
            if (_service.Error is null) { _drafts.Remove(id); LoadFields(_service.Catalog.Instances.Single(x => x.Id == id)); }
            Refresh();
        }));
        var saveFooter = new StackPanel { Spacing = 6, Margin = new Thickness(0, 10, 0, 0) };
        saveFooter.Children.Add(saveActions); saveFooter.Children.Add(_draftStatus);
        form.Children.Add(new Expander { Header = "当前有效设置与来源诊断", Content = _effective, HorizontalAlignment = HorizontalAlignment.Stretch });
        var scroll = new ScrollViewer { Content = form, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); _detailPage.Children.Add(scroll); Grid.SetRow(saveFooter, 2); _detailPage.Children.Add(saveFooter);
        body.Children.Add(_detailPage); Grid.SetRow(body, 1); layout.Children.Add(body);
        var footer = new StackPanel { Spacing = 8, Margin = new Thickness(0, 16, 0, 0) };
        _error.Foreground = Palette.Error; _error.MaxHeight = 70; footer.Children.Add(_error); footer.Children.Add(_status);
        _retry = Button("重试实例索引读取", "重试读取实例索引", () => { _localError = null; _service.Reload(); });
        footer.Children.Add(_retry);
        Grid.SetRow(footer, 2); layout.Children.Add(footer); Content = layout;
        _service.Changed += Changed; Closed += (_, _) => { _closed = true; _service.Changed -= Changed; };
        SetLayout(initialLayout); Refresh();
    }

    private async Task Import()
    {
        try
        {
            _localError = null;
            var path = _pickDirectory is not null ? await _pickDirectory() : (await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择已有游戏或实例目录", AllowMultiple = false })).FirstOrDefault()?.TryGetLocalPath();
            if (path is not null)
            {
                await _service.ImportAsync(path);
                _localError = _service.Error;
                var fullPath = CanonicalRoot(path);
                var imported = _service.Catalog.Instances.FirstOrDefault(x =>
                    SameRoot(x.RootDirectory, fullPath) || SameRoot(x.SourcePath, fullPath) || SameRoot(x.DefaultGameDirectory, fullPath) ||
                    x.SourcePath.StartsWith(fullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
                if (imported is not null) _rootPath = CanonicalRoot(imported.RootDirectory);
            }
        }
        catch (Exception ex) { _localError = "目录选择失败：" + ex.Message; }
        Refresh();
    }
    private void Changed(object? sender, EventArgs args)
    {
        if (Dispatcher.UIThread.CheckAccess()) Refresh();
        else if (Interlocked.Exchange(ref _pendingRefresh, 1) == 0) Dispatcher.UIThread.Post(() => { Interlocked.Exchange(ref _pendingRefresh, 0); Refresh(); });
    }
    private void Refresh()
    {
        if (_closed) return;
        var catalog = _service.Catalog;
        var selected = catalog.Instances.FirstOrDefault(x => x.Id == catalog.SelectedInstanceId);
        var instance = catalog.Instances.FirstOrDefault(x => x.Id == _currentId);
        _updating = true;
        try
        {
            var roots = catalog.GameDirectories.Select(path => new RootRow(path, catalog.Instances.Count(x => SameRoot(x.RootDirectory, path)))).ToArray();
            if (!roots.Any(x => SameRoot(x.Path, _rootPath)))
                _rootPath = roots.FirstOrDefault(x => selected is not null && SameRoot(x.Path, selected.RootDirectory))?.Path ?? roots.FirstOrDefault()?.Path;
            if (!_rootRows.SequenceEqual(roots))
            {
                _rootRows = roots; _roots.Children.Clear();
                foreach (var root in roots)
                {
                    var card = new InstanceBrowserCard("游戏目录：" + root.Name, root.Name, root.Count + " 个实例", root.Path,
                        () => { _rootPath = root.Path; Refresh(); }, () => _ = EditDirectory(root.Path), () => _ = DeleteDirectory(root.Path));
                    card.Tag = root.Path; _roots.Children.Add(card);
                }
            }
            foreach (var card in _roots.Children.OfType<InstanceBrowserCard>()) card.Selected = SameRoot((string)card.Tag!, _rootPath);
            var rows = catalog.Instances.Where(x => SameRoot(x.RootDirectory, _rootPath))
                .Select(x => new InstanceRow(x.Id, x.Name, x.VersionLabel, SourceName(x.Source), x.SourcePath)).ToArray();
            if (!_instanceRows.SequenceEqual(rows))
            {
                _instanceRows = rows; _list.Children.Clear();
                foreach (var row in rows)
                {
                    var card = new InstanceBrowserCard("实例：" + row.Name, row.Name, row.Version, row.Source + " · " + row.Path,
                        () => SelectInstance(row.Id), () => OpenInstance(row.Id), () => _ = DeleteInstance(row.Id));
                    card.Tag = row.Id; _list.Children.Add(card);
                }
                SizeInstanceCards();
            }
            foreach (var card in _list.Children.OfType<InstanceBrowserCard>()) card.Selected = (string?)card.Tag == selected?.Id;
            foreach (var card in _list.Children.Concat(_roots.Children).OfType<InstanceBrowserCard>())
            {
                card.SettingsButton.IsEnabled = !_service.IsBusy && !_service.IsReadBlocked;
                card.DeleteButton.IsEnabled = !_service.IsBusy && !_service.IsReadBlocked;
                card.SelectButton.IsEnabled = !_service.IsReadBlocked;
            }
            _rootHeading.Text = roots.FirstOrDefault(x => SameRoot(x.Path, _rootPath)) is { } selectedRoot ? selectedRoot.Name + " · " + rows.Length + " 个实例" : "已有实例";
            ToolTip.SetTip(_rootHeading, _rootPath);
            _empty.Text = roots.Length == 0 ? "还没有添加游戏目录\n\n点击左侧“添加游戏目录”，读取已经安装的 Minecraft。" : "此目录暂无实例\n\n安装游戏后可重新扫描，也可以通过目录卡片设置或删除这个目录。";
            _empty.IsVisible = rows.Length == 0;
            if (instance is null) { _showDetails = false; _currentId = null; }
            _browser.IsVisible = !_showDetails; _detailPage.IsVisible = _showDetails;
            _detailTitle.Text = instance?.Name; ToolTip.SetTip(_detailTitle, instance?.Name);
            _detailVersion.Text = instance?.VersionLabel;
            _details.Text = instance is null ? "" : SourceName(instance.Source) + "  ·  " + instance.SourcePath;
            if (instance is not null)
            {
                var s = instance.EffectiveSettings;
                _effective.Text = $"Java：{s.JavaPath ?? "自动查找"}\n内存：{s.MinimumMemoryMb}–{s.MaximumMemoryMb} MiB\n窗口：{s.WindowWidth} × {s.WindowHeight} · {(s.FullScreen == true ? "全屏" : "窗口")}\n游戏目录：{s.GameDirectory}\n配置来源：{instance.ConfigSource}\n\n" + string.Join("\n", instance.Warnings);
                var inherited = (instance with { Overrides = new() }).EffectiveSettings;
                _fields["min"].PlaceholderText = "继承 · " + inherited.MinimumMemoryMb; _fields["max"].PlaceholderText = "继承 · " + inherited.MaximumMemoryMb;
                _fields["width"].PlaceholderText = "继承 · " + inherited.WindowWidth; _fields["height"].PlaceholderText = "继承 · " + inherited.WindowHeight;
            }
            _error.Text = _service.IsReadBlocked ? _service.Error : _localError ?? "";
            _error.IsVisible = !string.IsNullOrEmpty(_error.Text); ToolTip.SetTip(_error, _error.Text);
            _retry.IsVisible = _service.IsReadBlocked;
            _status.Text = selected is null ? "尚未选择启动实例。" : "当前启动实例：" + selected.Name; ToolTip.SetTip(_status, _status.Text);
            _import.IsEnabled = !_service.IsBusy; _refresh.IsEnabled = instance is not null && !_service.IsBusy;
            _refreshRoot.IsEnabled = _rootPath is not null && !_service.IsBusy;
        }
        finally { _updating = false; }
    }
    private void LoadFields(GameInstance instance)
    {
        var wasUpdating = _updating; _updating = true;
        try
        {
            if (!_drafts.TryGetValue(instance.Id, out var draft))
            {
                var s = instance.Overrides;
                draft = new(new() { ["name"] = instance.Name, ["java"] = s.JavaPath ?? "", ["directory"] = s.GameDirectory ?? "", ["min"] = s.MinimumMemoryMb?.ToString() ?? "", ["max"] = s.MaximumMemoryMb?.ToString() ?? "", ["width"] = s.WindowWidth?.ToString() ?? "", ["height"] = s.WindowHeight?.ToString() ?? "", ["jvm"] = s.JvmArguments ?? "", ["game"] = s.GameArguments ?? "" }, s.FullScreen is null ? 0 : s.FullScreen == true ? 2 : 1, s.JvmArguments == "", s.GameArguments == "");
            }
            foreach (var field in _fields) field.Value.Text = draft.Text[field.Key];
            _fullscreen.SelectedIndex = draft.Fullscreen; _clearJvm.IsChecked = draft.ClearJvm; _clearGame.IsChecked = draft.ClearGame;
            _draftStatus.Text = _drafts.ContainsKey(instance.Id) ? "未保存的编辑 · 切换实例时保留" : "设置已保存 · 空白项继承全局或来源";
        }
        finally { _updating = wasUpdating; }
    }
    private void RememberDraft()
    {
        if (_currentId is { } id && _draftStatus.Text?.StartsWith("未保存") == true)
            _drafts[id] = new(_fields.ToDictionary(x => x.Key, x => x.Value.Text ?? ""), _fullscreen.SelectedIndex, _clearJvm.IsChecked == true, _clearGame.IsChecked == true);
    }
    private void MarkDraft() { if (!_updating) _draftStatus.Text = "未保存的编辑 · 点击保存后用于下次启动"; }
    private void Save()
    {
        if (_currentId is not { } id) return;
        try
        {
            string? Value(string key) => string.IsNullOrWhiteSpace(_fields[key].Text) ? null : _fields[key].Text!.Trim();
            int? Number(string key) => Value(key) is not { } text ? null : int.TryParse(text, out var value) ? value : throw new ArgumentException("内存和窗口尺寸必须是整数。");
            var settings = new InstanceSettings
            {
                JavaPath = Value("java"), GameDirectory = Value("directory"), MinimumMemoryMb = Number("min"), MaximumMemoryMb = Number("max"), WindowWidth = Number("width"), WindowHeight = Number("height"),
                FullScreen = _fullscreen.SelectedIndex == 0 ? null : _fullscreen.SelectedIndex == 2,
                JvmArguments = _clearJvm.IsChecked == true ? "" : Value("jvm"), GameArguments = _clearGame.IsChecked == true ? "" : Value("game")
            };
            _localError = null; _service.UpdateInstance(id, Value("name") ?? "", settings);
            _localError = _service.Error;
            if (_service.Error is null) { _drafts.Remove(id); LoadFields(_service.Catalog.Instances.Single(x => x.Id == id)); }
        }
        catch (ArgumentException ex) { _localError = ex.Message; }
        Refresh();
    }
    private void AddField(StackPanel parent, string key, string label, string? placeholder = null)
    {
        var panel = new StackPanel { Spacing = 5 }; panel.Children.Add(Text(label));
        var input = new TextBox { PlaceholderText = placeholder, MaxLength = key == "name" ? 120 : 32768, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(input, label); input.PropertyChanged += (_, args) => { if (args.Property == TextBox.TextProperty) MarkDraft(); };
        _fields[key] = input; panel.Children.Add(input); parent.Children.Add(panel);
    }
    private static (Grid Grid, StackPanel Left, StackPanel Right) Pair()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") }; var left = new StackPanel(); var right = new StackPanel();
        grid.Children.Add(left); Grid.SetColumn(right, 2); grid.Children.Add(right); return (grid, left, right);
    }
    private static Button Button(string name, string label, Action action)
    {
        var button = new Button { Content = label, MinHeight = 34 }; AutomationProperties.SetName(button, name); button.Click += (_, _) => action(); return button;
    }
    private static TextBlock Text(string text = "") => new() { Text = text, FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    private static string SourceName(InstanceSource source) => source switch { InstanceSource.Hmcl => "HMCL", InstanceSource.Pcl => "PCL", InstanceSource.Prism => "Prism", InstanceSource.MultiMc => "MultiMC", _ => "官方目录" };
    private static string CanonicalRoot(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static bool SameRoot(string path, string? other) => other is not null && string.Equals(CanonicalRoot(path), CanonicalRoot(other), StringComparison.OrdinalIgnoreCase);
    private void OpenInstance(string id)
    {
        var instance = _service.Catalog.Instances.FirstOrDefault(x => x.Id == id);
        if (instance is null) return;
        RememberDraft(); _localError = null; _currentId = id;
        LoadFields(instance); _showDetails = true;
        Refresh();
    }
    private void SelectInstance(string id)
    {
        _localError = null; _service.Select(id); _localError = _service.Error; Refresh();
    }
    private Task DeleteInstance(string id) => ConfirmDelete(() => _service.PrepareDeleteInstance(id));
    private Task DeleteDirectory(string path) => ConfirmDelete(() => _service.PrepareDeleteDirectory(path));
    private async Task ConfirmDelete(Func<InstanceDeletePlan> prepare)
    {
        try
        {
            _localError = null;
            var plan = prepare();
            var dialog = new DeleteConfirmationWindow(plan.Title, plan.Path, plan.Description, async word =>
            {
                var deleted = await _service.DeleteAsync(plan, word);
                return deleted ? null : _service.Error ?? "删除未完成。";
            }) { FontFamily = FontFamily };
            await dialog.ShowDialog<bool>(this);
            foreach (var id in _drafts.Keys.Where(id => !_service.Catalog.Instances.Any(x => x.Id == id)).ToArray()) _drafts.Remove(id);
        }
        catch (Exception ex) { _localError = ex.Message; }
        Refresh();
    }
    private async Task EditDirectory(string path)
    {
        try
        {
            _localError = null;
            var dialog = new DirectorySettingsWindow(path, async target =>
            {
                var moved = await _service.MoveDirectoryAsync(path, target);
                if (!moved) return _service.Error ?? "目录修改未完成。";
                _rootPath = target;
                foreach (var draft in _drafts.Values)
                    foreach (var key in new[] { "java", "directory", "jvm", "game" })
                    {
                        var text = draft.Text[key];
                        if (string.Equals(text, path, StringComparison.OrdinalIgnoreCase)) draft.Text[key] = target;
                        else draft.Text[key] = text.Replace(path + Path.DirectorySeparatorChar, target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                    }
                return null;
            }) { FontFamily = FontFamily };
            await dialog.ShowDialog<bool>(this);
        }
        catch (Exception ex) { _localError = ex.Message; }
        Refresh();
    }
    private void SizeInstanceCards()
    {
        var width = _list.Bounds.Width;
        if (width <= 0) return;
        var columns = Math.Max(1, (int)(width / 220));
        var gridWidth = Math.Min(260, Math.Max(1, width / columns - 10));
        foreach (var card in _list.Children.OfType<InstanceBrowserCard>())
        {
            var isList = _layout == InstanceLayout.List;
            card.Width = isList ? width : gridWidth;
            card.Margin = new Thickness(0, 0, isList ? 0 : 10, 10);
        }
    }
    public void SetLayout(InstanceLayout layout)
    {
        _layout = layout;
        _listView.IsChecked = layout == InstanceLayout.List; _gridView.IsChecked = layout == InstanceLayout.Grid;
        SizeInstanceCards();
    }
    private void ChangeLayout(InstanceLayout layout)
    {
        var changed = _layout != layout;
        SetLayout(layout);
        if (!changed) return;
        _instanceScroll.Offset = default;
        _layoutChanged?.Invoke(layout);
    }
    private static StackPanel SettingSection(string title, string description)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = title, Foreground = Palette.Accent, FontSize = 17, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(PageStyles.Hint(description)); return panel;
    }
    private sealed record RootRow(string Path, int Count)
    {
        public string Name => System.IO.Path.GetFileName(Path) is { Length: > 0 } name ? name : Path;
    }
    private sealed record InstanceRow(string Id, string Name, string Version, string Source, string Path);
    private sealed record Draft(Dictionary<string, string> Text, int Fullscreen, bool ClearJvm, bool ClearGame);
}
