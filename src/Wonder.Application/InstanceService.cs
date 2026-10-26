// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Core;

namespace Wonder.Application;

public sealed partial class InstanceService
{
    private readonly object _gate = new();
    private readonly IInstanceStore _store;
    private readonly IInstanceDiscovery _discovery;
    private readonly IGameLauncher _launcher;
    private readonly string _cacheDirectory;
    private readonly ILaunchAccountProvider? _accounts;
    private CredentialRedactor _launchRedactor = new([]);
    private InstanceCatalog _catalog = new();
    private readonly List<string> _logs = [];
    private bool _loadFailed, _operation;
    private string? _launchId;
    private CancellationTokenSource? _preparation;
    public event EventHandler? Changed;
    public string? Error { get; private set; }
    public string StatusMessage { get; private set; } = "选择已有实例开始。";
    public LaunchPhase LaunchPhase { get; private set; }
    public bool IsBusy { get { lock (_gate) return _operation || _launchId is not null; } }
    public bool IsReadBlocked { get { lock (_gate) return _loadFailed; } }
    public InstanceCatalog Catalog { get { lock (_gate) { var result = Copy(_catalog); result.Instances = result.Instances.Select(x => x with { GlobalSettings = result.GlobalSettings }).ToList(); return result; } } }
    public GameInstance? SelectedInstance { get { lock (_gate) return _catalog.Instances.FirstOrDefault(x => x.Id == _catalog.SelectedInstanceId) is { } item ? Copy(item) with { GlobalSettings = _catalog.GlobalSettings } : null; } }
    public IReadOnlyList<string> LogLines { get { lock (_gate) return _logs.ToArray(); } }

    public InstanceService(IInstanceStore store, IInstanceDiscovery discovery, IGameLauncher launcher, string cacheDirectory, IInstanceFileOperations? fileOperations = null, ILaunchAccountProvider? accounts = null)
    { _store = store; _discovery = discovery; _launcher = launcher; _cacheDirectory = cacheDirectory; _files = fileOperations; _accounts = accounts; Reload(); }

    public void Reload()
    {
        lock (_gate)
        {
            if (IsBusy) return;
            try { var loaded = _store.Load(); loaded.Validate(); _catalog = Copy(loaded); _loadFailed = false; Error = null; }
            catch (Exception ex) { _loadFailed = true; Error = "实例索引读取失败，原文件已保留；修复后重试读取。\n" + ex.Message; }
        }
        Notify();
    }

    public Task ImportAsync(string path) => Discover(path, null);
    public Task RefreshAsync(string id)
    {
        var item = Catalog.Instances.FirstOrDefault(x => x.Id == id);
        return item is null ? Task.CompletedTask : Discover(item.SourcePath, id);
    }

    private async Task Discover(string path, string? onlyId)
    {
        lock (_gate)
        {
            if (_loadFailed || IsBusy) return;
            _operation = true; Error = null; StatusMessage = "正在读取实例配置…";
        }
        Notify();
        try
        {
            var found = await _discovery.DiscoverAsync(path);
            var original = onlyId is null ? null : Catalog.Instances.FirstOrDefault(x => x.Id == onlyId);
            var candidates = found.Instances.Where(x => onlyId is null || x.Id == onlyId || original is not null && SameSource(x, original)).ToArray();
            if (candidates.Length == 0) throw new InvalidDataException("没有发现可登记的实例。" + string.Join("\n", found.Diagnostics));
            Commit(catalog =>
            {
                foreach (var candidate in candidates)
                {
                    if (!catalog.GameDirectories.Any(path => Same(path, candidate.RootDirectory))) catalog.GameDirectories.Add(Full(candidate.RootDirectory));
                    var index = catalog.Instances.FindIndex(x => x.Id == candidate.Id || SameSource(x, candidate));
                    if (index < 0) catalog.Instances.Add(Copy(candidate));
                    else
                    {
                        var previous = catalog.Instances[index];
                        var refreshed = Copy(candidate) with { Id = previous.Id, Name = previous.Name, Overrides = previous.Overrides, PreviousRootDirectories = [.. previous.PreviousRootDirectories] };
                        foreach (var oldRoot in previous.PreviousRootDirectories) refreshed = RebaseImportedSettings(refreshed, oldRoot, refreshed.RootDirectory);
                        catalog.Instances[index] = refreshed;
                    }
                }
                catalog.SelectedInstanceId ??= catalog.Instances.First(x => x.Id == candidates[0].Id || SameSource(x, candidates[0])).Id;
            });
            if (Error is null) StatusMessage = $"已读取 {candidates.Length} 个实例。" + (found.Diagnostics.Count > 0 ? "\n" + string.Join("\n", found.Diagnostics) : "");
        }
        catch (Exception ex) { Error = "实例读取失败：" + ex.Message; }
        finally { lock (_gate) _operation = false; Notify(); }
    }

