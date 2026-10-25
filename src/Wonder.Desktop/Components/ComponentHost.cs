// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Collections.ObjectModel;
using Avalonia.Threading;
using Wonder.Extensibility.Contracts;
using Wonder.Workspace;

namespace Wonder.Desktop.Components;

internal enum ComponentRunStatus { Loading, Ready, Suspended, Faulted, Unavailable, Disposed }

/// <summary>Owns application runtimes for a workspace, independently of transient views and shortcuts.</summary>
internal sealed class ComponentHost : IDisposable
{
    private readonly Dictionary<string, Entry> _applications = new(StringComparer.Ordinal);
    private readonly Action _stateChanged;
    private bool _disposed;
    public ComponentRegistry Registry { get; }
    public event EventHandler? Changed;

    public ComponentHost(Action openSettings, Action stateChanged, string? dataDirectory = null, ComponentRegistry? registry = null)
    {
        _stateChanged = stateChanged;
        Registry = registry ?? BuiltInComponents.CreateRegistry(openSettings, dataDirectory ?? AppContext.BaseDirectory);
    }

    public ComponentDefinition GetDefinition(ComponentItem item) =>
        Registry.Find(item.Application.DefinitionId)?.Definition ??
        new(item.Application.DefinitionId, "组件不可用", "组件定义未注册；原有内容与位置已保留。", 280, 190);

    public ComponentSession Attach(ComponentItem item, bool visible = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var application = item.Application;
        if (!_applications.TryGetValue(application.Id, out var entry))
        {
            entry = new Entry(this, application);
            _applications.Add(application.Id, entry);
        }
        else if (!ReferenceEquals(entry.Application, application))
            throw new InvalidOperationException("同一个应用标识不能同时绑定到不同的数据对象。");
        return new ComponentSession(entry, visible);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var entry in _applications.Values) entry.Dispose();
        _applications.Clear();
        Changed = null;
    }

    internal static void Notify(EventHandler? handlers, object sender)
    {
        if (handlers is null) return;
        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            // An observer belongs to one view; it cannot interrupt delivery to other views or cleanup.
            try { handler(sender, EventArgs.Empty); }
            catch { }
        }
    }

    internal sealed class Entry(ComponentHost host, ComponentItem application) : IDisposable
    {
        private readonly ComponentHost _host = host;
        private int _visibleViews;
        private bool _running;
        private bool _disposed;
        private StateAccess? _state;
        private EventHandler? _runtimeChanged;
        public ComponentItem Application { get; } = application;
        public IComponentRuntime? Runtime { get; private set; }
        public ComponentRunStatus Status { get; private set; } = ComponentRunStatus.Loading;
        public string? Error { get; private set; }
        public IReadOnlyDictionary<string, string> Values { get; private set; } =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(application.State, StringComparer.Ordinal));
        public event EventHandler? Changed;

        public void ChangeVisibility(int delta)
        {
            if (_disposed) return;
            _visibleViews += delta;
            if (_visibleViews > 0 && Status == ComponentRunStatus.Loading) Initialize();
            else if (_visibleViews > 0 && Runtime is not null && !_running && Status == ComponentRunStatus.Suspended)
                Run(() => { _running = true; Runtime.Resume(); Status = ComponentRunStatus.Ready; RefreshValues(); });
            else if (_visibleViews == 0 && Runtime is not null && _running)
                Run(() => { _running = false; Runtime.Suspend(); Status = ComponentRunStatus.Suspended; });
        }

        private void Initialize()
        {
            var registration = _host.Registry.Find(Application.DefinitionId);
            if (registration is null)
            {
                Status = ComponentRunStatus.Unavailable;
                Error = "尚未安装或注册：" + Application.DefinitionId;
                Publish();
                return;
            }
            Run(() =>
            {
                _state = new StateAccess(this);
                Runtime = registration.CreateRuntime(_state) ?? throw new InvalidOperationException("组件没有提供运行实例。");
                var runtime = Runtime;
                _runtimeChanged = (_, _) =>
                {
                    void Refresh()
                    {
                        if (!_disposed && ReferenceEquals(Runtime, runtime))
                            Run(RefreshValues);
                    }
                    if (Dispatcher.UIThread.CheckAccess()) Refresh();
                    else Dispatcher.UIThread.Post(Refresh);
                };
                Runtime.Changed += _runtimeChanged;
                if (_visibleViews > 0)
                {
                    _running = true;
                    Runtime.Resume();
                }
                Status = _running ? ComponentRunStatus.Ready : ComponentRunStatus.Suspended;
                Error = null;
                RefreshValues();
            });
        }

        public void Invoke(string actionId, string? value)
        {
            if (_disposed || Runtime is null || Status is not (ComponentRunStatus.Ready or ComponentRunStatus.Suspended)) return;
            Run(() => Runtime.Invoke(actionId, value), notify: false);
        }

        public void Retry()
        {
            if (_disposed) return;
            ReleaseRuntime();
            Status = ComponentRunStatus.Loading;
            Error = null;
            Publish();
            Initialize();
        }

        public void Fail(Exception exception)
        {
            if (_disposed) return;
            ReleaseRuntime();
            Status = ComponentRunStatus.Faulted;
            Error = exception.Message;
            Values = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(Application.State, StringComparer.Ordinal));
            Publish();
        }

        private void Run(Action action, bool notify = true)
        {
            if (_disposed) return;
            try { action(); }
            catch (Exception exception) { Fail(exception); return; }
            if (notify) Publish();
        }

        private void RefreshValues() => Values = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(Runtime?.Values ?? Application.State, StringComparer.Ordinal));

        private void Publish()
        {
            Notify(Changed, this);
            Notify(_host.Changed, _host);
        }

        private void ReleaseRuntime()
        {
            var runtime = Runtime;
            Runtime = null;
            _state?.Revoke();
            _state = null;
            if (runtime is null) return;
            try { if (_runtimeChanged is not null) runtime.Changed -= _runtimeChanged; }
            catch { }
            _runtimeChanged = null;
            // A broken component must not prevent the rest of the workspace from closing.
            try { if (_running) runtime.Suspend(); }
            catch { }
            _running = false;
            try { runtime.Dispose(); }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ReleaseRuntime();
            Status = ComponentRunStatus.Disposed;
            Changed = null;
        }

        private sealed class StateAccess(Entry entry) : IComponentState
        {
            private bool _active = true;
            public IReadOnlyDictionary<string, string> Snapshot =>
                new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(entry.Application.State, StringComparer.Ordinal));
            public string Get(string key, string fallback = "") => entry.Application.State.GetValueOrDefault(key, fallback);
            public void Revoke() => _active = false;
            public void Set(string key, string value)
            {
                ObjectDisposedException.ThrowIf(!_active, this);
                Dispatcher.UIThread.VerifyAccess();
                if (string.IsNullOrWhiteSpace(key) || key.Length > 80 || value is null || value.Length > 65_536 ||
                    !entry.Application.State.ContainsKey(key) && entry.Application.State.Count >= 64)
                    throw new ArgumentException("组件数据超出了允许的大小。");
                if (entry.Application.State.TryGetValue(key, out var previous) && previous == value) return;
                entry.Application.State[key] = value;
                entry.RefreshValues();
                // Saving does not rebuild the component view; focused editors retain caret and selection.
                try { entry._host._stateChanged(); }
                finally { entry.Publish(); }
            }
        }
    }
}

