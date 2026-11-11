// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Wonder.Core;

namespace Wonder.Desktop;

internal sealed partial class DownloadCenterWindow
{
    private ComboBox _gameVersions = null!, _gameType = null!, _loader = null!, _loaderVersions = null!;
    private TextBox _gameQuery = null!, _gameParent = null!, _gameName = null!, _javaPath = null!;
    private TextBlock _gameStatus = null!;
    private Button _installGame = null!;
    private IReadOnlyList<MinecraftVersion> _allGameVersions = [];
    private MinecraftVersion[] _visibleGameVersions = [];
    private IReadOnlyList<LoaderVersion> _availableLoaders = [];
    private CancellationTokenSource? _loaderRequest;
    private bool _loadingVersions, _loadingLoaders;
    private static readonly string[] LoaderIds = ["vanilla", "fabric", "quilt", "forge", "neoforge"];
    private static readonly string[] LoaderLabels = ["原版 Minecraft", "Fabric", "Quilt", "Forge", "NeoForge"];
    private readonly Dictionary<GameAddonKind, ComboBox> _addonChoices = [];
    private readonly Dictionary<GameAddonKind, IReadOnlyList<GameAddonVersion>> _addonVersions = [];
    private CancellationTokenSource? _addonRequest;
    private TextBlock _addonStatus = null!, _isolationHint = null!;
    private string? _defaultGameParent;
    private string? _suggestedGameName;

