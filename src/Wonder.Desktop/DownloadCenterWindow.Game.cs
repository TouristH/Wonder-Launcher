// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Avalonia.Controls;
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

    private Control BuildGamePage()
    {
        _gameType = Choice("游戏版本类型", ["正式版", "快照 / 预发布", "远古版本", "全部版本"]); _gameType.SelectedIndex = 0;
        _gameQuery = Input("筛选游戏版本", "搜索版本，例如 1.21"); _gameVersions = Choice("Minecraft 版本");
        _loader = Choice("加载器", ["原版 Minecraft", "Fabric", "Quilt", "Forge", "NeoForge"]); _loader.SelectedIndex = 0;
        _loaderVersions = Choice("加载器版本"); _loaderVersions.IsEnabled = false;
        _gameStatus = PageStyles.Hint("正在准备游戏版本列表…");
        _gameType.SelectionChanged += (_, _) => FilterGameVersions();
        _gameQuery.TextChanged += (_, _) => FilterGameVersions();
        _gameVersions.SelectionChanged += (_, _) =>
        {
            if (_gameVersions.SelectedIndex >= 0 && _gameVersions.SelectedIndex < _visibleGameVersions.Length)
            {
                var version = _visibleGameVersions[_gameVersions.SelectedIndex];
                if (string.IsNullOrWhiteSpace(_gameName.Text) || _gameName.Text.StartsWith("Minecraft ", StringComparison.Ordinal)) _gameName.Text = "Minecraft " + version.Id;
            }
            _ = LoadLoaderVersionsAsync();
        };
        _loader.SelectionChanged += (_, _) => _ = LoadLoaderVersionsAsync();
        var versionCard = Stack(Heading("1 · 选择版本"), Field("版本分类", _gameType), _gameQuery,
            Field("Minecraft", _gameVersions), Field("加载器", _loader), Field("加载器版本", _loaderVersions), _gameStatus,
            ActionButton("刷新游戏版本", () => _ = LoadGameVersionsAsync()));
        _gameName = Input("新实例名称", "给这个实例取个名字");
        _gameParent = Input("游戏安装父目录", "选择实例保存位置", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "Wonder"));
        _javaPath = Input("安装器 Java 路径", "留空自动安装游戏所需 Java", _instances.Catalog.GlobalSettings.JavaPath);
        _installGame = ActionButton("安装游戏", QueueGame); _installGame.IsEnabled = false; _installGame.HorizontalAlignment = HorizontalAlignment.Left;
        var destination = Stack(Heading("2 · 安装位置"), Field("实例名称", _gameName), DirectoryField("游戏保存目录", _gameParent),
            Field("Java 可执行文件（留空自动安装）", _javaPath), PageStyles.Hint("每个实例使用独立目录。完成后在实例管理中选择账户即可启动。"), _installGame);
        return Stack(PageStyles.Card(versionCard), PageStyles.Card(destination));
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
        _loaderRequest?.Cancel(); _loaderRequest = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); var request = _loaderRequest;
        _availableLoaders = []; _loaderVersions.ItemsSource = Array.Empty<string>(); _loaderVersions.SelectedIndex = -1;
        _loaderVersions.IsEnabled = false; _loadingLoaders = false;
        var game = _gameVersions.SelectedIndex;
        if (_loader.SelectedIndex <= 0 || game < 0 || game >= _visibleGameVersions.Length) { _loaderRequest = null; request.Dispose(); UpdateGameInstall(); return; }
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
    private void UpdateGameInstall()
    {
        if (_installGame is null) return;
        _installGame.IsEnabled = !_loadingVersions && !_loadingLoaders && _gameVersions.SelectedIndex >= 0 &&
            (_loader.SelectedIndex == 0 || _availableLoaders.Count > 0);
    }
    private void QueueGame()
    {
        try
        {
            var version = _visibleGameVersions[_gameVersions.SelectedIndex]; var loader = LoaderIds[_loader.SelectedIndex];
            var target = NewInstancePath(_gameParent.Text, _gameName.Text);
            var request = new GameInstallRequest(version.Id, _gameName.Text!.Trim(), target, loader,
                loader == "vanilla" ? null : _availableLoaders[_loaderVersions.SelectedIndex].Version, string.IsNullOrWhiteSpace(_javaPath.Text) ? null : _javaPath.Text.Trim());
            _downloads.QueueGame(request); Queued(request.InstanceName);
        }
        catch (Exception ex) { ShowStatus(ex.Message, true); }
    }
}
