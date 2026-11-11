// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wonder.Core;

namespace Wonder.Desktop;

internal sealed partial class DownloadCenterWindow
{
    private TextBox _resourceQuery = null!, _resourceGame = null!, _packParent = null!, _packName = null!, _resourceFileDirectory = null!;
    private ComboBox _resourceLoader = null!, _targetInstance = null!, _detailTargetInstance = null!, _resourceDownloadMode = null!;
    private TextBlock _resourceStatus = null!, _projectTitle = null!, _projectDescription = null!, _projectAuthor = null!, _projectCoverage = null!,
        _versionDetails = null!, _installTargetDescription = null!, _selectedRelease = null!;
    private StackPanel _resourceResults = null!, _versionFamilies = null!, _versionGroups = null!;
    private Control _resourceDetails = null!, _packDestination = null!, _targetInstanceRow = null!, _detailTargetRow = null!, _resourceSearchPanel = null!, _resourcePaging = null!, _resourceFileDestination = null!;
    private Button _previousResources = null!, _nextResources = null!, _installResource = null!, _projectSource = null!, _retryVersions = null!;
    private ResourceIconView _projectIcon = null!;
    private CancellationTokenSource? _resourceRequest, _versionRequest;
    private ResourceProject? _selectedProject;
    private ResourceVersion? _selectedResourceVersion;
    private IReadOnlyList<ResourceVersionFamily> _resourceFamilies = [];
    private GameInstance[] _targetInstances = [];
    private int _offset;
    private bool _refreshingInstances, _filtersInitialized;
    private DownloadResourceKind? _configuredKind;
    private string[] _browseLoaders = [];
    private string? _defaultPackParent, _suggestedPackName;
    private Vector _browseOffset;
    private static readonly string[] ResourceLoaderIds = ["", "fabric", "quilt", "forge", "neoforge"];
    private static readonly string[] ShaderLoaderIds = ["", "iris", "optifine", "canvas"];
    private DownloadResourceKind ResourceKind => _page switch { 2 => DownloadResourceKind.Modpack, 3 => DownloadResourceKind.ResourcePack, 4 => DownloadResourceKind.Shader, _ => DownloadResourceKind.Mod };

