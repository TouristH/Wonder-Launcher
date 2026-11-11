// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Wonder.Core;

namespace Wonder.Desktop;

internal sealed partial class DownloadCenterWindow
{
    private TextBox _resourceQuery = null!, _resourceGame = null!, _packParent = null!, _packName = null!;
    private ComboBox _resourceLoader = null!, _targetInstance = null!, _resourceVersions = null!;
    private TextBlock _resourceStatus = null!, _projectTitle = null!, _projectDescription = null!, _versionDetails = null!, _installTargetDescription = null!;
    private StackPanel _resourceResults = null!;
    private Control _resourceDetails = null!, _packDestination = null!, _targetInstanceRow = null!, _resourceSearchPanel = null!, _resourcePaging = null!;
    private Button _previousResources = null!, _nextResources = null!, _installResource = null!;
    private CancellationTokenSource? _resourceRequest, _versionRequest;
    private ResourceProject? _selectedProject;
    private IReadOnlyList<ResourceVersion> _availableResourceVersions = [];
    private GameInstance[] _targetInstances = [];
    private int _offset;
    private bool _refreshingInstances, _filtersInitialized;
    private static readonly string[] ResourceLoaderIds = ["", "fabric", "quilt", "forge", "neoforge"];
    private DownloadResourceKind ResourceKind => _page switch { 2 => DownloadResourceKind.Modpack, 3 => DownloadResourceKind.ResourcePack, 4 => DownloadResourceKind.Shader, _ => DownloadResourceKind.Mod };

