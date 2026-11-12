// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Core;

namespace Wonder.Application;

public sealed partial class InstanceService
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _discoveryGate = new(1);
    private readonly SemaphoreSlim _catalogWriter = new(1);
    private readonly IInstanceStore _store;
    private readonly IInstanceDiscovery _discovery;
    private readonly IGameLauncher _launcher;
    private readonly string _cacheDirectory;
    private readonly ILaunchAccountProvider? _accounts;
    private readonly Func<LaunchCrashReport, Task>? _offerCrashReport;
    private InstanceCatalog _catalog = new();
    private readonly List<string> _logs = [];
    private bool _loadFailed, _operation, _pendingLoad;
    private int _pendingSaves;
    private string? _launchId;
    private string? _browseDirectory;
    private CancellationTokenSource? _preparation;
    public event EventHandler? Changed;
    public string? Error { get; private set; }
    public string StatusMessage { get; private set; } = "选择已有实例开始。";
    public LaunchPhase LaunchPhase { get; private set; }
    public bool IsBusy { get { lock (_gate) return _operation || _pendingLoad || _pendingSaves != 0 || _launchId is not null; } }
    public bool IsReadBlocked { get { lock (_gate) return _loadFailed; } }
    public bool IsLoading { get { lock (_gate) return _pendingLoad; } }
    public InstanceCatalog Catalog { get { lock (_gate) { var result = Copy(_catalog); result.Instances = result.Instances.Select(x => x with { GlobalSettings = result.GlobalSettings }).ToList(); return result; } } }
    public GameInstance? SelectedInstance { get { lock (_gate) return _catalog.Instances.FirstOrDefault(x => x.Id == _catalog.SelectedInstanceId) is { } item ? Copy(item) with { GlobalSettings = _catalog.GlobalSettings } : null; } }
    public string? SelectedGameDirectory
    {
        get
        {
            lock (_gate)
                return _browseDirectory is { } current && _catalog.GameDirectories.Any(path => Same(path, current))
                    ? current : _catalog.Instances.FirstOrDefault(x => x.Id == _catalog.SelectedInstanceId)?.GameDirectoryGroup ?? _catalog.GameDirectories.FirstOrDefault();
        }
    }
    public IReadOnlyList<string> LogLines { get { lock (_gate) return _logs.ToArray(); } }

    public InstanceService(IInstanceStore store, IInstanceDiscovery discovery, IGameLauncher launcher, string cacheDirectory,
        IInstanceFileOperations? fileOperations = null, ILaunchAccountProvider? accounts = null, Func<LaunchCrashReport, Task>? offerCrashReport = null,
        bool loadImmediately = true)
    {
        _store = store; _discovery = discovery; _launcher = launcher; _cacheDirectory = cacheDirectory;
        _files = fileOperations; _accounts = accounts; _offerCrashReport = offerCrashReport;
        _pendingLoad = true;
        if (loadImmediately) Reload();
    }

    public async Task ReloadAsync()
    {
        await _catalogWriter.WaitAsync().ConfigureAwait(false);
        try { await Task.Run(ReloadCatalog).ConfigureAwait(false); }
        finally { _catalogWriter.Release(); Notify(); }
    }

    public void Reload()
    {
        lock (_gate) if (_operation || _launchId is not null) return;
        _catalogWriter.Wait();
        try { ReloadCatalog(); }
        finally { _catalogWriter.Release(); Notify(); }
    }

    private void ReloadCatalog()
    {
        lock (_gate)
        {
            if (_operation || _launchId is not null) return;
            _pendingLoad = true;
        }
        try
        {
            var loaded = _store.Load(); loaded.Validate();
            lock (_gate) { _catalog = Copy(loaded); _loadFailed = false; Error = null; }
        }
        catch (Exception ex)
        {
            LauncherDiagnostics.Warn("ReadInstanceCatalog", ex);
            lock (_gate) { _loadFailed = true; Error = "实例索引读取失败，原文件已保留；修复后重试读取。\n" + ex.Message; }
        }
        finally { lock (_gate) _pendingLoad = false; }
    }

    public async Task WaitForPersistenceAsync()
    {
        await _discoveryGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _catalogWriter.WaitAsync().ConfigureAwait(false);
            _catalogWriter.Release();
        }
        finally { _discoveryGate.Release(); }
    }

    public Task ImportAsync(string path) => Discover(path, null);
    public Task ImportInstalledAsync(string path) => Discover(path, null, allowRunning: true);
    public Task ReadRegisteredDirectoriesAsync() => DiscoverDirectories(Catalog.GameDirectories, null, allowRunning: true, automatic: true);
    public Task ReadGameDirectoryAsync(string path) => DiscoverDirectories([path], null, allowRunning: true, automatic: true);
    public Task RefreshAsync(string id)
    {
        var item = Catalog.Instances.FirstOrDefault(x => x.Id == id);
        return item is null ? Task.CompletedTask : Discover(item.SourcePath, id);
    }

    private Task Discover(string path, string? onlyId, bool allowRunning = false) =>
        DiscoverDirectories([path], onlyId, allowRunning, automatic: false);

    private async Task DiscoverDirectories(IReadOnlyList<string> paths, string? onlyId, bool allowRunning, bool automatic)
    {
        if (paths.Count == 0) return;
        await _discoveryGate.WaitAsync().ConfigureAwait(false);
        var started = false;
        var writerHeld = false;
        var showDiscoveryStatus = false;
        try
        {
            while (!started)
            {
                var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void ChangedWhileBusy(object? _, EventArgs args) => finished.TrySetResult();
                lock (_gate)
                {
                    if (_loadFailed || _pendingLoad || (!allowRunning && (_operation || _launchId is not null))) return;
                    if (!_operation)
                    {
                        started = _operation = true; Error = null;
                        showDiscoveryStatus = _launchId is null;
                        if (showDiscoveryStatus) StatusMessage = "正在读取实例配置…";
                    }
                    else Changed += ChangedWhileBusy;
                }
                if (!started)
                {
                    try { await finished.Task.ConfigureAwait(false); }
                    finally { Changed -= ChangedWhileBusy; }
                }
            }
            Notify();
            await _catalogWriter.WaitAsync().ConfigureAwait(false);
            writerHeld = true;
            await Task.Run(() => ReadAndMergeInstancesAsync(paths, onlyId, automatic, showDiscoveryStatus)).ConfigureAwait(false);
        }
        catch (Exception ex) { Error = "实例读取失败：" + ex.Message; }
        finally
        {
            if (started) lock (_gate) _operation = false;
            if (writerHeld) _catalogWriter.Release();
            _discoveryGate.Release();
            if (started) Notify();
        }
    }

    private async Task ReadAndMergeInstancesAsync(IReadOnlyList<string> paths, string? onlyId, bool automatic, bool showDiscoveryStatus)
    {
        var discovered = new List<GameInstance>();
        var diagnostics = new List<string>();
        foreach (var path in paths)
        {
            if (automatic && !Directory.Exists(path)) continue;
            try
            {
                var found = await _discovery.DiscoverAsync(path);
                discovered.AddRange(found.Instances); diagnostics.AddRange(found.Diagnostics);
            }
            catch (Exception ex) when (automatic && ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
            { diagnostics.Add(ex.Message); }
        }
        var registered = Catalog.Instances;
        var original = onlyId is null ? null : registered.FirstOrDefault(x => x.Id == onlyId);
        var candidates = discovered.Where(x => onlyId is null || x.Id == onlyId || original is not null && SameSource(x, original))
            .Where(candidate =>
            {
                if (!registered.Any(item => item.Source == InstanceSource.Wonder && Same(item.RootDirectory, candidate.RootDirectory) &&
                    (candidate.Source != InstanceSource.Wonder || candidate.Id != item.Id))) return true;
                diagnostics.Add("目录中的配置与已登记的 Wonder 实例不一致，已保留原登记：" + candidate.RootDirectory);
                return false;
            }).DistinctBy(x => x.Id).ToArray();
        if (candidates.Length == 0)
        {
            if (!automatic) throw new InvalidDataException("没有发现可登记的实例。" + string.Join("\n", diagnostics.Take(8)));
            if (showDiscoveryStatus) StatusMessage = "已检查游戏目录。" + (diagnostics.Count > 0 ? "\n" + string.Join("\n", diagnostics.Take(8)) : "");
            return;
        }
        CommitCatalog(catalog =>
        {
            foreach (var candidate in candidates)
            {
                if (!catalog.GameDirectories.Any(path => Same(path, candidate.GameDirectoryGroup))) catalog.GameDirectories.Add(Full(candidate.GameDirectoryGroup));
                var index = catalog.Instances.FindIndex(x => x.Id == (onlyId ?? candidate.Id));
                if (index < 0)
                {
                    index = catalog.Instances.FindIndex(x => SameSource(x, candidate));
                    if (index >= 0 && catalog.Instances.FindLastIndex(x => SameSource(x, candidate)) != index)
                        throw new InvalidDataException("此来源已有多个实例登记，无法确定要更新的记录；请分别刷新，或保留需要的设置后移除多余登记。");
                }
                if (index < 0) catalog.Instances.Add(Copy(candidate));
                else
                {
                    var previous = catalog.Instances[index];
                    var refreshed = Copy(candidate) with { Id = previous.Id,
                        Name = candidate.Source == InstanceSource.Wonder ? candidate.Name : previous.Name,
                        Overrides = candidate.Source == InstanceSource.Wonder ? candidate.Overrides : previous.Overrides,
                        PreviousRootDirectories = [.. previous.PreviousRootDirectories] };
                    foreach (var oldRoot in previous.PreviousRootDirectories) refreshed = RebaseImportedSettings(refreshed, oldRoot, refreshed.RootDirectory);
                    catalog.Instances[index] = refreshed;
                }
            }
            catalog.SelectedInstanceId ??= catalog.Instances.First(x => x.Id == candidates[0].Id || SameSource(x, candidates[0])).Id;
        }, onlySaveChanges: automatic);
        if (Error is null && showDiscoveryStatus) StatusMessage = $"已读取 {candidates.Length} 个实例。" + (diagnostics.Count > 0 ? "\n" + string.Join("\n", diagnostics.Take(8)) : "");
    }

    public void Select(string id) => CompleteSelection(id, Commit(c => SetSelection(c, id)));
    public async Task<string?> SelectAsync(string id) => CompleteSelection(id, await CommitAsync(c => SetSelection(c, id)).ConfigureAwait(false));
    private static void SetSelection(InstanceCatalog catalog, string id) { Require(catalog, id); catalog.SelectedInstanceId = id; }
    private string? CompleteSelection(string id, string? error)
    {
        if (error is null && SelectedInstance is { } item && item.Id == id) SelectGameDirectory(item.GameDirectoryGroup);
        return error;
    }
    public void SelectGameDirectory(string path)
    {
        lock (_gate)
        {
            if (_browseDirectory is not null && Same(_browseDirectory, path) || !_catalog.GameDirectories.Any(root => Same(root, path))) return;
            _browseDirectory = Full(path);
        }
        Notify();
    }
    public void Rename(string id, string name) => Commit(c => Replace(c, id, x => x with { Name = name.Trim() }));
    public void UpdateSettings(string id, InstanceSettings overrides) => Commit(c => Replace(c, id, x => x with { Overrides = overrides }));
    public Task<string?> UpdateSettingsAsync(string id, InstanceSettings overrides) => CommitAsync(c => Replace(c, id, x => x with { Overrides = overrides }));
    public void UpdateInstance(string id, string name, InstanceSettings overrides) => Commit(c => Replace(c, id, x => x with { Name = name.Trim(), Overrides = overrides }));
    public Task<string?> UpdateInstanceAsync(string id, string name, InstanceSettings overrides) => CommitAsync(c => Replace(c, id, x => x with { Name = name.Trim(), Overrides = overrides }));
    public void SetOfflineUsername(string username) => Commit(c => c.OfflineUsername = username.Trim());
    public Task<string?> SetOfflineUsernameAsync(string username) => CommitAsync(c => c.OfflineUsername = username.Trim());
    public void UpdateGlobalSettings(InstanceSettings settings) => Commit(c => c.GlobalSettings = settings);
    public void UpdateGlobalSettings(InstanceSettings settings, bool versionIsolationEnabled) => Commit(c =>
    { c.GlobalSettings = settings; c.VersionIsolationEnabled = versionIsolationEnabled; });
    public Task<string?> UpdateGlobalSettingsAsync(InstanceSettings settings, bool versionIsolationEnabled) => CommitAsync(c =>
    { c.GlobalSettings = settings; c.VersionIsolationEnabled = versionIsolationEnabled; });
    public void ResetGlobalSettings() => UpdateGlobalSettings(new(), true);
    public void Remove(string id) => Commit(c =>
    {
        if (id == _launchId) throw new InvalidOperationException("实例正在启动或运行，暂不能移除登记。");
        Require(c, id); c.Instances.RemoveAll(x => x.Id == id);
        if (c.SelectedInstanceId == id) c.SelectedInstanceId = c.Instances.FirstOrDefault()?.Id;
    });

    private string? Commit(Action<InstanceCatalog> change)
    {
        lock (_gate)
        {
            if (_fileOperation) return Error = "文件操作进行中，请稍后再修改实例。";
            _pendingSaves++;
        }
        _catalogWriter.Wait();
        try { return CommitCatalog(change); }
        finally { FinishSave(); }
    }

    private async Task<string?> CommitAsync(Action<InstanceCatalog> change)
    {
        lock (_gate)
        {
            if (_fileOperation) return Error = "文件操作进行中，请稍后再修改实例。";
            _pendingSaves++;
        }
        await _catalogWriter.WaitAsync().ConfigureAwait(false);
        try { return await Task.Run(() => CommitCatalog(change)).ConfigureAwait(false); }
        finally { FinishSave(); }
    }

    private void FinishSave()
    {
        lock (_gate) _pendingSaves--;
        _catalogWriter.Release();
        Notify();
    }

    // Catalog readers continue to see the last committed snapshot while disk I/O is pending.
    private string? CommitCatalog(Action<InstanceCatalog> change, bool onlySaveChanges = false)
    {
        try
        {
            InstanceCatalog previous, next;
            lock (_gate)
            {
                if (_loadFailed || _pendingLoad) return Error ?? "正在读取实例设置，请稍后重试。";
                if (_fileOperation) return Error = "文件操作进行中，请稍后再修改实例。";
                previous = _catalog;
                next = Copy(previous); change(next); next.Validate();
                if (onlySaveChanges && next.SelectedInstanceId == _catalog.SelectedInstanceId &&
                    next.GameDirectories.SequenceEqual(_catalog.GameDirectories, WonderPath.Comparer) &&
                    next.Instances.Count == _catalog.Instances.Count && next.Instances.Zip(_catalog.Instances, SameReadInstance).All(equal => equal))
                    return Error = null;
            }
            _store.Save(Copy(next), Copy(previous));
            lock (_gate) { _catalog = next; return Error = null; }
        }
        catch (Exception ex)
        {
            LauncherDiagnostics.Warn("SaveInstanceCatalog", ex);
            lock (_gate) return Error = "修改未保存，保留原设置：" + ex.Message;
        }
    }

    private static bool SameReadInstance(GameInstance left, GameInstance right) =>
        left with
        {
            MetadataPaths = right.MetadataPaths, ModLoaders = right.ModLoaders,
            Warnings = right.Warnings, PreviousRootDirectories = right.PreviousRootDirectories
        } == right && left.MetadataPaths.SequenceEqual(right.MetadataPaths, WonderPath.Comparer) &&
        left.ModLoaders.SequenceEqual(right.ModLoaders) && left.Warnings.SequenceEqual(right.Warnings) &&
        left.PreviousRootDirectories.SequenceEqual(right.PreviousRootDirectories, WonderPath.Comparer);

    public async Task LaunchSelectedAsync()
    {
        LaunchRequest request;
        CancellationTokenSource preparation;
        lock (_gate)
        {
            if (_loadFailed || IsBusy) return;
            var instance = SelectedInstance;
            if (instance is null) { Error = "请先选择一个实例。"; Notify(); return; }
            if (instance.NativeConfigurationDiagnostic is not null) { Error = "Wonder 实例配置不可用，修复配置并重新读取后才能启动。"; Notify(); return; }
            request = new(instance, _catalog.OfflineUsername, _cacheDirectory);
            preparation = _preparation = new(); _launchId = instance.Id;
            _logs.Clear(); Error = null; LaunchPhase = LaunchPhase.Preparing;
            StatusMessage = "正在检查 " + instance.Name + "…";
        }
        var startedAt = DateTimeOffset.UtcNow;
        var endedAt = startedAt;
        var processStartingAt = startedAt;
        var processStarted = false;
        int? exitCode = null;
        string? failureReason = null;
        LaunchPlan? plan = null;
        HashSet<LaunchDiagnosticFile>? existingFiles = null;
        IReadOnlyList<LaunchDiagnosticFile> files = [];
        var redactor = new CredentialRedactor([]);
        using var transcript = _offerCrashReport is null ? null : new LaunchTranscript();

        string Redact(string value) => redactor.Redact(plan?.RedactOutput(value) ?? value);
        void Capture(string line)
        {
            line = Redact(line);
            transcript?.WriteLine(line);
            AddLog(line);
        }
        void Record(string message) => transcript?.WriteLine(Redact($"[{DateTimeOffset.UtcNow:O}] [Wonder] {message}"));

        Record(StatusMessage);
        Notify();
        try
        {
            try
            {
                if (_accounts is not null)
                {
                    Record("正在准备游戏会话。");
                    var session = await _accounts.GetSelectedSessionAsync(preparation.Token);
                    redactor = new([session.AccessToken]);
                    request = request with { Session = session };
                }
                preparation.Token.ThrowIfCancellationRequested();
                Record("正在检查 Java、版本文件和依赖并生成启动计划。");
                plan = await _launcher.BuildPlanAsync(request, preparation.Token);
                foreach (var notice in plan.Notices)
                {
                    Record(notice);
                    AddLog(Redact(notice));
                }
                if (_offerCrashReport is not null)
                {
                    // Filesystem timestamps may round a new report to just before process start.
                    var cutoff = DateTimeOffset.UtcNow.AddSeconds(-1);
                    existingFiles = await Task.Run(() => LaunchCrashReport.FindFiles(plan.WorkingDirectory, cutoff).ToHashSet(), preparation.Token);
                }
                Record("启动计划已就绪，正在创建游戏进程。");
                preparation.Token.ThrowIfCancellationRequested();
                processStartingAt = DateTimeOffset.UtcNow;
                exitCode = await _launcher.RunAsync(plan, () =>
                {
                    processStarted = true;
                    var kind = request.Session?.Kind switch { AccountKind.Microsoft => "正版", AccountKind.Yggdrasil => "第三方", _ => "离线" };
                    LaunchPhase = LaunchPhase.Running;
                    StatusMessage = request.Instance.Name + " · 游戏运行中（" + kind + "）";
                    Record(StatusMessage);
                    Notify();
                }, Capture, CancellationToken.None);
                endedAt = DateTimeOffset.UtcNow;
                StatusMessage = request.Instance.Name + $" · 已退出（代码 {exitCode}）";
                Record(StatusMessage);
            }
            catch (OperationCanceledException) when (!processStarted && preparation.IsCancellationRequested)
            {
                LaunchPhase = LaunchPhase.Idle;
                StatusMessage = "已取消启动准备。";
                return;
            }
            catch (Exception ex)
            {
                endedAt = DateTimeOffset.UtcNow;
                StatusMessage = processStarted ? "游戏运行异常" : "启动失败";
                failureReason = StatusMessage + "：" + Redact(ex.Message);
                Record(StatusMessage + "\n" + ex);
            }

            if (_offerCrashReport is not null && processStarted && plan is not null)
            {
                try
                {
                    files = await Task.Run(() => LaunchCrashReport.FindFiles(plan.WorkingDirectory, processStartingAt.AddSeconds(-1))
                        .Where(file => existingFiles?.Contains(file) != true).ToArray());
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { Record("无法读取游戏诊断文件：" + ex.Message); }
            }
            var crashed = exitCode is not 0 || files.Any(file => file.IsCrashReport);
            LaunchPhase = crashed ? LaunchPhase.Failed : LaunchPhase.Exited;
            if (!crashed) return;

            var reason = failureReason ?? (exitCode == 0 ? "游戏生成了崩溃报告。" : "游戏异常退出，请查看启动日志。");
            Error = reason;
            Record(reason);
            Notify();
            if (_offerCrashReport is not null)
            {
                try
                {
                    var directory = Path.GetFullPath(Path.Combine(_cacheDirectory, "..", "CrashReports"));
                    await _offerCrashReport(new(request.Instance, directory, startedAt, endedAt, exitCode, reason, plan,
                        Redact, transcript?.Finish(), LogLines, files));
                }
                catch (Exception ex) { Error += "\n无法显示错误日志保存窗口：" + Redact(ex.Message); }
            }
        }
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
            _logs.Add(line.Length > 2048 ? line[..2048] : line);
            if (_logs.Count > 200) _logs.RemoveAt(0);
        }
        Notify();
    }
    private void Notify()
    {
        if (Changed is not { } handlers) return;
        foreach (EventHandler handler in handlers.GetInvocationList())
            try { handler(this, EventArgs.Empty); }
            catch (Exception ex) { LauncherDiagnostics.Warn("NotifyInstancesChanged", ex); }
    }
    private static GameInstance Require(InstanceCatalog c, string id) => c.Instances.FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException("实例不存在。");
    private static void Replace(InstanceCatalog c, string id, Func<GameInstance, GameInstance> update)
    { var current = Require(c, id); c.Instances[c.Instances.IndexOf(current)] = update(current); }
    private static GameInstance Copy(GameInstance item) => item with { MetadataPaths = [.. item.MetadataPaths], Warnings = [.. item.Warnings], ModLoaders = [.. item.ModLoaders], PreviousRootDirectories = [.. item.PreviousRootDirectories], GlobalSettings = new() };
    private static InstanceCatalog Copy(InstanceCatalog c) => new()
    {
        Version = c.Version, Instances = c.Instances.Select(Copy).ToList(),
        GameDirectories = c.GameDirectories.Concat(c.Instances.Select(x => x.GameDirectoryGroup)).Select(Full).Distinct(WonderPath.Comparer).ToList(),
        SelectedInstanceId = c.SelectedInstanceId, OfflineUsername = c.OfflineUsername, GlobalSettings = c.GlobalSettings,
        VersionIsolationEnabled = c.VersionIsolationEnabled
    };
}