    private Control BuildResourcesPage()
    {
        _resourceQuery = Input("搜索资源", "搜索项目名称或关键词");
        _resourceGame = Input("资源 Minecraft 版本", "留空查看全部版本");
        _resourceLoader = Choice("资源加载器筛选");
        _targetInstance = Choice("资源目标实例");
        _detailTargetInstance = Choice("详情目标实例");
        _targetInstance.SelectionChanged += (_, _) => TargetChanged(fromDetails: false);
        _detailTargetInstance.SelectionChanged += (_, _) => TargetChanged(fromDetails: true);
        _targetInstanceRow = Field("按实例筛选", _targetInstance);
        _detailTargetRow = Field("安装到实例", _detailTargetInstance);
        _defaultPackParent = DefaultGameInstallDirectory();
        _packParent = Input("整合包安装父目录", "选择游戏目录", _defaultPackParent);
        _packName = Input("整合包实例名称", "给整合包实例取个名字");
        _packDestination = Pair(Field("新实例名称", _packName), DirectoryField("游戏目录", _packParent), "1*,12,2*");

        var searchRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,Auto") };
        searchRow.Children.Add(_resourceQuery);
        var search = ActionButton("搜索 Modrinth", () => _ = SearchResourcesAsync(0));
        Grid.SetColumn(search, 2); searchRow.Children.Add(search);
        _resourceQuery.KeyDown += SearchOnEnter;
        _resourceGame.KeyDown += SearchOnEnter;
        _resourceStatus = PageStyles.Hint("输入关键词，或浏览热门资源。");
        _resourceResults = new StackPanel { Spacing = 10 };
        _previousResources = ActionButton("上一页资源", () => _ = SearchResourcesAsync(Math.Max(0, _offset - 20)));
        _nextResources = ActionButton("下一页资源", () => _ = SearchResourcesAsync(_offset + 20));
        _previousResources.IsEnabled = _nextResources.IsEnabled = false;
        _resourcePaging = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _previousResources, _nextResources } };
        _resourceSearchPanel = PageStyles.Card(Stack(_targetInstanceRow, searchRow, Field("Minecraft 版本筛选", _resourceGame), _resourceLoader, _resourceStatus));
        _resourceDetails = BuildResourceDetails();
        _resourceDetails.IsVisible = false;
        return Stack(_resourceSearchPanel, _resourceResults, _resourcePaging, _resourceDetails);
    }

    private void SearchOnEnter(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter) return;
        args.Handled = true;
        _ = SearchResourcesAsync(0);
    }

    private Control BuildResourceDetails()
    {
        var back = ActionButton("返回资源列表", () => ShowResourceResults(restoreScroll: true));
        _projectIcon = new ResourceIconView(72) { VerticalAlignment = VerticalAlignment.Top };
        _projectTitle = Heading("", 22);
        _projectDescription = PageStyles.Hint("");
        _projectAuthor = PageStyles.Hint("");
        _projectCoverage = PageStyles.Hint("");
        _projectSource = ActionButton("在 Modrinth 查看项目", () => _ = OpenResourceSourceAsync());
        _projectSource.HorizontalAlignment = HorizontalAlignment.Left;
        _projectAuthor.VerticalAlignment = VerticalAlignment.Center;
        var overviewText = Stack(_projectTitle, _projectDescription, Pair(_projectAuthor, _projectSource, "*,12,Auto"), _projectCoverage);
        var overview = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,16,*"), Children = { _projectIcon, overviewText } };
        Grid.SetColumn(overviewText, 2);

        _installTargetDescription = PageStyles.Hint("");
        _selectedRelease = PageStyles.Hint("");
        _installResource = ActionButton("安装所选资源", QueueResource);
        _installResource.IsEnabled = false;
        _installResource.HorizontalAlignment = HorizontalAlignment.Left;
        _resourceDownloadMode = Choice("资源下载方式");
        _resourceDownloadMode.SelectionChanged += (_, _) => UpdateResourceInstall();
        _resourceFileDirectory = Input("资源文件保存目录", "输入完整路径或选择目录");
        _resourceFileDirectory.TextChanged += (_, _) => UpdateResourceInstall();
        _resourceFileDestination = DirectoryField("文件保存目录", _resourceFileDirectory);
        var destination = new Grid { Children = { _detailTargetRow, _packDestination, _resourceFileDestination } };
        _selectedRelease.VerticalAlignment = VerticalAlignment.Center;

        _versionDetails = PageStyles.Hint("");
        _versionFamilies = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _versionGroups = new StackPanel { Spacing = 8 };
        var families = new ScrollViewer { Content = _versionFamilies, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetName(families, "资源大版本");
        _retryVersions = ActionButton("重试获取发布包", () => { if (_selectedProject is { } selected) _ = SelectProjectAsync(selected); });
        _retryVersions.IsVisible = false;
        return Stack(PageStyles.Card(Stack(back, overview)),
            PageStyles.Card(Stack(Heading("下载"), Pair(Field("下载方式", _resourceDownloadMode), destination, "190,12,*"),
                _installTargetDescription, Pair(_selectedRelease, _installResource, "*,12,Auto"))),
            PageStyles.Card(Stack(Heading("版本选择"), _versionDetails, _retryVersions, families, _versionGroups)));
    }

    private static Grid Pair(Control left, Control right, string columns)
    {
        Grid.SetColumn(right, 2);
        return new Grid { ColumnDefinitions = new ColumnDefinitions(columns), Children = { left, right } };
    }

    private async Task OpenResourceSourceAsync()
    {
        if (!Uri.TryCreate(_selectedProject?.PageUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "modrinth.com" || uri.UserInfo.Length != 0) return;
        try
        {
            if (!await Launcher.LaunchUriAsync(uri)) ShowStatus("无法打开 Modrinth 项目页面。", true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException) { ShowStatus("无法打开 Modrinth 项目页面。", true); }
    }

    private void ShowResourceResults(bool restoreScroll = false)
    {
        _versionRequest?.Cancel(); _selectedProject = null;
        _resourceDetails.IsVisible = false; _resourceSearchPanel.IsVisible = _resourceResults.IsVisible = _resourcePaging.IsVisible = true;
        _projectIcon.SetProject(null);
        _versionFamilies.Children.Clear();
        _versionGroups.Children.Clear();
        _resourceFamilies = [];
        if (restoreScroll)
        {
            Dispatcher.UIThread.Post(() => { if (!_closed && !_resourceDetails.IsVisible) _scroll.Offset = _browseOffset; }, DispatcherPriority.Loaded);
        }
        else _scroll.Offset = default;
    }

    private GameInstance? SelectedResourceTarget => _targetInstance.SelectedIndex >= 0 && _targetInstance.SelectedIndex < _targetInstances.Length
        ? _targetInstances[_targetInstance.SelectedIndex] : null;

    private void TargetChanged(bool fromDetails)
    {
        if (_refreshingInstances) return;
        _refreshingInstances = true;
        try
        {
            if (fromDetails) _targetInstance.SelectedIndex = _detailTargetInstance.SelectedIndex;
            else _detailTargetInstance.SelectedIndex = _targetInstance.SelectedIndex;
        }
        finally { _refreshingInstances = false; }
        if (SelectedResourceTarget is { } instance)
        {
            _resourceGame.Text = instance.GameVersion;
            SelectLoaderForTarget(instance);
        }
        if (fromDetails)
        {
            FocusResourceVersions();
            UpdateResourceInstall();
        }
        else if (_page is 1 or 3 or 4) _ = SearchResourcesAsync(0);
    }

    private void RefreshInstances()
    {
        var defaultPackParent = DefaultGameInstallDirectory();
        if (_packParent.Text == _defaultPackParent) _packParent.Text = defaultPackParent;
        _defaultPackParent = defaultPackParent;
        var previous = SelectedResourceTarget?.Id;
        var catalog = _instances.Catalog;
        _refreshingInstances = true;
        try
        {
            _targetInstances = catalog.Instances.ToArray();
            var names = _targetInstances.Select(x => x.Name + " · " + x.VersionLabel).ToArray();
            _targetInstance.ItemsSource = names;
            _detailTargetInstance.ItemsSource = names;
            var index = Array.FindIndex(_targetInstances, x => x.Id == (previous ?? catalog.SelectedInstanceId));
            _targetInstance.SelectedIndex = _detailTargetInstance.SelectedIndex = index >= 0 ? index : _targetInstances.Length > 0 ? 0 : -1;
            _targetInstance.IsEnabled = _detailTargetInstance.IsEnabled = _targetInstances.Length > 0;
        }
        finally { _refreshingInstances = false; }
        if (!_filtersInitialized && SelectedResourceTarget is { } instance)
        {
            _filtersInitialized = true;
            _resourceGame.Text = instance.GameVersion;
            SelectLoaderForTarget(instance);
        }
        if (_selectedProject is not null)
        {
            if (previous != SelectedResourceTarget?.Id) FocusResourceVersions();
            UpdateResourceInstall();
        }
    }

    private void ConfigureResourceFilters(DownloadResourceKind kind)
    {
        _resourceLoader.IsVisible = kind != DownloadResourceKind.ResourcePack;
        if (_configuredKind == kind) return;
        var previous = ActiveLoaderFilter();
        var shader = kind == DownloadResourceKind.Shader;
        _configuredKind = kind;
        _browseLoaders = shader ? ShaderLoaderIds : ResourceLoaderIds;
        _resourceLoader.ItemsSource = _browseLoaders.Select(id => id.Length == 0 ? "全部加载器" : LoaderLabel(id)).ToArray();
        _resourceLoader.SelectedIndex = Math.Max(0, Array.IndexOf(_browseLoaders, previous));
        if (SelectedResourceTarget is { } target && _resourceLoader.SelectedIndex == 0 && kind == DownloadResourceKind.Mod)
            SelectLoaderForTarget(target);
    }

    private string ActiveLoaderFilter() => _resourceLoader.SelectedIndex >= 0 && _resourceLoader.SelectedIndex < _browseLoaders.Length ? _browseLoaders[_resourceLoader.SelectedIndex] : "";

    private void SelectLoaderForTarget(GameInstance instance)
    {
        var loader = ResourceKind == DownloadResourceKind.Shader
            ? (instance.ModLoaders.Any(x => x.Name.Equals("OptiFine", StringComparison.OrdinalIgnoreCase)) ? "optifine" : "")
            : InstanceLoader(instance);
        _resourceLoader.SelectedIndex = Math.Max(0, Array.IndexOf(_browseLoaders, loader));
    }

    private async Task SearchResourcesAsync(int offset)
    {
        if (_closed || _page is < 1 or > 4) return;
        _resourceRequest?.Cancel();
        var request = _resourceRequest = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var kind = ResourceKind;
        ConfigureResourceFilters(kind);
        ShowResourceResults(); _resourceResults.Children.Clear();
        _previousResources.IsEnabled = _nextResources.IsEnabled = false;
        _resourceStatus.Text = "正在搜索 Modrinth…";
        try
        {
            var result = await _downloads.Catalog.SearchResourcesAsync(kind, _resourceQuery.Text?.Trim() ?? "", _resourceGame.Text?.Trim(),
                kind == DownloadResourceKind.ResourcePack ? null : ActiveLoaderFilter(), offset, _downloads.Options, request.Token);
            if (_closed || request.IsCancellationRequested || _page is < 1 or > 4 || ResourceKind != kind) return;
            _offset = result.Offset;
            _resourceStatus.Text = result.Projects.Count == 0 ? "没有找到匹配资源。试试其他关键词、版本或加载器。" :
                $"共 {result.Total:N0} 项 · 第 {result.Offset + 1}–{result.Offset + result.Projects.Count} 项";
            foreach (var project in result.Projects) _resourceResults.Children.Add(CreateResourceCard(project));
            _previousResources.IsEnabled = result.Offset > 0;
            _nextResources.IsEnabled = result.Offset + result.Projects.Count < result.Total;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!request.IsCancellationRequested) _resourceStatus.Text = "搜索失败，可点击搜索重试：" + ex.Message; }
        finally { if (ReferenceEquals(_resourceRequest, request)) _resourceRequest = null; request.Dispose(); }
    }

    private Button CreateResourceCard(ResourceProject project)
    {
        var icon = new ResourceIconView(52);
        icon.SetProject(project);
        var button = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12) };
        var title = Heading(project.Title, 16);
        title.MaxLines = 1; title.TextTrimming = TextTrimming.CharacterEllipsis;
        var description = PageStyles.Hint(project.Description);
        description.MaxLines = 2; description.TextTrimming = TextTrimming.CharacterEllipsis;
        var summary = PageStyles.Hint((string.IsNullOrWhiteSpace(project.Author) ? "" : project.Author + " · ") + $"{project.Downloads:N0} 次下载");
        var text = new StackPanel { Spacing = 4, Children = { title, description, summary } };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*,20"), Children = { icon, text } };
        Grid.SetColumn(text, 2);
        var arrow = new TextBlock { Text = "›", FontSize = 22, Foreground = Palette.Accent, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(arrow, 3); row.Children.Add(arrow);
        button.Content = row;
        AutomationProperties.SetName(button, "资源项目·" + project.Id);
        button.Click += (_, _) => _ = SelectProjectAsync(project);
        return button;
    }

    private async Task SelectProjectAsync(ResourceProject project)
    {
        _versionRequest?.Cancel();
        var request = _versionRequest = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var opening = !_resourceDetails.IsVisible || _selectedProject?.Id != project.Id;
        if (!_resourceDetails.IsVisible) _browseOffset = _scroll.Offset;
        _selectedProject = project;
        _selectedResourceVersion = null;
        _resourceFamilies = [];
        _versionFamilies.Children.Clear(); _versionGroups.Children.Clear();
        _retryVersions.IsVisible = false;
        _resourceDetails.IsVisible = true;
        _resourceSearchPanel.IsVisible = _resourceResults.IsVisible = _resourcePaging.IsVisible = false;
        if (opening)
        {
            _resourceDownloadMode.ItemsSource = new[] { project.Kind == DownloadResourceKind.Modpack ? "新建整合包实例" : "安装到实例", "仅下载文件" };
            _resourceDownloadMode.SelectedIndex = 0;
        }
        if (project.Kind == DownloadResourceKind.Modpack)
        {
            var suggestedName = string.Concat(project.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' ? '-' : c)).Trim().TrimEnd('.');
            if (string.IsNullOrWhiteSpace(_packName.Text) || _packName.Text == _suggestedPackName) _packName.Text = suggestedName;
            _suggestedPackName = suggestedName;
        }
        PaintProject(project);
        _projectCoverage.Text = "适配版本 · 正在读取…";
        _versionDetails.Text = "正在获取发布包…";
        UpdateResourceInstall();
        _scroll.Offset = default;
        var overview = LoadProjectOverviewAsync(project, request.Token);
        try
        {
            // Keep every release available for browsing. The instance and search filters choose the starting group.
            var versions = await _downloads.Catalog.GetResourceVersionsAsync(project.Id, null, null, _downloads.Options, request.Token);
            if (_closed || request.IsCancellationRequested) return;
            _resourceFamilies = ResourceVersionGroups.Create(versions);
            _projectCoverage.Text = "适配版本 · " + ResourceVersionGroups.DescribeCoverage(_resourceFamilies);
            _versionDetails.Text = versions.Count == 0 ? "该项目没有可选发布包。" : $"{versions.Count:N0} 个发布包";
            FocusResourceVersions();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!request.IsCancellationRequested)
            {
                _projectCoverage.Text = "适配版本 · 暂不可用";
                _versionDetails.Text = "版本获取失败：" + ex.Message;
                _retryVersions.IsVisible = true;
            }
        }
        finally
        {
            await overview;
            if (ReferenceEquals(_versionRequest, request)) _versionRequest = null;
            request.Dispose();
        }
    }

    private async Task LoadProjectOverviewAsync(ResourceProject project, CancellationToken ct)
    {
        try
        {
            var details = await _downloads.Catalog.GetResourceProjectAsync(project, _downloads.Options, ct);
            if (_closed || ct.IsCancellationRequested) return;
            _selectedProject = details;
            PaintProject(details);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Search results already contain the overview shown when metadata is unavailable. */ }
    }

    private void PaintProject(ResourceProject project)
    {
        _projectTitle.Text = project.Title;
        _projectDescription.Text = project.Description;
        _projectAuthor.Text = "作者 · " + (string.IsNullOrWhiteSpace(project.Author) ? "未标注" : project.Author) + $"    来源 · Modrinth    {project.Downloads:N0} 次下载";
        _projectSource.IsVisible = !string.IsNullOrWhiteSpace(project.PageUrl);
        _projectIcon.SetProject(project);
    }

    private void FocusResourceVersions()
    {
        var project = _selectedProject;
        if (project is null || _resourceFamilies.Count == 0) return;
        var instance = project.Kind == DownloadResourceKind.Modpack ? null : SelectedResourceTarget;
        var gameVersion = string.IsNullOrWhiteSpace(_resourceGame.Text) ? instance?.GameVersion : _resourceGame.Text.Trim();
        var loader = ActiveLoaderFilter();
        if (loader.Length == 0 && instance is not null)
        {
            loader = project.Kind switch
            {
                DownloadResourceKind.Mod => InstanceLoader(instance),
                DownloadResourceKind.Shader when instance.ModLoaders.Any(x => x.Name.Equals("OptiFine", StringComparison.OrdinalIgnoreCase)) => "optifine",
                DownloadResourceKind.Shader when InstanceLoader(instance) is "fabric" or "quilt" => "iris",
                DownloadResourceKind.ResourcePack => "minecraft",
                _ => ""
            };
        }
        var (family, group) = ResourceVersionGroups.Focus(_resourceFamilies, gameVersion, loader);
        if (family is null) return;
        _selectedResourceVersion = group?.Releases.FirstOrDefault(version => CanInstallVersion(version, project, instance, ResourceDownloadOnly)) ?? group?.Releases.FirstOrDefault();
        RenderResourceFamilies(family, group);
        UpdateResourceInstall();
    }

    private void RenderResourceFamilies(ResourceVersionFamily active, ResourceVersionGroup? expanded)
    {
        _versionFamilies.Children.Clear();
        foreach (var family in _resourceFamilies)
        {
            var button = new ToggleButton { Content = family.Name, MinHeight = 34, IsChecked = family == active };
            AutomationProperties.SetName(button, "资源大版本·" + family.Name);
            button.Click += (_, _) => RenderResourceFamilies(family, family == active ? expanded : null);
            _versionFamilies.Children.Add(button);
        }
        _versionGroups.Children.Clear();
        foreach (var group in active.Groups)
        {
            var title = $"{group.MinecraftVersion} · {LoaderLabel(group.Loader)}";
            var content = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 4) };
            var fold = new Expander { Header = title + $"  ({group.Releases.Count:N0})", Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(fold, "资源小版本·" + title);
            fold.PropertyChanged += (_, args) =>
            {
                if (args.Property == Expander.IsExpandedProperty && fold.IsExpanded && content.Children.Count == 0)
                    foreach (var version in group.Releases) content.Children.Add(CreateReleaseButton(version));
            };
            _versionGroups.Children.Add(fold);
            fold.IsExpanded = group == expanded;
        }
    }

    private Button CreateReleaseButton(ResourceVersion version)
    {
        var (symbol, channel) = ReleaseChannel(version.ReleaseType);
        var button = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 8) };
        var label = new TextBlock { Text = $"{symbol}  {channel}", Foreground = version.ReleaseType is "beta" or "alpha" ? Palette.Error : Palette.Accent };
        var name = Heading(version.Name == version.VersionNumber ? version.Name : version.Name + " · " + version.VersionNumber, 14);
        var date = version.PublishedAt?.ToLocalTime().ToString("yyyy-MM-dd") ?? "";
        var details = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*,Auto"), Children = { label, name } };
        Grid.SetColumn(name, 1);
        var time = PageStyles.Hint(date); Grid.SetColumn(time, 2); details.Children.Add(time);
        button.Content = details;
        button.Classes.Set("resource-release", true);
        button.Background = _selectedResourceVersion?.Id == version.Id ? Palette.Active : Palette.Surface;
        AutomationProperties.SetName(button, "资源发布包·" + version.Id);
        button.Click += (_, _) =>
        {
            _selectedResourceVersion = version;
            foreach (var release in _versionGroups.GetVisualDescendants().OfType<Button>().Where(x => x.Classes.Contains("resource-release")))
                release.Background = AutomationProperties.GetName(release) == "资源发布包·" + version.Id ? Palette.Active : Palette.Surface;
            UpdateResourceInstall();
        };
        return button;
    }

    private void UpdateResourceInstall()
    {
        var project = _selectedProject;
        var version = _selectedResourceVersion;
        var instance = SelectedResourceTarget;
        if (project is null) return;
        var onlyFiles = ResourceDownloadOnly;
        var pack = project.Kind == DownloadResourceKind.Modpack;
        _detailTargetRow.IsVisible = !onlyFiles && !pack;
        _packDestination.IsVisible = !onlyFiles && pack;
        _resourceFileDestination.IsVisible = onlyFiles;
        _installTargetDescription.Text = onlyFiles && pack ? "保存原始 .mrpack 文件。" : !onlyFiles && !pack && instance is null ? "请先安装或导入一个游戏实例。" : "";
        _installTargetDescription.IsVisible = _installTargetDescription.Text.Length > 0;
        var compatible = version is not null && CanInstallVersion(version, project, instance, onlyFiles);
        _selectedRelease.Text = version is null ? "从下方选择发布包。" :
            ReleaseChannel(version.ReleaseType).Channel + " · " + version.VersionNumber + (compatible ? "" : " · 与当前目标不兼容");
        _selectedRelease.Foreground = version is not null && !compatible ? Palette.Error : Palette.Muted;
        var action = onlyFiles ? "下载所选文件" : "安装所选资源";
        _installResource.Content = action;
        AutomationProperties.SetName(_installResource, action);
        _installResource.IsEnabled = compatible && (!onlyFiles || (_resourceFileDirectory.Text is { } directory && Path.IsPathFullyQualified(directory.Trim())));
    }

    private bool ResourceDownloadOnly => _resourceDownloadMode.SelectedIndex == 1;

    private static bool CanInstallVersion(ResourceVersion version, ResourceProject project, GameInstance? instance, bool downloadOnly = false) =>
        version.Files.Count > 0 && (downloadOnly || project.Kind == DownloadResourceKind.Modpack || instance is not null && version.GameVersions.Contains(instance.GameVersion) &&
            (project.Kind != DownloadResourceKind.Mod || InstanceLoader(instance) != "vanilla" && version.Loaders.Contains(InstanceLoader(instance), StringComparer.OrdinalIgnoreCase)));

    private static (string Symbol, string Channel) ReleaseChannel(string? type) => type switch
    {
        "release" => ("●", "稳定版"), "beta" => ("▲", "测试版"), "alpha" => ("◆", "早期版"), _ => ("◇", "其他版本")
    };

    private static string LoaderLabel(string id) => id switch
    {
        "fabric" => "Fabric", "quilt" => "Quilt", "forge" => "Forge", "neoforge" => "NeoForge",
        "optifine" => "OptiFine", "iris" => "Iris", "canvas" => "Canvas", "minecraft" or "vanilla" => "Minecraft", "" => "未标注加载器", _ => id
    };

    private static string InstanceLoader(GameInstance instance) => instance.ModLoaders.Select(x => x.Name.ToLowerInvariant()).FirstOrDefault(ResourceLoaderIds.Contains) ?? "vanilla";

    private void QueueResource()
    {
        try
        {
            var project = _selectedProject ?? throw new InvalidOperationException("请先选择资源项目。");
            var version = _selectedResourceVersion ?? throw new InvalidOperationException("请先选择资源版本。");
            ResourceInstallRequest request;
            if (ResourceDownloadOnly)
            {
                var directory = _resourceFileDirectory.Text?.Trim();
                if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory)) throw new ArgumentException("请选择完整的文件保存目录。");
                request = new(project, version, Path.GetFullPath(directory)) { DownloadOnly = true };
            }
            else if (project.Kind == DownloadResourceKind.Modpack)
                request = new(project, version, NewGameInstallPath(_packParent.Text, _packName.Text), InstanceName: _packName.Text!.Trim(), JavaPath: _instances.Catalog.GlobalSettings.JavaPath) { InstallInCollection = true };
            else
            {
                var instance = SelectedResourceTarget ?? throw new InvalidOperationException("请先在实例管理中安装或导入一个实例。");
                if (_instances.IsBusy) throw new InvalidOperationException("实例正在运行或处理，请结束后安装资源。");
                if (!CanInstallVersion(version, project, instance)) throw new InvalidOperationException("发布包不兼容当前目标实例。");
                request = new(project, version, instance.EffectiveSettings.GameDirectory ?? instance.DefaultGameDirectory, instance.GameVersion, InstanceLoader(instance));
            }
            _downloads.QueueResource(request); Queued(project.Title);
        }
        catch (Exception ex) { ShowStatus(ex.Message, true); }
    }
}
