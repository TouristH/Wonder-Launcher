// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Text.Json;
using Wonder.Core;

namespace Wonder.Application;

/// <summary>Owns install jobs independently of component and window lifetimes.</summary>
public sealed class DownloadService : IDisposable
{
    private readonly object _gate = new();
    private readonly List<Job> _jobs = [];
    private readonly string? _journalPath;
    private readonly Func<string, Task>? _import;
    private DownloadOptions _options = new();
    private bool _draining, _disposed, _journalBlocked;
    public IDownloadCatalog Catalog { get; }
    public event EventHandler? Changed;
    public string? StorageError { get; private set; }
    public DownloadOptions Options { get { lock (_gate) return _options; } }
    public IReadOnlyList<DownloadTaskSnapshot> Tasks { get { lock (_gate) return _jobs.Select(x => x.Snapshot).ToArray(); } }

    public DownloadService(IDownloadCatalog catalog, string? journalPath = null, Func<string, Task>? import = null)
    {
        Catalog = catalog; _journalPath = journalPath; _import = import;
        if (journalPath is null || !File.Exists(journalPath)) return;
        try
        {
            if (new FileInfo(journalPath).Length > 16 * 1024 * 1024) throw new InvalidDataException("任务记录过大。");
            var saved = JsonSerializer.Deserialize<List<SavedJob>>(File.ReadAllText(journalPath)) ?? throw new InvalidDataException("任务记录为空。");
            if (saved.Count > 500 || saved.Select(x => x.Snapshot.Id).Distinct().Count() != saved.Count) throw new InvalidDataException("任务记录无效。");
            foreach (var item in saved)
            {
                item.Options.Validate();
                if ((item.Game is null) == (item.Resource is null) || !Enum.IsDefined(item.Snapshot.State) ||
                    item.Snapshot.Progress is null || string.IsNullOrWhiteSpace(item.Snapshot.Id)) throw new InvalidDataException("任务类型无效。");
                Destination(item.Game, item.Resource);
                var snapshot = item.Snapshot;
                if (snapshot.State is DownloadTaskState.Running or DownloadTaskState.Queued)
                    snapshot = snapshot with { State = DownloadTaskState.Paused, Progress = snapshot.Progress with { Stage = "上次未完成，点击继续", BytesPerSecond = 0 } };
                _jobs.Add(new(snapshot, item.Options, item.Game, SnapshotResource(item.Resource)));
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NullReferenceException)
        {
            _jobs.Clear(); _journalBlocked = true;
            StorageError = "下载任务记录读取失败，原文件已保留；本次任务仅在当前会话中保存。" + ex.Message;
        }
    }

    public void SetOptions(DownloadOptions options)
    { options.Validate(); lock (_gate) _options = options; Notify(); }

    public string QueueGame(GameInstallRequest request) => Enqueue(request.InstanceName + " · Minecraft " + request.MinecraftVersion, request, null);
    public string QueueResource(ResourceInstallRequest request) => Enqueue(request.Project.Title + " · " + request.Version.VersionNumber, null, request);

    private string Enqueue(string title, GameInstallRequest? game, ResourceInstallRequest? resource)
    {
        string id;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_jobs.Count >= 500) throw new InvalidOperationException("下载记录已满，请先清除已结束任务。");
            resource = SnapshotResource(resource);
            EnsureNoConflict(game, resource);
            id = Guid.NewGuid().ToString("N");
            _jobs.Add(new(new(id, title, DownloadTaskState.Queued, new("等待下载"), null, null, DateTimeOffset.UtcNow), _options, game, resource));
            Save(); Schedule();
        }
        Notify(); return id;
    }

    public void Pause(string id)
    {
        lock (_gate)
        {
            var job = Find(id);
            if (job is null || job.Snapshot.State is not (DownloadTaskState.Running or DownloadTaskState.Queued)) return;
            job.Snapshot = job.Snapshot with { State = DownloadTaskState.Paused, Progress = job.Snapshot.Progress with { Stage = "已暂停", BytesPerSecond = 0 } };
            job.Cancellation?.Cancel(); Save();
        }
        Notify();
    }
    public void Resume(string id) => Restart(id, DownloadTaskState.Paused);
    public void Retry(string id) => Restart(id, DownloadTaskState.Failed, DownloadTaskState.Cancelled);
    private void Restart(string id, params DownloadTaskState[] states)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var job = Find(id);
            if (job is null || !states.Contains(job.Snapshot.State)) return;
            EnsureNoConflict(job.Game, job.Resource, job);
            job.Snapshot = job.Snapshot with { State = DownloadTaskState.Queued, Error = null, Progress = new("等待继续") };
            Save(); Schedule();
        }
        Notify();
    }
    public void Cancel(string id)
    {
        lock (_gate)
        {
            var job = Find(id);
            if (job is null || job.Snapshot.State is DownloadTaskState.Completed or DownloadTaskState.Cancelled) return;
            job.Snapshot = job.Snapshot with { State = DownloadTaskState.Cancelled, Progress = job.Snapshot.Progress with { Stage = "已取消", BytesPerSecond = 0 } };
            job.Cancellation?.Cancel(); Save();
        }
        Notify();
    }
    public void ClearFinished()
    {
        lock (_gate) { _jobs.RemoveAll(x => x.Cancellation is null && x.Snapshot.State is DownloadTaskState.Completed or DownloadTaskState.Cancelled); Save(); }
        Notify();
    }

    private Job? Find(string id) => _jobs.FirstOrDefault(x => x.Snapshot.Id == id);
    private static string Destination(GameInstallRequest? game, ResourceInstallRequest? resource)
    {
        var path = game?.TargetDirectory ?? resource?.TargetDirectory;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new ArgumentException("安装目标必须是完整路径。");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static bool CreatesInstance(GameInstallRequest? game, ResourceInstallRequest? resource) =>
        game is not null || resource is { DownloadOnly: false, Project.Kind: DownloadResourceKind.Modpack };
    private void EnsureNoConflict(GameInstallRequest? game, ResourceInstallRequest? resource, Job? except = null)
    {
        var target = Destination(game, resource);
        var createsInstance = CreatesInstance(game, resource);
        foreach (var other in _jobs)
        {
            if (ReferenceEquals(other, except) || other.Snapshot.State is not (DownloadTaskState.Queued or DownloadTaskState.Running or DownloadTaskState.Paused)) continue;
            if (!WonderPath.Comparer.Equals(Destination(other.Game, other.Resource), target)) continue;
            // Installs own the entire new directory. Resource tasks are serialized and may share
            // an existing instance, but two pending versions of the same project are ambiguous.
            var sameProject = other.Resource?.Project is { } existing && resource?.Project is { } requested &&
                existing.Provider == requested.Provider && existing.Id == requested.Id;
            if (createsInstance || CreatesInstance(other.Game, other.Resource) ||
                sameProject)
                throw new InvalidOperationException("此目标已有相同或冲突的未结束任务，请等待完成或先取消。");
        }
    }
    private static ResourceInstallRequest? SnapshotResource(ResourceInstallRequest? request)
    {
        if (request is null) return null;
        if (request.Project is null || request.Version is null || !Enum.IsDefined(request.Project.Kind) ||
            !Enum.IsDefined(request.Project.Provider) || string.IsNullOrWhiteSpace(request.Project.Id))
            throw new ArgumentException("下载资源信息无效。");
        var version = request.Version;
        return request with { Version = version with { GameVersions = version.GameVersions.ToArray(), Loaders = version.Loaders.ToArray(), Files = version.Files.ToArray(), Dependencies = version.Dependencies.ToArray() } };
    }
    private void Schedule()
    { if (!_draining && !_disposed) { _draining = true; _ = Task.Run(DrainAsync); } }
    private async Task DrainAsync()
    {
        while (true)
        {
            Job job;
            CancellationTokenSource cancel;
            lock (_gate)
            {
                var next = _jobs.FirstOrDefault(x => x.Snapshot.State == DownloadTaskState.Queued);
                if (_disposed || next is null) { _draining = false; return; }
                job = next; cancel = job.Cancellation = new();
                job.Snapshot = job.Snapshot with { State = DownloadTaskState.Running, Error = null, Progress = new("准备下载") }; Save();
            }
            Notify();
            try
            {
                var progress = new DirectProgress(value =>
                {
                    lock (_gate)
                    {
                        if (_disposed || job.Snapshot.State != DownloadTaskState.Running) return;
                        job.Snapshot = job.Snapshot with { Progress = value };
                    }
                    Notify();
                });
                var path = job.Game is { } game
                    ? await Catalog.InstallGameAsync(game, job.Options, progress, cancel.Token).ConfigureAwait(false)
                    : await Catalog.InstallResourceAsync(job.Resource!, job.Options, progress, cancel.Token).ConfigureAwait(false);
                // A final filesystem rename can finish while shutdown cancellation is arriving.
                // Leave the persisted paused job recoverable; do not mutate the instance index
                // after the owner has closed and released its data-directory guard.
                lock (_gate) if (_disposed) return;
                string? warning = null;
                if (_import is not null && CreatesInstance(job.Game, job.Resource))
                {
                    try { await _import(path).ConfigureAwait(false); }
                    catch (Exception ex) { warning = "文件已安装，实例登记尚未完成：" + ex.Message + "。可在实例管理中导入安装目录。"; }
                }
                lock (_gate)
                    job.Snapshot = job.Snapshot with { State = DownloadTaskState.Completed, ResultPath = path, Error = warning,
                        Progress = job.Snapshot.Progress with { Stage = warning is null ? "安装完成" : "安装完成 · 请导入实例", BytesPerSecond = 0 } };
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                lock (_gate)
                    if (job.Snapshot.State == DownloadTaskState.Running)
                        job.Snapshot = job.Snapshot with { State = DownloadTaskState.Paused, Progress = job.Snapshot.Progress with { Stage = "已暂停", BytesPerSecond = 0 } };
            }
            catch (Exception ex)
            {
                lock (_gate)
                    if (job.Snapshot.State == DownloadTaskState.Running)
                        job.Snapshot = job.Snapshot with { State = DownloadTaskState.Failed, Error = ex.Message, Progress = job.Snapshot.Progress with { Stage = "下载失败", BytesPerSecond = 0 } };
            }
            finally
            {
                lock (_gate) { job.Cancellation = null; cancel.Dispose(); if (!_disposed) Save(); }
                Notify();
            }
        }
    }

    private void Save()
    {
        if (_journalPath is null || _journalBlocked || _disposed) return;
        if (_jobs.Count == 0 && !File.Exists(_journalPath)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_journalPath))!);
            var json = JsonSerializer.Serialize(_jobs.Select(x => new SavedJob(x.Snapshot, x.Options, x.Game, x.Resource)), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_journalPath + ".tmp", json);
            File.Move(_journalPath + ".tmp", _journalPath, true); StorageError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { StorageError = "下载记录未保存：" + ex.Message; }
    }
    private void Notify()
    {
        if (_disposed || Changed is not { } handlers) return;
        foreach (EventHandler handler in handlers.GetInvocationList())
            try { handler(this, EventArgs.Empty); }
            catch (Exception ex) { LauncherDiagnostics.Warn("NotifyDownloadsChanged", ex); }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            foreach (var job in _jobs.Where(x => x.Snapshot.State is DownloadTaskState.Running or DownloadTaskState.Queued))
                job.Snapshot = job.Snapshot with { State = DownloadTaskState.Paused, Progress = job.Snapshot.Progress with { Stage = "已暂停", BytesPerSecond = 0 } };
            Save(); _disposed = true;
            foreach (var job in _jobs) job.Cancellation?.Cancel();
            Changed = null;
        }
    }
    private sealed class Job(DownloadTaskSnapshot snapshot, DownloadOptions options, GameInstallRequest? game, ResourceInstallRequest? resource)
    {
        public DownloadTaskSnapshot Snapshot = snapshot;
        public readonly DownloadOptions Options = options;
        public readonly GameInstallRequest? Game = game;
        public readonly ResourceInstallRequest? Resource = resource;
        public CancellationTokenSource? Cancellation;
    }
    public sealed record SavedJob(DownloadTaskSnapshot Snapshot, DownloadOptions Options, GameInstallRequest? Game, ResourceInstallRequest? Resource);
    private sealed class DirectProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    { public void Report(DownloadProgress value) => report(value); }
}