    public void Select(string id) => Commit(c => { Require(c, id); c.SelectedInstanceId = id; });
    public void Rename(string id, string name) => Commit(c => Replace(c, id, x => x with { Name = name.Trim() }));
    public void UpdateSettings(string id, InstanceSettings overrides) => Commit(c => Replace(c, id, x => x with { Overrides = overrides }));
    public void UpdateInstance(string id, string name, InstanceSettings overrides) => Commit(c => Replace(c, id, x => x with { Name = name.Trim(), Overrides = overrides }));
    public void SetOfflineUsername(string username) => Commit(c => c.OfflineUsername = username.Trim());
    public void UpdateGlobalSettings(InstanceSettings settings) => Commit(c => c.GlobalSettings = settings);
    public void ResetGlobalSettings() => UpdateGlobalSettings(new());
    public void Remove(string id) => Commit(c =>
    {
        if (id == _launchId) throw new InvalidOperationException("实例正在启动或运行，暂不能移除登记。");
        Require(c, id); c.Instances.RemoveAll(x => x.Id == id);
        if (c.SelectedInstanceId == id) c.SelectedInstanceId = c.Instances.FirstOrDefault()?.Id;
    });

    private void Commit(Action<InstanceCatalog> change)
    {
        lock (_gate)
        {
            if (_loadFailed) return;
            if (_fileOperation) { Error = "文件操作进行中，请稍后再修改实例。"; return; }
            try
            {
                var next = Copy(_catalog); change(next); next.Validate();
                _store.Save(Copy(next)); _catalog = next; Error = null;
            }
            catch (Exception ex) { Error = "修改未保存，保留原设置：" + ex.Message; }
        }
        Notify();
    }

    public async Task LaunchSelectedAsync()
    {
        LaunchRequest request;
        CancellationTokenSource preparation;
        lock (_gate)
        {
            if (_loadFailed || IsBusy) return;
            var instance = SelectedInstance;
            if (instance is null) { Error = "请先选择一个实例。"; Notify(); return; }
            request = new(instance, _catalog.OfflineUsername, _cacheDirectory);
            preparation = _preparation = new(); _launchId = instance.Id;
            _logs.Clear(); _launchRedactor = new([]); Error = null; LaunchPhase = LaunchPhase.Preparing;
            StatusMessage = "正在检查 " + instance.Name + "…";
        }
        Notify();
        try
        {
            if (_accounts is not null)
            {
                var session = await _accounts.GetSelectedSessionAsync(preparation.Token);
                _launchRedactor = new([session.AccessToken]);
                request = request with { Session = session };
            }
            preparation.Token.ThrowIfCancellationRequested();
            var plan = await _launcher.BuildPlanAsync(request, preparation.Token);
            foreach (var notice in plan.Notices) AddLog(notice);
            preparation.Token.ThrowIfCancellationRequested();
            var exit = await _launcher.RunAsync(plan, () =>
            {
                var kind = request.Session?.Kind switch { AccountKind.Microsoft => "正版", AccountKind.Yggdrasil => "第三方", _ => "离线" };
                LaunchPhase = LaunchPhase.Running; StatusMessage = request.Instance.Name + " · 游戏运行中（" + kind + "）"; Notify();
            }, AddLog, CancellationToken.None);
            LaunchPhase = exit == 0 ? LaunchPhase.Exited : LaunchPhase.Failed;
            StatusMessage = request.Instance.Name + $" · 已退出（代码 {exit}）";
            if (exit != 0) Error = "游戏异常退出，请查看启动日志。";
        }
        catch (OperationCanceledException) { LaunchPhase = LaunchPhase.Idle; StatusMessage = "已取消启动准备。"; }
        catch (Exception ex) { LaunchPhase = LaunchPhase.Failed; Error = "启动失败：" + _launchRedactor.Redact(ex.Message); StatusMessage = "启动失败"; }
        finally
        {
            lock (_gate) { _launchId = null; _preparation = null; }
            preparation.Dispose(); Notify();
        }
    }

    public void CancelPreparation()
    { lock (_gate) if (LaunchPhase == LaunchPhase.Preparing) _preparation?.Cancel(); }

    private void AddLog(string line)
    {
        lock (_gate)
        {
            line = _launchRedactor.Redact(line);
            _logs.Add(line.Length > 2048 ? line[..2048] : line);
            if (_logs.Count > 200) _logs.RemoveAt(0);
        }
        Notify();
    }
    private void Notify()
    {
        if (Changed is not { } handlers) return;
        foreach (EventHandler handler in handlers.GetInvocationList())
            try { handler(this, EventArgs.Empty); } catch { }
    }
    private static GameInstance Require(InstanceCatalog c, string id) => c.Instances.FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException("实例不存在。");
    private static void Replace(InstanceCatalog c, string id, Func<GameInstance, GameInstance> update)
    { var current = Require(c, id); c.Instances[c.Instances.IndexOf(current)] = update(current); }
    private static GameInstance Copy(GameInstance item) => item with { MetadataPaths = [.. item.MetadataPaths], Warnings = [.. item.Warnings], ModLoaders = [.. item.ModLoaders], PreviousRootDirectories = [.. item.PreviousRootDirectories], GlobalSettings = new() };
    private static InstanceCatalog Copy(InstanceCatalog c) => new()
    {
        Version = c.Version, Instances = c.Instances.Select(Copy).ToList(),
        GameDirectories = c.GameDirectories.Concat(c.Instances.Select(x => x.RootDirectory)).Select(Full).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        SelectedInstanceId = c.SelectedInstanceId, OfflineUsername = c.OfflineUsername, GlobalSettings = c.GlobalSettings
    };
}