    private Control BuildGamePage()
    {
        _gameType = Choice("游戏版本类型", ["正式版", "快照 / 预发布", "远古版本", "全部版本"]); _gameType.SelectedIndex = 0;
        _gameQuery = Input("筛选游戏版本", "搜索版本，例如 1.21"); _gameVersions = Choice("Minecraft 版本");
        _loader = Choice("加载器");
        _loader.ItemsSource = LoaderIds.Select((key, i) => new LoaderOption(key, LoaderLabels[i])).ToArray();
        _loader.ItemTemplate = new FuncDataTemplate<LoaderOption>((option, _) =>
        {
            if (option is null) return null;
            var icon = new InstanceIconView(23); icon.SetIcon(null, option.Key);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = option.Name, VerticalAlignment = VerticalAlignment.Center });
            return row;
        });
        _loader.SelectedIndex = 0;
        _loaderVersions = Choice("加载器版本"); _loaderVersions.IsEnabled = false;
        _gameStatus = PageStyles.Hint("正在准备游戏版本列表…");
        _gameType.SelectionChanged += (_, _) => FilterGameVersions();
        _gameQuery.TextChanged += (_, _) => FilterGameVersions();
        _gameVersions.SelectionChanged += (_, _) => _ = LoadLoaderVersionsAsync();
        _loader.SelectionChanged += (_, _) => _ = LoadLoaderVersionsAsync();
        _loaderVersions.SelectionChanged += (_, _) =>
        {
            UpdateGameInstall();
            _ = LoadGameAddonsAsync();
        };
        var optional = Stack(Heading("可选组件"));
        foreach (var (kind, title) in new[] { (GameAddonKind.OptiFine, "OptiFine"), (GameAddonKind.FabricApi, "Fabric API"), (GameAddonKind.QuiltApi, "Quilted Fabric API / QSL") })
        {
            var choice = Choice(title, ["不安装"]); choice.SelectedIndex = 0;
            _addonChoices.Add(kind, choice);
            var field = Field(title, choice); field.IsVisible = kind == GameAddonKind.OptiFine;
            optional.Children.Add(field);
        }
        _addonStatus = PageStyles.Hint("选择 Minecraft 版本后查询可用组件。"); optional.Children.Add(_addonStatus);
        var versionCard = Stack(Heading("选择版本"), Field("版本分类", _gameType), _gameQuery,
            Field("Minecraft", _gameVersions), Field("加载器", _loader), Field("加载器版本", _loaderVersions), _gameStatus,
            ActionButton("刷新游戏版本", () => _ = LoadGameVersionsAsync()));
        _gameName = Input("新实例名称", "给这个实例取个名字");
        _defaultGameParent = DefaultGameInstallDirectory();
        _gameParent = Input("游戏安装父目录", "选择游戏目录", _defaultGameParent);
        _javaPath = Input("安装器 Java 路径", "留空自动安装游戏所需 Java", _instances.Catalog.GlobalSettings.JavaPath);
        _installGame = ActionButton("安装游戏", QueueGame); _installGame.IsEnabled = false; _installGame.HorizontalAlignment = HorizontalAlignment.Left;
        _isolationHint = PageStyles.Hint(""); RefreshGameInstallDirectory();
        var destination = Stack(Heading("安装位置"), Field("实例名称", _gameName), DirectoryField("游戏保存目录", _gameParent),
            Field("Java 可执行文件（留空自动安装）", _javaPath), _isolationHint, _installGame);
        return Stack(PageStyles.Card(versionCard), PageStyles.Card(optional), PageStyles.Card(destination));
    }

    private async Task LoadGameVersionsAsync()
    {
        if (_loadingVersions || _closed) return; _loadingVersions = true; _installGame.IsEnabled = false;
        _gameStatus.Text = "正在获取游戏版本列表…";
        try
        {
            var list = await _downloads.Catalog.GetMinecraftVersionsAsync(_downloads.Options, _lifetime.Token);
            if (_closed) return; _allGameVersions = list; FilterGameVersions();
            _gameStatus.Text = $"已获取 {list.Count} 个版本。";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _gameStatus.Text = "版本列表获取失败：" + ex.Message; ShowStatus(_gameStatus.Text, true); }
        finally { _loadingVersions = false; UpdateGameInstall(); }
    }
    private void FilterGameVersions()
    {
        var previous = _gameVersions.SelectedIndex >= 0 && _gameVersions.SelectedIndex < _visibleGameVersions.Length ? _visibleGameVersions[_gameVersions.SelectedIndex].Id : null;
        var query = _gameQuery.Text?.Trim() ?? "";
        _visibleGameVersions = _allGameVersions.Where(x => x.Id.Contains(query, StringComparison.OrdinalIgnoreCase) &&
            (_gameType.SelectedIndex switch { 0 => x.Type == "release", 1 => x.Type == "snapshot", 2 => x.Type is "old_alpha" or "old_beta", _ => true })).ToArray();
        _gameVersions.ItemsSource = _visibleGameVersions.Select(x => x.Id + " · " + x.ReleaseTime.ToString("yyyy-MM-dd")).ToArray();
        _gameVersions.SelectedIndex = Math.Max(0, Array.FindIndex(_visibleGameVersions, x => x.Id == previous));
        if (_visibleGameVersions.Length == 0) _gameVersions.SelectedIndex = -1;
        UpdateGameInstall();
    }
    private async Task LoadLoaderVersionsAsync()
    {
        if (_closed) return;
        _loaderRequest?.Cancel(); _loaderRequest = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); var request = _loaderRequest;
        _availableLoaders = []; _loaderVersions.ItemsSource = Array.Empty<string>(); _loaderVersions.SelectedIndex = -1;
        _loaderVersions.IsEnabled = false; _loadingLoaders = false;
        var game = _gameVersions.SelectedIndex;
        if (_loader.SelectedIndex <= 0 || game < 0 || game >= _visibleGameVersions.Length)
        {
            _loaderRequest = null; request.Dispose(); UpdateGameInstall();
            _ = LoadGameAddonsAsync();
            return;
        }
        _loadingLoaders = true; UpdateGameInstall(); _gameStatus.Text = "正在获取兼容的加载器版本…";
        try
        {
            var versions = await _downloads.Catalog.GetLoaderVersionsAsync(LoaderIds[_loader.SelectedIndex], _visibleGameVersions[game].Id, _downloads.Options, request.Token);
            if (_closed || request.IsCancellationRequested) return;
            _availableLoaders = versions; _loaderVersions.ItemsSource = versions.Select(x => x.Version + (x.Stable ? " · 稳定" : "")).ToArray();
            _loaderVersions.SelectedIndex = versions.Count > 0 ? 0 : -1; _loaderVersions.IsEnabled = versions.Count > 0;
            _gameStatus.Text = versions.Count > 0 ? $"找到 {versions.Count} 个兼容版本。" : "这个 Minecraft 版本暂无该加载器，试试其他版本或加载器。";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!request.IsCancellationRequested) _gameStatus.Text = "加载器列表获取失败：" + ex.Message; }
        finally { if (ReferenceEquals(request, _loaderRequest)) { _loaderRequest = null; _loadingLoaders = false; UpdateGameInstall(); } request.Dispose(); }
    }

    private async Task LoadGameAddonsAsync()
    {
        if (_closed || _addonStatus is null) return;
        _addonRequest?.Cancel(); _addonRequest = null;
        _addonVersions.Clear();
        var gameIndex = _gameVersions.SelectedIndex;
        var loaderIndex = _loader.SelectedIndex;
        var loaderVersionIndex = _loaderVersions.SelectedIndex;
        var game = gameIndex >= 0 && gameIndex < _visibleGameVersions.Length ? _visibleGameVersions[gameIndex].Id : null;
        var loader = loaderIndex >= 0 && loaderIndex < LoaderIds.Length ? LoaderIds[loaderIndex] : "vanilla";
        var loaderVersion = loaderVersionIndex >= 0 && loaderVersionIndex < _availableLoaders.Count ? _availableLoaders[loaderVersionIndex].Version : null;
        var supported = loader switch
        {
            "vanilla" or "forge" => GameAddonKind.OptiFine,
            "fabric" => GameAddonKind.FabricApi,
            "quilt" => GameAddonKind.QuiltApi,
            _ => (GameAddonKind?)null
        };
        foreach (var (kind, choice) in _addonChoices)
        {
            choice.ItemsSource = new[] { "不安装" }; choice.SelectedIndex = 0; choice.IsEnabled = false;
            if (choice.Parent is StackPanel field) field.IsVisible = supported == kind;
        }
        if (supported is not { } selected)
        { _addonStatus.Text = "此加载器没有可单独选装的配套组件。"; return; }
        if (game is null || loader != "vanilla" && loaderVersion is null)
        { _addonStatus.Text = "选择游戏及加载器版本后查询可用组件。"; return; }
        var request = _addonRequest = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _addonStatus.Text = "正在查询可选组件…";
        try
        {
            var versions = await _downloads.Catalog.GetGameAddonsAsync(selected, loader, game, loaderVersion, _downloads.Options, request.Token);
            if (request.IsCancellationRequested || _closed) return;
            _addonVersions[selected] = versions;
            var choice = _addonChoices[selected];
            choice.ItemsSource = new[] { "不安装" }.Concat(versions.Select(x => x.Name + (x.Stable ? "" : " · 预览"))).ToArray();
            choice.SelectedIndex = 0; choice.IsEnabled = versions.Count > 0;
            _addonStatus.Text = versions.Count == 0 ? "没有适配当前组合的版本。" : "请选择要随实例安装的组件版本。";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!request.IsCancellationRequested && !_closed) _addonStatus.Text = "可选组件读取失败：" + ex.Message; }
        finally { if (ReferenceEquals(_addonRequest, request)) _addonRequest = null; request.Dispose(); }
    }

    private string? SelectedAddon(GameAddonKind kind)
    {
        var index = _addonChoices[kind].SelectedIndex - 1;
        return index >= 0 && _addonVersions.TryGetValue(kind, out var choices) && index < choices.Count ? choices[index].Id : null;
    }

    private void RefreshGameInstallDirectory()
    {
        if (_gameParent is null) return;
        var next = DefaultGameInstallDirectory();
        if (_gameParent.Text == _defaultGameParent) _gameParent.Text = next;
        _defaultGameParent = next;
        if (_isolationHint is not null) _isolationHint.Text = _instances.Catalog.VersionIsolationEnabled
            ? "版本隔离已开启，新实例在所选游戏目录的 instances 文件夹内独立保存。"
            : "版本隔离已关闭，新实例与所选游戏目录共用存档、模组和配置。";
    }
    private void UpdateGameInstall()
    {
        if (_installGame is null) return;
        var gameIndex = _gameVersions.SelectedIndex;
        var loaderIndex = _loader.SelectedIndex;
        var versionIndex = _loaderVersions.SelectedIndex;
        var gameSelected = gameIndex >= 0 && gameIndex < _visibleGameVersions.Length;
        var loaderVersionSelected = loaderIndex > 0 && loaderIndex < LoaderIds.Length &&
            versionIndex >= 0 && versionIndex < _availableLoaders.Count;
        _installGame.IsEnabled = !_loadingVersions && !_loadingLoaders && gameSelected && (loaderIndex == 0 || loaderVersionSelected);
        if (!gameSelected) return;

        var gameVersion = _visibleGameVersions[gameIndex].Id;
        var suggestion = "Minecraft " + gameVersion;
        if (loaderVersionSelected)
        {
            var loaderVersion = _availableLoaders[versionIndex].Version;
            var repeatedPrefix = gameVersion + "-";
            if (loaderVersion.StartsWith(repeatedPrefix, StringComparison.Ordinal)) loaderVersion = loaderVersion[repeatedPrefix.Length..];
            suggestion += $" - {LoaderLabels[loaderIndex]} {loaderVersion}";
        }
        if (string.IsNullOrWhiteSpace(_gameName.Text) || _gameName.Text == _suggestedGameName) _gameName.Text = suggestion;
        _suggestedGameName = suggestion;
    }
    private void QueueGame()
    {
        try
        {
            var version = _visibleGameVersions[_gameVersions.SelectedIndex]; var loader = LoaderIds[_loader.SelectedIndex];
            var target = NewGameInstallPath(_gameParent.Text, _gameName.Text);
            var request = new GameInstallRequest(version.Id, _gameName.Text!.Trim(), target, loader,
                loader == "vanilla" ? null : _availableLoaders[_loaderVersions.SelectedIndex].Version, string.IsNullOrWhiteSpace(_javaPath.Text) ? null : _javaPath.Text.Trim(),
                SelectedAddon(GameAddonKind.OptiFine), SelectedAddon(GameAddonKind.FabricApi), SelectedAddon(GameAddonKind.QuiltApi))
            { InstallInCollection = true, ShareGameDirectory = !_instances.Catalog.VersionIsolationEnabled };
            _downloads.QueueGame(request); Queued(request.InstanceName);
        }
        catch (Exception ex) { ShowStatus(ex.Message, true); }
    }

    private sealed record LoaderOption(string Key, string Name);
}