internal sealed class ComponentSession : IDisposable
{
    private readonly ComponentHost.Entry _entry;
    private bool _visible;
    private bool _disposed;
    public IComponentRuntime? Runtime => _entry.Runtime;
    public ComponentRunStatus Status => _entry.Status;
    public string? Error => _entry.Error;
    public IReadOnlyDictionary<string, string> Values => _entry.Values;
    public event EventHandler? Changed;

    internal ComponentSession(ComponentHost.Entry entry, bool visible)
    {
        _entry = entry;
        _entry.Changed += OnChanged;
        SetVisible(visible);
    }
    private void OnChanged(object? sender, EventArgs args)
    {
        if (!_disposed) ComponentHost.Notify(Changed, this);
    }
    public void SetVisible(bool visible)
    {
        if (_disposed || _visible == visible) return;
        _visible = visible;
        _entry.ChangeVisibility(visible ? 1 : -1);
    }
    public void Invoke(string actionId, string? value = null)
    {
        if (!_disposed) _entry.Invoke(actionId, value);
    }
    public void Retry() { if (!_disposed) _entry.Retry(); }
    internal void Fail(Exception exception) { if (!_disposed) _entry.Fail(exception); }
    public void Dispose()
    {
        if (_disposed) return;
        SetVisible(false);
        _disposed = true;
        _entry.Changed -= OnChanged;
        Changed = null;
    }
}