    private Control BuildResourcesPage()
    {
        _resourceQuery = Input("搜索资源", "搜索项目名称或关键词");
        _resourceGame = Input("资源 Minecraft 版本", "留空查看全部版本");
        _resourceLoader = Choice("资源加载器筛选", ["全部加载器", "Fabric", "Quilt", "Forge", "NeoForge"]); _resourceLoader.SelectedIndex = 0;
        _targetInstance = Choice("资源目标实例");
        _targetInstance.SelectionChanged += (_, _) =>
        {
            if (_refreshingInstances) return;
            if (_targetInstance.SelectedIndex >= 0 && _targetInstance.SelectedIndex < _targetInstances.Length)
            {
                var instance = _targetInstances[_targetInstance.SelectedIndex];
                _resourceGame.Text = instance.GameVersion;
                _resourceLoader.SelectedIndex = Math.Max(0, Array.IndexOf(ResourceLoaderIds, InstanceLoader(instance)));
                _selectedProject = null; _resourceDetails.IsVisible = false;
                if (_page is 1 or 3 or 4) _ = SearchResourcesAsync(0);
            }
        };
        _targetInstanceRow = Field("安装到实例", _targetInstance);
        _packParent = Input("整合包安装父目录", "选择实例保存位置", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "Wonder"));
        _packName = Input("整合包实例名称", "给整合包实例取个名字");
        _packDestination = Stack(Field("新实例名称", _packName), DirectoryField("整合包保存目录", _packParent));
        _packDestination.IsVisible = false;
        var searchRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,Auto") };
        searchRow.Children.Add(_resourceQuery);
        var search = ActionButton("搜索 Modrinth", () => _ = SearchResourcesAsync(0)); Grid.SetColumn(search, 2); searchRow.Children.Add(search);
        _resourceQuery.KeyDown += (_, args) => { if (args.Key == Key.Enter) { args.Handled = true; _ = SearchResourcesAsync(0); } };
        _resourceStatus = PageStyles.Hint("输入关键词，或浏览热门资源。");
        _resourceResults = new StackPanel { Spacing = 10 };
        _previousResources = ActionButton("上一页资源", () => _ = SearchResourcesAsync(Math.Max(0, _offset - 20)));
        _nextResources = ActionButton("下一页资源", () => _ = SearchResourcesAsync(_offset + 20));
        _previousResources.IsEnabled = _nextResources.IsEnabled = false;
        var pages = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; pages.Children.Add(_previousResources); pages.Children.Add(_nextResources);
        _projectTitle = Heading(""); _projectDescription = PageStyles.Hint(""); _installTargetDescription = PageStyles.Hint("");
        _resourceVersions = Choice("资源版本");
        _versionDetails = PageStyles.Hint("");
        _installResource = ActionButton("安装所选资源", QueueResource); _installResource.IsEnabled = false;
        _installResource.HorizontalAlignment = HorizontalAlignment.Left;
        _resourceVersions.SelectionChanged += (_, _) =>
        {
            var version = SelectedResourceVersion();
            _installResource.IsEnabled = version is not null;
            _versionDetails.Text = version is null ? "选择一个兼容版本。" :
                "Minecraft " + string.Join("、", version.GameVersions) + "\n" + string.Join("、", version.Loaders) +
                $" · {version.Files.Count} 个主文件 · {version.Dependencies.Count(x => x.Type == "required")} 项必需依赖（自动安装）";
        };
        var back = ActionButton("返回资源列表", ShowResourceResults); back.HorizontalAlignment = HorizontalAlignment.Left;
        var details = Stack(back, _projectTitle, _projectDescription, _installTargetDescription, Field("可安装版本", _resourceVersions), _versionDetails, _packDestination,
            PageStyles.Hint("资源保留作者原有许可。模组会安装到 mods，资源包到 resourcepacks，光影到 shaderpacks；游戏内启用资源包或光影。"), _installResource);
        _resourceDetails = PageStyles.Card(details); _resourceDetails.IsVisible = false;
        _resourceSearchPanel = PageStyles.Card(Stack(_targetInstanceRow, searchRow, Field("Minecraft 版本筛选", _resourceGame), _resourceLoader, _resourceStatus));
        _resourcePaging = pages;
        return Stack(_resourceSearchPanel, _resourceResults, pages, _resourceDetails);
    }

    private void ShowResourceResults()
    {
        _versionRequest?.Cancel(); _selectedProject = null;
        _resourceDetails.IsVisible = false; _resourceSearchPanel.IsVisible = _resourceResults.IsVisible = _resourcePaging.IsVisible = true;
        _scroll.Offset = default;
    }

    private void RefreshInstances()
    {
        var previous = _targetInstance.SelectedIndex >= 0 && _targetInstance.SelectedIndex < _targetInstances.Length ? _targetInstances[_targetInstance.SelectedIndex].Id : _instances.Catalog.SelectedInstanceId;
        _refreshingInstances = true;
        try
        {
            _targetInstances = _instances.Catalog.Instances.ToArray();
            _targetInstance.ItemsSource = _targetInstances.Select(x => x.Name + " · " + x.VersionLabel).ToArray();
            var index = Array.FindIndex(_targetInstances, x => x.Id == previous);
            _targetInstance.SelectedIndex = index >= 0 ? index : _targetInstances.Length > 0 ? 0 : -1;
        }
        finally { _refreshingInstances = false; }
        _targetInstance.IsEnabled = _targetInstances.Length > 0;
        if (!_filtersInitialized && _targetInstance.SelectedIndex >= 0)
        {
            _filtersInitialized = true;
            var selected = _targetInstances[_targetInstance.SelectedIndex];
            _resourceGame.Text = selected.GameVersion;
            _resourceLoader.SelectedIndex = Math.Max(0, Array.IndexOf(ResourceLoaderIds, InstanceLoader(selected)));
        }
    }

    private async Task SearchResourcesAsync(int offset)
    {
        if (_closed || _page is < 1 or > 4) return;
        _resourceRequest?.Cancel(); _versionRequest?.Cancel();
        var request = _resourceRequest = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var kind = ResourceKind;
        ShowResourceResults(); _resourceResults.Children.Clear();
        _previousResources.IsEnabled = _nextResources.IsEnabled = false;
        _resourceStatus.Text = "正在搜索 Modrinth…";
        try
        {
            var result = await _downloads.Catalog.SearchResourcesAsync(kind, _resourceQuery.Text?.Trim() ?? "", _resourceGame.Text?.Trim(),
                kind is DownloadResourceKind.Mod or DownloadResourceKind.Modpack ? ResourceLoaderIds[Math.Max(0, _resourceLoader.SelectedIndex)] : null,
                offset, _downloads.Options, request.Token);
            if (_closed || request.IsCancellationRequested) return;
            _offset = result.Offset;
            _resourceStatus.Text = result.Projects.Count == 0 ? "没有找到匹配资源。试试其他关键词、版本或加载器。" :
                $"共 {result.Total:N0} 项 · 第 {result.Offset + 1}–{result.Offset + result.Projects.Count} 项";
            foreach (var project in result.Projects)
            {
                var button = PageStyles.CardButton("资源项目·" + project.Id, project.Title, project.Description, $"{project.Downloads:N0} 次下载 · 查看版本", () => _ = SelectProjectAsync(project));
                _resourceResults.Children.Add(button);
            }
            _previousResources.IsEnabled = result.Offset > 0;
            _nextResources.IsEnabled = result.Offset + result.Projects.Count < result.Total;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!request.IsCancellationRequested) _resourceStatus.Text = "搜索失败，可点击搜索重试：" + ex.Message; }
        finally { if (ReferenceEquals(_resourceRequest, request)) _resourceRequest = null; request.Dispose(); }
    }

    private async Task SelectProjectAsync(ResourceProject project)
    {
        _versionRequest?.Cancel(); var request = _versionRequest = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _selectedProject = project; _availableResourceVersions = []; _resourceVersions.ItemsSource = Array.Empty<string>(); _resourceVersions.SelectedIndex = -1;
        _installResource.IsEnabled = false; _resourceDetails.IsVisible = true;
        _resourceSearchPanel.IsVisible = _resourceResults.IsVisible = _resourcePaging.IsVisible = false;
        _projectTitle.Text = project.Title; _projectDescription.Text = project.Description; _versionDetails.Text = "正在获取兼容版本…";
        _installTargetDescription.Text = project.Kind == DownloadResourceKind.Modpack ? "创建新的独立实例" :
            _targetInstance.SelectedIndex >= 0 ? "安装到：" + _targetInstances[_targetInstance.SelectedIndex].Name + " · " + _targetInstances[_targetInstance.SelectedIndex].VersionLabel : "尚未选择实例，请返回资源列表选择目标。";
        if (project.Kind == DownloadResourceKind.Modpack && string.IsNullOrWhiteSpace(_packName.Text)) _packName.Text = project.Title;
        _scroll.Offset = default;
        try
        {
            var versions = await _downloads.Catalog.GetResourceVersionsAsync(project.Id, _resourceGame.Text?.Trim(),
                project.Kind is DownloadResourceKind.Mod or DownloadResourceKind.Modpack ? ResourceLoaderIds[Math.Max(0, _resourceLoader.SelectedIndex)] : null,
                _downloads.Options, request.Token);
            if (_closed || request.IsCancellationRequested) return;
            _availableResourceVersions = versions;
            _resourceVersions.ItemsSource = versions.Select(x => x.Name + " · " + x.VersionNumber).ToArray();
            _resourceVersions.SelectedIndex = versions.Count > 0 ? 0 : -1;
            if (versions.Count == 0) _versionDetails.Text = "没有兼容版本，请调整版本或加载器筛选后重新搜索。";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!request.IsCancellationRequested) _versionDetails.Text = "版本获取失败，请重新选择项目重试：" + ex.Message; }
        finally { if (ReferenceEquals(request, _versionRequest)) _versionRequest = null; request.Dispose(); }
    }
    private ResourceVersion? SelectedResourceVersion() => _resourceVersions.SelectedIndex >= 0 && _resourceVersions.SelectedIndex < _availableResourceVersions.Count ? _availableResourceVersions[_resourceVersions.SelectedIndex] : null;
    private static string InstanceLoader(GameInstance instance) => instance.ModLoaders.Select(x => x.Name.ToLowerInvariant()).FirstOrDefault(ResourceLoaderIds.Contains) ?? "vanilla";
    private void QueueResource()
    {
        try
        {
            var project = _selectedProject ?? throw new InvalidOperationException("请先选择资源项目。");
            var version = SelectedResourceVersion() ?? throw new InvalidOperationException("请先选择资源版本。");
            ResourceInstallRequest request;
            if (project.Kind == DownloadResourceKind.Modpack)
                request = new(project, version, NewInstancePath(_packParent.Text, _packName.Text), InstanceName: _packName.Text!.Trim(), JavaPath: _instances.Catalog.GlobalSettings.JavaPath);
            else
            {
                if (_targetInstance.SelectedIndex < 0 || _targetInstance.SelectedIndex >= _targetInstances.Length) throw new InvalidOperationException("请先在实例管理中安装或导入一个实例。");
                var instance = _targetInstances[_targetInstance.SelectedIndex];
                if (_instances.IsBusy) throw new InvalidOperationException("实例正在运行或处理，请结束后安装资源。");
                var loader = InstanceLoader(instance);
                if (!version.GameVersions.Contains(instance.GameVersion)) throw new InvalidOperationException("资源版本与目标实例的 Minecraft 版本不兼容，请按目标版本重新搜索。");
                if (project.Kind == DownloadResourceKind.Mod && !version.Loaders.Contains(loader)) throw new InvalidOperationException("模组与目标实例的加载器不兼容，请按目标加载器重新搜索。");
                request = new(project, version, instance.EffectiveSettings.GameDirectory ?? instance.DefaultGameDirectory, instance.GameVersion, loader);
            }
            _downloads.QueueResource(request); Queued(project.Title);
        }
        catch (Exception ex) { ShowStatus(ex.Message, true); }
    }
}
